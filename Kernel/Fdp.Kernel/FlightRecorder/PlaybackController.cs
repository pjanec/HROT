using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Kernel.FlightRecorder.Metadata;

namespace Fdp.Kernel.FlightRecorder
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
        
        public PlaybackController(string filePath)
        {
            // Step 1: Load and validate the schema manifest BEFORE opening the binary stream.
            // This prevents silent memory corruption when struct layouts have changed since recording.
            var metadataPath = filePath + ".meta.json";
            _metadata = LoadMetadata(metadataPath);
            SchemaValidator.Validate(_metadata);

            // Step 2: Open the binary frame stream.
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_fileStream);
            _playback = new PlaybackSystem();

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
            
            if (FormatVersion != FdpConfig.FORMAT_VERSION)
            {
                throw new InvalidDataException(
                    $"Format version mismatch. File version: {FormatVersion}, Expected: {FdpConfig.FORMAT_VERSION}");
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
            Span<byte> headerBytes = stackalloc byte[FrameOuterHeader.Size];

            while (_fileStream.Position < _fileStream.Length)
            {
                try
                {
                    long frameStart = _fileStream.Position;
                    if (_fileStream.Position + FrameOuterHeader.Size > _fileStream.Length) break; // Incomplete header

                    // Read the entire header in one bulk read
                    _fileStream.Read(headerBytes);
                    FrameOuterHeader header = System.Runtime.InteropServices.MemoryMarshal.Read<FrameOuterHeader>(headerBytes);

                    if (header.CompressedSize <= 0) break;

                    _frameIndex.Add(new FrameMetadata
                    {
                        FilePosition = frameStart,
                        CompressedSize = header.CompressedSize,
                        UncompressedSize = header.UncompressedSize,
                        Tick = header.Tick,
                        FrameType = (FrameType)header.FrameType,
                        WallClockTicks = header.WallClockTicks
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
        
        private void ApplyFrame(EntityRepository repo, int frameIndex)
        {
            var metadata = _frameIndex[frameIndex];
            
            // Seek to frame position
            _fileStream.Position = metadata.FilePosition;

            // Skip the structured header to reach the compressed payload
            _fileStream.Position += FrameOuterHeader.Size;

            // Read compressed data
            byte[] compressedData = _reader.ReadBytes(metadata.CompressedSize);
            
            // Decompress
            byte[] rawFrame = new byte[metadata.UncompressedSize];
            K4os.Compression.LZ4.LZ4Codec.Decode(compressedData, 0, metadata.CompressedSize, rawFrame, 0, metadata.UncompressedSize);
            
            // Apply frame
            using (var ms = new MemoryStream(rawFrame))
            using (var frameReader = new BinaryReader(ms))
            {
                _playback.ApplyFrame(repo, frameReader, EventBus);
            }
        }
        
        /// <summary>
        /// Optional EventBus to restore events into during playback.
        /// </summary>
        public FdpEventBus? EventBus { get; set; }
        
        public void Dispose()
        {
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
    }
    
    public enum FrameType : byte
    {
        Delta = 0,
        Keyframe = 1
    }
}
