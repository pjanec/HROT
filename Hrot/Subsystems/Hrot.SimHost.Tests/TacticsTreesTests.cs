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

        private const string Crossing = "{\"sensor\":{\"RouteTo\":[300,300,0]},\"walk\":{\"X\":300,\"Y\":300,\"Speed\":1.5,\"ArrivalRadius\":3}}";

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
}
}
