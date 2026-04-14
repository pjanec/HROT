using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for WCR-BATCH-01: Frame Header &amp; Metadata Extensions (Phase 1).
    /// Covers tasks WCR-P1-T001, WCR-P1-T002, WCR-P1-T003.
    /// </summary>
    public class WcrBatch01Tests : IDisposable
    {
        private readonly string _testFilePath;

        public WcrBatch01Tests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"wcr_batch01_{Guid.NewGuid()}.fdp");
        }

        public void Dispose()
        {
            TryDelete(_testFilePath);
            TryDelete(_testFilePath + ".meta.json");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ================================================================
        // WCR-P1-T001: FORMAT_VERSION and RecorderSystem frame writers
        // ================================================================

        [Fact]
        public void WCR_P1_T001_FormatVersion_Is3()
        {
            Assert.Equal(3u, FdpConfig.FORMAT_VERSION);
        }

        [Fact]
        public void WCR_P1_T001_RecordDeltaFrame_WritesWallClockTicks()
        {
            // Arrange
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            repo.Tick(); // advance tick so GlobalVersion > 1

            const long knownTicks = 42L;

            // Act
            recorder.RecordDeltaFrame(repo, 0, writer, knownTicks);

            // Assert: layout is [Tick(ulong):8][Type(byte):1][WallClockTicks(long):8][...]
            // WallClockTicks starts at byte offset 9.
            byte[] bytes = stream.ToArray();
            Assert.True(bytes.Length >= 17, $"Expected at least 17 bytes in payload, got {bytes.Length}");

            long writtenTicks = BitConverter.ToInt64(bytes, 9);
            Assert.Equal(knownTicks, writtenTicks);
        }

        [Fact]
        public void WCR_P1_T001_RecordKeyframe_WritesWallClockTicks()
        {
            // Arrange
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            repo.Tick();

            const long knownTicks = 123456789L;

            // Act
            recorder.RecordKeyframe(repo, writer, knownTicks);

            // Assert: WallClockTicks at bytes 9-16 (same layout for keyframes)
            byte[] bytes = stream.ToArray();
            Assert.True(bytes.Length >= 17, $"Expected at least 17 bytes in payload, got {bytes.Length}");

            long writtenTicks = BitConverter.ToInt64(bytes, 9);
            Assert.Equal(knownTicks, writtenTicks);
        }

        // ================================================================
        // WCR-P1-T002: AsyncRecorder outer header extended to 25 bytes
        // ================================================================

        [Fact]
        public void WCR_P1_T002_OuterHeader_WallClockTicks_WrittenCorrectly()
        {
            // Arrange: Record one keyframe (blocking) and capture surrounding timestamps
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 1 });

            long captureTimeBefore = DateTime.UtcNow.Ticks;

            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
            }

            long captureTimeAfter = DateTime.UtcNow.Ticks;

            // Act: Read the outer frame header using the typed struct (no magic numbers)
            using var fs = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read);

            // Skip the global header using its compile-time size
            fs.Position = RecordingGlobalHeader.Size;

            Span<byte> headerBytes = stackalloc byte[FrameOuterHeader.Size];
            fs.Read(headerBytes);
            FrameOuterHeader header = MemoryMarshal.Read<FrameOuterHeader>(headerBytes);

            // Assert
            Assert.True(header.CompressedSize > 0, "CompressedSize must be positive");
            Assert.True(header.UncompressedSize > 0, "UncompressedSize must be positive");

            // WallClockTicks must be within 1 second of the actual capture window
            long oneSecond = TimeSpan.FromSeconds(1).Ticks;
            Assert.True(header.WallClockTicks >= captureTimeBefore - oneSecond,
                $"WallClockTicks {header.WallClockTicks} must be >= capture start {captureTimeBefore} (delta: {captureTimeBefore - header.WallClockTicks} ticks)");
            Assert.True(header.WallClockTicks <= captureTimeAfter + oneSecond,
                $"WallClockTicks {header.WallClockTicks} must be <= capture end {captureTimeAfter} (delta: {header.WallClockTicks - captureTimeAfter} ticks)");
        }

        [Fact]
        public void WCR_P1_T002_WallClockTicks_Monotonically_NonDecreasing()
        {
            // Arrange: Record 3 frames in quick succession
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 0 });

            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: keyframe
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);

                // Frames 1-2: delta frames
                for (int i = 1; i <= 2; i++)
                {
                    repo.Tick();
                    ref var val = ref repo.GetComponentRW<IntComponent>(e);
                    val.Value = i;
                    recorder.CaptureFrame(repo, (uint)(repo.GlobalVersion - 1), DateTime.UtcNow.Ticks, blocking: true);
                }
            }

            // Act: Parse all three outer frame headers using the typed struct (no magic numbers)
            using var fs = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read);

            // Skip global header using its compile-time size
            fs.Position = RecordingGlobalHeader.Size;

            // Pre-allocate the buffer once outside the loop (avoids CA2014 stackalloc-in-loop warning)
            Span<byte> headerBytes = stackalloc byte[FrameOuterHeader.Size];
            long prevTicks = long.MinValue;
            for (int frameIdx = 0; frameIdx < 3; frameIdx++)
            {
                fs.Read(headerBytes);
                FrameOuterHeader header = MemoryMarshal.Read<FrameOuterHeader>(headerBytes);

                Assert.True(header.WallClockTicks >= prevTicks,
                    $"Frame {frameIdx}: WallClockTicks {header.WallClockTicks} must be >= previous {prevTicks}");

                prevTicks = header.WallClockTicks;
                fs.Position += header.CompressedSize; // advance to next frame header
            }
        }

        // ================================================================
        // WCR-P1-T003: PlaybackController reads 25-byte header, populates WallClockTicks
        // ================================================================

        [Fact]
        public void WCR_P1_T003_BuildFrameIndex_PopulatesWallClockTicks()
        {
            // Arrange: Record keyframe + 2 delta frames
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            var e = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e, new IntComponent { Value = 0 });

            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                sourceRepo.Tick();
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);

                for (int i = 1; i <= 2; i++)
                {
                    sourceRepo.Tick();
                    ref var val = ref sourceRepo.GetComponentRW<IntComponent>(e);
                    val.Value = i;
                    recorder.CaptureFrame(sourceRepo, (uint)(sourceRepo.GlobalVersion - 1), DateTime.UtcNow.Ticks, blocking: true);
                }
            }

            // Act
            using var playback = new PlaybackController(_testFilePath);

            // Assert
            Assert.Equal(3, playback.TotalFrames);

            Assert.True(playback.GetFrameMetadata(0).WallClockTicks > 0,
                "Frame 0 WallClockTicks must be non-zero");
            Assert.True(playback.GetFrameMetadata(1).WallClockTicks >= playback.GetFrameMetadata(0).WallClockTicks,
                "Frame 1 WallClockTicks must be >= Frame 0 WallClockTicks");
            Assert.True(playback.GetFrameMetadata(2).WallClockTicks >= playback.GetFrameMetadata(1).WallClockTicks,
                "Frame 2 WallClockTicks must be >= Frame 1 WallClockTicks");
        }

        [Fact]
        public void WCR_P1_T003_StepForward_AppliesAllFrames_NoException()
        {
            // Arrange: Record keyframe + 2 delta frames
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            var e = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e, new IntComponent { Value = 0 });

            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                sourceRepo.Tick();
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);

                for (int i = 1; i <= 2; i++)
                {
                    sourceRepo.Tick();
                    ref var val = ref sourceRepo.GetComponentRW<IntComponent>(e);
                    val.Value = i;
                    recorder.CaptureFrame(sourceRepo, (uint)(sourceRepo.GlobalVersion - 1), DateTime.UtcNow.Ticks, blocking: true);
                }
            }

            // Act: Open with PlaybackController, step through all frames
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();

            using var playback = new PlaybackController(_testFilePath);

            var exception = Record.Exception(() =>
            {
                playback.StepForward(targetRepo); // frame 0 (keyframe)
                playback.StepForward(targetRepo); // frame 1 (delta)
                playback.StepForward(targetRepo); // frame 2 (delta)
            });

            // Assert
            Assert.Null(exception);
            Assert.Equal(2, playback.CurrentFrame);
        }

        /// <summary>
        /// Integration test: full record → playback round-trip verifying WallClockTicks is
        /// populated in FrameMetadata and is within a reasonable window of the test run time.
        /// </summary>
        [Fact]
        public void WCR_P1_T003_RoundTrip_WallClockTicks()
        {
            // Arrange
            long testStartTicks = DateTime.UtcNow.Ticks;

            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();

            var e1 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 10 });
            var e2 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e2, new IntComponent { Value = 20 });

            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: keyframe
                sourceRepo.Tick();
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 1: delta – modify e1
                sourceRepo.Tick();
                ref var v1 = ref sourceRepo.GetComponentRW<IntComponent>(e1);
                v1.Value = 11;
                recorder.CaptureFrame(sourceRepo, (uint)(sourceRepo.GlobalVersion - 1), DateTime.UtcNow.Ticks, blocking: true);

                // Frame 2: delta – modify e2
                sourceRepo.Tick();
                ref var v2 = ref sourceRepo.GetComponentRW<IntComponent>(e2);
                v2.Value = 21;
                recorder.CaptureFrame(sourceRepo, (uint)(sourceRepo.GlobalVersion - 1), DateTime.UtcNow.Ticks, blocking: true);
            }

            long testEndTicks = DateTime.UtcNow.Ticks;

            // Act: Open with PlaybackController and replay to end
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();

            using var playback = new PlaybackController(_testFilePath);
            playback.PlayToEnd(targetRepo);

            // Assert: WallClockTicks for every frame is within 10 seconds of the test window
            long tenSeconds = TimeSpan.FromSeconds(10).Ticks;
            Assert.Equal(3, playback.TotalFrames);

            for (int i = 0; i < playback.TotalFrames; i++)
            {
                long wallTicks = playback.GetFrameMetadata(i).WallClockTicks;
                Assert.True(wallTicks >= testStartTicks - tenSeconds,
                    $"Frame {i}: WallClockTicks {wallTicks} must be within 10s of test start {testStartTicks}");
                Assert.True(wallTicks <= testEndTicks + tenSeconds,
                    $"Frame {i}: WallClockTicks {wallTicks} must be within 10s of test end {testEndTicks}");
            }

            // Also verify entity state was correctly replayed (proves full round-trip integrity)
            Assert.Equal(2, targetRepo.EntityCount);
            Assert.Equal(21, targetRepo.GetComponentRO<IntComponent>(e2).Value);
        }
    }
}
