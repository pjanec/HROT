using System;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

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
        [Fact]
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
    }
}

