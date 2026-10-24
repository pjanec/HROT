using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Look;
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

            /// <summary>⭐ WL-4 — the unit as the shipped Grenadier: its type's suite (mount 0 rifle, mount 1 an M67 thrown at 15 m/s,
            /// range 25 m) and the grenade mount child with four rounds.</summary>
            public void ArmGrenades()
            {
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                db.Register(new Fdp.Interfaces.TkbTemplate("M67 grenade", 6001) { DisType = new DISEntityType { Kind = 2 } });
                var type = new Fdp.Interfaces.TkbTemplate("Grenadier", 77);
                type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                {
                    new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Range = 300f },
                    new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { MuzzleVelocity = 15f, Range = 25f, AmmoGuid = 6001 },
                } });
                db.Register(type);
                Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                Repo.AddComponent(Unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 77 });
                if (!Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponMountInfo>())
                    Repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();   // CGF registers it (CgfComponentRegistry)
                var grenade = Repo.CreateEntity();
                Repo.AddComponent(grenade, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = Unit, InstanceId = 1 });
                Repo.AddComponent(grenade, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 1 });
                Repo.AddComponent(grenade, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 4, MaxAmmo = 4, MuzzleVelocity = 15f });
            }

            /// <summary>An answer of <paramref name="score"/> (the best point's score) on <paramref name="sensor"/>.</summary>
            public void AnswerScored(Entity sensor, uint tick, float x, float y, float score)
            {
                var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = tick };
                buf.GetSpanRW()[0] = new EqsResult { PositionX = x, PositionY = y, Score = score };
                Repo.SetComponent(sensor, buf);
            }

            /// <summary>⭐ CE-2275 D-2d — an answer of several points, best first.</summary>
            public void AnswerPoints(Entity sensor, uint tick, params (float X, float Y)[] points)
            {
                var buf = new EqsCognitiveBuffer { Count = points.Length, LastUpdateTick = tick };
                for (int i = 0; i < points.Length; i++)
                    buf.GetSpanRW()[i] = new EqsResult { PositionX = points[i].X, PositionY = points[i].Y, Score = 1f - 0.1f * i };
                Repo.SetComponent(sensor, buf);
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

            /// <summary>⭐ CE-3082 — the names of the HSM behaviour's active leaves (one per region).</summary>
            public unsafe string[] ActiveStates(string behaviour)
            {
                Assert.True(Registry.TryGetId(behaviour, out int id));
                Assert.True(Registry.TryGetDefinition(id, out var def));
                if (!Fdp.Toolkit.Behavior.RootHsmAccess.TryGetInstance(Repo, Unit, out byte* ptr, out int size)) return System.Array.Empty<string>();
                ushort* leaves = Fhsm.Kernel.HsmKernel.GetActiveLeafIds(ptr, size, out int count);
                var names = new string[count];
                for (int i = 0; i < count; i++) names[i] = leaves[i] == 0xFFFF ? "-" : def!.HsmMetadata?.GetStateName(leaves[i]) ?? $"State_{leaves[i]}";
                return names;
            }

            public void SetHealth(float health01) => Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(Unit).Current = health01 * 100f;

            /// <summary>⭐ CE-3082 / CE-3160 — the decision's CURRENT WINNER as the tree keeps it: <c>Choice</c> in the C# CombatPosture's block.</summary>
            public unsafe byte Winner(string behaviour)
            {
                Assert.True(Registry.TryGetId(behaviour, out int hash));
                Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetBlockFor<CombatPostureBehavior.CombatPostureBlackboard>(Repo, Unit, hash, out var b));
                return b->Choice.Winner;
            }

            /// <summary>⭐ CE-3084 — a sensor holds <paramref name="contact"/> NOW, by sight (what <c>ThreatInSight</c> reads).</summary>
            public unsafe void See(Entity contact)
            {
                if (!Repo.IsComponentTypeRegistered<ActiveSensorTracks>()) Repo.RegisterComponent<ActiveSensorTracks>();
                if (!Repo.HasComponent<ActiveSensorTracks>(Unit)) Repo.AddComponent(Unit, new ActiveSensorTracks());
                ref var t = ref Repo.GetComponentRW<ActiveSensorTracks>(Unit);
                t.EntityIds[t.Count] = (long)contact.PackedValue;
                t.Modalities[t.Count] = (byte)SensorModality.Visual;
                t.Count++;
            }

            /// <summary>⭐ CE-3084 — the nested approach decision's current winner (<c>St.approachChoice</c> in CombatPosture's block).</summary>
            public unsafe byte ApproachWinner()
            {
                Assert.True(Registry.TryGetId("CombatPosture", out int hash));
                Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetBlockFor<CombatPostureBehavior.CombatPostureBlackboard>(Repo, Unit, hash, out var b));
                return b->ApproachChoice.Winner;
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

        private const string StayObjective = "{\"advance\":{\"Objective\":[200,0,0],\"Speed\":3,\"ArrivalRadius\":5,\"CooldownSeconds\":1},\"stay\":true}";

        /// <summary>⭐ <c>CE-2297</c> slice 2a (R-2055) — a STAYING posture: the same advance against a weak enemy does not end on
        /// arrival — he is still on the task and still firing (the twin of the rail above, which ends there).</summary>
        [Fact]
        public void CE2297_Staying_ArrivalDoesNotEndThePosture_AndHeKeepsFiring()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);                                   // a weak enemy ⇒ AdvanceAndAttack
            w.Order("CombatPosture", StayObjective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            uint move = w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActionInstanceId;
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);                 // he stays
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            // … THERE: the arrived advance holds (AdvanceParams.Stay) — without it the branch succeeds, restarts and re-issues the move
            Assert.Equal(move, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActionInstanceId);
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite).IsNull, "the run and its sensors go on");
        }

        /// <summary>⭐ <c>CE-2297</c> slice 2a — with nothing to fight a staying posture arrives and holds there (the squad's Hold /
        /// Watch man: before, the posture ended on arrival and he idled with no eyes — Look Direction §3.3a known-rot).</summary>
        [Fact]
        public void CE2297_Staying_WithNothingToFight_HeHoldsAtThePoint()
        {
            var w = new World();
            w.Arm();
            w.Order("CombatPosture", StayObjective);
            for (int i = 0; i < 4; i++) w.Tick();
            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
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

        /// <summary>
        /// ⭐ <c>CE-2123</c> — the library <c>AdvanceTo</c>: with an enemy IN SIGHT a healthy soldier still goes to its objective and
        /// fires on the way. 📐 Why it exists: <c>CombatPosture</c> in the same situation chooses Suppress and stays put (measured on
        /// this harness and live on <c>tt-suppress-flank</c>), so it cannot carry a squad leader's MOVE order. DESIGN_Squad_Tactics §3.6.
        /// </summary>
        [Fact]
        public void CE2123_AdvanceTo_MovesToItsObjective_UnderContact_FiringOnTheWay()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: true);
            w.See(enemy);
            w.Order("AdvanceTo", "{\"Objective\":[200,0,0],\"Speed\":3}");
            for (int i = 0; i < 6; i++) Step(w);
            Assert.Equal("AdvanceTo", w.TaskName);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Arrive(w);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { w.Tick(); done = w.Finished(out result); }
            Assert.True(done, "arrival ends the order");
            Assert.Equal(Fbt.NodeStatus.Success, result);
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
            // ⭐ CE-3158 G-6 — the TakeCover option runs the library's fight-from-cover node (PeekAndFire) and its sensor
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PeekAndFireNodes.CoverSite).IsNull,
                "the TakeCover child runs, with its own sensor");
        }

        /// <summary>
        /// ⭐ <c>CE-3090</c> (🔒 user 2026-10-07, docs/DESIGN_Decision_Layer.md §3.3f) — a WOUNDED unit on OPEN GROUND (no cover, no
        /// hidden retreat), outnumbered: the posture picks HoldProne, stops moving and returns fire. ⛔ SUPERSEDED: TakeCover and Flee
        /// both scored 0 and the unit kept advancing at 10 HP (U6, backend).
        /// </summary>
        [Fact]
        public void CE3090_WoundedOnOpenGround_HoldsProne_StopsAndReturnsFire()
        {
            const string host = "CombatPosture";   // ⭐ CE-3160 (R-266) — the HSM and blueprint hosts are retired
            var w = new World();
            w.Arm(health01: 0.2f);
            w.Repo.AddComponent(w.Unit, new Hrot.MuscleCharacter.Animation.Components.StanceIntent());   // CE-2121: a body that can lie down
            w.Contact(armed: true);
            w.Contact(armed: true);                                    // outnumbered
            w.Order(host, Objective);
            for (int i = 0; i < 8; i++) w.Tick();                      // the sensors answer nothing: open ground
            Assert.Equal((byte)Fdp.Toolkit.Utility.Posture.HoldProne, w.Winner(host));
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.Equal(host, w.TaskName);                            // the posture holds; the mission task does not end
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone,       // ⭐ CE-2121 — and asks the body to lie down
                w.Repo.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(w.Unit).TargetStance);
        }

        // ── ⭐ CE-3084 (G6) — the approach decision nested in the advance (docs/DESIGN_Decision_Layer.md §3.3e) ─────────────

        [Fact]
        public void CE3084_AttackApproach_IsRegistered_WithItsOptionNames()
        {
            Fdp.Toolkit.Utility.UtilityDecisionCatalog.EnsureRegistered();
            var id = Fdp.Toolkit.Utility.UtilityDecisionCatalog.ComputeId("3c6f9e42-5d10-6f3a-ac23-approach00001");
            Assert.True(Fdp.Toolkit.Utility.UtilityDecisionCatalog.Shared.TryGet(id, out var def, out _));
            Assert.Equal(typeof(Fdp.Toolkit.Utility.Approach), def!.OptionNames);
            Assert.Equal("Flank", def.OptionName((int)Fdp.Toolkit.Utility.Approach.Flank));
        }

        /// <summary>⭐ ④ — out of sight, with a scored flank, the advancing unit FLANKS (moving there, firing); on arrival it does not
        /// end the run — it sees the target from the flank and goes straight on to the objective (Direct).</summary>
        [Fact]
        public void CE3084_OutOfSight_TheAdvanceFlanksFirst_ThenGoesStraightOnFromTheFlank()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: false);                       // a weak enemy ⇒ AdvanceAndAttack; NOT in sight
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.Direct, w.ApproachWinner());   // nothing scored yet ⇒ straight on
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            var flankScore = EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FlankScoreSite);
            Assert.False(flankScore.IsNull, "the approach keeps its own flank sensor");
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FiringScoreSite).IsNull, "…and its firing-position sensor");
            Assert.Equal(enemy, w.Repo.GetComponentRO<EqsSensor>(flankScore).ContextSlot1);

            w.Answer(flankScore, 5, 60f, 80f);                         // a good flank exists
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.Flank, w.ApproachWinner());
            var flank = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FlankSite);
            Assert.False(flank.IsNull, "the Flank manoeuvre runs, with its own sensor");
            w.Answer(flank, 6, 30f, 40f);
            w.Tick();
            Assert.Equal(new Vector3(30f, 40f, 0f), w.Destination());   // to the flank …
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);   // … firing

            w.See(enemy);                                              // from the flank the target is in sight
            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // arrived at the flank
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);                 // ⭐ F2: arrival did NOT end the run
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.Direct, w.ApproachWinner());
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());  // straight on to the objective
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
        }

        private const string PostedObjective = "{\"advance\":{\"Objective\":[0,0,0],\"Speed\":3,\"ArrivalRadius\":1,\"CooldownSeconds\":1},\"stay\":true,"
                                             + "\"post\":{\"point\":[0,0,0],\"radius\":4,\"window\":false}}";

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2d (PC-14, DA-44) — a POSTED man's flank stays on his post: of a flank answer whose best point is 50 m off
        /// it and whose second is inside it, he takes the second; an answer with nothing on his post moves him nowhere and he fights
        /// from where he stands. 🔴 Red before: measured live (<c>sq-defend-house</c>) the approach moves took posted men 5–7 m off.
        /// </summary>
        [Fact]
        public void CE2275_APostedMansFlank_StaysOnHisPost_AndNothingOnItMeansHeFightsWhereHeStands()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: false);                       // a weak enemy ⇒ AdvanceAndAttack; NOT in sight
            w.Order("CombatPosture", PostedObjective);
            for (int i = 0; i < 4; i++) w.Tick();
            w.Answer(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FlankScoreSite), 5, 60f, 80f);   // a good flank exists
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.Flank, w.ApproachWinner());
            var flank = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FlankSite);
            Assert.False(flank.IsNull, "the Flank manoeuvre runs");

            w.AnswerPoints(flank, 6, (30f, 40f), (3f, 1f));            // best 50 m off his post, second inside it
            w.Tick();
            Assert.Equal(new Vector3(3f, 1f, 0f), w.Destination());     // ⭐ the best point ON his post

            var w2 = new World();
            w2.Arm();
            w2.Contact(armed: false);
            w2.Order("CombatPosture", PostedObjective);
            for (int i = 0; i < 4; i++) w2.Tick();
            w2.Answer(EqsChildSensor.Find(w2.Repo, w2.Unit, PostureNodes.FlankScoreSite), 5, 60f, 80f);
            for (int i = 0; i < 3; i++) w2.Tick();
            var flank2 = EqsChildSensor.Find(w2.Repo, w2.Unit, EqsTacticsNodes.FlankSite);
            Assert.False(flank2.IsNull, "the Flank manoeuvre runs");
            w2.AnswerPoints(flank2, 6, (30f, 40f), (12f, 9f));         // nothing on his post
            for (int i = 0; i < 3; i++) w2.Tick();
            Assert.NotEqual(new Vector3(30f, 40f, 0f), w2.Destination());
            Assert.NotEqual(new Vector3(12f, 9f, 0f), w2.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w2.Repo.GetComponentRO<WeaponChannel>(w2.Unit).ActiveAction);   // … he fights
        }

        private const string PostedAway = "{\"advance\":{\"Objective\":[10,0,0],\"Speed\":3,\"ArrivalRadius\":1,\"CooldownSeconds\":1},\"stay\":true,"
                                        + "\"post\":{\"point\":[10,0,0],\"radius\":4,\"window\":false}}";

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2e (PC-14, DA-45) — a POSTED man not yet on his post goes there whatever the fight's decision: here an armed
        /// enemy he remembers but cannot see makes the decision something other than the advance, and he still walks to his post. 🔴 Red
        /// before: measured live (<c>sq-defend-house</c>) the first contact flipped the posture to Suppress on the stairs and he stood there.
        /// </summary>
        [Fact]
        public void CE2275_APostedManNotYetOnHisPost_GoesThere_WhateverTheDecision()
        {
            var w = new World();
            w.Arm();
            w.Repo.AddComponent(w.Unit, new SimTransform { Position = Vector3.Zero });
            w.Contact(armed: true);
            w.Order("CombatPosture", PostedAway);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.NotEqual((byte)Fdp.Toolkit.Utility.Posture.AdvanceAndAttack, w.Winner("CombatPosture"));
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);
            Assert.Equal(new Vector3(10f, 0f, 0f), w.Destination());   // ⭐ to his post
        }

        /// <summary>⭐ D-2e — <see cref="PostureNodes.OffPost"/>'s latch: off ⇒ go; within 1.5 m ⇒ occupied, and inside his room (4 m) the fight
        /// may move him without being pulled back (D-2d's moves); carried out of the room ⇒ back; his alternate counts as on his post; no post
        /// ⇒ never.</summary>
        [Fact]
        public void CE2275_OffPost_LatchesOnArrival_FreesHisRoom_AndCallsHimBackFromOutsideIt()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new SimTransform { Position = Vector3.Zero });
            var p = new PeekAndFireParams { Anchor = new Vector3(10f, 0f, 0f), AnchorRadius = 4f, MatchRadius = 1f,
                                            AnchorAlternate = new Vector3(0f, 10f, 0f), AnchorHasAlternate = 1 };
            var ws = default(OccupyState);
            void At(float x, float y) => w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(x, y, 0f);
            bool Off() => PostureNodes.OffPost(ref p, ref ws, w.Unit, w.Repo);

            Assert.True(Off(), "not yet on his post");
            At(7f, 0f);   Assert.True(Off(), "3 m off and never there yet: still going");
            At(9f, 0f);   Assert.False(Off());
            At(7f, 0f);   Assert.False(Off(), "inside his room after occupying: the fight may move him");
            At(4f, 0f);   Assert.True(Off(), "carried out of his room: back");
            At(9.2f, 0f); Assert.False(Off());
            var ws2 = default(OccupyState);
            At(0f, 10.5f); Assert.False(PostureNodes.OffPost(ref p, ref ws2, w.Unit, w.Repo), "his alternate is his post too");
            var none = new PeekAndFireParams();
            Assert.False(PostureNodes.OffPost(ref none, ref ws2, w.Unit, w.Repo), "no post: never");
        }

        /// <summary>
        /// ⭐⭐ WL-4 W-12f (DESIGN_Weapon_Layers §5f) — out of sight, a GRENADIER with an equally good flank and throwing spot CLOSES
        /// TO THROW: the approach keeps a throwing-position sensor pointed at the LAST-KNOWN point (L-5 — not the live entity), the
        /// CloseToThrow manoeuvre moves the unit there and the weapon keeps firing (the weapon brain throws while the target hides).
        /// 🔴 Red before: the approach had no throw option and no throw sensor.
        /// </summary>
        [Fact]
        public void WL4_OutOfSight_AGrenadier_ClosesToThrow_AtTheLastKnownPoint()
        {
            var w = new World();
            w.Arm();
            w.ArmGrenades();
            var enemy = w.Contact(armed: false);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            var throwScore = EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.ThrowScoreSite);
            Assert.False(throwScore.IsNull, "a grenadier's approach keeps a throwing-position sensor");
            var cfg = w.Repo.GetComponentRO<EqsSensor>(throwScore);
            Assert.True(cfg.ContextSlot1.IsNull, "L-5: not pointed at the live entity");
            Assert.NotEqual(0, cfg.ContextPointMask & EqsSensor.Point1Bit);   // … but at the remembered point

            w.AnswerScored(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FlankScoreSite), 5, 60f, 80f, 0.5f);
            w.AnswerScored(throwScore, 5, 10f, 12f, 0.5f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.CloseToThrow, w.ApproachWinner());

            var close = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.CloseToThrowSite);
            Assert.False(close.IsNull, "the CloseToThrow manoeuvre runs, with its own sensor");
            w.Answer(close, 6, 10f, 12f);
            w.Tick();
            Assert.Equal(new Vector3(10f, 12f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            _ = enemy;
        }

        /// <summary>⭐ WL-4 W-12f — a RIFLEMAN (no throwable) keeps no throw sensor and never closes to throw: he flanks.</summary>
        [Fact]
        public void WL4_ARifleman_NeverClosesToThrow()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.ThrowScoreSite).IsNull);
            w.AnswerScored(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FlankScoreSite), 5, 60f, 80f, 0.5f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.Flank, w.ApproachWinner());
        }

        // ── ⭐⭐ AQ89 (R-269) — Suppress at a HIDDEN, equally armed enemy (docs/blueprints/Architect_Question_89_Suppress_And_Close.md) ──

        private static void Temperament(World w, float? risk = null, float? obey = null)
        {
            if (!w.Repo.IsComponentTypeRegistered<CombatTemperament>()) w.Repo.RegisterComponent<CombatTemperament>();
            var t = new CombatTemperament();
            if (risk is { } r) { t.RiskAcceptance = r; t.Set |= CombatTemperament.RiskAcceptanceSet; }
            if (obey is { } o) { t.Obedience = o; t.Set |= CombatTemperament.ObedienceSet; }
            w.Repo.AddComponent(w.Unit, t);
        }

        /// <summary>The unit has a sensor that holds nothing NOW — without one, <c>SightNow</c> reads "seen" (a world with no sensor model).</summary>
        private static void SeesNothing(World w)
        {
            if (!w.Repo.IsComponentTypeRegistered<ActiveSensorTracks>()) w.Repo.RegisterComponent<ActiveSensorTracks>();
            if (!w.Repo.HasComponent<ActiveSensorTracks>(w.Unit)) w.Repo.AddComponent(w.Unit, new ActiveSensorTracks());
        }

        private static unsafe byte BlindFireAsked(World w)
        {
            ref readonly var ch = ref w.Repo.GetComponentRO<WeaponChannel>(w.Unit);
            fixed (byte* src = ch.Params) return ((Fdp.Toolkit.Combat.Executors.AimAndFireParams*)src)->BlindFire;
        }

        /// <summary>
        /// ⭐ AQ89 A — an equally armed enemy out of sight: the posture is Suppress and the weapon is asked to SUPPRESS BLIND (the weapon
        /// brain then fires at the last-known spot). No close for a rifleman. 🔴 Red before: Suppress asked for aimed fire only, which
        /// held at a hidden target until the memory faded.
        /// </summary>
        [Fact]
        public void AQ89_A_AHiddenEqualEnemy_IsSuppressedBlind()
        {
            var w = new World();
            w.Arm();
            SeesNothing(w);
            w.Contact(armed: true);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Posture.Suppress, w.Winner("CombatPosture"));
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.Equal(1, BlindFireAsked(w));
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.CloseToThrowSite).IsNull, "a rifleman does not close");
        }

        /// <summary>
        /// ⭐⭐ AQ89 B/C/D — a grenadier at a hidden enemy CLOSES TO THROW under his own blind fire only when WILLING: brave on his own
        /// (risk 0.8), or ordered to assault (an ordinary soldier's obedience 0.8). An ordinary grenadier left to himself (0.5), or a
        /// disobedient one ordered to assault (0.3), suppresses where he is.
        /// </summary>
        [Theory]
        [InlineData(0.8f, null, false, true)]
        [InlineData(null, null, false, false)]
        [InlineData(null, null, true, true)]
        [InlineData(null, 0.3f, true, false)]
        public void AQ89_C_AGrenadierClosesToThrow_OnlyWhenWilling(float? risk, float? obey, bool assault, bool closes)
        {
            var w = new World();
            w.Arm();
            w.ArmGrenades();
            Temperament(w, risk, obey);
            SeesNothing(w);
            var enemy = w.Contact(armed: true);
            w.Order("CombatPosture", assault ? Objective[..^1] + ",\"assault\":true}" : Objective);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Posture.Suppress, w.Winner("CombatPosture"));
            var close = EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.CloseToThrowSite);
            Assert.True(closes == !close.IsNull,
                $"throwable={Fdp.Toolkit.Combat.HiddenTargetThrow.ThrowableMount(w.Repo, w.Unit, out _, out _, out _, out _)} " +
                $"lastKnown={Fdp.Toolkit.Combat.HiddenTargetThrow.LastKnown(w.Repo, w.Unit, enemy, out _)} " +
                $"seen={Fdp.Toolkit.Perception.SightNow.Sees(w.Repo, w.Unit, enemy)} " +
                $"willing={TemperamentOf.Willing(w.Repo, w.Unit, assault)} origin={w.Repo.GetComponentRO<BehaviorState>(w.Unit).Origin}");
            Assert.Equal(1, BlindFireAsked(w));   // closing or holding, he fires blind at the spot
            if (!closes) return;
            w.Answer(close, 6, 10f, 12f);
            w.Tick();
            Assert.Equal(new Vector3(10f, 12f, 0f), w.Destination());
        }

        /// <summary>⭐ ⑤ — with the target IN SIGHT a scored flank does not matter: Direct, the old advance.</summary>
        [Fact]
        public void CE3084_InSight_TheAdvanceGoesStraightOn()
        {
            var w = new World();
            w.Arm();
            var enemy = w.Contact(armed: false);
            w.See(enemy);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            w.Answer(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FlankScoreSite), 5, 60f, 80f);
            w.Answer(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FiringScoreSite), 5, 20f, 10f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Approach.Direct, w.ApproachWinner());
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FlankSite).IsNull, "no manoeuvre runs");
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        /// <summary>⭐ ⑥ — the OUTER posture still decides: hurt and outnumbered while flanking ⇒ TakeCover, and the flank manoeuvre
        /// goes with the branch (its deactivator drops its sensor). The approach's SCORING sensors stay — they live in the outer
        /// parallel (§3.3e F3).</summary>
        [Fact]
        public void CE3084_WhileFlanking_TheOuterPostureStillSwitchesToTakeCover()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            w.Answer(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.FlankScoreSite), 5, 60f, 80f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FlankSite).IsNull, "flanking");

            w.SetHealth(0.3f);
            w.Remember(Entity.Null);
            w.Contact(armed: true);
            w.Contact(armed: true);                                    // hurt and outnumbered
            w.Tick();
            w.Tick();
            w.Answer(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite), 6, 30f, 40f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.False(EqsChildSensor.Find(w.Repo, w.Unit, PeekAndFireNodes.CoverSite).IsNull, "TakeCover runs");   // ⭐ CE-3158 G-6 — PeekAndFire's sensor
            Assert.True(EqsChildSensor.Find(w.Repo, w.Unit, EqsTacticsNodes.FlankSite).IsNull, "the flank manoeuvre (and its sensor) went with the branch");
            Assert.NotEqual(new Vector3(60f, 80f, 0f), w.Destination());
        }

        /// <summary>⭐ CE-2117 — the shipped CombatPosture BTree: when TakeCover takes over from the advance, the advance's
        /// deactivator runs and its fire stops. ✅ Red before the kernel fix (measured: fire still on, the move still at the
        /// objective, while TakeCover had already won).</summary>
        [Fact]
        public void CE2117_ThePostureBTree_SwitchingToTakeCover_StopsTheAdvancesFire()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            w.SetHealth(0.3f);
            w.Remember(Entity.Null);
            w.Contact(armed: true);
            w.Contact(armed: true);                                    // hurt and outnumbered
            w.Tick();
            w.Tick();
            w.Answer(EqsChildSensor.Find(w.Repo, w.Unit, PostureNodes.CoverSite), 6, 30f, 40f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal((byte)Fdp.Toolkit.Utility.Posture.TakeCover, w.Winner("CombatPosture"));
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        // ⛔ CE-3082 (G4, the HSM host) and CE-3083 (G5, the blueprint host) rails RETIRED with those hosts — CE-3160 (R-266, R-258):
        //   the library's CombatPosture is ONE C#-built tree (docs/DESIGN_Decision_Layer.md §3.3h); nothing is left to compare.

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
        /// ⭐⭐ <c>CE-2133</c> (B1, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §1 P2) — the Sentry's one Input IS its params
        /// DTO, so its JSON is <c>ThreatCountParams</c>' fields FLAT (the shape <c>ua-danger-crossing</c> now saves), overlaid
        /// onto the baked default: a sent key wins, an absent one keeps its default. The published contract is that DTO.
        /// ⛔ The old wrapped form <c>{"sentry":{…}}</c> is an unknown key now — ignored, so the defaults stand.
        /// </summary>
        [Fact]
        public unsafe void CE2133_TheSentryReadsFlatParams_OverItsBakedDefaults()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("Sentry", out int id));
            Assert.True(w.Registry.TryGetDefinition(id, out var def));
            Assert.Equal(typeof(ThreatCountParams), def!.JsonParamsDtoType);

            ThreatCountParams Parse(string json)
            {
                var block = new byte[RootParamsAccess.RootParamsBytes(def)];
                fixed (byte* p = block) def.ParseParams!(json, p, block.Length, w.Repo, w.Unit);
                return System.Runtime.CompilerServices.Unsafe.ReadUnaligned<ThreatCountParams>(ref block[0]);
            }

            var flat = Parse("{\"WithinMetres\":145,\"count\":3}");
            Assert.Equal(145f, flat.WithinMetres);   // sent
            Assert.Equal(3, flat.Count);             // sent, case-insensitive like every params parse
            Assert.Equal(0.5f, flat.MinDanger);      // absent: the baked default (DefaultValueJson) stands

            var wrapped = Parse("{\"sentry\":{\"WithinMetres\":145}}");
            Assert.Equal(90f, wrapped.WithinMetres); // the retired wrapper is an unknown key
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

        // ── ⭐ CE-3079 H5 — the DangerCrossing tree (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.4) ───────────────────────────────

        /// <summary>⭐ <c>CE-2132</c> — the resolver writes only what the contract names: the objective into the sensor's route and
        /// the walk, the speeds, <c>CrossWithin</c>; the asset's baked tuning (here <c>holdAhead.WithinMetres = 60</c>) stays.
        /// 🔴 Red-proof: drop the walk line → the walk goes to (0, 0).</summary>
        [Fact]
        public void CE2132_TheDangerCrossingResolver_WritesTheContract_AndKeepsTheBakedTuning()
        {
            var block = new Hrot.AI.Behaviors.Brains.DangerCrossingConfig();
            block.holdAhead.WithinMetres = 60f;   // as the asset's ParamsDefaultJson bakes it
            var authored = new Hrot.AI.Behaviors.Brains.DangerCrossingParams { Objective = new System.Numerics.Vector3(300, 250, 0), RushSpeed = 5f };
            Hrot.AI.Behaviors.Brains.DangerAreaNodes.ResolveDangerCrossing(authored, ref block, null!, default);

            Assert.Equal(new System.Numerics.Vector3(300, 250, 0), block.sensor.RouteTo);
            Assert.Equal(300f, block.walk.X);
            Assert.Equal(250f, block.walk.Y);
            Assert.Equal(1.5f, block.walk.Speed);      // the contract's default WalkSpeed
            Assert.Equal(1.5f, block.hold.Speed);
            Assert.Equal(5f, block.cross.Speed);
            Assert.Equal(15f, block.crossAhead.WithinMetres);
            Assert.Equal(60f, block.holdAhead.WithinMetres);
        }

        private const string Crossing = "{\"Objective\":[300,300,0]}";   // ⭐ CE-2132 — the flat contract (DangerCrossingParams), its twin's

        private static Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor Area(uint id, float threat, float along,
            Fdp.Toolkit.Squad.DangerArea.DangerAreaKind kind = Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.StreetCrossing)
            => new()
            {
                FeatureId = id, ThreatRating = threat, Kind = kind, DistanceAlongRoute = along,
                NearSideHandle = new Vector3(100f + id, 100f, 0f), FarSideHandle = new Vector3(120f + id, 100f, 0f),
            };

        private static void DangerAnswer(World w, Entity sensor, uint tick, params Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[] areas)
        {
            var buf = new Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer { Count = areas.Length, LastUpdateTick = tick };
            for (int i = 0; i < areas.Length; i++) buf.GetSpanRW()[i] = areas[i];
            w.Repo.SetComponent(sensor, buf);
        }

        /// <summary>One frame, with the MoveTo executor's half emulated: a NEW action instance enters Running (its <c>OnEnter</c>).</summary>
        private static void Step(World w)
        {
            uint before = w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActionInstanceId;
            w.Tick();
            ref var ch = ref w.Repo.GetComponentRW<LocomotionChannel>(w.Unit);
            if (ch.ActionInstanceId != before && ch.ActiveAction == NavigationConstants.ActionIdMoveTo) ch.Status = Fbt.NodeStatus.Running;
        }

        private static void Arrive(World w) => w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;

        private static unsafe float Speed(World w)
        {
            ref readonly var ch = ref w.Repo.GetComponentRO<LocomotionChannel>(w.Unit);
            fixed (byte* src = ch.Params) return ((MoveToParams*)src)->Speed;
        }

        /// <summary>
        /// 🔴 <c>CE-3079</c> H5 — the shipped <c>DangerCrossing</c> tree through the real ingress and brain, against a HAND-FILLED
        /// answer on the run's own sensor: it walks → rushes across a 0-threat crossing (and does not cross it again on the stale
        /// answer) → walks on → holds short of a 0.8 crossing (and keeps holding at 0.45: hysteresis) → resumes when the route is
        /// clear → arrives, and the run ends Success.
        /// </summary>
        [Fact]
        public void CE3079_DangerCrossing_Walks_RushesACrossing_HoldsShortOfAWatchedOne_ThenArrives()
        {
            var w = new World();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>();
            Assert.True(w.Registry.TryGetId("DangerCrossing", out _), "DangerCrossing must be registered so an order can name it");
            w.Order("DangerCrossing", Crossing);
            Step(w); Step(w); Step(w);
            Assert.Equal("DangerCrossing", w.TaskName);

            var sensor = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(w.Repo, w.Unit, SensorModality.DangerArea);
            Assert.False(sensor.IsNull, "the tree's first step makes the run's danger sensor");
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Repo.GetComponentRO<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(sensor).Settings.RoutePoint);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // no answer yet: walks
            Assert.Equal(1.5f, Speed(w));

            DangerAnswer(w, sensor, 1, Area(1, 0f, 10f));                               // an unwatched crossing, 10 m ahead
            Step(w);
            Assert.Equal(new Vector3(101f, 100f, 0f), w.Destination());               // rush: to the near side…
            Assert.Equal(4.5f, Speed(w));
            Arrive(w); Step(w);
            Assert.Equal(new Vector3(121f, 100f, 0f), w.Destination());               // …and across
            Arrive(w); Step(w);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // walks on
            Assert.Equal(1.5f, Speed(w));
            for (int i = 0; i < 10; i++) Step(w);                                      // the stale answer still lists it:
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // ⛔ not crossed back

            DangerAnswer(w, sensor, 2, Area(2, 0.8f, 50f));                             // a WATCHED crossing, 50 m ahead
            Step(w);
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());               // hold short: to its near side
            Arrive(w);
            for (int i = 0; i < 300; i++) { Step(w); Assert.False(w.Finished(out _)); }
            DangerAnswer(w, sensor, 3, Area(2, 0.45f, 2f));                             // 0.45: still ≥ 0.5 − 0.1
            for (int i = 0; i < 5; i++) Step(w);
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());               // still holding

            DangerAnswer(w, sensor, 4);                                                 // the route is clear
            Step(w);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());               // resumes
            Arrive(w);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 5 && !done; i++) { Step(w); done = w.Finished(out result); }
            Assert.True(done, "arriving at the objective ends the run");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>
        /// 🔴 <c>CE-3079</c> H7 / <c>CE-3078</c> — the same behaviour as a BLUEPRINT built only from the per-kind sensor nodes
        /// (<c>DangerCrossingBp</c>: SpawnSensor(DangerArea) → When SensorResult(ThreatCrossed ≥ 0.5) → ReadSensorResult(0) near /
        /// far handles → MoveTo), through the real ingress and brain, against a hand-filled answer: walks → rushes across a
        /// 0-threat crossing to its far side → walks on → holds at a 0.8 crossing's near side → resumes when the route is clear →
        /// arrives, and the run ends Success.
        /// </summary>
        [Fact]
        public void CE3079_DangerCrossingBlueprint_Walks_RushesACrossing_HoldsShortOfAWatchedOne_ThenArrives()
        {
            var w = new World();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()) w.Repo.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>();
            Assert.True(w.Registry.TryGetId("DangerCrossingBp", out _), "the blueprint must be registered so an order can name it");
            w.Order("DangerCrossingBp", "{\"Objective\":[300,300,0]}");
            Step(w); Step(w); Step(w);
            Assert.Equal("DangerCrossingBp", w.TaskName);

            var sensor = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(w.Repo, w.Unit, SensorModality.DangerArea);
            Assert.False(sensor.IsNull, "SpawnSensor(DangerArea) makes the run's sensor");
            var settings = w.Repo.GetComponentRO<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(sensor).Settings;
            Assert.Equal(Fdp.Toolkit.Squad.DangerArea.DangerRouteSource.ToPoint, settings.RouteSource);   // never the unit's own move
            Assert.Equal(new Vector3(300f, 300f, 0f), settings.RoutePoint);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());   // no answer: walks
            Assert.Equal(1.5f, Speed(w));

            DangerAnswer(w, sensor, 1, Area(1, 0f, 10f));                  // an unwatched crossing 10 m ahead
            Step(w);
            Assert.Equal(new Vector3(121f, 100f, 0f), w.Destination());   // rush: straight to the FAR side
            Assert.Equal(4.5f, Speed(w));
            Arrive(w);
            for (int i = 0; i < 5; i++) Step(w);                           // the stale answer still lists it: no move back
            Assert.Equal(new Vector3(121f, 100f, 0f), w.Destination());

            DangerAnswer(w, sensor, 2, Area(2, 0.8f, 50f));                 // refreshed: past it; a WATCHED crossing 50 m on
            Step(w); Step(w);
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());   // When ThreatCrossed → hold at its near side
            Assert.Equal(1.5f, Speed(w));
            Arrive(w);
            for (int i = 0; i < 300; i++) { Step(w); Assert.False(w.Finished(out _)); }
            Assert.Equal(new Vector3(102f, 100f, 0f), w.Destination());   // still holding

            DangerAnswer(w, sensor, 3);                                     // the route is clear (threat falls with it)
            Step(w); Step(w);
            Assert.Equal(new Vector3(300f, 300f, 0f), w.Destination());   // resumes the walk
            Arrive(w);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 5 && !done; i++) { Step(w); done = w.Finished(out result); }
            Assert.True(done, "arriving at the objective ends the run");
            Assert.Equal(Fbt.NodeStatus.Success, result);
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
    
        /// <summary>
        /// ⭐ <c>AQ88</c> B (R-265) — the ONE move sets the body it is made in: a soldier moves UPRIGHT when nothing threatens him,
        /// CROUCHED when he remembers a live contact, a heard one, or was hit within 10 s; a cautious move (Crouched) always crouches,
        /// a civilian's (Upright) never does, and Keep leaves the stance to the caller. A corpse in memory is no threat (CE-2120).
        /// 🔴 Red before: no move touched the stance — every soldier ran upright under fire.
        /// </summary>
        [Fact]
        public void AQ88_TheMove_CrouchesWhenThreatenedOrCautious_UprightOtherwise()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new Hrot.MuscleCharacter.Animation.Components.StanceIntent());
            Fdp.Toolkit.Tkb.Domain.StanceId Move(MoveGait gait)
            {
                Assert.True(LocomotionMoveTo.Issue(w.Repo, w.Unit, new System.Numerics.Vector3(10f, 0f, 0f), 3f, 1f, gait));
                return w.Repo.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(w.Unit).TargetStance;
            }
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Move(MoveGait.Tactical));    // nothing known
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Move(MoveGait.Crouched));    // cautious
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Move(MoveGait.Keep));        // left as it was

            var corpse = w.Contact(armed: true);
            w.Repo.AddComponent(corpse, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Move(MoveGait.Tactical));    // a dead contact

            w.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(corpse).Current = 50f;
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Move(MoveGait.Tactical));    // a live contact
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Move(MoveGait.Upright));     // a civilian runs upright

            w.Remember(Entity.Null);
            w.Hear(30f, 0f, 10f);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Move(MoveGait.Tactical));    // a heard shot

            w.Remember(Entity.Null);
            var senses = new RecentSenses();
            senses.Record(Fdp.Toolkit.Perception.Events.SensorChange.Hit, w.Repo.GetSingleton<GlobalTime>().TotalTime);
            w.Repo.AddComponent(w.Unit, senses);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Move(MoveGait.Tactical));    // hit, shooter unknown
        }

        // ── ⭐ AQ96 (CE-2319 / CE-2320) — how a soldier moves under fire: docs/blueprints/Architect_Question_96_Movement_Under_Fire.md ────

        /// <summary>A soldier on the move, as the dispatcher leaves him: the move issued through the one helper and its intent written.</summary>
        private static unsafe Fdp.Toolkit.Navigation.MoveToParams Mover(World w, Vector3 to, float speed = 2f, MoveGait gait = MoveGait.Tactical)
        {
            w.Repo.AddComponent(w.Unit, new Hrot.MuscleCharacter.Animation.Components.StanceIntent());
            if (!w.Repo.HasComponent<SimTransform>(w.Unit))
                w.Repo.AddComponent(w.Unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            if (!w.Repo.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(w.Unit))
                w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Navigation.NavigationIntent());
            if (!w.Repo.HasComponent<RecentSenses>(w.Unit)) w.Repo.AddComponent(w.Unit, new RecentSenses());
            Assert.True(LocomotionMoveTo.Issue(w.Repo, w.Unit, to, speed, 1f, gait));
            ref var ch = ref w.Repo.GetComponentRW<LocomotionChannel>(w.Unit);
            new Fdp.Toolkit.Navigation.Executors.MoveToExecutor().OnEnter(w.Unit, ref ch, w.Repo);
            fixed (byte* src = ch.Params) return *(Fdp.Toolkit.Navigation.MoveToParams*)src;
        }

        private static Fdp.Toolkit.Tkb.Domain.StanceId Stance(World w)
            => w.Repo.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(w.Unit).TargetStance;

        private static Fdp.Toolkit.Navigation.NavigationIntent Intent(World w) => w.Repo.GetComponentRO<Fdp.Toolkit.Navigation.NavigationIntent>(w.Unit);

        private static UnderFireMove Reflex(World w) => w.Repo.GetComponentRO<UnderFireMove>(w.Unit);

        private static void ShotAt(World w, double at) => w.Repo.GetComponentRW<RecentSenses>(w.Unit).Record(Fdp.Toolkit.Perception.Events.SensorChange.NearMiss, at);

        /// <summary>One pass of the reflex at sim time <paramref name="t"/> (the clock every reader of the senses uses, set to it).</summary>
        private static void StepAt(World w, double t)
        {
            w.Repo.SetSingleton(new GlobalTime { TotalTime = t, DeltaTime = 0.016f, TimeScale = 1f });
            UnderFire.Step(w.Repo, w.Unit, t);
        }

        /// <summary>
        /// ⭐⭐ AQ96 A + B — a soldier who set off unthreatened (upright, the order's 2 m/s) crouches and rushes when a threat appears MID-MOVE,
        /// and the play's move is the same move (its instance unchanged). 🔴 Red before: the gait and the pace were fixed at issue (CE-2319).
        /// ✅ Red-proof: the threat re-check removed from <c>UnderFire.Step</c> ⇒ he stays upright at 2 ⇒ red.
        /// </summary>
        [Fact]
        public void AQ96_AThreatMidMove_CrouchesHim_AndRushes_TheSameMove()
        {
            var w = new World();
            Mover(w, new Vector3(50f, 0f, 0f));
            uint instance = w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActionInstanceId;
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Stance(w));
            Assert.Equal(2f, Intent(w).TargetSpeed);

            w.Contact(armed: true);
            StepAt(w, 10.0);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Stance(w));
            Assert.Equal(UnderFire.RushSpeed, Intent(w).TargetSpeed);
            Assert.Equal(instance, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActionInstanceId);
            Assert.Equal(Fbt.NodeStatus.Running, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).Status);
        }

        /// <summary>
        /// ⭐⭐ AQ96 C + C2 — a near miss in the open DROPS him: prone, the move halted, and the move's executor keeps the play Running even
        /// when the halted intent reports a result. While rounds keep coming he stays down; 3 s without one ⇒ he side-steps 1.5 m prone
        /// (never up where he dropped), then rises crouched and rushes on to the play's point. ✅ Red-proof: the executor's hold check
        /// removed ⇒ the halted intent's "arrived" ends the play's move ⇒ red.
        /// </summary>
        [Fact]
        public void AQ96_ANearMissInTheOpen_DropsHim_HeStaysDownUnderFire_ThenShiftsAndRises()
        {
            var w = new World();
            var move = Mover(w, new Vector3(50f, 0f, 0f));
            ShotAt(w, 20.0);
            StepAt(w, 20.1);
            Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, Stance(w));
            Assert.Equal(Fdp.Toolkit.Navigation.NavigationMode.None, Intent(w).Mode);
            Assert.Equal(Intent(w).IntentId, Reflex(w).HoldIntentId);

            // the halted intent "arrives" (the muscle reports on it): the play's move does not end
            if (!w.Repo.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(w.Unit))
                w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Navigation.NavigationStatus());
            w.Repo.SetComponent(w.Unit, new Fdp.Toolkit.Navigation.NavigationStatus { IntentId = Intent(w).IntentId, Result = Fdp.Toolkit.Navigation.NavigationResult.Arrived });
            ref var ch = ref w.Repo.GetComponentRW<LocomotionChannel>(w.Unit);
            new Fdp.Toolkit.Navigation.Executors.MoveToExecutor().Execute(w.Unit, ref ch, w.Repo, 0.016f);
            Assert.Equal(Fbt.NodeStatus.Running, ch.Status);

            // still under fire: a round every 2 s keeps him down
            ShotAt(w, 22.0);
            StepAt(w, 22.5);
            ShotAt(w, 24.0);
            StepAt(w, 25.0);
            Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);

            // 3 s without one: the side step, prone, across his line of advance
            StepAt(w, 27.1);
            Assert.Equal(UnderFirePhase.Shifting, Reflex(w).Phase);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, Stance(w));
            var step = Reflex(w).SubTarget;
            Assert.Equal(UnderFire.ShiftMetres, MathF.Abs(step.Y), 3);
            Assert.Equal(0f, step.X, 3);

            // there: up, crouched, on to the play's point at the rush speed — the move's own intent again
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = step;
            StepAt(w, 28.5);
            Assert.Equal(UnderFirePhase.Moving, Reflex(w).Phase);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Stance(w));
            Assert.Equal(move.Destination, Intent(w).FinalDestination);
            Assert.Equal(UnderFire.RushSpeed, Intent(w).TargetSpeed);
            Assert.Equal(0u, Reflex(w).HoldIntentId);

            // and not down again straight away: one rush first
            ShotAt(w, 29.0);
            StepAt(w, 29.1);
            Assert.Equal(UnderFirePhase.Moving, Reflex(w).Phase);
        }

        /// <summary>
        /// ⭐ AQ96 C3 — in a squad, his half COVERED by the other half's fire: he rises even with rounds still coming (that is what covering
        /// fire is for); alone, the same fire keeps him down. ✅ Red-proof: the covered test removed ⇒ he stays down ⇒ red.
        /// </summary>
        [Fact]
        public void AQ96_CoveredByHisHalf_HeRisesUnderFire_AloneHeStaysDown()
        {
            var w = new World();
            Mover(w, new Vector3(50f, 0f, 0f));
            ShotAt(w, 20.0);
            StepAt(w, 20.1);
            ShotAt(w, 21.0);
            StepAt(w, 21.1);
            Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);   // alone: still down

            UnderFire.MarkCovered(w.Repo, w.Unit, 21.2);
            ShotAt(w, 21.3);
            StepAt(w, 21.4);
            Assert.Equal(UnderFirePhase.Shifting, Reflex(w).Phase);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2343</c> (R-2066 C) — he drops short of his post, crawls to the cover point near him (a berm's safe face, facing the
        /// enemy to the north) and, when the fire lapses, does NOT rise and walk back to a post just past the berm in the enemy's view: the
        /// move ends at his cover, crouched. 📌 Live on <c>pc-defend-house</c>: crawl round the corner to safety, up, back, down — again.
        /// The control: a destination on his side of the cover ⇒ the side step and the rise as before. ✅ Red-proof: the
        /// <c>Exposed</c> check dropped ⇒ he shifts and rises back toward the post.
        /// </summary>
        [Fact]
        public unsafe void CE2343_AfterACrawlToCover_HeDoesNotRiseBackIntoTheEnemysView()
        {
            World Crawled(Vector3 post)
            {
                var w = new World();
                w.Repo.SetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>(new Fdp.Toolkit.Spatial.Eqs.ManualCoverProvider(new[]
                {
                    new Fdp.Toolkit.Spatial.Eqs.CoverPoint { PositionX = 2f, PositionY = 2.25f, DirectionX = 0f, DirectionY = 1f, Quality = 1f, StanceHeight = 1 },
                }));
                w.Contact(armed: true);
                ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
                mem.PositionsX[0] = 0f; mem.PositionsY[0] = 100f;   // the enemy, north
                Mover(w, post);
                ShotAt(w, 20.0);
                StepAt(w, 20.1);
                StepAt(w, 20.2);
                Assert.Equal(UnderFirePhase.Crawling, Reflex(w).Phase);
                w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(2f, 2.25f, 0f);
                StepAt(w, 21.0);
                Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);
                StepAt(w, 23.2);   // 3 s without a round
                return w;
            }

            var w = Crawled(new Vector3(0f, 6f, 0f));   // the post: past the berm, in the enemy's view
            Assert.Equal(UnderFirePhase.Moving, Reflex(w).Phase);
            Assert.Equal(new Vector3(2f, 2.25f, 0f), Intent(w).FinalDestination);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Stance(w));

            var c = Crawled(new Vector3(6f, 0f, 0f));   // the post: on his side of it
            Assert.Equal(UnderFirePhase.Shifting, Reflex(c).Phase);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2363</c> (AQ102 R2-3) — pinned in the open under steady fire with no cover near, he does not lie on one spot: after a
        /// seeded 6–12 s down he scoots 2–4 m square to the line to his shooter (north ⇒ along x), down again there, and the next scoot goes
        /// the other way. 🔒 User: <i>"each of them would fire a few shots and then take a pause and move to a different spot. In the open
        /// area, that means the guy would be crawling"</i>. ✅ Red-proof: the scoot branch removed ⇒ he stays Down ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2363_PinnedInTheOpen_HeScootsSideways_OnAlternatingSides()
        {
            var w = new World();
            w.Contact(armed: true);
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            mem.PositionsX[0] = 0f; mem.PositionsY[0] = 100f;   // the shooter, north
            Mover(w, new Vector3(0f, 50f, 0f));
            ShotAt(w, 20.0);
            StepAt(w, 20.1);
            Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);

            double ScootDue(out float side)
            {
                double down = Reflex(w).DownSince, after = UnderFire.ScootAfter(w.Unit, down);
                Assert.InRange(after, UnderFire.ScootAfterMinSeconds, UnderFire.ScootAfterMaxSeconds);
                for (double t = down + 0.5; t < down + after - 0.05; t += 0.5)   // a round every half second: still down, no scoot yet
                {
                    ShotAt(w, t);
                    StepAt(w, t + 0.01);
                    Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);
                }
                ShotAt(w, down + after);
                StepAt(w, down + after + 0.01);
                Assert.Equal(UnderFirePhase.Scooting, Reflex(w).Phase);
                Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, Stance(w));
                var at = w.Repo.GetComponentRO<SimTransform>(w.Unit).Position;
                var off = Reflex(w).SubTarget - at;
                var toShooter = Vector3.Normalize(new Vector3(0f, 100f, 0f) - at);
                Assert.Equal(0f, Vector3.Dot(off, toShooter), 3);                    // square to the line to the shooter
                Assert.InRange(off.Length(), UnderFire.ScootMinMetres, UnderFire.ScootMaxMetres);
                Assert.Equal(Reflex(w).SubTarget, Intent(w).FinalDestination);
                side = MathF.Sign(off.X);
                // there: down again, his play's move still in hand
                w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = Reflex(w).SubTarget;
                ShotAt(w, down + after + 0.5);
                StepAt(w, down + after + 0.6);
                Assert.Equal(UnderFirePhase.Down, Reflex(w).Phase);
                return down + after;
            }

            ScootDue(out float first);
            ScootDue(out float second);
            Assert.Equal(-first, second);
        }

        /// <summary>
        /// ⭐ <c>CE-2363</c> (AQ102 R2-3) — pinned 30 s with no friend within 30 m, his next move is a crawl to cover out to 30 m (20 m here,
        /// beyond the 10 m crawl); a friend 15 m off and he scoots instead. ✅ Red-proof: the widened search removed ⇒ he only scoots ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2363_PinnedAloneThirtySeconds_CrawlsToCoverOutTo30m_WithAFriendNearHeScoots()
        {
            World Pinned(bool friend, out double crawlAt)
            {
                var w = new World();
                w.Repo.SetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>(new Fdp.Toolkit.Spatial.Eqs.ManualCoverProvider(new[]
                {
                    new Fdp.Toolkit.Spatial.Eqs.CoverPoint { PositionX = 20f, PositionY = 0f, DirectionX = 0f, DirectionY = 1f, Quality = 1f, StanceHeight = 1 },
                }));
                w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Friend });
                if (friend)
                {
                    var f = w.Repo.CreateEntity();
                    w.Repo.AddComponent(f, new EntityInfo { ForceId = ForceId.Friend });
                    w.Repo.AddComponent(f, new SimTransform { Position = new Vector3(-15f, 0f, 0f), Rotation = Quaternion.Identity });
                    w.Repo.AddComponent(f, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
                }
                w.Contact(armed: true);
                ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
                mem.PositionsX[0] = 0f; mem.PositionsY[0] = 100f;
                Mover(w, new Vector3(0f, 50f, 0f));
                ShotAt(w, 20.0);
                StepAt(w, 20.1);
                crawlAt = -1;
                for (double t = 20.5; t < 75.0; t += 0.5)
                {
                    ShotAt(w, t);
                    StepAt(w, t + 0.01);
                    var phase = Reflex(w).Phase;
                    if (phase == UnderFirePhase.Crawling) { crawlAt = t + 0.01; break; }
                    if (phase == UnderFirePhase.Scooting) w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = Reflex(w).SubTarget;
                }
                return w;
            }

            var alone = Pinned(friend: false, out double crawlAt);
            Assert.True(crawlAt >= 20.1 + UnderFire.AlonePinnedSeconds, $"crawled at {crawlAt}");
            Assert.Equal(new Vector3(20f, 0f, 0f), Reflex(alone).SubTarget);
            Assert.True(Reflex(alone).SubSeconds >= 18f / UnderFire.CrawlSpeed, $"{Reflex(alone).SubSeconds} s for the far crawl");
            Assert.True(Reflex(alone).Scoots > 0);   // he scooted while he waited

            var near = Pinned(friend: true, out double never);
            Assert.Equal(-1, never);
            Assert.True(Reflex(near).Scoots >= 3, $"{Reflex(near).Scoots} scoots");
        }

        // ── CE-2368 (AQ103, R-2074) — idle life: a stationary person shifts a little ─────────────────────────────────────────────

        /// <summary>A person standing (or lying) still at the origin with the SOP's idle choice: a stance, a move intent, the senses.</summary>
        private static void Idler(World w, Fdp.Toolkit.Tkb.Domain.StanceId stance = Fdp.Toolkit.Tkb.Domain.StanceId.Standing,
                                  BehaviorOrigin origin = BehaviorOrigin.Sop, string behaviour = BehaviorNames.Idle)
        {
            w.Repo.AddComponent(w.Unit, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = stance });
            w.Repo.AddComponent(w.Unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Navigation.NavigationIntent());
            w.Repo.AddComponent(w.Unit, new RecentSenses());
            ref var task = ref w.Repo.GetComponentRW<BehaviorState>(w.Unit);
            task.ActiveBehaviorHash = BehaviorHash.FromName(behaviour);
            task.Origin = origin;
        }

        private static Restless Settle(World w) => w.Repo.GetComponentRO<Restless>(w.Unit);

        /// <summary>His shift arrives (the muscle reports on the reflex's intent) and he stands where it went.</summary>
        private static void ShiftArrives(World w)
        {
            var to = Intent(w).FinalDestination;
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = to;
            if (!w.Repo.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(w.Unit))
                w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Navigation.NavigationStatus());
            w.Repo.SetComponent(w.Unit, new Fdp.Toolkit.Navigation.NavigationStatus { IntentId = Intent(w).IntentId, Result = Fdp.Toolkit.Navigation.NavigationResult.Arrived });
        }

        private static void IdleAt(World w, double t)
        {
            w.Repo.SetSingleton(new GlobalTime { TotalTime = t, DeltaTime = 0.016f, TimeScale = 1f });
            Restlessness.Step(w.Repo, w.Unit, t);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2368</c> (AQ103 A/C) — a soldier on the SOP's idle choice, still 6 s, takes a 0.5–2 m step (his own intent, the play's
        /// channel untouched), then waits a seeded 8–20 s for the next — and across many shifts never strays past 3 m from where he settled.
        /// 🔒 User: <i>"Any entity which is just standing doing nothing is feeling weird … it should move a little bit here and there"</i>.
        /// ✅ Red-proof: the shift removed ⇒ no intent is written ⇒ red.
        /// </summary>
        [Fact]
        public void CE2368_AnIdleSoldier_StepsALittle_NowAndThen_AndNeverStraysPast3m()
        {
            var w = new World();
            Idler(w);
            IdleAt(w, 0.0);
            uint before = Intent(w).IntentId;
            IdleAt(w, Restlessness.StillSeconds - 0.1);
            Assert.Equal(before, Intent(w).IntentId);                       // not before he has been still a while
            IdleAt(w, Restlessness.StillSeconds + 0.1);
            Assert.NotEqual(before, Intent(w).IntentId);
            Assert.Equal(Settle(w).HoldIntentId, Intent(w).IntentId);       // the reflex's own intent
            Assert.InRange(Intent(w).FinalDestination.Length(), Restlessness.StepMinMetres - 1e-3f, Restlessness.StepMaxMetres + 1e-3f);
            Assert.Equal(0, w.Repo.GetComponentRO<LocomotionChannel>(w.Unit).ActiveAction);   // the play's channel untouched

            double t = Restlessness.StillSeconds + 0.2;
            for (int shift = 0; shift < 12; shift++)
            {
                ShiftArrives(w);
                IdleAt(w, t);
                var r = Settle(w);
                Assert.Equal(0, r.Moving);
                double wait = r.NextAt - t;
                Assert.InRange(wait, Restlessness.StandMinSeconds, Restlessness.StandMaxSeconds);
                IdleAt(w, r.NextAt + 0.01);
                Assert.Equal(1, Settle(w).Moving);
                Assert.True(Intent(w).FinalDestination.Length() <= Restlessness.TetherMetres + 1e-3f,
                            $"shift {shift} to {Intent(w).FinalDestination}, past the 3 m tether");
                t = r.NextAt + 0.02;
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2368</c> (AQ103 B) — the ONE stay-still rule: each hold keeps him still; the SOP's own idle choice does not (the default).
        /// ✅ Red-proof: each clause of <see cref="RestlessHolds.Holds"/> dropped in turn ⇒ its row goes red.
        /// </summary>
        [Theory]
        [InlineData("default", RestlessHold.None)]
        [InlineData("ordered hold", RestlessHold.OrderedToStay)]
        [InlineData("dead", RestlessHold.Down)]
        [InlineData("possessed", RestlessHold.Possessed)]
        [InlineData("ambush", RestlessHold.HiddenOnPurpose)]
        [InlineData("aiming", RestlessHold.WeaponUp)]
        [InlineData("on a move", RestlessHold.Moving)]
        [InlineData("on an upright move", RestlessHold.Moving)]
        [InlineData("sentry", RestlessHold.OrderedToStay)]
        public void CE2368_TheStayStillRule_EachHoldKeepsHimStill_TheDefaultDoesNot(string why, RestlessHold expected)
        {
            var w = new World();
            switch (why)
            {
                case "ordered hold": Idler(w, origin: BehaviorOrigin.Superior); break;
                case "possessed": Idler(w, origin: BehaviorOrigin.Possession); break;
                case "ambush": Idler(w, behaviour: BehaviorNames.Ambush, origin: BehaviorOrigin.Superior); break;
                case "sentry": Idler(w, behaviour: "Sentry", origin: BehaviorOrigin.Sop); break;
                default: Idler(w); break;
            }
            if (why == "dead") { w.Arm(); w.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(w.Unit).Current = 0f; }
            if (why == "aiming")
            {
                if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>()) w.Repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
                w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed });
            }
            if (why == "on a move") Mover(w, new Vector3(30f, 0f, 0f));
            if (why == "on an upright move") Mover(w, new Vector3(30f, 0f, 0f), gait: MoveGait.Upright);   // the channel alone (the reflex keeps no upright move)
            Assert.Equal(expected, RestlessHolds.Holds(w.Repo, w.Unit));

            IdleAt(w, 0.0);
            uint before = Intent(w).IntentId;
            IdleAt(w, Restlessness.StillSeconds + 0.1);
            Assert.Equal(expected == RestlessHold.None, Intent(w).IntentId != before);
        }

        /// <summary>
        /// ⭐ <c>CE-2368</c> (AQ103 C/D) — lying calm he crawls 0.5–1.5 m; lying UNDER FIRE he scoots 2–4 m square to the shooter (R2-3's rule),
        /// even with his weapon up (the pinned man's scoot is made firing). ✅ Red-proof: the under-fire branch dropped ⇒ a calm crawl ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2368_LyingHeCrawls_UnderFireHeScootsSquareToTheShooter()
        {
            var calm = new World();
            Idler(calm, Fdp.Toolkit.Tkb.Domain.StanceId.Prone);
            IdleAt(calm, 0.0);
            IdleAt(calm, Restlessness.StillSeconds + 0.1);
            Assert.InRange(Intent(calm).FinalDestination.Length(), Restlessness.CrawlMinMetres - 1e-3f, Restlessness.CrawlMaxMetres + 1e-3f);

            var shot = new World();
            Idler(shot, Fdp.Toolkit.Tkb.Domain.StanceId.Prone);
            shot.Contact(armed: true);
            ref var mem = ref shot.Repo.GetComponentRW<TargetMemory>(shot.Unit);
            mem.PositionsX[0] = 0f; mem.PositionsY[0] = 100f;   // the shooter, north
            if (!shot.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>()) shot.Repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            shot.Repo.AddComponent(shot.Unit, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed });
            IdleAt(shot, 0.0);
            ShotAt(shot, Restlessness.StillSeconds);
            IdleAt(shot, Restlessness.StillSeconds + 0.1);
            var to = Intent(shot).FinalDestination;
            Assert.Equal(0f, to.Y, 3);                                     // square to the line to the shooter
            Assert.InRange(MathF.Abs(to.X), UnderFire.ScootMinMetres, UnderFire.ScootMaxMetres);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2368</c> (AQ103 I) — a relaxed man with nothing to do shows an IDLE gesture (the flag the IG animates: smoking, shifting
        /// weight…) within the first minute, holds it a while, and a round near him clears it within a second; a gesture his BEHAVIOUR set
        /// (hands up) is never touched. 🔒 User: <i>"This might need some flag that the IG can run an animation in such a case"</i>.
        /// ✅ Red-proof: the calm test dropped ⇒ the idle gesture survives the near miss ⇒ red.
        /// </summary>
        [Fact]
        public void CE2368_ARelaxedIdleMan_ShowsAnIdleGesture_AThreatClearsIt_AnActIsNeverTouched()
        {
            var w = new World();
            Idler(w);
            double t = 0, shownAt = -1;
            for (; t < 60.0; t += 0.5)
            {
                IdleAt(w, t);
                if (Fdp.Toolkit.Combat.Gestures.IsIdle(Fdp.Toolkit.Combat.Gestures.Of(w.Repo, w.Unit))) { shownAt = t; break; }
            }
            Assert.True(shownAt >= 0, "no idle gesture within a minute of idling");
            ShotAt(w, t + 0.5);
            IdleAt(w, t + 0.6);
            IdleAt(w, t + 1.7);
            Assert.Equal(Fdp.Toolkit.Combat.GestureKind.None, Fdp.Toolkit.Combat.Gestures.Of(w.Repo, w.Unit));

            // ⭐ a man merely STILL inside another play (a protester resting between his fists) is not idle: no idle gesture
            var busy = new World();
            Idler(busy, behaviour: BehaviorNames.CombatPosture);
            for (double s = 0; s < 90.0; s += 0.5) IdleAt(busy, s);
            Assert.Equal(Fdp.Toolkit.Combat.GestureKind.None, Fdp.Toolkit.Combat.Gestures.Of(busy.Repo, busy.Unit));

            var act = new World();
            Idler(act);
            Fdp.Toolkit.Combat.Gestures.Set(act.Repo, act.Unit, Fdp.Toolkit.Combat.GestureKind.HandsUp);
            for (double s = 0; s < 90.0; s += 0.5) IdleAt(act, s);
            Assert.Equal(Fdp.Toolkit.Combat.GestureKind.HandsUp, Fdp.Toolkit.Combat.Gestures.Of(act.Repo, act.Unit));
        }

        /// <summary>
        /// ⭐ <c>CE-2368</c> — lying under steady fire his scoots (2–4 m, sides alternating) would drift him off; the 3 m tether holds every one of
        /// them. ✅ Red-proof: the clamp dropped ⇒ a scoot ends past 3 m ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2368_PinnedHisScootsStayWithin3mOfWhereHeSettled()
        {
            var w = new World();
            Idler(w, Fdp.Toolkit.Tkb.Domain.StanceId.Prone);
            w.Contact(armed: true);
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            mem.PositionsX[0] = 0f; mem.PositionsY[0] = 100f;
            IdleAt(w, 0.0);
            double t = 0.0;
            for (int scoot = 0; scoot < 20; scoot++)
            {
                for (int i = 0; i < 30 && Settle(w).Moving == 0; i++) { t += 0.5; ShotAt(w, t); IdleAt(w, t + 0.01); }
                Assert.Equal(1, Settle(w).Moving);
                var to = Intent(w).FinalDestination;
                Assert.True(new Vector2(to.X, to.Y).Length() <= Restlessness.TetherMetres + 1e-3f, $"scoot {scoot} to {to}, past the 3 m tether");
                ShiftArrives(w);
                t += 0.5; ShotAt(w, t); IdleAt(w, t + 0.01);
            }
        }

        /// <summary>⭐ <c>CE-2368</c> — moved by anything else (a play's move, a push), he settles anew where he stands: the tether follows him,
        /// and the 6 s of stillness start again.</summary>
        [Fact]
        public void CE2368_MovedByAnythingElse_HeSettlesAnew()
        {
            var w = new World();
            Idler(w);
            IdleAt(w, 0.0);
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(20f, 0f, 0f);
            IdleAt(w, 3.0);
            Assert.Equal(new Vector3(20f, 0f, 0f), Settle(w).Anchor);
            uint before = Intent(w).IntentId;
            IdleAt(w, 3.0 + Restlessness.StillSeconds - 0.1);
            Assert.Equal(before, Intent(w).IntentId);
        }

        /// <summary>AQ96 C — no drop while arriving at his point (within 3 m — he is getting into his cover), and a civilian's upright run
        /// is never kept by the reflex.</summary>
        [Fact]
        public void AQ96_NoDropWhenArriving_AndAnUprightRunIsNotKept()
        {
            var w = new World();
            Mover(w, new Vector3(2f, 0f, 0f));
            ShotAt(w, 20.0);
            StepAt(w, 20.1);
            Assert.Equal(UnderFirePhase.Moving, Reflex(w).Phase);

            var c = new World();
            Mover(c, new Vector3(50f, 0f, 0f), gait: MoveGait.Upright);
            Assert.False(c.Repo.HasComponent<UnderFireMove>(c.Unit));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2318</c> (Injury States I4) — A MAN DOWN IS NO THREAT AND NO TARGET. An enemy who is incapacitated (alive, bleeding,
        /// below the band) is dropped as the top threat even while he is the CURRENT one and nothing else scores — the case live, where
        /// soldiers kept firing at a man lying wounded — and a mover no longer crouches for him. Wounded he is still fought.
        /// ✅ Red-proof: <c>Engageable</c> back to <c>CombatLife.IsAlive</c> ⇒ he stays the threat ⇒ red.
        /// </summary>
        [Fact]
        public void CE2318_AnIncapacitatedEnemy_IsNoLongerTheThreat_NorAThreatToMoveFrom()
        {
            var w = new World();
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.InjuryThresholds>())
                w.Repo.RegisterComponent<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
            w.Repo.AddComponent(w.Unit, new Hrot.MuscleCharacter.Animation.Components.StanceIntent());
            var enemy = w.Contact(armed: true);
            w.Repo.AddComponent(enemy, new Fdp.Toolkit.Combat.Components.Health { Current = 20f, Max = 100f });   // below 0.3: down
            w.Repo.AddComponent(enemy, Fdp.Toolkit.Combat.Translators.InjuryTkbTranslator.Thresholds(new Fdp.Toolkit.Tkb.Domain.InjuryDefDto()));

            Assert.True(Fdp.Toolkit.Combat.CombatLife.IsAlive(w.Repo, enemy));                          // alive — he bleeds
            Assert.False(EqsTacticsNodes.TopThreat(w.Repo, w.Unit, enemy, out _));                        // but not the threat
            Assert.True(LocomotionMoveTo.Issue(w.Repo, w.Unit, new Vector3(10f, 0f, 0f), 3f, 1f, MoveGait.Tactical));
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing,
                w.Repo.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(w.Unit).TargetStance);

            w.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(enemy).Current = 50f;            // wounded: still a fighter
            Assert.True(EqsTacticsNodes.TopThreat(w.Repo, w.Unit, enemy, out var threat));
            Assert.Equal(enemy, threat);
        }

        // ── ⭐ CE-2297 LD-3 — who points the eyes (docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.3a) ─────────────────

        private static void CanLook(World w)
        {
            if (!w.Repo.IsComponentTypeRegistered<LookAtChannel>()) w.Repo.RegisterComponent<LookAtChannel>();
            w.Repo.AddComponent(w.Unit, new LookAtChannel());
            if (!w.Repo.HasComponent<SimTransform>(w.Unit))
                w.Repo.AddComponent(w.Unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        }

        private static unsafe Vector3 LookPoint(World w)
        {
            ref readonly var ch = ref w.Repo.GetComponentRO<LookAtChannel>(w.Unit);
            Assert.Equal(LookAtActionIds.LookAtPoint, ch.ActiveAction);
            LookAtPointParams p;
            fixed (byte* src = ch.Params) p = *(LookAtPointParams*)src;
            return new Vector3(p.WorldPointX, p.WorldPointY, p.WorldPointZ);
        }

        private static float AngleOff(float a, float b) => MathF.Abs(((a - b) % 360f + 540f) % 360f - 180f);

        /// <summary>⭐ The compass and the scan: 0 = north (+Y), clockwise; five fixations across ±half, ping-pong, each held a step.</summary>
        [Fact]
        public void CE2297_TheScan_FiveFixations_PingPong_OnTheCompass()
        {
            Assert.Equal(90f, LookAt.BearingTo(Vector3.Zero, new Vector3(10, 0, 0)), 3);
            Assert.Equal(180f, LookAt.BearingTo(Vector3.Zero, new Vector3(0, -10, 0)), 3);
            var p = LookAt.PointAt(Vector3.Zero, 90f, 100f);
            Assert.Equal(100f, p.X, 3);
            Assert.Equal(0f, p.Y, 3);
            Assert.Equal(90f, LookAt.FacingBearing(Quaternion.Identity), 3);               // the body's forward is +X ⇒ east
            float[] expected = { 60, 75, 90, 105, 120, 105, 90, 75, 60 };
            for (int k = 0; k < expected.Length; k++)
                Assert.Equal(expected[k], LookAt.ScanBearing(90f, 30f, 2f, k * 2.0 + 0.5), 3);
        }

        /// <summary>⭐⭐ LD-3 in the posture every holding man runs: a threat takes his eyes (the channel follows the entity, this run
        /// stamped on it). ✅ Red-proof: the Look node removed from CombatPosture's decide Parallel ⇒ the channel stays empty.</summary>
        [Fact]
        public unsafe void CE2297_CombatPosture_LooksAtItsTopThreat()
        {
            var w = new World();
            w.Arm();
            CanLook(w);
            var enemy = w.Contact(armed: true);
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
            ref readonly var ch = ref w.Repo.GetComponentRO<LookAtChannel>(w.Unit);
            Assert.Equal(LookAtActionIds.LookAtEntity, ch.ActiveAction);
            Assert.Equal(w.Repo.GetComponentRO<BehaviorState>(w.Unit).InstanceId, ch.BehaviorInstanceId);
            Assert.Equal((uint)enemy.Index, Unsafe.As<byte, LookAtEntityParams>(ref Unsafe.AsRef(in ch.Params[0])).TargetEntityId);
        }

        /// <summary>⭐ LD-3b / LD-3c — with no threat his SECTOR points his eyes: the watch bearing, stepped across ±30°; with no sector
        /// either, a look this run wrote is released. ✅ Red-proof: the scan step removed ⇒ the point never moves.</summary>
        [Fact]
        public void CE2297_NoThreat_TheSectorPointsTheEyes_Scanning_ThenReleased()
        {
            var w = new World();
            w.Arm();
            CanLook(w);
            Assert.True(Fdp.Toolkit.Behavior.Systems.EngagementAreaSystem.Apply(w.Repo, new Fdp.Toolkit.Behavior.Components.SetEngagementAreaEvent
                { Entity = w.Unit, Origin = BehaviorOrigin.Superior, WatchSet = 1, WatchBearing = 90f }));
            w.Order("CombatPosture", Objective);
            for (int i = 0; i < 4; i++) w.Tick();
            var first = LookPoint(w);
            Assert.InRange(LookAt.BearingTo(Vector3.Zero, first), 60f - 0.5f, 120f + 0.5f);   // east, within ±30°
            for (int i = 0; i < 130; i++) w.Tick();                                          // > one 2 s step
            Assert.NotEqual(first, LookPoint(w));                                            // the scan moved on

            Assert.True(Fdp.Toolkit.Behavior.Systems.EngagementAreaSystem.Apply(w.Repo, new Fdp.Toolkit.Behavior.Components.SetEngagementAreaEvent
                { Entity = w.Unit, Origin = BehaviorOrigin.Superior }));                    // withdrawn
            w.Tick();
            Assert.Equal(LookAtActionIds.ReleaseLook, w.Repo.GetComponentRO<LookAtChannel>(w.Unit).ActiveAction);
        }

        /// <summary>LD-3d — a look another run wrote is NOT released by this one (a reaction's look stays its own).</summary>
        [Fact]
        public void CE2297_ALookAnotherRunWrote_IsNotReleased()
        {
            var w = new World();
            CanLook(w);
            Assert.True(LookRequest.AtPoint(w.Repo, w.Unit, new Vector3(0, 50, 0), behaviorInstance: 9999));
            var p = new LookParams();
            var ws = new LookNodeState { Source = LookChoice.Sector };
            Assert.Equal(LookChoice.Released, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 1.0));
            Assert.Equal(LookAtActionIds.LookAtPoint, w.Repo.GetComponentRO<LookAtChannel>(w.Unit).ActiveAction);
        }

        /// <summary>LD-3b — SectorOnly: a threat elsewhere does not take a security man's eyes off his sector.</summary>
        [Fact]
        public void CE2297_SectorOnly_KeepsTheSector_WhenAThreatIsKnown()
        {
            var w = new World();
            w.Arm();
            CanLook(w);
            w.Contact(armed: true);
            Assert.True(Fdp.Toolkit.Behavior.Systems.EngagementAreaSystem.Apply(w.Repo, new Fdp.Toolkit.Behavior.Components.SetEngagementAreaEvent
                { Entity = w.Unit, Origin = BehaviorOrigin.Superior, WatchSet = 1, WatchBearing = 0f }));
            var p = new LookParams { Mode = LookMode.SectorOnly, HalfWidth = 0.001f };
            var ws = new LookNodeState();
            Assert.Equal(LookChoice.Sector, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 0.0));
            var at = LookPoint(w);
            Assert.True(AngleOff(LookAt.BearingTo(Vector3.Zero, at), 0f) < 1f);   // north
        }

        /// <summary>⭐ LD-5 ④'s half on the brain — a shot HEARD behind him (an anonymous memory slot, which <c>TopThreat</c> never
        /// ranks) takes his eyes to the sound while it is fresh; once older than the window his order's bearing has them again.
        /// ✅ Red-proof: the heard source removed ⇒ he keeps looking north.</summary>
        [Fact]
        public unsafe void CE2297_AHeardShot_TurnsTheEyesToTheSound_WhileItIsFresh()
        {
            var w = new World();
            CanLook(w);
            w.Hear(0f, -50f, 5f);                                                           // behind him (south)
            w.Repo.GetComponentRW<TargetMemory>(w.Unit).LastSeenTick[0] = Fdp.Toolkit.Perception.PerceptionClock.Now(w.Repo);   // CE-3292: the step clock
            var p = new LookParams { BearingSet = 1, Bearing = 0f };                        // ordered to watch north
            var ws = new LookNodeState();
            Assert.Equal(LookChoice.Heard, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 0.0));
            Assert.True(AngleOff(LookAt.BearingTo(Vector3.Zero, LookPoint(w)), 180f) < 1f);
            var clock = w.Repo.GetSingletonUnmanaged<GlobalTime>();                          // the sound is old now (CE-3292: in sim steps)
            clock.FrameNumber += LookAt.HeardWindowTicks + 1;
            w.Repo.SetSingletonUnmanaged(clock);
            Assert.Equal(LookChoice.Bearing, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 0.0));
            Assert.True(AngleOff(LookAt.BearingTo(Vector3.Zero, LookPoint(w)), 0f) < 1f);
        }

        /// <summary>⭐ <c>CE-2356</c> (AQ102 C, R-2072) — a threat IN SIGHT is followed; once out of sight he is looked for where he is
        /// remembered, at the eyes of the stance he was seen in — never followed through the wall. 📌 User, 2026-10-23 (a defender's head
        /// tracked a player under the wall). 🔴 Red-proof: drop the InSightNow branch (always AtEntity) ⇒ the second half is red.</summary>
        [Fact]
        public unsafe void CE2356_AThreatOutOfSight_IsLookedForWhereRemembered_NotFollowed()
        {
            var w = new World();
            w.Arm();
            CanLook(w);
            var enemy = w.Contact(armed: true);
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            mem.PositionsX[0] = 5f; mem.PositionsY[0] = 20f; mem.PositionsZ[0] = 0f;
            mem.SeenStance[0] = (byte)Fdp.Toolkit.Tkb.Domain.StanceId.Crouched + 1;
            mem.LastSeenTick[0] = 100;
            var p = new LookParams();
            var ws = new LookNodeState();
            w.Repo.SetSingleton(new GlobalTime { TotalTime = 2.0, FrameNumber = 110 });   // seen 10 ticks ago: in sight
            Assert.Equal(LookChoice.Threat, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 2.0));
            Assert.Equal(LookAtActionIds.LookAtEntity, w.Repo.GetComponentRO<LookAtChannel>(w.Unit).ActiveAction);
            w.Repo.SetSingleton(new GlobalTime { TotalTime = 5.0, FrameNumber = 300 });   // 200 ticks ago: out of sight
            Assert.Equal(LookChoice.Threat, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 5.0));
            Assert.Equal(new Vector3(5f, 20f, 1.0f), LookPoint(w));                       // the remembered spot, crouched eyes
            Assert.False(enemy.IsNull);
        }

        /// <summary>⭐ <c>CE-2356</c> (AQ102 B) — a post at an OPENING (window shape, frame) is one PeekAndFire fights from; a plain cover post
        /// and an unposted man are not. 🔴 Red-proof: return Success for any post ⇒ the cover-post half is red.</summary>
        [Fact]
        public void CE2356_AtOpeningPost_IsAWindowOrFramePost_NotACoverPost()
        {
            var w = new World();
            var window = new PeekAndFireParams();
            CombatPostureBehavior.Post(new Hrot.Map.Definitions.Behavior.CombatPosturePostJsonDto { Point = new Vector3(1, 2, 3), Radius = 4f, Window = true }, ref window);
            var cover = new PeekAndFireParams();
            CombatPostureBehavior.Post(new Hrot.Map.Definitions.Behavior.CombatPosturePostJsonDto { Point = new Vector3(1, 2, 0), Radius = 4f }, ref cover);
            var none = new PeekAndFireParams();
            Assert.Equal(Fbt.NodeStatus.Success, PostureNodes.AtOpeningPost(ref window, w.Unit, w.Repo));
            Assert.Equal(Fbt.NodeStatus.Failure, PostureNodes.AtOpeningPost(ref cover, w.Unit, w.Repo));
            Assert.Equal(Fbt.NodeStatus.Failure, PostureNodes.AtOpeningPost(ref none, w.Unit, w.Repo));
        }

        /// <summary>⭐ LD-3b — SHOT AT: with a threat in sight in front of him, a near miss turns his eyes to the sound of the shot behind
        /// him; without the near miss the seen threat keeps them. 📌 Found live in ld-sentry-surprise. ✅ Red-proof: the under-fire branch
        /// removed ⇒ he keeps looking at the threat.</summary>
        [Fact]
        public unsafe void CE2297_ShotAt_TheSoundOfTheShot_BeatsTheThreatInSight()
        {
            var w = new World();
            w.Arm();
            CanLook(w);
            var front = w.Contact(armed: true);                                             // a threat he sees
            w.Hear(0f, -40f, 5f);                                                           // a shot behind him
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            for (int i = 0; i < mem.Count; i++) if (TargetMemory.IsAnonymous(in mem, i)) mem.LastSeenTick[i] = w.Repo.SimulationTick;
            var p = new LookParams();
            var ws = new LookNodeState();
            Assert.Equal(LookChoice.Threat, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 0.0));    // not shot at: the threat in sight
            var senses = new RecentSenses();
            senses.Record(Fdp.Toolkit.Perception.Events.SensorChange.NearMiss, w.Repo.GetSingleton<GlobalTime>().TotalTime);
            w.Repo.AddComponent(w.Unit, senses);
            Assert.Equal(LookChoice.Heard, LookAt.Want(w.Repo, w.Unit, in p, ref ws, 0.0));     // shot at: the sound of the shot
            Assert.True(AngleOff(LookAt.BearingTo(Vector3.Zero, LookPoint(w)), 180f) < 1f);
        }

        /// <summary>⭐ LD-3 — the operator's HOLD carries his eyes: a sector set on him (the ROE-twin event, as the debug route raises it)
        /// points them, scanned ±30° around its watch bearing. ✅ Red-proof: PersonHold without its Look activity ⇒ the channel stays empty.</summary>
        [Fact]
        public void CE2297_Hold_TheSectorPointsTheEyes()
        {
            var w = new World();
            CanLook(w);
            Assert.True(Fdp.Toolkit.Behavior.Systems.EngagementAreaSystem.Apply(w.Repo, new Fdp.Toolkit.Behavior.Components.SetEngagementAreaEvent
                { Entity = w.Unit, Origin = BehaviorOrigin.Operator, WatchSet = 1, WatchBearing = 0f }));
            w.Order(BehaviorNames.PersonHold);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(BehaviorNames.PersonHold, w.TaskName);
            float bearing = LookAt.BearingTo(Vector3.Zero, LookPoint(w));
            Assert.True(AngleOff(bearing, 0f) <= 30.5f, $"looks north within the scan, at {bearing}");
        }

        // ── ⭐ CE-3170 — an ATTACK order: CombatPosture with a designated target (docs/DESIGN_Entity_Control.md C-5) ──────────

        private const long NamedId = 2000;

        /// <summary>The enemy an attack order names: alive, on the map at <paramref name="at"/>, known to the node by
        /// <see cref="NamedId"/>, and NOT in the unit's memory yet.</summary>
        private static Entity NamedEnemy(World w, Vector3 at)
        {
            var e = w.Repo.CreateEntity();
            w.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.Repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            map.Register(NamedId, e);
            w.Repo.SetSingletonManaged(map);
            return e;
        }

        /// <summary>The unit has SEEN <paramref name="e"/>: a visual memory slot, as perception writes on a sighting (this world runs
        /// no perception system), and a sensor track.</summary>
        private static unsafe void SeenBefore(World w, Entity e)
        {
            ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);
            mem.EntityIds[mem.Count] = (long)e.PackedValue;
            mem.Freshness[mem.Count] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
            mem.Modalities[mem.Count] = (byte)SensorModality.Visual;
            mem.Count++;
            w.See(e);
        }

        private static unsafe Entity AimedAt(World w)
        {
            ref readonly var ch = ref w.Repo.GetComponentRO<WeaponChannel>(w.Unit);
            if (ch.ActiveAction != Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire) return Entity.Null;
            fixed (byte* src = ch.Params) return ((Fdp.Toolkit.Combat.Executors.AimAndFireParams*)src)->Target;
        }

        [Fact]
        public void CE3170_AnAttackOrder_FightsTheNamedEnemy_NotTheNearerThreat_AndSucceedsWhenItIsDown()
        {
            var w = new World();
            w.Arm();
            var nearer = w.Contact(armed: false);                      // remembered, in sight
            var named = NamedEnemy(w, new Vector3(80f, 10f, 0f));      // NOT remembered: the order tells the unit
            w.Order("CombatPosture", "{\"target\":" + NamedId + ",\"assault\":true}");
            for (int i = 0; i < 4; i++) w.Tick();

            Assert.Equal("CombatPosture", w.TaskName);
            Assert.True(DesignatedTarget.Of(w.Repo, w.Unit, out var designated), "the run designates its target");
            Assert.Equal(named, designated);
            Assert.True(Fdp.Toolkit.Combat.HiddenTargetThrow.LastKnown(w.Repo, w.Unit, named, out var told), "the order TELLS the unit where it is");
            Assert.Equal(new Vector3(80f, 10f, 0f), told);
            Assert.Equal(named, AimedAt(w));                            // ⭐ whichever branch the decision picked — never the nearer one
            Assert.NotEqual(nearer, AimedAt(w));

            w.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(named).Current = 0f;    // down
            bool finished = false;
            Fbt.NodeStatus result = default;
            for (int i = 0; i < 3 && !finished; i++) { w.Tick(); finished = w.Finished(out result); }
            Assert.True(finished, "the order ends when its target is out of the fight");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        [Fact]
        public void CE3170_AdvancingOnTheNamedEnemy_HuntsItsLastKnownPoint_AndArrivingDoesNotEndTheOrder()
        {
            var w = new World();
            w.Arm();
            var named = NamedEnemy(w, new Vector3(80f, 10f, 0f));
            w.Repo.AddComponent(w.Unit, new TargetMemory());
            SeenBefore(w, named);                                       // one weak enemy in sight ⇒ the decision advances (two outnumber: Suppress)
            w.Order("CombatPosture", "{\"target\":" + NamedId + "}");
            for (int i = 0; i < 4; i++) w.Tick();

            Assert.Equal((byte)Fdp.Toolkit.Utility.Posture.AdvanceAndAttack, w.Winner("CombatPosture"));
            Assert.Equal(new Vector3(80f, 10f, 0f), w.Destination());   // the TARGET, not an authored objective
            Assert.Equal(named, AimedAt(w));

            w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // there — the fight goes on
            w.Tick();
            w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);

            unsafe
            {
                ref var mem = ref w.Repo.GetComponentRW<TargetMemory>(w.Unit);                    // it was seen further on
                for (int i = 0; i < mem.Count; i++)
                    if (mem.EntityIds[i] == (long)named.PackedValue) { mem.PositionsX[i] = 120f; mem.PositionsY[i] = 10f; }
            }
            w.Tick();
            w.Tick();
            Assert.Equal(new Vector3(120f, 10f, 0f), w.Destination());  // the move follows it
        }

        [Fact]
        public void CE3170_TheDesignation_BelongsToItsRun_ALaterBehaviourDoesNotInheritIt()
        {
            var w = new World();
            w.Arm();
            w.Contact(armed: false);
            NamedEnemy(w, new Vector3(80f, 10f, 0f));
            w.Order("CombatPosture", "{\"target\":" + NamedId + "}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.True(DesignatedTarget.Of(w.Repo, w.Unit, out _));

            w.Order("CombatPosture", Objective);                       // a new run, no target named
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.False(DesignatedTarget.Of(w.Repo, w.Unit, out _), "a new run starts with no designated target");
        }

        [Fact]
        public void CE3170_AnUnknownTarget_LeavesThePostureAsAuthored()
        {
            var w = new World();
            w.Arm();
            w.Order("CombatPosture", "{\"target\":4242,\"advance\":{\"Objective\":[200,0,0]}}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("CombatPosture", w.TaskName);
            Assert.False(DesignatedTarget.Of(w.Repo, w.Unit, out _));
            Assert.Equal(new Vector3(200f, 0f, 0f), w.Destination());
        }
}
}
