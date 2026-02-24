using System.Numerics;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ThreatEvaluationSystem"/>.
    /// Uses the same IModuleSystem test pattern as <see cref="VisionBroadphaseSystemTests"/>:
    /// EntityRepository cast to ISimulationView, ECB flushed and buffers swapped after Execute.
    /// </summary>
    public class ThreatEvaluationSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void ThreatEvaluation_DecaysExistingScore_ByConstantFactor()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            // Seed TargetMemory with a single entry at score 100.
            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem,
                entityId:   42L,
                posX:       10f,
                posY:       20f,
                scoreBoost: 100f,
                tick:       0u);
            world.AddComponent(observer, initMem);

            // Act — 1-second tick; ThreatScoreDecayPerSecond = 0.1 → factor = 0.9
            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            // Assert — score decayed from 100 to 90.
            // decay factor = 1 − (dt × ThreatScoreDecayPerSecond) = 1 − (1.0 × 0.1) = 0.9
            const float expected = 100f * (1f - PerceptionConstants.ThreatScoreDecayPerSecond * 1.0f);
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(expected, resultMem.ThreatScores[0]);
        }
    }
}
