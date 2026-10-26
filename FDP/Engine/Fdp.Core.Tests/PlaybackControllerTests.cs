using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Comprehensive tests for PlaybackController (seeking, fast-forward, rewind).
    /// </summary>
    public class PlaybackControllerTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public PlaybackControllerTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"test_playback_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
        
        private void CreateTestRecording(int frameCount, int keyframeInterval)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 0, Y = 0, Z = 0 });
            
            using var recorder = new AsyncRecorder(_testFilePath);
            uint prevTick = 0;
            
            for (int frame = 0; frame < frameCount; frame++)
            {
                // CRITICAL: Tick FIRST to advance to new version
                repo.Tick();
                uint currentTick = repo.GlobalVersion;
                
                // NOW modify components - tagged with currentTick
                ref var pos = ref repo.GetComponentRW<Position>(entity);
                pos.X = frame;
                pos.Y = frame * 2;
                pos.Z = frame * 3;
                
                if (frame % keyframeInterval == 0)
                {
                    recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                }
                else
                {
                    // Version check: currentTick > prevTick succeeds
                    recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                }
                
                prevTick = currentTick;
            }
        }
        
        // ================================================
        // FRAME INDEXING TESTS
        // ================================================
        
        [Fact]
        public void PlaybackController_BuildsFrameIndex_Correctly()
        {
            // Arrange
            CreateTestRecording(frameCount: 100, keyframeInterval: 10);
            
            // Act
            using var controller = new PlaybackController(_testFilePath);
            
            // Assert
            Assert.Equal(100, controller.TotalFrames);
            Assert.Equal(-1, controller.CurrentFrame); // Not started yet
            Assert.True(controller.IsAtStart);
            Assert.False(controller.IsAtEnd);
        }
        
        [Fact]
        public void PlaybackController_IdentifiesKeyframes_Correctly()
        {
            // Arrange
            CreateTestRecording(frameCount: 50, keyframeInterval: 10);
            
            // Act
            using var controller = new PlaybackController(_testFilePath);
            var keyframes = controller.GetKeyframeIndices();
            
            // Assert
            Assert.Equal(5, keyframes.Count); // Frames 0, 10, 20, 30, 40
            Assert.Contains(0, keyframes);
            Assert.Contains(10, keyframes);
            Assert.Contains(20, keyframes);
            Assert.Contains(30, keyframes);
            Assert.Contains(40, keyframes);
        }
        
        [Fact]
        public void GetFrameMetadata_ReturnsCorrectInfo()
        {
            // Arrange
            CreateTestRecording(frameCount: 10, keyframeInterval: 5);
            
            // Act
            using var controller = new PlaybackController(_testFilePath);
            var frame0 = controller.GetFrameMetadata(0);
            var frame5 = controller.GetFrameMetadata(5);
            
            // Assert
            Assert.Equal(FrameType.Keyframe, frame0.FrameType);
            Assert.Equal(FrameType.Keyframe, frame5.FrameType);
            Assert.True(frame0.CompressedSize > 0);
            Assert.True(frame5.CompressedSize > 0);
        }
        
        // ================================================
        // STEP FORWARD/BACKWARD TESTS
        // ================================================
        
        [Fact]
        public void StepForward_AdvancesFrame_Correctly()
        {
            // Arrange
            CreateTestRecording(frameCount: 10, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act
            bool result1 = controller.StepForward(repo);
            bool result2 = controller.StepForward(repo);
            
            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(1, controller.CurrentFrame);
        }
        
        [Fact]
        public void StepForward_AtEnd_ReturnsFalse()
        {
            // Arrange
            CreateTestRecording(frameCount: 3, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Step to end
            controller.StepForward(repo);
            controller.StepForward(repo);
            controller.StepForward(repo);
            
            bool result = controller.StepForward(repo);
            
            // Assert
            Assert.False(result);
            Assert.True(controller.IsAtEnd);
        }
        
        [Fact]
        public void StepBackward_RewindsToKeyframe_AndReplays()
        {
            // Arrange
            CreateTestRecording(frameCount: 15, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Step forward to frame 7
            for (int i = 0; i < 8; i++)
            {
                controller.StepForward(repo);
            }
            Assert.Equal(7, controller.CurrentFrame);
            
            // Step backward
            bool result = controller.StepBackward(repo);
            
            // Assert
            Assert.True(result);
            Assert.Equal(6, controller.CurrentFrame);
            
            // Verify state is correct
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(6f, pos.X); // Frame 6 position
            }
        }
        
        [Fact]
        public void StepBackward_AtStart_ReturnsFalse()
        {
            // Arrange
            CreateTestRecording(frameCount: 10, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Try to step backward without stepping forward
            bool result = controller.StepBackward(repo);
            
            // Assert
            Assert.False(result);
        }
        
        // ================================================
        // SEEKING TESTS
        // ================================================
        
        [Fact]
        public void SeekToFrame_JumpsToCorrectFrame()
        {
            // Arrange
            CreateTestRecording(frameCount: 50, keyframeInterval: 10);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Seek to frame 25
            controller.SeekToFrame(repo, 25);
            
            // Assert
            Assert.Equal(25, controller.CurrentFrame);
            
            // Verify state
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(25f, pos.X);
                Assert.Equal(50f, pos.Y);
                Assert.Equal(75f, pos.Z);
            }
        }
        
        [Fact]
        public void SeekToFrame_FindsNearestKeyframe_AndReplays()
        {
            // Arrange
            CreateTestRecording(frameCount: 50, keyframeInterval: 10);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Seek to frame 37 (nearest keyframe is 30)
            controller.SeekToFrame(repo, 37);
            
            // Assert
            Assert.Equal(37, controller.CurrentFrame);
            
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(37f, pos.X);
            }
        }
        
        [Fact]
        public void SeekToFrame_OutOfRange_ThrowsException()
        {
            // Arrange
            CreateTestRecording(frameCount: 10, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => controller.SeekToFrame(repo, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => controller.SeekToFrame(repo, -1));
        }
        
        [Fact]
        public void SeekToTick_FindsCorrectFrame()
        {
            // Arrange
            CreateTestRecording(frameCount: 20, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // With the fixed recording order:
            // Frame 0: Tick 2 (after first Tick() call)
            // Frame 1: Tick 3
            // ...
            // Frame 8: Tick 10
            // So tick 10 maps to frame 8
            
            // Act - Seek to tick 10 (maps to frame 8 with new recording order)
            controller.SeekToTick(repo, 10);
            
            // Assert
            Assert.Equal(8, controller.CurrentFrame);
            
            // Verify the position matches frame 8
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(8f, pos.X);
            }
        }
        
        // ================================================
        // REWIND TESTS
        // ================================================
        
        [Fact]
        public void Rewind_ReturnsToStart()
        {
            // Arrange
            CreateTestRecording(frameCount: 20, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Step forward then rewind
            for (int i = 0; i < 10; i++)
            {
                controller.StepForward(repo);
            }
            Assert.Equal(9, controller.CurrentFrame);
            
            controller.Rewind(repo);
            
            // Assert
            Assert.Equal(0, controller.CurrentFrame);
            
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(0f, pos.X);
            }
        }
        
        // ================================================
        // FAST FORWARD TESTS
        // ================================================
        
        [Fact]
        public void FastForward_SkipsFrames_Efficiently()
        {
            // Arrange
            CreateTestRecording(frameCount: 100, keyframeInterval: 10);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Start at frame 0, fast forward 50 frames
            controller.StepForward(repo);
            Assert.Equal(0, controller.CurrentFrame);
            
            controller.FastForward(repo, 50);
            
            // Assert
            Assert.Equal(50, controller.CurrentFrame);
            
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(50f, pos.X);
            }
        }
        
        [Fact]
        public void FastForward_BeyondEnd_StopsAtEnd()
        {
            // Arrange
            CreateTestRecording(frameCount: 10, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act
            controller.StepForward(repo);
            controller.FastForward(repo, 1000);
            
            // Assert
            Assert.Equal(9, controller.CurrentFrame); // Last frame (0-indexed)
            Assert.True(controller.IsAtEnd);
        }
        
        // ================================================
        // PLAY TO END TESTS
        // ================================================
        
        [Fact]
        public void PlayToEnd_PlaysAllFrames()
        {
            // Arrange
            CreateTestRecording(frameCount: 20, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            int progressCallCount = 0;
            
            // Act
            controller.PlayToEnd(repo, (current, total) =>
            {
                progressCallCount++;
                Assert.True(current >= 0 && current < total);
            });
            
            // Assert
            Assert.Equal(19, controller.CurrentFrame);
            Assert.True(controller.IsAtEnd);
            Assert.Equal(20, progressCallCount);
        }
        
        // ================================================
        // COMPLEX SCENARIO TESTS
        // ================================================
        
        [Fact]
        public void ComplexNavigation_SeekForwardBackwardRepeat_MaintainsCorrectState()
        {
            // Arrange
            CreateTestRecording(frameCount: 30, keyframeInterval: 5);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Complex navigation pattern
            controller.SeekToFrame(repo, 10);
            Assert.Equal(10, controller.CurrentFrame);
            
            controller.SeekToFrame(repo, 20);
            Assert.Equal(20, controller.CurrentFrame);
            
            controller.SeekToFrame(repo, 5);
            Assert.Equal(5, controller.CurrentFrame);
            
            controller.FastForward(repo, 10);
            Assert.Equal(15, controller.CurrentFrame);
            
            controller.StepBackward(repo);
            Assert.Equal(14, controller.CurrentFrame);
            
            // Assert - Verify final state
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(14f, pos.X);
                Assert.Equal(28f, pos.Y);
                Assert.Equal(42f, pos.Z);
            }
        }
        
        [Fact]
        public void SeekBetweenKeyframes_MultipleTimes_RestoresCorrectly()
        {
            // Arrange
            CreateTestRecording(frameCount: 50, keyframeInterval: 10);
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Act - Seek to various frames multiple times
            for (int iteration = 0; iteration < 3; iteration++)
            {
                controller.SeekToFrame(repo, 15);
                VerifyPosition(repo, 15);
                
                controller.SeekToFrame(repo, 25);
                VerifyPosition(repo, 25);
                
                controller.SeekToFrame(repo, 5);
                VerifyPosition(repo, 5);
            }
        }
        
        private void VerifyPosition(EntityRepository repo, float expectedX)
        {
            var query = repo.Query().With<Position>().Build();
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Position>(e);
                Assert.Equal(expectedX, pos.X);
                Assert.Equal(expectedX * 2, pos.Y);
                Assert.Equal(expectedX * 3, pos.Z);
            }
        }

        // ── CGF1-S0304 success condition 4 ───────────────────────────────────────

        /// <summary>
        /// Records a 1 000-frame sequence with incrementing wall-clock ticks, seeks to
        /// the midpoint using <see cref="PlaybackController.SeekToWallClockTicks"/>, and
        /// asserts:
        /// <list type="bullet">
        ///   <item>The seek completes in less than 5 ms (binary search — O(log n)).</item>
        ///   <item>The entity state after the seek matches the state recorded at that
        ///   wall-clock tick (floor-seek semantics).</item>
        /// </list>
        /// </summary>
        [Fdp.Testing.FlakyFact("proves binary search by a wall-clock 5 ms threshold")]
        [Trait("Category", "Flaky")]
        public void SeekToWallClockTicks_UsesBinarySearch()
        {
            // Arrange: 1 000 frames, one keyframe every 100 frames.
            const int FrameCount      = 1_000;
            const int KeyInterval     = 100;
            const long TicksPerFrame  = 160_000L; // 16 ms expressed in 100-ns ticks

            using var world = new EntityRepository();
            world.RegisterComponent<Position>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new Position { X = 0, Y = 0, Z = 0 });

            using var recorder = new AsyncRecorder(_testFilePath);
            uint prevTick = 0;
            long wallTicks = 1_000_000L; // arbitrary start

            for (int frame = 0; frame < FrameCount; frame++)
            {
                world.Tick();
                uint currentTick = world.GlobalVersion;

                ref var pos = ref world.GetComponentRW<Position>(entity);
                pos.X = frame;
                pos.Y = frame * 2;
                pos.Z = frame * 3;

                if (frame % KeyInterval == 0)
                    recorder.CaptureKeyframe(world, wallTicks, blocking: true);
                else
                    recorder.CaptureFrame(world, prevTick, wallTicks, blocking: true);

                prevTick  = currentTick;
                wallTicks += TicksPerFrame;
            }
            recorder.Dispose();

            using var controller = new PlaybackController(_testFilePath);
            using var repoSeek   = new EntityRepository();
            repoSeek.RegisterComponent<Position>();

            // Target: midpoint frame.
            int  midFrame     = FrameCount / 2;
            long midWallTicks = 1_000_000L + midFrame * TicksPerFrame;

            // Act: measure seek time.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            controller.SeekToWallClockTicks(repoSeek, midWallTicks);
            sw.Stop();

            // Assert: O(log n) binary search must finish well within 5 ms even on slow CI.
            Assert.True(sw.Elapsed.TotalMilliseconds < 5.0,
                $"SeekToWallClockTicks took {sw.Elapsed.TotalMilliseconds:F2} ms — expected < 5 ms.");

            // Floor-seek: landed at or before the target tick.
            int seekedFrame = controller.CurrentFrame;
            Assert.True(seekedFrame >= 0 && seekedFrame <= midFrame,
                $"CurrentFrame {seekedFrame} must be in [0, {midFrame}].");

            // Entity X matches the frame we landed on.
            var q = repoSeek.Query().With<Position>().Build();
            foreach (var e in q)
            {
                ref readonly var pos = ref repoSeek.GetComponentRO<Position>(e);
                Assert.Equal((float)seekedFrame, pos.X);
            }
        }

        // ================================================
        // ⭐ CE-3329 P0 — the v7 frame index: sim time + flags (docs/DESIGN_Replay_Time_Axis_And_Review.md §5 R1, §8 rows 1–5)
        // ================================================

        /// <summary>Records <paramref name="frames"/> frames: frame i advances the clock unless <paramref name="halted"/>(i), and moves the
        /// entity only when <paramref name="moves"/>(i). The clock is the world's GlobalTime singleton, as the kernel writes it.</summary>
        private void CreateClockedRecording(int frames, Func<int, bool> halted, Func<int, bool> moves)
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position());
            using var recorder = new AsyncRecorder(_testFilePath);
            uint prevTick = 0;
            double sim = 0;
            for (int i = 0; i < frames; i++)
            {
                repo.Tick();
                float dt = halted(i) ? 0f : 0.5f;
                sim += dt;
                repo.SetSingletonUnmanaged(new GlobalTime { TotalTime = sim, DeltaTime = dt, TimeScale = 1f, TotalWallTicks = 1_000 + i });
                if (moves(i)) repo.GetComponentRW<Position>(entity).X = i;
                if (i == 0) recorder.CaptureKeyframe(repo, 1_000 + i, blocking: true);
                else recorder.CaptureFrame(repo, prevTick, 1_000 + i, blocking: true);
                prevTick = repo.GlobalVersion;
            }
        }

        /// <summary>
        /// ⭐⭐ CE-3329 P0 — every frame's index entry carries its RECORDED sim time and its flags: <c>Halted</c> exactly where the clock
        /// did not advance (the clock's own word, R-126), <c>Changed</c> where the frame carried anything besides the clock — so a paused
        /// stretch, and whether anything happened in it, is read without decoding a frame.
        /// </summary>
        [Fact]
        public void V7_TheFrameIndexCarriesTheRecordedSimTimeAndTheFlags()
        {
            // frames 0–3 run · 4–7 paused, nothing moves · 8 paused, the entity moves (a change during a pause) · 9 runs
            CreateClockedRecording(10, halted: i => i is >= 4 and <= 8, moves: i => i < 4 || i == 8 || i == 9);

            using var controller = new PlaybackController(_testFilePath);
            Assert.Equal(FdpConfig.FORMAT_VERSION, controller.FormatVersion);
            Assert.Equal(7u, FdpConfig.FORMAT_VERSION);

            var sims  = new double[10];
            for (int i = 0; i < 10; i++) sims[i] = controller.GetFrameMetadata(i).SimTime;
            Assert.Equal(new[] { 0.5, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.5 }, sims);

            for (int i = 0; i < 10; i++)
            {
                var flags = controller.GetFrameMetadata(i).Flags;
                Assert.Equal(i is >= 4 and <= 8, flags.HasFlag(FrameFlags.Halted));
                Assert.False(flags.HasFlag(FrameFlags.Unknown));
            }
            Assert.True(controller.GetFrameMetadata(0).Flags.HasFlag(FrameFlags.Changed));   // a keyframe
            for (int i = 4; i <= 7; i++)
                Assert.False(controller.GetFrameMetadata(i).Flags.HasFlag(FrameFlags.Changed));   // an unchanged pause
            Assert.True(controller.GetFrameMetadata(8).Flags.HasFlag(FrameFlags.Changed));    // a change inside the pause
        }

        /// <summary>Rewrites a v7 recording as format v6 (25-byte frame headers) — what every recording before CE-3329 is.</summary>
        private static void WriteAsV6(string v7Path, string v6Path)
        {
            var bytes = File.ReadAllBytes(v7Path);
            using var output = new FileStream(v6Path, FileMode.CreateNew);
            var header = bytes.AsSpan(0, RecordingSlice.GlobalHeaderSize).ToArray();
            BitConverter.TryWriteBytes(header.AsSpan(6, 4), 6u);
            output.Write(header);
            int pos = RecordingSlice.GlobalHeaderSize;
            while (pos < bytes.Length)
            {
                int compressed = BitConverter.ToInt32(bytes, pos);
                output.Write(bytes, pos, FrameOuterHeader.SizeV6);
                output.Write(bytes, pos + FrameOuterHeader.Size, compressed);
                pos += FrameOuterHeader.Size + compressed;
            }
        }

        /// <summary>
        /// ⭐⭐ CE-3329 P0 (row 3) — a v6 recording (every one made before this change) still opens, indexes and seeks: same frames,
        /// same wall stamps, same world; its index says it knows nothing about sim time (<see cref="FrameFlags.Unknown"/>, NaN).
        /// </summary>
        [Fact]
        public void V6_ARecordingInTheOldFormat_StillOpensIndexesAndSeeks()
        {
            CreateTestRecording(frameCount: 20, keyframeInterval: 5);
            var v6 = _testFilePath + ".v6.fdp";
            try
            {
                WriteAsV6(_testFilePath, v6);
                using var current = new PlaybackController(_testFilePath);
                using var old = new PlaybackController(v6);

                Assert.Equal(6u, old.FormatVersion);
                Assert.Equal(current.TotalFrames, old.TotalFrames);
                for (int i = 0; i < old.TotalFrames; i++)
                {
                    Assert.Equal(current.GetFrameMetadata(i).WallClockTicks, old.GetFrameMetadata(i).WallClockTicks);
                    Assert.True(double.IsNaN(old.GetFrameMetadata(i).SimTime));
                    Assert.Equal(FrameFlags.Unknown, old.GetFrameMetadata(i).Flags);
                }

                using var repo = new EntityRepository();
                repo.RegisterComponent<Position>();
                old.SeekToFrame(repo, 13);
                var q = repo.Query().With<Position>().Build();
                foreach (var e in q) Assert.Equal(13f, repo.GetComponentRO<Position>(e).X);
            }
            finally { if (File.Exists(v6)) File.Delete(v6); }
        }

        /// <summary>
        /// ⭐ CE-3329 P0 (row 5) — a branch of a v6 parent (D18's history copy) is written in the CURRENT format, so the recorder's v7
        /// frames appended after it never share a file with 25-byte headers.
        /// </summary>
        [Fact]
        public void V6_ABranchCopyOfAnOldRecording_IsWrittenInTheCurrentFormat()
        {
            CreateTestRecording(frameCount: 12, keyframeInterval: 4);
            var v6 = _testFilePath + ".v6.fdp";
            var branch = _testFilePath + ".branch.fdp";
            try
            {
                WriteAsV6(_testFilePath, v6);
                RecordingSlice.CopyFrames(v6, branch, 0, 8);

                using var copy = new PlaybackController(branch);
                Assert.Equal(FdpConfig.FORMAT_VERSION, copy.FormatVersion);
                Assert.Equal(8, copy.TotalFrames);
                Assert.Equal(FrameFlags.Unknown, copy.GetFrameMetadata(7).Flags);
            }
            finally
            {
                if (File.Exists(v6)) File.Delete(v6);
                if (File.Exists(branch)) File.Delete(branch);
            }
        }
    }
}
