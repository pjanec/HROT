using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for WCR-BATCH-02: Binary Search Seeking (Phase 2).
    /// Covers tasks WCR-P2-T001 (SeekToWallClockTicks) and WCR-P2-T002 (SeekToTick refactor).
    /// </summary>
    public class WcrBatch02Tests : IDisposable
    {
        private readonly string _testFilePath;

        public WcrBatch02Tests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"wcr_batch02_{Guid.NewGuid()}.fdp");
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

        /// <summary>
        /// Creates a recording with 5 all-keyframe frames, each preceded by 10 repo ticks
        /// so that <c>FrameMetadata.Tick</c> values are [10, 20, 30, 40, 50] and
        /// <c>FrameMetadata.WallClockTicks</c> are [100, 200, 300, 400, 500].
        /// </summary>
        private void CreateSyntheticRecording()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new IntComponent { Value = 0 });

            using var recorder = new AsyncRecorder(_testFilePath);

            for (int frame = 0; frame < 5; frame++)
            {
                // Advance 10 repo ticks so FrameMetadata.Tick = (frame+1)*10
                for (int j = 0; j < 10; j++) repo.Tick();

                long wallTicks = (frame + 1) * 100L; // 100, 200, 300, 400, 500
                recorder.CaptureKeyframe(repo, wallTicks, blocking: true);
            }
        }

        /// <summary>
        /// Creates a minimal .fdp file containing only the global header and no frames.
        /// PlaybackController will build an empty _frameIndex from this file.
        /// </summary>
        private void CreateEmptyRecording()
        {
            using var fs = new FileStream(_testFilePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // Magic: "FDPREC"
            bw.Write(System.Text.Encoding.ASCII.GetBytes("FDPREC"));
            // FormatVersion
            bw.Write(FdpConfig.FORMAT_VERSION);
            // Timestamp
            bw.Write(DateTime.UtcNow.Ticks);
        }

        // ================================================================
        // WCR-P2-T001: SeekToWallClockTicks — binary search (floor seek)
        // ================================================================

        [Fact]
        public void WCR_P2_T001_SeekToWallClockTicks_ExactMatch()
        {
            // Arrange: recording with WallClockTicks [100, 200, 300, 400, 500]
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            // Pre-condition check
            Assert.Equal(300L, controller.GetFrameMetadata(2).WallClockTicks);

            // Act: seek to exact ticks of frame 2
            controller.SeekToWallClockTicks(repo, 300L);

            // Assert: lands on frame 2 (index of ticks==300)
            Assert.Equal(2, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T001_SeekToWallClockTicks_BetweenFrames()
        {
            // Arrange: recording with WallClockTicks [100, 200, 300, 400, 500]
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            // Act: seek to 250 (between frame 1 ticks=200 and frame 2 ticks=300)
            controller.SeekToWallClockTicks(repo, 250L);

            // Assert: floor seek → frame 1 (last frame with ticks <= 250 is index 1, ticks=200)
            Assert.Equal(1, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T001_SeekToWallClockTicks_BeforeFirst()
        {
            // Arrange: recording with WallClockTicks [100, 200, 300, 400, 500]
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            // Act: seek to 50, which is before the first frame's ticks (100)
            controller.SeekToWallClockTicks(repo, 50L);

            // Assert: clamps to frame 0
            Assert.Equal(0, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T001_SeekToWallClockTicks_AfterLast()
        {
            // Arrange: recording with WallClockTicks [100, 200, 300, 400, 500]
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            // Act: seek to 999 (beyond all frames)
            controller.SeekToWallClockTicks(repo, 999L);

            // Assert: lands on last frame (index 4)
            Assert.Equal(4, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T001_SeekToWallClockTicks_EmptyIndex_NoException()
        {
            // Arrange: empty physical file (global header only) → empty _frameIndex
            CreateEmptyRecording();
            using var repo = new EntityRepository();
            using var controller = new PlaybackController(_testFilePath);
            Assert.Equal(0, controller.TotalFrames);

            // Act & Assert: no exception on empty index
            var ex = Record.Exception(() => controller.SeekToWallClockTicks(repo, 100L));
            Assert.Null(ex);
        }

        // ================================================================
        // WCR-P2-T002: SeekToTick — binary search (ceiling seek)
        // ================================================================

        [Fact]
        public void WCR_P2_T002_SeekToTick_ExactMatch()
        {
            // Arrange: recording with FrameMetadata.Tick values spaced 10 apart
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            // Use the actual tick of frame 2 (avoids hardcoding GlobalVersion start value)
            ulong tick2 = controller.GetFrameMetadata(2).Tick;

            // Act: seek to exact tick of frame 2
            controller.SeekToTick(repo, tick2);

            // Assert: lands on frame 2
            Assert.Equal(2, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T002_SeekToTick_BetweenTicks()
        {
            // Arrange: recording with FrameMetadata.Tick values spaced 10 apart
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            ulong tick1 = controller.GetFrameMetadata(1).Tick; // e.g. 21
            ulong tick2 = controller.GetFrameMetadata(2).Tick; // e.g. 31
            // A value strictly between frame 1 and frame 2 ticks (they are 10 apart)
            ulong betweenTick = tick1 + 1;

            // Act: seek to a tick between frame 1 and frame 2
            // SeekToTick is a ceiling seek: first frame with tick >= betweenTick is frame 2
            controller.SeekToTick(repo, betweenTick);

            // Assert: lands on frame 2 (first frame whose tick >= betweenTick)
            Assert.Equal(2, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T002_SeekToTick_BeforeFirst()
        {
            // Arrange: recording with FrameMetadata.Tick values spaced 10 apart
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            ulong tick0 = controller.GetFrameMetadata(0).Tick;
            // A tick value below the first frame's tick
            ulong beforeFirst = tick0 > 0 ? tick0 - 1 : 0;

            // Act: seek to a tick before all frames
            controller.SeekToTick(repo, beforeFirst);

            // Assert: lands on frame 0 (first frame with tick >= beforeFirst)
            Assert.Equal(0, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T002_SeekToTick_AfterLast()
        {
            // Arrange: recording with FrameMetadata.Tick values [10, 20, 30, 40, 50]
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            // Act: seek to tick 100 (beyond all frames — no frame has tick >= 100)
            controller.SeekToTick(repo, 100UL);

            // Assert: clamps to last frame (index 4)
            Assert.Equal(4, controller.CurrentFrame);
        }

        [Fact]
        public void WCR_P2_T002_SeekToTick_EmptyIndex_NoException()
        {
            // Arrange: empty index
            CreateEmptyRecording();
            using var repo = new EntityRepository();
            using var controller = new PlaybackController(_testFilePath);
            Assert.Equal(0, controller.TotalFrames);

            // Act & Assert: no exception
            var ex = Record.Exception(() => controller.SeekToTick(repo, 30UL));
            Assert.Null(ex);
        }

        [Fact]
        public void WCR_P2_T002_SeekToTick_BehaviorUnchanged()
        {
            // Regression test: verify binary search produces the same result as the
            // reference linear scan (old O(N) algorithm) for every possible tick value.
            CreateSyntheticRecording();
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            using var controller = new PlaybackController(_testFilePath);

            int totalFrames = controller.TotalFrames;
            var ticks = new ulong[totalFrames];
            for (int i = 0; i < totalFrames; i++)
                ticks[i] = controller.GetFrameMetadata(i).Tick;

            // Reference implementation: original O(N) linear scan (ceiling seek)
            int LinearScan(ulong targetTick)
            {
                for (int i = 0; i < totalFrames; i++)
                    if (ticks[i] >= targetTick)
                        return i;
                return totalFrames - 1;
            }

            // Test various tick values: below first, exact matches, between values, above last
            ulong[] testValues = { 0UL, ticks[0], ticks[0] + 1, ticks[2], ticks[2] - 1,
                                   ticks[totalFrames - 1], ticks[totalFrames - 1] + 1000UL };

            foreach (var targetTick in testValues)
            {
                int expectedFrame = LinearScan(targetTick);
                controller.SeekToTick(repo, targetTick);
                int binaryResult = controller.CurrentFrame;
                Assert.True(expectedFrame == binaryResult,
                    $"Mismatch for tick={targetTick}: binary search={binaryResult}, linear={expectedFrame}");
            }
        }
    }
}
