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
        
        // Pre-allocated write buffer with 4-byte header space (zero-allocation)
        private readonly byte[] _writeBuffer;
        
        private Task? _workerTask;
        private readonly FileStream _outputStream;
        private readonly RecorderSystem _recorderSystem;
        private readonly RecordingMetadata _metadata;
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
        
        public AsyncRecorder(string filePath, RecordingMetadata? metadata = null)
        {
            _filePath = filePath;
            _metadata = metadata ?? new RecordingMetadata();
            if (_metadata.Timestamp == default) _metadata.Timestamp = DateTime.UtcNow;

            _frontBuffer = new byte[BUFFER_SIZE];
            _backBuffer = new byte[BUFFER_SIZE];
            _writeBuffer = new byte[BUFFER_SIZE + 4]; // Pre-allocate for length prefix
            
            // Allocate compression buffer (Worst case size)
            int maxOutput = K4os.Compression.LZ4.LZ4Codec.MaximumOutputSize(BUFFER_SIZE);
            _compressedBuffer = new byte[maxOutput];
            
            _recorderSystem = new RecorderSystem();
            
            // Open file for async I/O
            _outputStream = new FileStream(
                filePath, 
                FileMode.Create, 
                FileAccess.Write, 
                FileShare.None, 
                4096, 
                useAsync: false);
            
            // Write Global Header immediately (See DES-002)
            WriteGlobalHeader();
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
                    _workerTask.Wait();
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
            
            using (var ms = new MemoryStream(_frontBuffer))
            using (var writer = new BinaryWriter(ms))
            {
                // Use the logic from FDP-DES-002
                // Use the logic from FDP-DES-002
                _recorderSystem.RecordDeltaFrame(repo, prevTick, writer, wallClockTicks, eventBus);
                writer.Flush();
                
                bytesWritten = (int)ms.Position;
            }
            
            // Clear the destruction log after recording
            repo.ClearDestructionLog();
            
            // 3. SWAP POINTERS
            var dataToWrite = _frontBuffer;
            var freeBuffer = _backBuffer;
            _frontBuffer = freeBuffer;
            _backBuffer = dataToWrite;
            
            // 4. DISPATCH WORKER
            // Capture 'bytesWritten' by value closure
            _workerTask = Task.Run(() => ProcessBuffer(dataToWrite, bytesWritten));
            
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
            _workerTask?.Wait();
            
            int bytesWritten = 0;
            
            using (var ms = new MemoryStream(_frontBuffer))
            using (var writer = new BinaryWriter(ms))
            {
                _recorderSystem.RecordKeyframe(repo, writer, wallClockTicks, eventBus);
                writer.Flush();
                bytesWritten = (int)ms.Position;
            }
            
            // Clear the destruction log
            repo.ClearDestructionLog();
            
            // Swap and dispatch
            var dataToWrite = _frontBuffer;
            var freeBuffer = _backBuffer;
            _frontBuffer = freeBuffer;
            _backBuffer = dataToWrite;
            
            _workerTask = Task.Run(() => ProcessBuffer(dataToWrite, bytesWritten));
            
            if (blocking)
            {
                _workerTask.Wait();
            }
            
            RecordedFrames++;
        }

        /// <summary>
        /// Runs on ThreadPool - ZERO ALLOCATION (uses pre-allocated buffers)
        /// </summary>
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
                        WallClockTicks = wallClockTicks
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
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _workerTask?.Wait();
            _outputStream?.Dispose();
            
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
                        Size       = isValueType ? Marshal.SizeOf(type) : 0,
                        LayoutHash = isValueType
                            ? ComponentLayoutHasher.ComputeHash(type)
                            : ComponentLayoutHasher.ComputeManagedHash(type),
                        IsManaged  = !isValueType
                    };
                }
                catch
                {
                    // Skip empty marker/tag structs that Marshal.SizeOf cannot handle.
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

                try
                {
                    bool isValueType = type.IsValueType;
                    manifest[attr.Id] = new ComponentSchemaInfo
                    {
                        Name       = type.FullName ?? type.Name,
                        Size       = isValueType ? Marshal.SizeOf(type) : 0,
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
