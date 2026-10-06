using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2092</c> / <c>CE-2093</c> — the SHIPPED <c>TakeCover</c> / <c>FallBack</c> trees
    /// (<c>Assets/BTrees/Tactics/</c>) run from the PRODUCTION registry through the real ingress and brain: an order starts
    /// the tree, the tree's node owns a sensor pointed at the threat, an answer moves the unit, and the run ends when
    /// nothing is left to hide from. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §2 (R-204).
    /// </summary>
    public sealed class TacticsTreesTests
    {
        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Unit;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private double _time;
            private long _frame;

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                Fdp.Toolkit.Utility.StandardInputs.RegisterAll();   // ⭐ CE-2073 — as CgfLogicPack's input scan does in production
                _ingress = new BehaviorIngressSystem(Registry);
                _brain   = new BrainTickSystem(Registry);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.AddComponent(Unit, new LocomotionChannel());
                Tick();
            }

            public string? TaskName => Registry.TryGetName(Repo.GetComponent<BehaviorState>(Unit).ActiveBehaviorHash, out var n) ? n : null;

            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                Repo.FlushCommandBuffers();
            }

            public void Order(string behaviour, string json = "{}")
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior });

            /// <summary>⭐ CE-2073 — the unit as a soldier: healthy, armed, able to fire.</summary>
            public void Arm(float health01 = 1f)
            {
                Repo.AddComponent(Unit, new Fdp.Toolkit.Combat.Components.Health { Current = health01 * 100f, Max = 100f });
                Repo.AddComponent(Unit, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 30, MaxAmmo = 30, MuzzleVelocity = 800f });
                Repo.AddComponent(Unit, new WeaponChannel());
            }

            /// <summary>⭐ CE-2073 — a contact remembered FRESH (tracked to saturation), armed or not.</summary>
            public unsafe Entity Contact(bool armed)
            {
                var e = Repo.CreateEntity();
                if (armed) Repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 30, MaxAmmo = 30 });
                if (!Repo.HasComponent<TargetMemory>(Unit)) Repo.AddComponent(Unit, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Unit);
                mem.EntityIds[mem.Count] = (long)e.PackedValue;
                mem.Freshness[mem.Count] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[mem.Count] = (byte)SensorModality.Visual;
                mem.Count++;
                return e;
            }

            public bool Finished(out Fbt.NodeStatus result)
            {
                result = default;
                foreach (var evt in Repo.Bus.Read<BehaviorFinishedEvent>())
                    if (evt.Entity.Equals(Unit)) { result = evt.Result; return true; }
                return false;
            }

            public unsafe void Remember(Entity threat)
            {
                if (!Repo.HasComponent<TargetMemory>(Unit)) Repo.AddComponent(Unit, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Unit);
                if (threat.IsNull) { mem.Count = 0; return; }
                mem.EntityIds[0] = (long)threat.PackedValue;
                mem.Freshness[0] = 10f;
                mem.Count = 1;
            }

            /// <summary>⭐ CE-3063 — a HEARD contact (no entity) in the unit's memory; returns its memory id.</summary>
            public long Hear(float x, float y, float radius)
            {
                if (!Repo.HasComponent<TargetMemory>(Unit)) Repo.AddComponent(Unit, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Unit);
                return TargetMemory.HearContact(ref mem, x, y, 0f, radius, 4 /* small arms */, 100f, (uint)_frame);
            }

            public void Answer(Entity sensor, uint tick, float x, float y)
            {
                var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = tick };
                buf.GetSpanRW()[0] = new EqsResult { PositionX = x, PositionY = y, Score = 1f };
                Repo.SetComponent(sensor, buf);
            }

            public unsafe Vector3 Destination()
            {
                ref readonly var ch = ref Repo.GetComponentRO<LocomotionChannel>(Unit);
                fixed (byte* src = ch.Params) return ((MoveToParams*)src)->Destination;
            }
        }

        // ── ⭐ CE-2073 — the CombatPosture tree (docs/DESIGN_Decision_Layer.md §3.3b) ───────────────────────────────────

        private const string Objective = "{\"advance\":{\"Objective\":[200,0,0],\"Speed\":3,\"ArrivalRadius\":5,\"CooldownSeconds\":1}}";

        [Fact]
        public void CE2073_CombatPosture_IsCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("CombatPosture", out _), "a mission task must be able to name it");
        }

        [Fact]
        public void CE2073_AgainstAWeakEnemy_ThePostureAdvancesFiring_AndEndsAtTheObjective()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: false);                       // a weak enemy ⇒ AdvanceAndAttack
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());  // towards the OBJECTIVE
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the posture keeps its own cover sensor");
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.RetreatSite).IsNull, "…and its retreat sensor");

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            w.Tick();
            w.Tick();
            Assert.NotEqual("CombatPosture", w.TaskName);              // the mission task is done
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the run's sensors go with it");
        }

        [Fact]
        /// <summary>⭐ <c>CE-2105</c> (R-208, 🔒 user: "advance without enemy") — with nothing to fight a healthy, armed unit ADVANCES
        /// on to its objective, firing at nothing. ⛔ SUPERSEDED (CE-2073): it held, and an advance whose enemy fell short of the
        /// objective never reached it.</summary>
        public void CE2105_WithNothingToFight_ThePostureAdvancesToTheObjective_WithoutFiring()
        {
            var w = new World();
            w.Arm();
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);                 // still on its way
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        [Fact]
        public void CE2073_HurtWithCover_ThePostureSwitchesToTakeCover()
        {
            var w = new World();
            w.Arm(health01: 0.3f);
            var enemy = w.Contact(armed: true);
            w.Contact(armed: true);                                    // outnumbered
            w.Order("CombatPosture", Objective);
            w.Tick();
            w.Tick();
            var cover = EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite);
            Assert.False(cover.IsNull);
            w.Answer(cover, 5, 30f, 40f);                              // good cover nearby ⇒ TakeCover wins
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite).IsNull,
                "the TakeCover child runs, with its own sensor");
        }

        [Fact]
        public void CE2092_BothTrees_AreCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("TakeCover", out _), "TakeCover must be registered so an order, an SOP reaction or a mission task can name it");
            Assert.True(w.Registry.TryGetId("FallBack", out _), "FallBack must be registered");
        }

        [Fact]
        public void CE2108_FlankAndFiringPosition_AreCompiledAndRegistered()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("Flank", out _), "Flank must be registered so an order, an SOP row or a mission task can name it");
            Assert.True(w.Registry.TryGetId("FiringPosition", out _), "FiringPosition must be registered");
        }

        /// <summary>
        /// ⭐ <c>CE-2108</c> — the FiringPosition TREE through the real ingress and brain: it moves to the answer FIRING on the way
        /// (the tree's default <c>FireWhileMoving = 1</c>), and on arrival its sensor goes and the tree's <c>Engage</c> keeps the
        /// weapon on the threat. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §9.
        /// </summary>
        [Fact]
        public void CE2108_FiringPosition_Ordered_MovesFiring_ThenEngagesOnArrival()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: true);
            w.Order("FiringPosition");
            w.Tick();
            w.Tick();
            Assert.Equal("FiringPosition", w.TaskName);
            var sensor = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FiringPositionSite);
            Assert.False(sensor.IsNull);
            Assert.Equal(enemy, w.Repo.GetComponentRO<EqsSensor>(sensor).ContextSlot1);

            w.Answer(sensor, 5, 30f, 40f);
            w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(30f, 40f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            w.Tick();
            w.Tick();
            Assert.Equal("FiringPosition", w.TaskName);   // Engage keeps the run going
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FiringPositionSite).IsNull, "the sensor goes on arrival");
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        // ── ⭐ CE-3079 H2 — the Sentry tree (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.5) ──────────────────────────────────

        /// <summary>Puts the unit at <paramref name="x"/>,0 and remembers one ARMED contact at (<paramref name="cx"/>, 0).</summary>
        private static unsafe Entity SentryAt(World w, float x, float cx)
        {
            if (!w.Repo.HasComponent<Fdp.Core.SimTransform>(w.Unit)) w.Repo.AddComponent(w.Unit, new Fdp.Core.SimTransform());
            w.Repo.GetComponentRW<Fdp.Core.SimTransform>(w.Unit).Position = new Vector3(x, 0f, 0f);
            var enemy = w.Contact(armed: true);
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            mem.PositionsX[mem.Count - 1] = cx;
            return enemy;
        }

        /// <summary>
        /// 🔴 <c>CE-3079</c> H2 — the Sentry run ENDS (Success) only after a dangerous contact is within its radius (90 m) AND
        /// the 15 s wait has run; a contact farther away keeps it watching. It never writes the weapon channel (no fire node).
        /// </summary>
        [Fact]
        public void CE3079_Sentry_EndsOnlyAfterAContactIsNear_AndTheWaitElapses_AndNeverFires()
        {
            var w = new World();
            w.Arm();
            Assert.True(w.Registry.TryGetId("Sentry", out _), "Sentry must be registered so a mission task can name it");
            SentryAt(w, 0f, 150f);                                    // an armed contact 150 m away: outside 90 m
            w.Order("Sentry");
            for (int i = 0; i < 1200; i++) { w.Tick(); Assert.False(w.Finished(out _), $"must keep watching while the contact is far (tick {i})"); }
            Assert.Equal("Sentry", w.TaskName);

            unsafe { w.Repo.GetComponentRW<TargetMemory>(w.Unit).PositionsX[0] = 60f; }   // it comes within 90 m
            int ticks = 0;
            bool done = false; Fbt.NodeStatus result = default;
            while (!done && ticks < 2000) { w.Tick(); ticks++; done = w.Finished(out result); }
            Assert.True(done, "the run must end once the contact is near and the wait has run");
            Assert.Equal(Fbt.NodeStatus.Success, result);
            Assert.True(ticks * 0.016 >= 14.9, $"it must wait the 15 s first; ended after {ticks * 0.016:F1} s");
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.Equal(0u, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActionInstanceId);   // never written
        }

        /// <summary>
        /// ⭐ <c>CE-3079</c> H2 — the point of the Sentry: a two-task mission ([Sentry, BehaviorFinished] → [next, BehaviorFinished])
        /// hands over to task 2 when the Sentry ends itself — the scenario's "sequencing of actions" (§10.5).
        /// </summary>
        [Fact]
        public void CE3079_ATwoTaskMission_AdvancesFromSentry_WhenItEndsItself()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("Sentry", out int sentry));
            Assert.True(w.Registry.TryGetId("FallBack", out int next));
            var queue = new MissionPlanQueue { PhaseCount = 2 };
            queue.Phases[0] = new MissionPhase { BehaviorId = sentry, Trigger = MissionTrigger.BehaviorFinished };
            queue.Phases[1] = new MissionPhase { BehaviorId = next,   Trigger = MissionTrigger.BehaviorFinished };
            w.Repo.AddComponent(w.Unit, queue);
            var director = new MissionDirectorSystem();
            SentryAt(w, 0f, 30f);                                     // a contact already near
            w.Order("Sentry");

            int ticks = 0;
            while (w.Repo.GetComponentRO<MissionPlanQueue>(w.Unit).CurrentPhase == 0 && ticks < 2000)
            {
                w.Tick();
                director.Execute(w.Repo, 0.016f);
                ticks++;
            }
            Assert.Equal(1, w.Repo.GetComponentRO<MissionPlanQueue>(w.Unit).CurrentPhase);
            Assert.True(ticks * 0.016 >= 14.9, $"task 2 only after the Sentry's wait; advanced after {ticks * 0.016:F1} s");
        }

        [Fact]
        public void CE2092_TakeCover_Ordered_MovesToTheAnswer_AndEndsWhenNothingIsLeftToHideFrom()
        {
            var w = new World();
            var threat = w.Repo.CreateEntity();
            w.Remember(threat);
            w.Order("TakeCover");
            w.Tick();
            w.Tick();
            Assert.Equal("TakeCover", w.TaskName);

            var sensor = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite);
            Assert.False(sensor.IsNull);
            Assert.Equal(threat, w.Repo.GetComponentRO<EqsSensor>(sensor).ContextSlot1);

            w.Answer(sensor, 5, 30f, 40f);
            w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(30f, 40f, 0f), w.Destination());

            w.Remember(Entity.Null);   // forgotten
            w.Tick();
            w.Tick();
            Assert.NotEqual("TakeCover", w.TaskName);
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite).IsNull, "the run's sensor must go with it");
        }

        /// <summary>
        /// ⭐ <c>CE-3063</c> ③ — a unit that only HEARD a shot hides from where it came from (🔒 user: "hide from a point is OK"):
        /// the run's sensor names no entity and carries the heard contact's point; the answer moves it. A NEW heard estimate far
        /// away re-points the sensor; a refresh of the same contact a little off does not. 📄 DESIGN_Thermal_And_Acoustic_Sensing §8.
        /// </summary>
        [Fact]
        public void CE3063_TakeCover_FromAHeardShot_PointsTheSensorAtThePoint_AndMoves()
        {
            var w = new World();
            w.Hear(80f, 20f, 15f);
            w.Order("TakeCover");
            w.Tick();
            w.Tick();
            Assert.Equal("TakeCover", w.TaskName);

            var sensor = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.TakeCoverSite);
            Assert.False(sensor.IsNull, "a heard contact is something to hide from — TakeCover must not end at once");
            var cfg = w.Repo.GetComponentRO<EqsSensor>(sensor);
            Assert.True(cfg.ContextSlot1.IsNull, "a heard contact is not an entity");
            Assert.Equal(EqsSensor.Point1Bit, cfg.ContextPointMask);
            Assert.Equal(new Vector3(80f, 20f, 0f), cfg.ContextPoint1);
            uint epoch = cfg.Epoch;

            w.Answer(sensor, 5, -30f, -40f);
            w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(-30f, -40f, 0f), w.Destination());

            w.Hear(81f, 20.5f, 15f);   // the same shot heard again: fused, it hardly moves ⇒ no re-point
            w.Tick();
            Assert.Equal(epoch, w.Repo.GetComponentRO<EqsSensor>(sensor).Epoch);
        }
    }
}
