using Fdp.Core;
using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core.FlightRecorder.Metadata;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// Advanced playback controller with seeking, fast-forward, and rewind capabilities.
    /// Maintains frame index for random access.
    /// </summary>
    public class PlaybackController : IDisposable
    {
        private readonly FileStream _fileStream;
        private readonly BinaryReader _reader;
        private readonly PlaybackSystem _playback;
        private readonly List<FrameMetadata> _frameIndex;
        private readonly RecordingMetadata _metadata;

        // Conservative average compressed frame size used to pre-size the frame index.
        // Real frames vary: a small keyframe is ~150 bytes; a dense delta may reach ~400 bytes.
        // 200 bytes is a safe middle ground that avoids repeated List growth on typical recordings.
        private const int EstimatedAvgFrameSize = 200;
        
        private int _currentFrameIndex = -1;
        private long _headerEndPosition;
        
        public uint FormatVersion { get; private set; }
        public long RecordingTimestamp { get; private set; }
        public int TotalFrames => _frameIndex.Count;
        public int CurrentFrame => _currentFrameIndex;
        public bool IsAtEnd => _currentFrameIndex >= _frameIndex.Count - 1;
        public bool IsAtStart => _currentFrameIndex < 0;

        /// <summary>
        /// ⭐ <c>R-4012</c> (Q91 §4-P P3) — how far outside its first and last frame a recording still counts as covering a moment.
        /// 📐 Measured live: nodes present for a whole exercise start recording when their own load hold ends — SimHost 0.7 s after
        /// CGF — and stop a frame or two apart. ⛔ Without the grace, a node present all along LISTENS at the replay's very start.
        /// A late joiner is many seconds out and still listens.
        /// </summary>
        public static readonly long CoverageGraceTicks = 2 * TimeSpan.TicksPerSecond;

        /// <summary>Wall-clock ticks of the first recorded frame, or 0 for an empty recording.</summary>
        public long FirstFrameWallTicks => _frameIndex.Count > 0 ? _frameIndex[0].WallClockTicks : 0;

        /// <summary>Wall-clock ticks of the last recorded frame, or 0 for an empty recording.</summary>
        public long LastFrameWallTicks => _frameIndex.Count > 0 ? _frameIndex[_frameIndex.Count - 1].WallClockTicks : 0;

        /// <summary>
        /// ⭐⭐ <c>R-4012</c> (Q91 §4-P P3) — <b>does this recording cover the moment?</b> Inside
        /// <c>[first frame − <see cref="CoverageGraceTicks"/>, last frame + <see cref="CoverageGraceTicks"/>]</c> (a moment in the
        /// lead-in shows the first frame); <see cref="long.MaxValue"/> means "the end" and is covered by any recording with frames. ⭐ A node asks this before it restores: outside, it restores nothing
        /// and listens. ⛔ <see cref="SeekToWallClockTicks"/> still floors a moment before the first frame to frame 0 — callers
        /// that must not show the first frame for an earlier moment ask here first.
        /// </summary>
        public bool Covers(long wallTicks)
        {
            if (_frameIndex.Count == 0) return false;
            if (wallTicks == long.MaxValue) return true;
            return wallTicks >= FirstFrameWallTicks - CoverageGraceTicks && wallTicks <= LastFrameWallTicks + CoverageGraceTicks;
        }
        
        /// <summary>⭐ CE-1478 P1 — frames the prefetcher keeps decoded ahead of the apply (Q3 of docs/DESIGN_Replay_Playback_Performance.md).</summary>
        public const int DefaultPrefetchDepth = 3;

        private readonly string _filePath;
        private readonly int _prefetchDepth;
        private FramePrefetcher? _prefetcher;

        /// <summary>Frames applied from the prefetcher's decoded buffers (diagnostic; the rest were read inline).</summary>
        public int PrefetchedApplies { get; private set; }
        /// <summary>Frames applied by the inline read + decode (cold start, seek, prefetch off).</summary>
        public int InlineApplies { get; private set; }

        /// <param name="filePath">the recording</param>
        /// <param name="prefetchDepth">⭐ CE-1478 P1 — frames read + decoded ahead on a background thread (started on the first apply, stopped by
        /// <see cref="Dispose"/>); 0 = every frame is read inline on the caller's thread (the pre-P1 behaviour)</param>
        public PlaybackController(string filePath, int prefetchDepth = DefaultPrefetchDepth)
        {
            _filePath = filePath;
            // ⭐ CE-1478 — measuring switches (docs/blueprints/batches/MEASURE_Ui_Replay_Frame_Cost.md §8): HROT_REPLAY_PREFETCH=<depth> (0 = inline), HROT_REPLAY_FULL_REPAIR=1
            if (int.TryParse(EnvVars.Read(EnvVars.HrotReplayPrefetch), out int envDepth)) prefetchDepth = envDepth;
            _prefetchDepth = Math.Max(0, prefetchDepth);
            // Step 1: Load and validate the schema manifest BEFORE opening the binary stream.
            // This prevents silent memory corruption when struct layouts have changed since recording.
            var metadataPath = filePath + ".meta.json";
            _metadata = LoadMetadata(metadataPath);
            // ⭐ CE-4050 — the assemblies that declare the recorded types are loaded BEFORE anything resolves an id: assemblies load lazily,
            //   and a type of one this process has not touched yet was "not found", which stopped playback (the browser, the dumper)
            RecordedAssemblies.EnsureLoaded(_metadata);
            SchemaValidator.Validate(_metadata);

            // Step 2: Open the binary frame stream.
            // ⭐ CE-3329 P3 (M1, row 43, F16) — ReadWrite: a REVIEW reads the recording its own recorder still holds open for writing
            //   (as RecordingSlice already does). ⛔ FileShare.Read means "no one else may write" — Windows refuses it while the
            //   recorder holds the file; Linux .NET only advises, so a Linux test passes either way.
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _reader = new BinaryReader(_fileStream);
            _playback = new PlaybackSystem();
            if (EnvVars.Read(EnvVars.HrotReplayFullRepair) == "1") _playback.ForceFullRepair = true;

            // Pre-size the frame index based on the file length to avoid repeated List growth
            // on large recordings. Capacity is clamped to at least 128 slots.
            long fileLength = new FileInfo(filePath).Length;
            int estimatedFrameCount = Math.Max(128, (int)(fileLength / EstimatedAvgFrameSize));
            _frameIndex = new List<FrameMetadata>(estimatedFrameCount);
            
            ReadGlobalHeader();
            BuildFrameIndex();
        }

        /// <summary>
        /// Loads the recording metadata from the <c>.meta.json</c> companion file.
        /// Returns a default <see cref="RecordingMetadata"/> (null SchemaManifest) when the
        /// file does not exist or cannot be parsed, for backward compatibility with legacy recordings.
        /// </summary>
        private static RecordingMetadata LoadMetadata(string metadataPath)
        {
            if (!File.Exists(metadataPath))
                return new RecordingMetadata(); // Legacy recording — no .meta.json

            try
            {
                var json = File.ReadAllText(metadataPath);
                return MetadataSerializer.Deserialize(json);
            }
            catch
            {
                // Malformed metadata file — fall back to an empty manifest.
                return new RecordingMetadata();
            }
        }
        
        private void ReadGlobalHeader()
        {
            // Read magic
            byte[] magic = _reader.ReadBytes(6);
            string magicStr = System.Text.Encoding.ASCII.GetString(magic);
            
            if (magicStr != "FDPREC")
            {
                throw new InvalidDataException($"Invalid file format. Expected 'FDPREC', got '{magicStr}'");
            }
            
            // Read version
            FormatVersion = _reader.ReadUInt32();
            
            // ⭐ CE-3329 P0 — v6 differs from v7 only in the outer header (FrameOuterHeader.SizeFor)
            if (FormatVersion < FdpConfig.MIN_READABLE_FORMAT_VERSION || FormatVersion > FdpConfig.FORMAT_VERSION)
            {
                throw new InvalidDataException(
                    $"Format version mismatch. File version: {FormatVersion}, Expected: {FdpConfig.MIN_READABLE_FORMAT_VERSION}–{FdpConfig.FORMAT_VERSION}");
            }
            
            // Read timestamp
            RecordingTimestamp = _reader.ReadInt64();
            
            _headerEndPosition = _fileStream.Position;
        }
        
        /// <summary>
        /// Builds an index of all frames in the file for fast seeking.
        /// </summary>
        private void BuildFrameIndex()
        {
            _frameIndex.Clear();
            _fileStream.Position = _headerEndPosition;

            // Pre-allocate the header buffer once outside the loop to avoid CA2014 (stackalloc in loop)
            int headerSize = FrameOuterHeader.SizeFor(FormatVersion);
            Span<byte> headerBytes = stackalloc byte[FrameOuterHeader.Size];
            headerBytes = headerBytes.Slice(0, headerSize);

            while (_fileStream.Position < _fileStream.Length)
            {
                try
                {
                    long frameStart = _fileStream.Position;
                    if (_fileStream.Position + headerSize > _fileStream.Length) break; // Incomplete header

                    // Read the entire header in one bulk read
                    _fileStream.Read(headerBytes);
                    FrameOuterHeader header = FrameOuterHeader.Read(headerBytes, FormatVersion);

                    if (header.CompressedSize <= 0) break;

                    _frameIndex.Add(new FrameMetadata
                    {
                        FilePosition = frameStart,
                        CompressedSize = header.CompressedSize,
                        UncompressedSize = header.UncompressedSize,
                        Tick = header.Tick,
                        FrameType = (FrameType)header.FrameType,
                        WallClockTicks = header.WallClockTicks,
                        SimTime = header.SimTime,
                        Flags = (FrameFlags)header.Flags,
                    });

                    // Skip compressed payload
                    if (_fileStream.Position + header.CompressedSize > _fileStream.Length) break; // Incomplete payload
                    _fileStream.Position += header.CompressedSize;
                }
                catch (EndOfStreamException)
                {
                    break;
                }
            }
        }
        
        /// <summary>
        /// Plays the next frame. Returns false if at end.
        /// </summary>
        public bool StepForward(EntityRepository repo)
        {
            if (IsAtEnd)
                return false;
            
            _currentFrameIndex++;
            ApplyFrame(repo, _currentFrameIndex);
            return true;
        }
        
        /// <summary>
        /// Rewinds to the previous keyframe and replays to the previous frame.
        /// Returns false if at start.
        /// </summary>
        public bool StepBackward(EntityRepository repo)
        {
            if (_currentFrameIndex <= 0)
                return false;
            
            int targetFrame = _currentFrameIndex - 1;
            
            // Find the last keyframe before target
            int keyframeIndex = FindPreviousKeyframe(targetFrame);
            
            // Seek to keyframe and replay to target
            SeekToFrame(repo, targetFrame, keyframeIndex);
            
            return true;
        }
        
        /// <summary>
        /// Seeks to a specific frame index.
        /// Automatically finds the nearest keyframe and replays from there.
        /// </summary>
        public void SeekToFrame(EntityRepository repo, int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= _frameIndex.Count)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            
            int keyframeIndex = FindPreviousKeyframe(frameIndex);
            SeekToFrame(repo, frameIndex, keyframeIndex);
        }
        
        private void SeekToFrame(EntityRepository repo, int targetFrame, int startKeyframe)
        {
            // Apply keyframe
            ApplyFrame(repo, startKeyframe);
            _currentFrameIndex = startKeyframe;
            
            // Replay deltas up to target
            while (_currentFrameIndex < targetFrame)
            {
                _currentFrameIndex++;
                ApplyFrame(repo, _currentFrameIndex);
            }
        }
        
        /// <summary>
        /// Seeks to the frame whose simulation tick is at or immediately after the given target tick.
        /// Uses binary search (O(log N)) over the sorted <c>_frameIndex</c>.
        /// </summary>
        public void SeekToTick(EntityRepository repo, ulong tick)
        {
            if (_frameIndex.Count == 0) return;

            int left = 0;
            int right = _frameIndex.Count - 1;
            int closestFrame = _frameIndex.Count - 1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (_frameIndex[mid].Tick >= tick)
                {
                    closestFrame = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }

            SeekToFrame(repo, closestFrame);
        }

        /// <summary>
        /// Seeks to the frame at or immediately before the given UTC wall-clock ticks.
        /// Uses binary search (O(log N)) — requires the recording invariant that
        /// WallClockTicks is monotonically non-decreasing across frames.
        /// </summary>
        public void SeekToWallClockTicks(EntityRepository repo, long targetWallTicks)
        {
            if (_frameIndex.Count == 0) return;

            int left = 0;
            int right = _frameIndex.Count - 1;
            int closestFrame = -1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                long midTicks = _frameIndex[mid].WallClockTicks;

                if (midTicks == targetWallTicks)
                {
                    closestFrame = mid;
                    break;
                }

                if (midTicks < targetWallTicks)
                {
                    closestFrame = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            if (closestFrame == -1) closestFrame = 0;
            SeekToFrame(repo, closestFrame);
        }
        
        /// <summary>
        /// Rewinds to the start (first keyframe).
        /// </summary>
        public void Rewind(EntityRepository repo)
        {
            if (_frameIndex.Count > 0)
            {
                SeekToFrame(repo, 0);
            }
        }
        
        /// <summary>
        /// Fast-forwards by applying frames without invoking systems.
        /// Useful for quickly reaching a specific point in the recording.
        /// </summary>
        public void FastForward(EntityRepository repo, int frameCount)
        {
            int targetFrame = Math.Min(_currentFrameIndex + frameCount, _frameIndex.Count - 1);
            SeekToFrame(repo, targetFrame);
        }
        
        /// <summary>
        /// Plays all frames from current position to end.
        /// </summary>
        public void PlayToEnd(EntityRepository repo, Action<int, int>? progressCallback = null)
        {
            while (!IsAtEnd)
            {
                StepForward(repo);
                progressCallback?.Invoke(_currentFrameIndex, _frameIndex.Count);
            }
        }
        
        /// <summary>
        /// Gets metadata for a specific frame without applying it.
        /// </summary>
        public FrameMetadata GetFrameMetadata(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= _frameIndex.Count)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            
            return _frameIndex[frameIndex];
        }
        
        /// <summary>
        /// Gets all keyframe indices.
        /// </summary>
        public List<int> GetKeyframeIndices()
        {
            var keyframes = new List<int>();
            for (int i = 0; i < _frameIndex.Count; i++)
            {
                if (_frameIndex[i].FrameType == FrameType.Keyframe)
                {
                    keyframes.Add(i);
                }
            }
            return keyframes;
        }
        
        private int FindPreviousKeyframe(int fromFrame)
        {
            for (int i = fromFrame; i >= 0; i--)
            {
                if (_frameIndex[i].FrameType == FrameType.Keyframe)
                {
                    return i;
                }
            }
            return 0; // Should always find at least one keyframe
        }
        
        /// <summary>Test hook: waits until the worker has frame <paramref name="frameIndex"/> decoded (false on timeout or with prefetch off).</summary>
        internal bool WaitForPrefetch(int frameIndex, int timeoutMs = 10000)
        {
            if (_prefetcher == null) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!_prefetcher.IsReady(frameIndex))
            {
                if (sw.ElapsedMilliseconds > timeoutMs) return false;
                System.Threading.Thread.Sleep(1);
            }
            return true;
        }

        /// <summary>The playback system that applies the frames (its repair statistics and <see cref="PlaybackSystem.ForceFullRepair"/>).</summary>
        public PlaybackSystem Playback => _playback;

        /// <summary>Test hook: the worker, for reading its decoded frames.</summary>
        internal FramePrefetcher? Prefetcher => _prefetcher;

        private static readonly bool StatsOn = EnvVars.Read(EnvVars.HrotReplayStats) == "1";

        private void ApplyFrame(EntityRepository repo, int frameIndex)
        {
            if (!StatsOn) { ApplyFrameCore(repo, frameIndex); return; }
            int pre = PrefetchedApplies;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), a0 = GC.GetAllocatedBytesForCurrentThread();
            ApplyFrameCore(repo, frameIndex);
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            var pb = _playback;
            Console.Error.WriteLine($"[ReplayApply] frame {frameIndex} {(PrefetchedApplies > pre ? "prefetched" : "inline")} total {ms:F1} ms, repair {pb.LastRepairMilliseconds:F1} ms ({(pb.LastRepairWasFull ? "full" : "partial")}, {pb.LastRepairEntitiesVisited} entities; hot {pb.LastTouchedHot} cold {pb.LastTouchedCold} managed {pb.LastTouchedManaged} destroyed {pb.LastDestroyed}), garbage {(GC.GetAllocatedBytesForCurrentThread() - a0) / 1024} KB");
        }

        private void ApplyFrameCore(EntityRepository repo, int frameIndex)
        {
            if (_prefetchDepth > 0)
            {
                // ⭐ CE-1478 P1 — the frame the worker already read + decoded is only applied here; not ready (cold start, a seek) ⇒ the inline path below.
                _prefetcher ??= new FramePrefetcher(_filePath, _frameIndex.ToArray(), FormatVersion, _prefetchDepth);
                if (_prefetcher.TryTake(frameIndex, out var prepared))
                {
                    try
                    {
                        using var pms = new MemoryStream(prepared.Raw, 0, prepared.RawSize, writable: false, publiclyVisible: true);
                        using var preader = new BinaryReader(pms);
                        _playback.ApplyFrame(repo, preader, EventBus);
                    }
                    finally { _prefetcher.Release(prepared); }
                    PrefetchedApplies++;
                    _prefetcher.Want(frameIndex + 1);
                    return;
                }
                _prefetcher.Want(frameIndex + 1);   // aim the worker at what follows while this frame is read inline
            }
            InlineApplies++;
            var metadata = _frameIndex[frameIndex];
            
            // Seek to frame position
            _fileStream.Position = metadata.FilePosition;

            // Skip the structured header to reach the compressed payload
            _fileStream.Position += FrameOuterHeader.SizeFor(FormatVersion);

            // ⭐ CE-4407 (R-2084) — the two buffers are REUSED (grown, never shrunk): a frame of a 1 000-man recording is 15–65 MB uncompressed, and a fresh
            //   `new byte[]` for it every applied frame was 11–65 MB of large-object garbage per frame (gen-2 collections on a 1.2 GB heap).
            int compressedSize = metadata.CompressedSize, rawSize = metadata.UncompressedSize;
            if (_compressedBuffer.Length < compressedSize) _compressedBuffer = new byte[FramePrefetcher.Grown(compressedSize)];   // ⭐ CE-4432 — with headroom
            if (_rawBuffer.Length < rawSize) _rawBuffer = new byte[FramePrefetcher.Grown(rawSize)];
            int got = 0;
            while (got < compressedSize)
            {
                int n = _fileStream.Read(_compressedBuffer, got, compressedSize - got);
                if (n <= 0) throw new EndOfStreamException("The recording ended inside a frame.");
                got += n;
            }

            // Decompress
            K4os.Compression.LZ4.LZ4Codec.Decode(_compressedBuffer, 0, compressedSize, _rawBuffer, 0, rawSize);

            // Apply frame
            using (var ms = new MemoryStream(_rawBuffer, 0, rawSize, writable: false, publiclyVisible: true))
            using (var frameReader = new BinaryReader(ms))
            {
                _playback.ApplyFrame(repo, frameReader, EventBus);
            }
        }

        private byte[] _compressedBuffer = System.Array.Empty<byte>();   // ⭐ CE-4407 — reused by every ApplyFrame
        private byte[] _rawBuffer = System.Array.Empty<byte>();

        /// <summary>
        /// Optional EventBus to restore events into during playback.
        /// </summary>
        public FdpEventBus? EventBus { get; set; }

        /// <summary>
        /// Recording metadata loaded from the companion <c>.meta.json</c> file.
        /// Provides access to fields such as <see cref="Fdp.Core.FlightRecorder.Metadata.RecordingMetadata.MaxNetworkId"/>
        /// for use during replay setup (e.g. ID allocator reset).
        /// </summary>
        public Fdp.Core.FlightRecorder.Metadata.RecordingMetadata Metadata => _metadata;
        
        public void Dispose()
        {
            _prefetcher?.Dispose();
            _reader?.Dispose();
            _fileStream?.Dispose();
        }
    }
    
    /// <summary>
    /// Metadata about a recorded frame.
    /// </summary>
    public struct FrameMetadata
    {
        public long FilePosition;
        public int CompressedSize;
        public int UncompressedSize;
        public ulong Tick;
        public FrameType FrameType;
        public long WallClockTicks;

        /// <summary>⭐ CE-3329 P0 — the frame's recorded sim time (<c>GlobalTime.TotalTime</c>); NaN in a v6 recording.</summary>
        public double SimTime;

        /// <summary>⭐ CE-3329 P0 — <see cref="FrameFlags"/>; <see cref="FrameFlags.Unknown"/> in a v6 recording.</summary>
        public FrameFlags Flags;
    }
    
    public enum FrameType : byte
    {
        Delta = 0,
        Keyframe = 1
    }
}
