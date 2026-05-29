using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ThreatEvaluationSystem"/>.
    /// Uses the IModuleSystem test pattern: EntityRepository cast to ISimulationView,
    /// ECB flushed and bus swapped after Execute.
    ///
    /// <para>
    /// Since the architectural refactor (CQRS sensor pipeline), <see cref="ThreatEvaluationSystem"/>
    /// reads <see cref="ActiveSensorTracks"/> (Brain cognitive buffer written by
    /// <c>SensorTrackStateIngressTranslator</c>) instead of <see cref="TargetVisibleEvent"/>.
    /// </para>
    /// </summary>
    public class ThreatEvaluationSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // ── Test 1: decay ─────────────────────────────────────────────────────────────

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

            // Act - 1-second tick; ThreatScoreDecayPerSecond = 0.1 -> factor = 0.9
            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            // Assert - score decayed from 100 to 90.
            const float expected = 100f * (1f - PerceptionConstants.ThreatScoreDecayPerSecond * 1.0f);
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(expected, resultMem.ThreatScores[0]);
        }

        // ── Test 2: ActiveSensorTracks boost ─────────────────────────────────────────

        /// <summary>
        /// Verifies that an <see cref="ActiveSensorTracks"/> buffer causes
        /// <see cref="ThreatEvaluationSystem"/> to boost the threat score.
        /// Boost = 50 * deltaTime per active track per second.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_BoostsScore_FromActiveSensorTracks()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            const long targetEntityId = 12345L;

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(observer, new TargetMemory());

            // Add an ActiveSensorTracks buffer with one acquired track.
            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0]  = targetEntityId;
            tracks.PositionsX[0] = 30f;
            tracks.PositionsY[0] = 0f;
            tracks.Count = 1;
            world.AddComponent(observer, tracks);

            // Act - 1 second tick so boost = 50 * 1.0 = 50.
            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            // Assert - TargetMemory should have one entry with a positive score.
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(targetEntityId, resultMem.EntityIds[0]);
            Assert.True(resultMem.ThreatScores[0] > 0f,
                "Score must be boosted when ActiveSensorTracks has acquired targets.");
            Assert.True(resultMem.ThreatScores[0] >= 49f,
                "Boost rate must be approximately 50 * deltaTime per second.");
        }

        // ── Test 3: zero-score retention policy ──────────────────────────────────────

        /// <summary>
        /// Phase 2 policy: zero-score entries are retained (not evicted).
        /// When eviction is added in a future phase, change assertion to Count == 0.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_ZeroScoreEntry_IsRetained()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: 99L, posX: 0f, posY: 0f, scoreBoost: 1.0f, tick: 0u);
            world.AddComponent(observer, initMem);

            // Apply dt large enough to drive score to 0.
            float dt = 1f / PerceptionConstants.ThreatScoreDecayPerSecond; // 10 seconds

            sys.Execute(view, dt);
            FlushEcbAndSwap(view, world);

            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(0f, resultMem.ThreatScores[0]);
        }

        // ── Test 4: no crash with no TargetMemory entities ───────────────────────────

        [Fact]
        public void ThreatEvaluation_DoesNotCrash_WithNoTargetMemoryEntities()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var ex = Record.Exception(() =>
            {
                sys.Execute(view, 1.0f);
                FlushEcbAndSwap(view, world);
            });

            Assert.Null(ex);
        }

        // ── Test 5: decay only when no ActiveSensorTracks ────────────────────────────

        /// <summary>
        /// When an entity has <see cref="TargetMemory"/> but no <see cref="ActiveSensorTracks"/>,
        /// only decay is applied (no boost).
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_OnlyDecays_WhenNoActiveTracks()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: 7L, posX: 1f, posY: 1f, scoreBoost: 100f, tick: 0u);
            world.AddComponent(observer, initMem);

            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            const float expected = 100f * (1f - PerceptionConstants.ThreatScoreDecayPerSecond * 1.0f);
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(expected, resultMem.ThreatScores[0]);
        }

        // ── Test 6: decay and boost in one frame ─────────────────────────────────────

        /// <summary>
        /// Entity has both <see cref="TargetMemory"/> (with existing entry) and
        /// <see cref="ActiveSensorTracks"/> (matching that entry).
        /// System must apply decay and then boost in the same frame.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_DecaysAndBoosts_WhenBothPresent()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            const long targetId = 999L;

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: targetId, posX: 5f, posY: 5f, scoreBoost: 100f, tick: 0u);
            world.AddComponent(observer, initMem);

            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0]  = targetId;
            tracks.PositionsX[0] = 5f;
            tracks.PositionsY[0] = 5f;
            tracks.Count = 1;
            world.AddComponent(observer, tracks);

            // dt=1s: decay factor=0.9, boost+=50*1=50 -> result = 100*0.9 + 50 = 140.
            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(targetId, resultMem.EntityIds[0]);
            Assert.True(resultMem.ThreatScores[0] > 90f,
                "Score must exceed the decay-only value (90) when ActiveSensorTracks is present.");
        }

        // ── Test 6b (P3D-206): boost records the live target's authoritative altitude ──

        /// <summary>
        /// When a live replica of the tracked target exists, <see cref="ThreatEvaluationSystem"/>
        /// must record that target's authoritative <c>SimTransform.Position.Z</c> into the
        /// observer's <see cref="TargetMemory.PositionsZ"/> slot.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_RecordsLiveTargetAltitude_IntoPositionsZ()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            // Live target at altitude 12.5 m.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform { Position = new Vector3(5f, 5f, 12.5f), Rotation = Quaternion.Identity });

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(observer, new TargetMemory());

            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0]  = (long)target.PackedValue;
            tracks.PositionsX[0] = 5f;
            tracks.PositionsY[0] = 5f;
            tracks.Count = 1;
            world.AddComponent(observer, tracks);

            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal((long)target.PackedValue, resultMem.EntityIds[0]);
            Assert.Equal(12.5f, resultMem.PositionsZ[0]); // authoritative altitude recorded
        }

        // ── Test 7 (VisionBroadphaseSystem carries full Entity handle) ───────────────

        /// <summary>
        /// Verifies that <see cref="VisionBroadphaseSystem"/> emits a
        /// <see cref="LosCheckRequestEvent"/> carrying full <see cref="Entity"/> handles
        /// (Index + Generation), not raw int indices.
        /// </summary>
        [Fact]
        public unsafe void LosCheckRequestEvent_CarriesFullEntityHandle_NotRawIndex()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;

            var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            var sys  = new VisionBroadphaseSystem(grid);

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new EntityInfo    { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f),
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });

            grid.Clear();
            grid.Add(target, new Vector2(100f, 0f));

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert
            var events = world.Bus.Read<LosCheckRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observer, events[0].Observer);
            Assert.Equal(target,   events[0].Target);
            Assert.NotEqual(0, events[0].Observer.Generation);
            Assert.NotEqual(0, events[0].Target.Generation);

            grid.Dispose();
        }

        // ── Test 8: multiple active tracks all boost ─────────────────────────────────

        /// <summary>
        /// Verifies that all entries in <see cref="ActiveSensorTracks"/> receive a
        /// continuous boost when <see cref="ThreatEvaluationSystem"/> executes.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_BoostsAllActiveTracks()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(observer, new TargetMemory());

            // Two active tracks.
            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0]  = 111L;
            tracks.PositionsX[0] = 10f;
            tracks.PositionsY[0] = 0f;
            tracks.EntityIds[1]  = 222L;
            tracks.PositionsX[1] = 20f;
            tracks.PositionsY[1] = 0f;
            tracks.Count = 2;
            world.AddComponent(observer, tracks);

            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(2, resultMem.Count);
            Assert.True(resultMem.ThreatScores[0] > 0f, "First track must have positive threat score.");
            Assert.True(resultMem.ThreatScores[1] > 0f, "Second track must have positive threat score.");
        }
    }
}