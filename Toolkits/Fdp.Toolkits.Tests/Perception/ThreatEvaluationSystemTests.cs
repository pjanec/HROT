using System;
using System.Numerics;
using CarKinem.Spatial;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.ModuleHost.Abstractions;
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
                entityId:   (long)target.PackedValue,
                posX:       30f,
                posY:       0f,
                scoreBoost: 0f,
                tick:       0u);
            world.AddComponent(observer, initMem);

            // Publish a TargetVisibleEvent confirming the target is visible.
            world.Bus.Publish(new TargetVisibleEvent
            {
                Observer = observer,
                Target   = target,
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
        /// DEBT-013 / DEBT-015: Documents and verifies the zero-score retention policy of
        /// <see cref="ThreatEvaluationSystem"/>.
        /// <para>
        /// Current policy (Phase 2): scores are decayed each tick but zero-score entries are
        /// <b>retained</b> in <see cref="TargetMemory"/> — eviction is not yet implemented.
        /// This test seeds a score of 1.0f and applies a large enough dt to decay it to 0,
        /// then asserts the entry is still present with score 0. A future eviction feature
        /// would change this assertion to <c>Count == 0</c>.
        /// </para>
        /// <para>
        /// <b>Policy source (DEBT-015):</b> DESIGN.md §4.3 describes
        /// <see cref="ThreatEvaluationSystem"/> as "Decays scores, integrates
        /// TargetVisibleEvent + AudioStimulusEvent; writes back via ECB." No eviction step is
        /// specified — the design is silent on zero-score removal. Therefore the retention
        /// behaviour is correct for Phase 2. When eviction is added in a future phase, rename
        /// this test to <c>ThreatEvaluation_ZeroScoreEntry_IsEvicted</c> and invert the assertion.
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
        // ── Test 4 (BATCH-18: recycled observer guard) ────────────────────────────

        /// <summary>
        /// BATCH-18 / DEBT-027: When the observer entity is destroyed before
        /// <see cref="ThreatEvaluationSystem"/> consumes the <see cref="TargetVisibleEvent"/>,
        /// the system must skip the event without throwing.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluationSystem_SkipsEvent_WhenObserverRecycled()
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
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(10f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            // Publish TargetVisibleEvent using full Entity handles, then destroy the observer.
            world.Bus.Publish(new TargetVisibleEvent { Observer = observer, Target = target });
            world.Bus.SwapBuffers();

            world.DestroyEntity(observer); // recycling happens before consume

            // Act — must not throw; IsAlive guard should skip the stale event.
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert: observer is gone, no crash occurred.
            Assert.False(world.IsAlive(observer));
        }

        // ── Test 5 (BATCH-18: recycled target guard) ─────────────────────────────

        /// <summary>
        /// BATCH-18 / DEBT-027: When the target entity is destroyed before
        /// <see cref="ThreatEvaluationSystem"/> consumes the <see cref="TargetVisibleEvent"/>,
        /// the system must skip the event. The observer's <see cref="TargetMemory"/> must be
        /// unchanged (no boost applied for a stale target).
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluationSystem_SkipsEvent_WhenTargetRecycled()
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
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(10f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            // Publish TargetVisibleEvent using full Entity handles, then destroy the target.
            world.Bus.Publish(new TargetVisibleEvent { Observer = observer, Target = target });
            world.Bus.SwapBuffers();

            world.DestroyEntity(target); // recycling happens before consume

            // Act — must not throw; IsAlive guard skips the stale event.
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert: TargetMemory unchanged — no boost was applied for the stale target.
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(0, resultMem.Count);
        }

        // ── Test 6 (BATCH-18: happy path with Entity handles) ─────────────────────

        /// <summary>
        /// BATCH-18 / DEBT-027: Happy path — both observer and target are alive.
        /// <see cref="ThreatEvaluationSystem"/> must boost the observer's
        /// <see cref="TargetMemory"/> entry for the target using full Entity handles.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluationSystem_UpdatesThreatMemory_WhenBothAlive()
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
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(30f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            world.Bus.Publish(new TargetVisibleEvent { Observer = observer, Target = target });
            world.Bus.SwapBuffers();

            // Act — dt=0 so no decay, only the boost is applied.
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert: one entry created in TargetMemory with the expected packed entity id and score.
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.True(resultMem.ThreatScores[0] >= 50f,
                "Score should be boosted by at least VisibleTargetScoreBoost (50).");
            Assert.Equal((long)target.PackedValue, resultMem.EntityIds[0]);
        }

        // ── Test 7 (BATCH-18: LosCheckRequestEvent carries full Entity handle) ────

        /// <summary>
        /// BATCH-18 / DEBT-027: Verifies that <see cref="VisionBroadphaseSystem"/> emits a
        /// <see cref="LosCheckRequestEvent"/> carrying full <see cref="Entity"/> handles
        /// (Index + Generation), not raw int indices. The generation of the emitted event's
        /// Observer field must match the observer entity's generation at creation time.
        /// </summary>
        [Fact]
        public unsafe void LosCheckRequestEvent_CarriesFullEntityHandle_NotRawIndex()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;

            // Use the real grid factory from the broadphase test helpers.
            var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            var sys  = new VisionBroadphaseSystem(grid);

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // facing east
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos(30°) — 60° full FOV
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new Faction { FactionId = 2 });

            grid.Clear();
            grid.Add(target, new Vector2(100f, 0f));

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert: the emitted event carries full Entity handles matching the live entities.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observer, events[0].Observer);
            Assert.Equal(target,   events[0].Target);
            // Generation must be non-zero (a valid, live entity — not a null/default Entity).
            Assert.NotEqual(0, events[0].Observer.Generation);
            Assert.NotEqual(0, events[0].Target.Generation);

            grid.Dispose();
        }

        // ── Test 8 (PACK-A001: TargetHeardEvent boost) ────────────────────────────

        /// <summary>
        /// PACK-A001 SC-4: Publishing a <see cref="TargetHeardEvent"/> and ticking
        /// <see cref="ThreatEvaluationSystem"/> must produce a non-zero <see cref="TargetMemory"/>
        /// entry for the listener keyed on <see cref="TargetHeardEvent.SourceEntityIndex"/>.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_BoostsScore_OnTargetHeardEvent()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var listener = world.CreateEntity();
            world.AddComponent(listener, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(listener, new TargetMemory());

            const int sourceEntityIndex = 42;

            // Publish TargetHeardEvent — simulates AudioPerceptionSystem output.
            world.Bus.Publish(new TargetHeardEvent
            {
                Listener          = listener,
                SourceEntityIndex = sourceEntityIndex,
                Origin            = new Vector3(10f, 20f, 0f),
            });
            world.Bus.SwapBuffers(); // move to readable slot

            // Act — dt=0 so no decay; only the heard-event boost is applied.
            sys.Execute(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert — TargetMemory of listener has a non-zero entry for SourceEntityIndex.
            var resultMem = world.GetComponent<TargetMemory>(listener);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal((long)sourceEntityIndex, resultMem.EntityIds[0]);
            Assert.True(resultMem.ThreatScores[0] > 0f,
                "Score must be boosted when a TargetHeardEvent is consumed.");
        }
    }
}
