using System.Collections.Generic;
using System.Numerics;
using FDP.Framework.Runner;
using Xunit;

namespace FDP.Framework.Runner.Tests
{
    /// <summary>
    /// Unit tests for DEM1-F001: Deterministic Mode in SubsystemOrchestrator.
    /// </summary>
    public class DeterministicOrchestratorTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>ISubsystem stub that records every deltaTime passed to Update().</summary>
        private sealed class RecordingSubsystem : ISubsystem
        {
            public string Name => "Recording";
            public Vector4 TitleBarColor => Vector4.Zero;
            public readonly List<float> RecordedDeltas = new();

            public void Initialize(SubsystemConfig config) { }
            public void Update(float deltaTime) => RecordedDeltas.Add(deltaTime);
            public void DrawWorld() { }
            public void DrawUI() { }
            public void Shutdown() { }
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void DeterministicOrchestratorPassesFixedDt_ToSubsystemUpdate()
        {
            // Given: RunnerOptions { Headless=true, Deterministic=true, FixedDeltaSeconds=0.1f }
            var options = new RunnerOptions
            {
                Headless = true,
                Deterministic = true,
                FixedDeltaSeconds = 0.1f
            };
            var recorder = new RecordingSubsystem();
            var orch = new SubsystemOrchestrator(new[] { recorder }, options);

            // When: orchestrator.RunFrames(5)
            orch.Initialize();
            orch.RunFrames(5);
            orch.Shutdown();

            // Then: All 5 recorded dt values == 0.1f (exact float equality)
            Assert.Equal(5, recorder.RecordedDeltas.Count);
            foreach (float dt in recorder.RecordedDeltas)
                Assert.Equal(0.1f, dt);
        }

        [Fact]
        public void NonDeterministicHeadlessOrchestratorPassesZeroDt()
        {
            // Given: RunnerOptions { Headless=true, Deterministic=false }
            var options = new RunnerOptions
            {
                Headless = true,
                Deterministic = false
            };
            var recorder = new RecordingSubsystem();
            var orch = new SubsystemOrchestrator(new[] { recorder }, options);

            // When: orchestrator.RunFrames(3)
            orch.Initialize();
            orch.RunFrames(3);
            orch.Shutdown();

            // Then: All 3 recorded dt values == 0.0f
            Assert.Equal(3, recorder.RecordedDeltas.Count);
            foreach (float dt in recorder.RecordedDeltas)
                Assert.Equal(0.0f, dt);
        }

        [Fact]
        public void SubsystemConfigPropagatesDeterministicFlag()
        {
            // Given: RunnerOptions { Deterministic=true, FixedDeltaSeconds=0.025f }
            var options = new RunnerOptions
            {
                Headless = true,
                Deterministic = true,
                FixedDeltaSeconds = 0.025f
            };

            SubsystemConfig? capturedConfig = null;
            var capturingSubsystem = new CapturingSubsystem(cfg => capturedConfig = cfg);
            var orch = new SubsystemOrchestrator(new[] { capturingSubsystem }, options);

            // When: subsystem.Initialize(config) is called
            orch.Initialize();
            orch.Shutdown();

            // Then: The orchestrator propagates deterministic settings into SubsystemConfig
            Assert.NotNull(capturedConfig);
            Assert.True(capturedConfig!.Deterministic);
            Assert.Equal(0.025f, capturedConfig.FixedDeltaSeconds);
        }

        private sealed class CapturingSubsystem : ISubsystem
        {
            private readonly System.Action<SubsystemConfig> _onInit;
            public string Name => "Capturing";
            public Vector4 TitleBarColor => Vector4.Zero;

            public CapturingSubsystem(System.Action<SubsystemConfig> onInit)
                => _onInit = onInit;

            public void Initialize(SubsystemConfig config) => _onInit(config);
            public void Update(float deltaTime) { }
            public void DrawWorld() { }
            public void DrawUI() { }
            public void Shutdown() { }
        }
    }
}
