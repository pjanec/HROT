using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using FDP.Toolkit.Replay;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Replay.Tests
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
            var module = new ReplayModule(badFilePath, world);

            var registry = new CapturingSystemRegistry();

            // Act + Assert: PlaybackController validates the magic header — throws InvalidDataException.
            Assert.Throws<InvalidDataException>(() => module.RegisterSystems(registry));
        }

        // ── P8T4 success condition 3 — Strategy A: small gap (≤ threshold) ────────

        [Fact]
        public void PlaybackTickSystem_StrategyA_SmallGap_UsesStepForward()
        {
            // Arrange: record a few frames into a temp file.
            var filePath = CreateSmallRecording(frameCount: 10);

            using var world    = new EntityRepository();
            using var playback = new PlaybackController(filePath);
            var sys = new PlaybackTickSystem(playback);

            // Advance 2 frames (≤ threshold of 3) via ExtraFramesThisTick.
            sys.ExtraFramesThisTick = 1; // 1 extra + 1 default = 2 total
            ISimulationView view = world;
            sys.Execute(view, 0.016f);

            // After 2 steps the current frame should be 1 (0-based after starting at -1).
            Assert.Equal(1, playback.CurrentFrame);
        }

        // ── P8T4 success condition 4 — Strategy B: large gap ─────────────────────

        [Fact]
        public void PlaybackTickSystem_StrategyB_LargeGap_UsesSeekToFrame()
        {
            // Arrange: record enough frames to trigger Strategy B.
            var filePath = CreateSmallRecording(frameCount: 10);

            using var world    = new EntityRepository();
            using var playback = new PlaybackController(filePath);
            var sys = new PlaybackTickSystem(playback);

            // Set a gap larger than StrategyBThreshold (3).
            sys.ExtraFramesThisTick = PlaybackTickSystem.StrategyBThreshold + 1; // 4 total
            ISimulationView view = world;
            sys.Execute(view, 0.016f);

            // After a seek, the current frame should be at the expected target
            // (subject to TotalFrames clamp).
            int expected = Math.Min(PlaybackTickSystem.StrategyBThreshold + 1,
                                     playback.TotalFrames - 1);
            Assert.Equal(expected, playback.CurrentFrame);
        }

        // ── P8T4 success condition 5 — SeekToFrameAsync is off main thread ────────

        [Fact]
        public async Task ReplayModule_SeekToFrameAsync_IsOffMainThread()
        {
            // Arrange: valid recording.
            var filePath = CreateSmallRecording(frameCount: 4);
            using var world  = new EntityRepository();
            var module = new ReplayModule(filePath, world);

            var registry = new CapturingSystemRegistry();
            module.RegisterSystems(registry);

            // Act: SeekToFrameAsync should return a Task that is not yet completed
            // (it runs on a background thread).
            var seekTask = module.SeekToFrameAsync(0);

            // Assert: the Task must NOT be completed synchronously — it was genuinely
            // dispatched to a background thread, proving it is off-main-thread.
            Assert.False(seekTask.IsCompleted,
                "SeekToFrameAsync completed synchronously; it must run on a background thread.");

            await seekTask; // completes without throwing

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
                recorder.CaptureFrame(world, (uint)i, DateTime.UtcNow.Ticks, blocking: true);

            // Dispose writes the file footer and .meta.json.
            return filePath;
        }

        private sealed class CapturingSystemRegistry : ISystemRegistry
        {
            public System.Collections.Generic.List<IEcsModuleSystem> Systems { get; } = new();
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);
        }
    }
}
