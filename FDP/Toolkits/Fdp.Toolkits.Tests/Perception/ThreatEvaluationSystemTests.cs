using System;
using System.Linq;
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
    /// <c>SensorTrackStateIngressTranslator</c>) instead of the retired <c>TargetVisibleEvent</c>.
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
                entityId:   Target(world),
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
            Assert.Equal(expected, resultMem.Freshness[0]);
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

            long targetEntityId = Target(world);   // CE-3046 — a live target (a dead one is forgotten)

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
            Assert.True(resultMem.Freshness[0] > 0f,
                "Score must be boosted when ActiveSensorTracks has acquired targets.");
            Assert.True(resultMem.Freshness[0] >= 49f,
                "Boost rate must be approximately 50 * deltaTime per second.");
        }

        // ── Test 3: CE-3046 — a faded entry no sensor tracks is FORGOTTEN ───────────

        /// <summary>
        /// ⭐ CE-3046 — once a score fades below <see cref="PerceptionConstants.ForgetThreatScore"/> and no sensor tracks the
        /// target, the entry is removed. 🔴 It used to be retained forever ("Phase 2 policy"), so a long scenario filled the
        /// table with dead and long-lost targets.
        /// </summary>
        [Fact]
        public unsafe void ThreatEvaluation_FadedUntrackedEntry_IsForgotten_CE3046()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: Target(world), posX: 0f, posY: 0f, scoreBoost: 1.0f, tick: 0u);
            world.AddComponent(observer, initMem);

            // Apply dt large enough to drive score to 0.
            float dt = 1f / PerceptionConstants.ThreatScoreDecayPerSecond; // 10 seconds

            sys.Execute(view, dt);
            FlushEcbAndSwap(view, world);

            Assert.Equal(0, world.GetComponent<TargetMemory>(observer).Count);
        }

        /// <summary>⭐ CE-3046 — a target that no longer exists is forgotten at once, whatever its score.</summary>
        [Fact]
        public unsafe void ThreatEvaluation_DeadTarget_IsForgotten_CE3046()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            long alive = Target(world);
            var gone   = world.CreateEntity();
            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: alive, posX: 0f, posY: 0f, scoreBoost: 100f, tick: 0u);
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: (long)gone.PackedValue, posX: 0f, posY: 0f, scoreBoost: 400f, tick: 0u);
            world.AddComponent(observer, initMem);
            world.DestroyEntity(gone);

            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            var mem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, mem.Count);
            Assert.Equal(alive, mem.EntityIds[0]);
        }

        /// <summary>⭐ CE-3046 — a faded entry a sensor STILL tracks is kept (it is boosted back up).</summary>
        [Fact]
        public unsafe void ThreatEvaluation_FadedButTrackedEntry_IsKept_CE3046()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var sys   = new ThreatEvaluationSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            long id = Target(world);
            var initMem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: id, posX: 0f, posY: 0f, scoreBoost: 0.01f, tick: 0u);
            world.AddComponent(observer, initMem);
            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0] = id;
            tracks.Count = 1;
            world.AddComponent(observer, tracks);

            sys.Execute(view, 0.001f);   // boost 0.05: still under the forget threshold
            FlushEcbAndSwap(view, world);

            Assert.Equal(1, world.GetComponent<TargetMemory>(observer).Count);
        }

        // ── ⭐ CE-3063 — anonymous (heard) contacts (DESIGN_Thermal_And_Acoustic_Sensing §5.1, §6 D′–D″, §6.1a) ──────────

        private static (EntityRepository world, ISimulationView view, ThreatEvaluationSystem sys, Entity unit) HearingWorld()
        {
            var world = PerceptionTestWorldFactory.Create();
            if (!world.Bus.IsRegistered<SoundContactEvent>()) world.RegisterEvent<SoundContactEvent>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(unit, new TargetMemory());
            return (world, world, new ThreatEvaluationSystem(), unit);
        }

        private static void Hear(EntityRepository world, Entity unit, float x, float y, float radius, byte cls)
        {
            world.Bus.Publish(new SoundContactEvent { Observer = unit, X = x, Y = y, Radius = radius, Kind = 2, SourceClass = cls });
            world.Bus.SwapBuffers();
        }

        private const byte Footsteps = 1, TrackedEngine = 3, SmallArms = 4;

        /// <summary>⭐ Acceptance 1 — a shot heard 200 m north is ONE anonymous contact there; repeated shots keep ONE and shrink it.</summary>
        [Fact]
        public unsafe void AHeardShot_IsOneAnonymousContact_AndRepeatsShrinkIt_CE3063()
        {
            var (world, view, sys, unit) = HearingWorld();

            Hear(world, unit, 3f, 200f, radius: 30f, SmallArms);          // 0.15 × 200 m
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);
            var mem = world.GetComponent<TargetMemory>(unit);
            Assert.Equal(1, mem.Count);
            Assert.True(TargetMemory.IsAnonymous(in mem, 0));
            Assert.True(mem.EntityIds[0] < 0, "a synthetic negative id, never an entity");
            Assert.Equal(30f, mem.Radius[0]);
            Assert.Equal(SmallArms, mem.SourceClass[0]);
            Assert.Equal((byte)SensorModality.Acoustic, mem.Modalities[0]);
            long id = mem.EntityIds[0];

            Hear(world, unit, -4f, 196f, radius: 30f, SmallArms);
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);
            mem = world.GetComponent<TargetMemory>(unit);
            Assert.Equal(1, mem.Count);
            Assert.Equal(id, mem.EntityIds[0]);                                // the SAME contact, followable across ticks
            Assert.True(mem.Radius[0] < 30f, $"fusing two estimates narrows the contact (radius {mem.Radius[0]})");
        }

        /// <summary>⭐ K3 — the class guards fusing: footsteps never fuse with a tank engine.</summary>
        [Fact]
        public void IncompatibleClasses_DoNotFuse_CE3063()
        {
            var (world, view, sys, unit) = HearingWorld();
            Hear(world, unit, 0f, 100f, 20f, Footsteps);
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);
            Hear(world, unit, 2f, 101f, 20f, TrackedEngine);
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);
            Assert.Equal(2, world.GetComponent<TargetMemory>(unit).Count);
        }

        /// <summary>⭐ Acceptance 2 — seeing the shooter inside the circle turns the contact INTO the shooter: no duplicate.</summary>
        [Fact]
        public unsafe void ASightingInsideTheCircle_AbsorbsTheHeardContact_CE3063()
        {
            var (world, view, sys, unit) = HearingWorld();
            Hear(world, unit, 0f, 200f, 30f, SmallArms);
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);

            long shooter = Target(world);
            var tracks = new ActiveSensorTracks();
            tracks.EntityIds[0] = shooter; tracks.PositionsX[0] = 10f; tracks.PositionsY[0] = 190f; tracks.Count = 1;
            world.AddComponent(unit, tracks);
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);

            var mem = world.GetComponent<TargetMemory>(unit);
            Assert.Equal(1, mem.Count);
            Assert.Equal(shooter, mem.EntityIds[0]);
            Assert.False(TargetMemory.IsAnonymous(in mem, 0));
            Assert.Equal(SmallArms, mem.SourceClass[0]);                       // what it sounded like is kept
        }

        /// <summary>⭐ A heard contact is not an entity: it is never forgotten as "dead", only by fading; and the first one
        /// is the unit's FirstThreat (K2).</summary>
        [Fact]
        public void AHeardContact_FadesButIsNotDead_AndIsAFirstThreat_CE3063()
        {
            var (world, view, sys, unit) = HearingWorld();
            Hear(world, unit, 0f, 50f, 8f, SmallArms);
            sys.Execute(view, 0.1f);
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer(); ecb.Playback(world);
            world.Bus.SwapBuffers();
            Assert.Contains(view.ReadEvents<SensorChangedEvent>().ToArray(), e => e.Unit == unit && e.What == SensorChange.FirstThreat);

            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);
            Assert.Equal(1, world.GetComponent<TargetMemory>(unit).Count);    // survives the dead-entity check

            sys.Execute(view, 60f); FlushEcbAndSwap(view, world);             // fades below the forget threshold
            Assert.Equal(0, world.GetComponent<TargetMemory>(unit).Count);
        }

        /// <summary>⭐ K3 — danger of a heard slot is read from its class (there is no entity to read).</summary>
        [Fact]
        public void TheDangerOfAHeardSlot_ComesFromItsClass_CE3063()
        {
            var (world, view, sys, unit) = HearingWorld();
            Hear(world, unit, 0f, 50f, 8f, Footsteps);
            sys.Execute(view, 0.1f); FlushEcbAndSwap(view, world);
            var mem = world.GetComponent<TargetMemory>(unit);
            Assert.Equal(ThreatDanger.OfClass(Footsteps), ThreatDanger.OfSlot(view, unit, in mem, 0));
            Assert.True(ThreatDanger.OfClass(Footsteps) < ThreatDanger.OfClass(TrackedEngine));
        }

        private static long Target(EntityRepository world) => (long)world.CreateEntity().PackedValue;

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
            TargetMemory.AddOrUpdateTarget(ref initMem, entityId: Target(world), posX: 1f, posY: 1f, scoreBoost: 100f, tick: 0u);
            world.AddComponent(observer, initMem);

            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            const float expected = 100f * (1f - PerceptionConstants.ThreatScoreDecayPerSecond * 1.0f);
            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(1, resultMem.Count);
            Assert.Equal(expected, resultMem.Freshness[0]);
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

            long targetId = Target(world);

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
            Assert.True(resultMem.Freshness[0] > 90f,
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
            tracks.EntityIds[0]  = Target(world);
            tracks.PositionsX[0] = 10f;
            tracks.PositionsY[0] = 0f;
            tracks.EntityIds[1]  = Target(world);
            tracks.PositionsX[1] = 20f;
            tracks.PositionsY[1] = 0f;
            tracks.Count = 2;
            world.AddComponent(observer, tracks);

            sys.Execute(view, 1.0f);
            FlushEcbAndSwap(view, world);

            var resultMem = world.GetComponent<TargetMemory>(observer);
            Assert.Equal(2, resultMem.Count);
            Assert.True(resultMem.Freshness[0] > 0f, "First track must have positive threat score.");
            Assert.True(resultMem.Freshness[1] > 0f, "Second track must have positive threat score.");
        }
    }
}