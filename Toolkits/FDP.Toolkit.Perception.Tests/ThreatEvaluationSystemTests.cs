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
        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // â”€â”€ Test 1 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // Act â€” 1-second tick; ThreatScoreDecayPerSecond = 0.1 â†’ factor = 0.9
            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            // Assert â€” score decayed from 100 to 90.
            // decay factor = 1 â’ (dt Ă— ThreatScoreDecayPerSecond) = 1 â’ (1.0 Ă— 0.1) = 0.9
            const float expected = 100f * (1f - PerceptionConstants.ThreatScoreDecayPerSecond * 1.0f);
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(expected, resultMem.ThreatScores[0]);
        }

        // â”€â”€ Test 2 (DEBT-013: boost path) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// DEBT-013: Verifies that a <see cref="TargetVisibleEvent"/> causes
        /// <see cref="ThreatEvaluationSystem"/> to boost the score for the confirmed target.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_BoostsScore_OnTargetVisibleEvent()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            // Observer with a TargetMemory already seeded for the target.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(30f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            // Seed TargetMemory with the target at score 0 (just acknowledged, not yet boosted).
            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem,
                entityId:   (long)target.Index,
                posX:       30f,
                posY:       0f,
                scoreBoost: 0f,
                tick:       0u);
            world.AddComponent(observer, initMem);

            // Publish a TargetVisibleEvent confirming the target is visible.
            world.Bus.Publish(new TargetVisibleEvent
            {
                ObserverEntityIndex = observer.Index,
                TargetEntityIndex   = target.Index,
            });
            world.Bus.SwapBuffers(); // move to readable slot

            // Act â€” dt=0 so decay factor = 1.0 (no decay); only the boost is applied.
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert â€” score must be positive (boosted by VisibleTargetScoreBoost = 50).
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.True(resultMem.ThreatScores[0] > 0f,
                "Score should be boosted when a TargetVisibleEvent is received.");
            // The boost is the internal constant 50f; verify it's at least that.
            Assert.True(resultMem.ThreatScores[0] >= 50f,
                "Score boost from a TargetVisibleEvent must be â‰Ą 50.");
        }

        // â”€â”€ Test 3 (DEBT-013: zero-score eviction policy) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// DEBT-013: Documents and verifies the zero-score retention policy of
        /// <see cref="ThreatEvaluationSystem"/>.
        /// <para>
        /// Current policy (Phase 2): scores are decayed each tick but zero-score entries are
        /// <b>retained</b> in <see cref="TargetMemory"/> â€” eviction is not yet implemented.
        /// This test seeds a score of 1.0f and applies a large enough dt to decay it to 0,
        /// then asserts the entry is still present with score 0. A future eviction feature
        /// would change this assertion to <c>Count == 0</c>.
        /// </para>
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_ZeroScoreEntry_IsRetained()
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

            // Seed TargetMemory with score 1.0f.
            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem,
                entityId:   99L,
                posX:       0f,
                posY:       0f,
                scoreBoost: 1.0f,
                tick:       0u);
            world.AddComponent(observer, initMem);

            // Apply dt large enough that decay drives score to â‰¤ 0.
            // decayFactor = 1 - (dt * ThreatScoreDecayPerSecond).
            // With dt = 1/ThreatScoreDecayPerSecond = 10, decayFactor = 0 â†’ score = 0.
            float dt = 1f / PerceptionConstants.ThreatScoreDecayPerSecond; // 10 seconds

            sys.Execute(view, dt);
            FlushEcbAndSwap(view, world);

            // Assert: Phase 2 policy â€” zero-score entry is retained (not evicted).
            // This assertion documents the current behaviour. When eviction is implemented,
            // change to: Assert.Equal(0, resultMem.Count)
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(0f, resultMem.ThreatScores[0]);
        }
    }
}
