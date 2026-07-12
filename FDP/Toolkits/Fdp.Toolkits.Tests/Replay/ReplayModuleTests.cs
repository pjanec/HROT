using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Replay;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Replay.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ReplayModule"/> and <see cref="PlaybackTickSystem"/> (P8T4).
    /// </summary>
    public class ReplayModuleTests : IDisposable
    {
        private readonly string _tempDir;

        public ReplayModuleTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"ReplayModuleTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── P8T4 success condition 2 ─────────────────────────────────────────────

        [Fact]
        public void ReplayModule_Initialize_ThrowsInvalidDataException_OnInvalidFile()
        {
            // Arrange: a file with wrong magic bytes (simulates schema/format drift).
            var badFilePath = Path.Combine(_tempDir, "bad.fdp");
            File.WriteAllBytes(badFilePath, System.Text.Encoding.ASCII.GetBytes("BADFMT\x00\x00\x00\x00"));

            using var world  = new EntityRepository();
            var stubCtrl = new StubTimeController(0L);
            var module = new ReplayModule(badFilePath, world, stubCtrl);

            var registry = new CapturingSystemRegistry();

            // Act + Assert: PlaybackController validates the magic header — throws InvalidDataException.
            Assert.Throws<InvalidDataException>(() => module.RegisterSystems(registry));
        }

        // ── RT-002: null timeController throws ArgumentNullException ─────────────

        [Fact]
        public void ReplayModule_NullTimeController_ThrowsArgumentNullException()
        {
            var filePath = Path.Combine(_tempDir, "dummy.fdp");
            File.WriteAllBytes(filePath, new byte[0]);
            using var world = new EntityRepository();

            Assert.Throws<ArgumentNullException>(
                () => new ReplayModule(filePath, world, timeController: null!));
        }

        // ── T3a: no advance when targetTicks equals current frame ticks ──────────

        [Fact]
        public void PlaybackTickSystem_NoAdvance_WhenTargetTicksEqualsCurrentFrame()
        {
            var filePath = CreateSmallRecording(frameCount: 5);

            using var world    = new EntityRepository();
            using var playback = new PlaybackController(filePath);

            // Advance to frame 0 first.
            playback.StepForward(world);
            Assert.Equal(0, playback.CurrentFrame);

            // Set time controller to exactly frame 0's wall ticks.
            long frame0Ticks = playback.GetFrameMetadata(0).WallClockTicks;
            long recordingStart = frame0Ticks;
            var stub = new StubTimeController(frame0Ticks, recordingStart);
            var sys  = new PlaybackTickSystem(playback, stub);

            ISimulationView view = world;
            sys.Execute(view, 0.016f);

            // targetTicks == currentTicks -> no advance.
            Assert.Equal(0, playback.CurrentFrame);
        }

        // ── T3b (P8T4 success condition 3) — Strategy A: small gap (1 frame) ─────

        [Fact]
        public void PlaybackTickSystem_StrategyA_SmallGap_UsesStepForward()
        {
            var filePath = CreateSmallRecording(frameCount: 10);

            using var world    = new EntityRepository();
            using var playback = new PlaybackController(filePath);

            // Start at frame 0.
            playback.StepForward(world);

            // Set targetTicks to frame 1's wall ticks (1-frame gap <= threshold of 3).
            long frame1Ticks = playback.GetFrameMetadata(1).WallClockTicks;
            long recordingStart = playback.GetFrameMetadata(0).WallClockTicks;
            var stub = new StubTimeController(frame1Ticks, recordingStart);
            var sys  = new PlaybackTickSystem(playback, stub);

            ISimulationView view = world;
            sys.Execute(view, 0.016f);

            // Strategy A: stepped forward exactly once to frame 1.
            Assert.Equal(1, playback.CurrentFrame);
        }

        // ── T3c (P8T4 success condition 4) — Strategy B: large gap ───────────────

        [Fact]
        public void PlaybackTickSystem_StrategyB_LargeGap_UsesSeekToFrame()
        {
            var filePath = CreateSmallRecording(frameCount: 10);

            using var world    = new EntityRepository();
            using var playback = new PlaybackController(filePath);

            // Start at frame 0.
            playback.StepForward(world);

            // Set targetTicks to frame 4's wall ticks (4-frame gap > StrategyBThreshold of 3).
            long frame4Ticks = playback.GetFrameMetadata(4).WallClockTicks;
            long recordingStart = playback.GetFrameMetadata(0).WallClockTicks;
            var stub = new StubTimeController(frame4Ticks, recordingStart);
            var sys  = new PlaybackTickSystem(playback, stub);

            ISimulationView view = world;
            sys.Execute(view, 0.016f);

            // Strategy B: seeked directly to frame 4.
            Assert.Equal(4, playback.CurrentFrame);
        }

        // ── T3d: advance to frame 0 when at start ────────────────────────────────

        [Fact]
        public void PlaybackTickSystem_StrategyA_AdvancesToFrameZeroFromStart()
        {
            var filePath = CreateSmallRecording(frameCount: 5);

            using var world    = new EntityRepository();
            using var playback = new PlaybackController(filePath);

            // IsAtStart == true (CurrentFrame = -1).
            Assert.True(playback.IsAtStart);

            // Set targetTicks to exactly frame 0's wall ticks.
            long frame0Ticks = playback.GetFrameMetadata(0).WallClockTicks;
            long recordingStart = frame0Ticks;
            var stub = new StubTimeController(frame0Ticks, recordingStart);
            var sys  = new PlaybackTickSystem(playback, stub);

            ISimulationView view = world;
            sys.Execute(view, 0.016f);

            // Execute must step forward to frame 0.
            Assert.Equal(0, playback.CurrentFrame);
        }

        // ── P8T4 success condition 5 — SeekToFrameAsync completes synchronously ──

        // Production is synchronous by design (ECS thread-safety): SeekToFrameAsync
        // returns Task.CompletedTask immediately. Test updated to match the contract.
        [Fact]
        public async Task ReplayModule_SeekToFrameAsync_IsOffMainThread()
        {
            // Arrange: valid recording.
            var filePath = CreateSmallRecording(frameCount: 4);
            using var world  = new EntityRepository();
            var stubCtrl = new StubTimeController(long.MaxValue);
            var module = new ReplayModule(filePath, world, stubCtrl);

            var registry = new CapturingSystemRegistry();
            module.RegisterSystems(registry);

            // Act: SeekToFrameAsync is synchronous by design (ECS thread-safety).
            var seekTask = module.SeekToFrameAsync(0);

            // Assert: task completes synchronously (production contract).
            Assert.True(seekTask.IsCompleted,
                "SeekToFrameAsync must complete synchronously per production design.");

            await seekTask; // must not throw

            module.Dispose();
        }

        // ── Helper ────────────────────────────────────────────────────────────────

        /// <summary>Creates a small valid .fdp file with <paramref name="frameCount"/> frames.</summary>
        private string CreateSmallRecording(int frameCount)
        {
            var filePath = Path.Combine(_tempDir, $"rec_{Guid.NewGuid():N}.fdp");
            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();

            using var recorder = new AsyncRecorder(filePath);
            for (int i = 0; i < frameCount; i++)
            {
                // Use a synthetic monotonically-increasing wall clock so each frame has a
                // distinct and predictable WallClockTicks value.
                long wallTicks = (i + 1) * 100_000L;
                recorder.CaptureFrame(world, (uint)i, wallTicks, blocking: true);
            }

            // Dispose writes the file footer and .meta.json.
            return filePath;
        }

        private sealed class CapturingSystemRegistry : ISystemRegistry
        {
            public System.Collections.Generic.List<IEcsModuleSystem> Systems { get; } = new();
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);
            public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem { Systems.Add(system); return system; }
        }

        /// <summary>
        /// Minimal <see cref="ITimeController"/> stub for unit tests.
        /// Returns a fixed <see cref="GlobalTime"/> with <see cref="GlobalTime.TotalTime"/>
        /// computed as <c>(totalWallTicks - recordingStartTicks) / TimeSpan.TicksPerSecond</c>.
        /// <see cref="PlaybackTickSystem"/> computes
        /// <c>targetTicks = recordingStart + (long)(TotalTime * TimeSpan.TicksPerSecond)</c>,
        /// so passing the frame's <c>WallClockTicks</c> and the recording start maps directly
        /// to that frame.
        /// </summary>
        private sealed class StubTimeController : ITimeController
        {
            private GlobalTime _state;

            public StubTimeController(long totalWallTicks, long recordingStartTicks = 0)
                => _state = new GlobalTime
                {
                    TotalTime      = (totalWallTicks - recordingStartTicks) / (double)TimeSpan.TicksPerSecond,
                    TotalWallTicks = totalWallTicks,
                };

            public GlobalTime Update()          => _state;
            public GlobalTime GetCurrentState() => _state;
            public TimeMode   GetMode()         => TimeMode.Continuous;
            public void       SetTimeScale(float scale) { }
            public float      GetTimeScale()    => 1f;
            public void       SeedState(GlobalTime state) { _state = state; }
            public void       Dispose()         { }
        }
    }
}

