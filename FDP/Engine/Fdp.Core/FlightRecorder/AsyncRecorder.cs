using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.FlightRecorder.Metadata;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// Asynchronous Flight Recorder with double buffering.
    /// Implements FDP-DES-004 design for non-blocking snapshot capture.
    /// **Zero-allocation after initialization** on hot path.
    /// </summary>
    public class AsyncRecorder : IDisposable
    {
        private const int BUFFER_SIZE = 32 * 1024 * 1024; // 32MB Buffer
        
        // Double Buffers (pre-allocated)
        private byte[] _frontBuffer;
        private byte[] _backBuffer;
        // ⭐ CE-2045 — one long-lived stream + writer per buffer, swapped WITH it: a frame rewinds instead of allocating a
        //   MemoryStream and a BinaryWriter (104 B/frame on the hot path, measured). Fdp.Core.md: "no heap allocation on the hot path".
        // ⭐ CE-2045 — the worker's arguments live in fields and its delegate is cached, so a dispatch allocates only the Task
        //   (the per-frame lambda captured a closure + a delegate). Safe: at most ONE worker is ever outstanding — a capture
        //   waits for (blocking) or drops on (non-blocking) the previous one before it dispatches.
        private byte[]? _pendingBuffer;
        private int _pendingBytes;
        private Action? _processPending;
        private MemoryStream _frontStream;
        private BinaryWriter _frontWriter;
        private MemoryStream _backStream;
        private BinaryWriter _backWriter;
        
        // Pre-allocated write buffer with 4-byte header space (zero-allocation)
        private readonly byte[] _writeBuffer;
        
        private Task? _workerTask;
        private readonly FileStream _outputStream;
        private readonly RecorderSystem _recorderSystem;
        private readonly RecordingMetadata _metadata;

        /// <summary>
        /// The metadata written to <c>.meta.json</c> at <see cref="Dispose"/>. Main-thread callers may fill fields that describe
        /// the recorded world (<c>CE-3126</c>: terrain name, geo origin) while recording.
        /// </summary>
        public RecordingMetadata Metadata => _metadata;
        private readonly string _filePath;
        
        // Stats
        public int DroppedFrames { get; private set; }
        public int RecordedFrames { get; private set; }
        
        // Error propagation for tests
        public Exception? LastError { get; private set; }
        
        /// <summary>
        /// ID threshold below which entities are NOT recorded.
        /// Passthrough to underlying RecorderSystem.
        /// </summary>
        public int MinRecordableId 
        { 
            get => _recorderSystem.MinRecordableId; 
            set => _recorderSystem.MinRecordableId = value; 
        }

        /// <summary>
        /// Optional entity filter predicate.  Passthrough to the underlying
        /// <see cref="RecorderSystem.EntityFilter"/>.  When <c>null</c> (default),
        /// all entities above <see cref="MinRecordableId"/> are recorded — existing
        /// behaviour is completely unchanged.
        /// </summary>
        public Predicate<Entity>? EntityFilter
        {
            get => _recorderSystem.EntityFilter;
            set => _recorderSystem.EntityFilter = value;
        }

        /// <summary>
        /// The highest network (DIS) entity ID observed during the recording session.
        /// Set this property before calling <see cref="Dispose"/> so the value is
        /// persisted to the <c>.meta.json</c> manifest.  The application layer
        /// (e.g. <c>RecordingModule</c>) is responsible for querying the network
        /// entity map to determine the correct value.
        /// When left at the default of <c>0</c>, the field is written as <c>0</c>
        /// in the manifest (pre-feature recordings are treated as unknown).
        /// </summary>
        public long MaxNetworkId { get; set; } = 0;

        private bool _disposed;
        
        /// <summary>A recorder that writes <paramref name="filePath"/>, replacing a file already there (tools, examples, tests).</summary>
        public AsyncRecorder(string filePath, RecordingMetadata? metadata = null)
            : this(filePath, metadata, appendAfterFrames: -1) { }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> (R-3047) — the cluster's exercise recorder (<c>RecordingModule</c>): it NEVER overwrites.
        /// </summary>
        /// <param name="appendAfterFrames">0: a NEW file — ⛔ <c>FileMode.CreateNew</c>, a loud failure on an existing one (the
        /// truncation D18 found). N &gt; 0: <paramref name="filePath"/> already holds the global header and N frames (a branch's
        /// copied history, <see cref="RecordingBranch"/>); this recorder appends after them and counts from N, so a checkpoint mark
        /// names its frame in the whole file. (-1, the other constructor: replace a file already there.)</param>
        public AsyncRecorder(string filePath, RecordingMetadata? metadata, int appendAfterFrames)
        {
            if (appendAfterFrames < -1) throw new ArgumentOutOfRangeException(nameof(appendAfterFrames));
            _filePath = filePath;
            _metadata = metadata ?? new RecordingMetadata();
            if (_metadata.Timestamp == default) _metadata.Timestamp = DateTime.UtcNow;

            _frontBuffer = new byte[BUFFER_SIZE];
            _backBuffer = new byte[BUFFER_SIZE];
            _frontStream = new MemoryStream(_frontBuffer);
            _frontWriter = new BinaryWriter(_frontStream);
            _backStream  = new MemoryStream(_backBuffer);
            _backWriter  = new BinaryWriter(_backStream);
            _writeBuffer = new byte[BUFFER_SIZE + 4]; // Pre-allocate for length prefix
            
            // Allocate compression buffer (Worst case size)
            int maxOutput = K4os.Compression.LZ4.LZ4Codec.MaximumOutputSize(BUFFER_SIZE);
            _compressedBuffer = new byte[maxOutput];
            
            _recorderSystem = new RecorderSystem();
            
            // Open file for async I/O
            _outputStream = new FileStream(
                filePath, 
                // ⭐ D18 (R-3047) — a recording never overwrites one: CreateNew, or append after a branch's copied frames.
                appendAfterFrames switch { > 0 => FileMode.Open, 0 => FileMode.CreateNew, _ => FileMode.Create }, 
                FileAccess.Write, 
                FileShare.Read,   // ⭐ CE-3299 B3 — a checkpoint is exported from the recording while it is still written 
                4096, 
                useAsync: false);
            
            if (appendAfterFrames > 0)
            {
                _outputStream.Seek(0, SeekOrigin.End);
                RecordedFrames = appendAfterFrames;
            }
            else
            {
                // Write Global Header immediately (See DES-002)
                WriteGlobalHeader();
            }
        }

        private byte[] _compressedBuffer;

        // Flag to force keyframe on next capture if a frame was dropped
        private bool _forceKeyframeNext;

        /// <summary>
        /// Call this at End-Of-Frame (Phase: PostSimulation).
        /// </summary>
        /// <param name="wallClockTicks">The frame-locked wall-clock timestamp (UTC ticks) for this frame.
        /// Must be supplied by the caller — typically read from
        /// <c>GlobalTime.TotalWallTicks</c> so the stamp is constant across all PostSimulation systems
        /// and is never sampled inside the async recording path.</param>
        /// <param name="blocking">If true, waits for previous write to complete instead of dropping frame.</param>
        /// <param name="eventBus">Optional event bus to capture events from for this frame.</param>
        public void CaptureFrame(EntityRepository repo, uint prevTick, long wallClockTicks, bool blocking = false, FdpEventBus? eventBus = null)
        {
            // Auto-Recovery: If we dropped a frame previously, force a Keyframe now to restore state.
            if (_forceKeyframeNext)
            {
                CaptureKeyframe(repo, wallClockTicks, blocking, eventBus);
                _forceKeyframeNext = false;
                return;
            }

            // 1. SAFETY CHECK
            // If the worker is still busy compressing the LAST frame, we are generating data faster than disk can write.
            if (_workerTask != null && !_workerTask.IsCompleted)
            {
                if (blocking)
                {
                    WaitForWorker();
                }
                else
                {
                    // Options:
                    // A) Block (Stutter game)
                    // B) Drop Frame (Gaps in replay) -> Preferred for Recorder
                    DroppedFrames++;
                    _forceKeyframeNext = true; // Recover on next frame
                    return;
                }
            }
            
            // 2. CAPTURE (Main Thread - Hot Path - ZERO ALLOCATION)
            int bytesWritten = 0;
            
            _frontStream.Position = 0;
            // Use the logic from FDP-DES-002
            _recorderSystem.RecordDeltaFrame(repo, prevTick, _frontWriter, wallClockTicks, eventBus);
            _frontWriter.Flush();
            StampIndex(repo);   // ⭐ CE-3329 P0 — SimTime + Flags for the v7 frame index
            bytesWritten = (int)_frontStream.Position;
            
            // Clear the destruction log after recording
            repo.ClearDestructionLog();
            
            // 3. SWAP POINTERS
            var dataToWrite = _frontBuffer;
            var freeBuffer = _backBuffer;
            _frontBuffer = freeBuffer;
            _backBuffer = dataToWrite;
            (_frontStream, _backStream) = (_backStream, _frontStream);
            (_frontWriter, _backWriter) = (_backWriter, _frontWriter);
            
            // 4. DISPATCH WORKER
            // Capture 'bytesWritten' by value closure
            _pendingBuffer = dataToWrite;
            _pendingBytes  = bytesWritten;
            _workerTask = Task.Run(_processPending ??= () => ProcessBuffer(_pendingBuffer!, _pendingBytes));
            
            RecordedFrames++;
        }
        
        /// <summary>
        /// Captures a full keyframe instead of a delta.
        /// </summary>
        /// <param name="wallClockTicks">The frame-locked wall-clock timestamp (UTC ticks) for this frame.
        /// Must be supplied by the caller — typically read from
        /// <c>GlobalTime.TotalWallTicks</c> so the stamp is constant across all PostSimulation systems.
        /// </param>
        public void CaptureKeyframe(EntityRepository repo, long wallClockTicks, bool blocking = false, FdpEventBus? eventBus = null)
        {
            // Wait for previous frame to complete
            WaitForWorker();
            
            int bytesWritten = 0;
            
            _frontStream.Position = 0;
            _recorderSystem.RecordKeyframe(repo, _frontWriter, wallClockTicks, eventBus);
            _frontWriter.Flush();
            StampIndex(repo);   // ⭐ CE-3329 P0
            bytesWritten = (int)_frontStream.Position;
            
            // Clear the destruction log
            repo.ClearDestructionLog();
            
            // Swap and dispatch
            var dataToWrite = _frontBuffer;
            var freeBuffer = _backBuffer;
            _frontBuffer = freeBuffer;
            _backBuffer = dataToWrite;
            (_frontStream, _backStream) = (_backStream, _frontStream);
            (_frontWriter, _backWriter) = (_backWriter, _frontWriter);
            
            _pendingBuffer = dataToWrite;
            _pendingBytes  = bytesWritten;
            _workerTask = Task.Run(_processPending ??= () => ProcessBuffer(_pendingBuffer!, _pendingBytes));
            
            if (blocking)
            {
                WaitForWorker();
            }
            
            RecordedFrames++;
        }

        /// <summary>
        /// Runs on ThreadPool - ZERO ALLOCATION (uses pre-allocated buffers)
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-2045</c> — wait for the outstanding worker WITHOUT allocating: <c>Task.Wait()</c> allocates a wait handle
        /// whenever it has to block (measured ~56 B), and a blocking capture is on the main thread. A completed task is then
        /// <c>Wait()</c>-ed once, which allocates nothing and still rethrows a worker failure.
        /// </summary>
        private void WaitForWorker()
        {
            var task = _workerTask;
            if (task == null) return;
            var spin = new SpinWait();
            // sleep1Threshold: -1 — yield, never Sleep(1): a blocking capture must wake the moment the worker finishes
            //   (Sleep(1) would add up to ~1 ms per stall, which Task.Wait's event wake does not).
            while (!task.IsCompleted) spin.SpinOnce(sleep1Threshold: -1);
            task.Wait();
        }

        // ⭐⭐ CE-3329 P0 (R1) — what the v7 frame index says about the frame being dispatched: its recorded sim time and flags.
        //   Set on the main thread before the worker is dispatched; the worker reads them (a capture waits for, or drops instead of
        //   overlapping, a busy worker, so they cannot change under it).
        private double _pendingSimTime = double.NaN;
        private byte _pendingFlags;

        private void StampIndex(EntityRepository repo)
        {
            var flags = _recorderSystem.LastFrameChanged ? FrameFlags.Changed : FrameFlags.None;
            if (repo.HasSingletonUnmanaged<GlobalTime>())
            {
                var time = repo.GetSingletonUnmanaged<GlobalTime>();
                _pendingSimTime = time.TotalTime;
                if (time.IsHalted) flags |= FrameFlags.Halted;
            }
            else _pendingSimTime = double.NaN;
            _pendingFlags = (byte)flags;
        }

        private void ProcessBuffer(byte[] rawData, int length)
        {
            try
            {
                // 1. Extract Metadata for Indexing (Duplicated in header to avoid decompression during scan)
                // Format ensures [Tick: 8 bytes] [Type: 1 byte] [WallClockTicks: 8 bytes] are at start
                ulong tick = BitConverter.ToUInt64(rawData, 0);
                byte type = rawData[8];
                long wallClockTicks = BitConverter.ToInt64(rawData, 9);

                // 2. COMPRESS
                // Compress the ENTIRE frame (including Tick/Type because PlaybackSystem expects them)
                int encodedLength = K4os.Compression.LZ4.LZ4Codec.Encode(
                    rawData, 0, length, 
                    _compressedBuffer, 0, _compressedBuffer.Length);

                lock (_outputStream)
                {
                    // 3. WRITE HEADER
                    FrameOuterHeader headerData = new FrameOuterHeader
                    {
                        CompressedSize = encodedLength,
                        UncompressedSize = length,
                        Tick = tick,
                        FrameType = type,
                        WallClockTicks = wallClockTicks,
                        SimTime = _pendingSimTime,
                        Flags = _pendingFlags,
                    };

                    ReadOnlySpan<byte> headerSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                        new ReadOnlySpan<FrameOuterHeader>(ref headerData));

                    _outputStream.Write(headerSpan);

                    // 4. WRITE PAYLOAD
                    _outputStream.Write(_compressedBuffer, 0, encodedLength);
                    _outputStream.Flush();
                }
            }
            catch (Exception ex)
            {
                // Store error for propagation
                LastError = ex;
            }
        }
        
        private void WriteGlobalHeader()
        {
            // Global Header Format (FDP-DES-002):
            // [Magic: 6 bytes] [Version: uint] [Timestamp: long]
            
            byte[] magic = System.Text.Encoding.ASCII.GetBytes("FDPREC");
            _outputStream.Write(magic, 0, 6);
            
            byte[] version = BitConverter.GetBytes(FdpConfig.FORMAT_VERSION);
            _outputStream.Write(version, 0, 4);
            
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            _outputStream.Write(timestampBytes, 0, 8);
            
            _outputStream.Flush();
        }
        
        /// <summary>
        /// ⭐ <c>CE-3299</c> B3 — waits until every frame captured so far is on disk (and the latest manifest written), so the file
        /// can be read up to its last frame — an export of a checkpoint just written.
        /// </summary>
        public void WaitForWrites()
        {
            WaitForWorker();
            Task? meta;
            lock (_metadataLock) meta = _metadataTask;
            meta?.Wait();
        }

        /// <summary>The recording file this recorder writes.</summary>
        public string FilePath => _filePath;

        /// <summary>⭐ <c>CE-3299</c> B3 — true when <paramref name="label"/> already names a checkpoint in this recording.</summary>
        public bool HasCheckpoint(string label) => _metadata.Checkpoints.Exists(m => m.Label == label);

        /// <summary>
        /// ⭐ <c>CE-3299</c> B3 (R-3044) — names the keyframe this recorder JUST captured (call right after <see cref="CaptureKeyframe"/>):
        /// the mark goes into the metadata and <c>.meta.json</c> is rewritten at once, so a crash keeps every checkpoint taken
        /// before it (a fresh process restores from it, D10). The manifests are built here (main thread, the registry); the
        /// JSON is written off it. Returns the mark.
        /// </summary>
        public CheckpointMark MarkCheckpoint(string label, long wallClockTicks, GlobalTime time = default)
        {
            if (HasCheckpoint(label)) throw new InvalidOperationException($"the recording already has a checkpoint named '{label}'");
            var mark = new CheckpointMark
            {
                Label = label, Frame = RecordedFrames - 1, WallTicks = wallClockTicks,
                // ⭐ B4 (D5) — the clock a restore of this mark moves the cluster back to
                TotalTime = time.TotalTime, UnscaledTotalTime = time.UnscaledTotalTime, FrameNumber = time.FrameNumber,
            };
            _metadata.Checkpoints.Add(mark);
            WriteMetadataSoon();
            return mark;
        }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> — writes <c>.meta.json</c> as it is NOW (main thread: the manifests read the registry). A
        /// recording calls it on its first frame (CE-3313 — a branch's parent and inherited checkpoints, every recording's exercise, mode
        /// and schema are on disk at once: a crash keeps them).
        /// </summary>
        public void WriteMetadata() => WriteMetadataSoon();

        private Task? _metadataTask;
        private readonly object _metadataLock = new();

        // ⭐ CE-3299 B3 — the manifest as it is NOW, serialized here and written by a pool thread (one write at a time, in order).
        private void WriteMetadataSoon()
        {
            _metadata.TotalFrames   = RecordedFrames;
            _metadata.MaxNetworkId  = MaxNetworkId;
            _metadata.SchemaManifest = BuildSchemaManifest();
            _metadata.EventManifest  = BuildEventManifest();
            string json = MetadataSerializer.Serialize(_metadata);
            string path = _filePath + ".meta.json";
            lock (_metadataLock)
            {
                var previous = _metadataTask ?? Task.CompletedTask;
                _metadataTask = previous.ContinueWith(_ =>
                {
                    try { File.WriteAllText(path, json); }
                    catch (Exception ex) { LastError = ex; }
                }, TaskScheduler.Default);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _workerTask?.Wait();
            lock (_metadataLock) _metadataTask?.Wait();   // ⭐ CE-3299 B3 — a mark's manifest is never written over the final one
            _outputStream?.Dispose();
            _frontWriter.Dispose();
            _backWriter.Dispose();
            
            try
            {
                _metadata.TotalFrames = RecordedFrames;
                _metadata.Duration = DateTime.UtcNow - _metadata.Timestamp;
                _metadata.MaxNetworkId = MaxNetworkId;

                // Capture the component schema manifest so playback can detect struct drift.
                _metadata.SchemaManifest = BuildSchemaManifest();
                _metadata.EventManifest = BuildEventManifest();

                var json = MetadataSerializer.Serialize(_metadata);
                File.WriteAllText(_filePath + ".meta.json", json);
            }
            catch
            {
                // Best effort metadata write
            }

            _disposed = true;
            
            if (LastError != null)
            {
                throw new IOException("Recorder background worker failed", LastError);
            }
        }

        /// <summary>
        /// Builds a schema manifest from all currently registered recordable component types.
        /// Includes unmanaged struct components and managed class components.
        /// </summary>
        private static Dictionary<int, ComponentSchemaInfo> BuildSchemaManifest()
        {
            var manifest = new Dictionary<int, ComponentSchemaInfo>();

            foreach (var componentId in ComponentTypeRegistry.GetRecordableTypeIds())
            {
                var type = ComponentTypeRegistry.GetType(componentId);
                if (type == null || type.IsEnum) continue;

                try
                {
                    bool isValueType = type.IsValueType;
                    manifest[componentId] = new ComponentSchemaInfo
                    {
                        Name       = type.FullName ?? type.Name,
                        Size       = isValueType ? TypeLayout.SizeOf(type) : 0,   // ⭐ CE-2044 — managed (was Marshal.SizeOf)
                        LayoutHash = isValueType
                            ? ComponentLayoutHasher.ComputeHash(type)
                            : ComponentLayoutHasher.ComputeManagedHash(type),
                        IsManaged  = !isValueType
                    };
                }
                catch
                {
                    // Skip a type the layout cannot be computed for (an open generic, a pointer, …).
                }
            }

            return manifest;
        }

        private static Dictionary<int, ComponentSchemaInfo> BuildEventManifest()
        {
            var manifest = new Dictionary<int, ComponentSchemaInfo>();

            foreach (var type in EventType.GetAllRegistered())
            {
                var attr = type.GetCustomAttribute<EventIdAttribute>();
                if (attr == null || type.IsEnum) continue;
                
                // Enforce the DataPolicy.NoReplay contract.
                var policyAttr = type.GetCustomAttribute<DataPolicyAttribute>();
                if (policyAttr != null && policyAttr.Policy.HasFlag(DataPolicy.NoReplay))
                {
                    continue;
                }

                try
                {
                    bool isValueType = type.IsValueType;
                    manifest[attr.Id] = new ComponentSchemaInfo
                    {
                        Name       = type.FullName ?? type.Name,
                        Size       = isValueType ? TypeLayout.SizeOf(type) : 0,   // ⭐ CE-2044 — managed (was Marshal.SizeOf)
                        LayoutHash = isValueType
                            ? ComponentLayoutHasher.ComputeHash(type)
                            : ComponentLayoutHasher.ComputeManagedHash(type),
                        IsManaged  = !isValueType
                    };
                }
                catch
                {
                    // Skip invalid types or empty marker structs
                }
            }

            return manifest;
        }
    }
}
