using System;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Perception.Events;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2080</c> — the SHIPPED SOP (<c>Assets/BTrees/Library/Sop/BasicInfantrySop.btree.json</c>, also the BTree recipe) run
    /// end to end from the PRODUCTION registry through the real ingress and brain: a hit pauses the task with a cover
    /// reaction, the task restarts when it ends, and the same hit does not fire it again. 📄 <c>docs/DESIGN_Decision_Layer.md</c>
    /// §4.7.
    /// </summary>
    public sealed class BasicInfantrySopTests
    {
        private const string Sop = "BasicInfantrySop";

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Unit;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private readonly RecentSensesSystem _senses = new();
            private readonly ChannelArbitrationSystem _arbitration = new();
            private double _time;
            private long _frame;

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                // ⭐ SimulationTick runs AHEAD of perception's clock (GlobalTime.FrameNumber, CE-3292), as measured in the cluster harness
                //   (3272 vs 3028): a reader comparing LastSeenTick with SimulationTick reads every sighting as stale ⇒ red.
                for (int i = 0; i < 5000; i++) Repo.Tick();
                if (!Repo.Bus.IsRegistered<SensorChangedEvent>()) Repo.Bus.Register<SensorChangedEvent>();
                var geo = UtmTransform.AtOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain   = new BrainTickSystem(Registry);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.AddComponent(Unit, new LocomotionChannel());
                Repo.AddComponent(Unit, new WeaponChannel());
                Tick();
            }

            public BehaviorState Task => Repo.GetComponent<BehaviorState>(Unit);
            public string? TaskName => Registry.TryGetName(Task.ActiveBehaviorHash, out var n) ? n : null;

            /// <summary>One frame in production order: Input (senses, ingress) then Simulation (brain, arbitration).</summary>
            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                // ⭐ CE-3469 — the kernel's order (as CivilianHsmTests / ScenarioRulesTests, CE-7035): version, swap, and the last-tick view before the brains;
                //   without it a declared cross-entity read under HROT_READ_CHECK=1 has no tick-start copy (it threw) or a stale one (a target's Health never fell)
                if (Fdp.Core.ParallelReadCheck.HasDeclarations) Repo.Tick();
                Repo.Bus.SwapBuffers();
                if (Fdp.Core.ParallelReadCheck.HasDeclarations) Repo.CaptureLastTick();
                _senses.Execute(Repo, 0.016f);
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                _arbitration.Execute(Repo, 0.016f);
            }

            public void Run(double seconds) { for (double t = 0; t < seconds; t += 0.016) Tick(); }

            public void Sense(SensorChange what) => Repo.Bus.Publish(new SensorChangedEvent { Unit = Unit, What = what });

            /// <summary>⭐ D6 (<c>CE-2094</c>): the reactions are the REAL <c>TakeCover</c> / <c>FallBack</c> trees, which run while
            /// the unit remembers a threat — so a reaction test gives it one, and ends it by forgetting it.</summary>
            public unsafe void Remember(bool threat)
            {
                if (!Repo.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(Unit))
                    Repo.AddComponent(Unit, new Fdp.Toolkit.Perception.Components.TargetMemory());
                ref var mem = ref Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(Unit);
                if (!threat) { mem.Count = 0; return; }
                mem.EntityIds[0] = (long)Repo.CreateEntity().PackedValue;
                mem.Freshness[0] = 10f;
                mem.Count = 1;
            }

            /// <summary>⭐ AQ98 C — a hostile in his memory: <paramref name="seen"/> = seen just now (the Visual bit), else only heard of.</summary>
            public unsafe Entity RememberEnemy(bool seen, ForceId force = ForceId.Hostile)
            {
                if (!Repo.HasComponent<EntityInfo>(Unit)) Repo.AddComponent(Unit, new EntityInfo { ForceId = ForceId.Friend });
                var enemy = Repo.CreateEntity();
                Repo.AddComponent(enemy, new EntityInfo { ForceId = force });
                if (!Repo.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(Unit))
                    Repo.AddComponent(Unit, new Fdp.Toolkit.Perception.Components.TargetMemory());
                ref var mem = ref Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(Unit);
                mem.EntityIds[0] = (long)enemy.PackedValue;
                mem.Freshness[0] = 10f;
                mem.LastSeenTick[0] = Fdp.Toolkit.Perception.PerceptionClock.Now(Repo);   // as perception stamps it (CE-3292)
                mem.Modalities[0] = seen ? (byte)Fdp.Toolkit.Perception.Components.SensorModality.Visual : (byte)0;
                mem.Count = 1;
                return enemy;
            }

            /// <summary>Keeps the remembered enemy SEEN (the eye re-stamps it every tick in production).</summary>
            public unsafe void KeepSeeing()
            {
                ref var mem = ref Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(Unit);
                mem.LastSeenTick[0] = Fdp.Toolkit.Perception.PerceptionClock.Now(Repo);   // as perception stamps it (CE-3292)
            }

            public void Order(string behaviour)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = behaviour, JsonParams = "{}", Origin = BehaviorOrigin.Superior });

            public void StartSop(string name = Sop)
                => Repo.Bus.PublishManaged(new AssignSopEvent { Entity = Unit, BehaviorName = name, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
        }

        [Fact]
        public void CE2080_TheShippedSop_IsRegistered_AndIdlesTheUnitWhenItHasNoOrder()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId(Sop, out _), "the shipped SOP must be compiled and registered so a TKB template can name it");
            w.StartSop();
            w.Run(0.3);

            Assert.Equal(0, w.Repo.GetComponent<SopState>(w.Unit).SopFaulted);
            Assert.Equal("Idle", w.TaskName);                                  // its "Do when idle" row
            Assert.Equal(BehaviorOrigin.Sop, w.Task.Origin);
        }

        [Fact]
        public void CE2080_AHit_PausesTheTask_WithCover_TheTaskRestarts_AndTheSameHitDoesNotFireAgain()
        {
            var w = new World();
            w.Order("Idle");
            w.StartSop();
            w.Run(0.3);
            Assert.Equal(BehaviorOrigin.Superior, w.Task.Origin);

            w.Remember(true);
            w.Sense(SensorChange.Hit);
            w.Run(0.1);
            Assert.Equal("FightFromCover", w.TaskName);                        // row 1: hit → React(cover, Hit) — AQ98 A
            Assert.Equal(BehaviorOrigin.Reaction, w.Task.Origin);
            Assert.Equal(ReactionUrgency.Hit, w.Task.Urgency);
            Assert.Equal("Idle", BehaviorIngressSystem.PausedTaskOf(w.Repo, w.Unit)!.BehaviorName);

            w.Run(1.3);
            Assert.Equal("FightFromCover", w.TaskName);                        // the fight lasts while a threat is remembered (within 8 s)
            w.Remember(false);                                                 // … and ends when it is forgotten
            w.Run(0.1);
            Assert.Equal("Idle", w.TaskName);                                  // the task is back, at its own rank
            Assert.Equal(BehaviorOrigin.Superior, w.Task.Origin);
            uint run = w.Task.InstanceId;

            w.Run(1.0);                                                        // the hit is still within 3 s …
            Assert.Equal(run, w.Task.InstanceId);                              // … but older than this run: no second reaction
            Assert.Equal(0, w.Repo.GetComponent<SopState>(w.Unit).SopFaulted);
        }

        [Fact]
        public void CE2080_AContactUnderHoldFire_Withdraws_OtherwiseFightsFromCover()
        {
            var held = new World();
            held.Repo.AddComponent(held.Unit, new Roe { Fire = RoeFire.HoldFire, SetBy = BehaviorOrigin.Superior });
            held.Order("Idle");
            held.StartSop();
            held.Run(0.3);
            held.Remember(true);
            held.Sense(SensorChange.FirstThreat);
            held.Run(0.1);
            Assert.Equal("FallBack", held.TaskName);                           // row 2 (ROE ≤ HoldFire)

            var free = new World();
            free.Order("Idle");
            free.StartSop();
            free.Run(0.3);
            free.Remember(true);
            free.Sense(SensorChange.FirstThreat);
            free.Run(0.1);
            Assert.Equal("FightFromCover", free.TaskName);                     // row 3 (AQ98 A)
            Assert.Equal(ReactionUrgency.Contact, free.Task.Urgency);
        }
    
        // ── ⭐ CE-2322 (AQ97, R-2062) — every lone soldier reacts by default; "no response" is said explicitly ────────────────────

        /// <summary>⭐ AQ97 B — rounds passing near a lone soldier are reason enough: React(cover, UnderFire) — <c>FightFromCover</c> since AQ98 A. ✅ Red-proof: the
        /// near-miss row removed from the asset ⇒ he stays idle ⇒ red.</summary>
        [Fact]
        public void CE2322_ANearMiss_SendsALoneSoldierToCover()
        {
            var w = new World();
            w.StartSop();
            w.Run(0.3);
            Assert.Equal("Idle", w.TaskName);
            w.Remember(true);
            w.Sense(SensorChange.NearMiss);
            w.Run(0.1);
            Assert.Equal("FightFromCover", w.TaskName);
            Assert.Equal(ReactionUrgency.UnderFire, w.Task.Urgency);
        }

        /// <summary>⭐ <c>CE-7029</c> (City Battle R-1d) — a LONE soldier under a drone takes cover: React(<c>ShelterFromAir</c>, UnderFire) — the vehicle's
        /// own reaction. (A man under command gets it from his squad — <c>SquadManeuverTests.CE7029_*</c>.) ✅ Red-proof: the drone row removed ⇒ he
        /// stays idle ⇒ red.</summary>
        [Fact]
        public void CE7029_ADroneOverALoneSoldier_SendsHimToCover()
        {
            var w = new World();
            w.StartSop();
            w.Run(0.3);
            Assert.Equal("Idle", w.TaskName);
            w.Sense(SensorChange.AirThreat);
            w.Run(0.1);
            Assert.Equal("ShelterFromAir", w.TaskName);
            Assert.Equal(ReactionUrgency.UnderFire, w.Task.Urgency);
        }

        /// <summary>⭐ AQ97 A — under a live commander, or aboard a vehicle, his own rows stand aside: a hit orders nothing (his unit or his
        /// vehicle decides) and he gets no idle choice either. His commander gone, the same hit sends him to cover.
        /// ✅ Red-proof: the UnderCommand row removed ⇒ the hit sends the squad man to cover ⇒ red.</summary>
        [Fact]
        public void CE2322_UnderCommandOrAboard_HisOwnRowsStandAside_HisCommanderGone_TheyAreHis()
        {
            var w = new World();
            var commander = w.Repo.CreateEntity();
            w.Repo.AddComponent(w.Unit, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });
            w.Order("Idle");
            w.StartSop();
            w.Run(0.3);
            w.Remember(true);
            w.Sense(SensorChange.Hit);
            w.Run(0.1);
            Assert.Equal("Idle", w.TaskName);
            Assert.Equal(BehaviorOrigin.Superior, w.Task.Origin);

            w.Repo.DestroyEntity(commander);
            w.Run(3.5);                                                        // the first hit is stale now
            w.Sense(SensorChange.Hit);
            w.Run(0.1);
            Assert.Equal("FightFromCover", w.TaskName);

            var crew = new World();
            crew.Repo.AddComponent(crew.Unit, new IsEmbarkedTag { VehicleEntity = crew.Repo.CreateEntity() });
            crew.Order("Idle");
            crew.StartSop();
            crew.Run(0.3);
            crew.Remember(true);
            crew.Sense(SensorChange.Hit);
            crew.Run(0.1);
            Assert.Equal("Idle", crew.TaskName);
            Assert.Equal(BehaviorOrigin.Superior, crew.Task.Origin);
        }

        /// <summary>⭐⭐ <c>CE-2436</c> AU-6 (R-2115) — his commander is a UNIT below its fighting line (here: nobody of it left in the fight), yet
        /// it still commands while he runs ITS order: a hit orders nothing. With no order of its (the SOP idles him), the hit is his own.
        /// 📌 Live cc-battalion run #10: assault squads went ReactToContact mid-ClearBuilding for minutes. 🔴 Red-proof: <c>RunsHisOrder</c>
        /// false ⇒ the hit sends him to cover while he runs the order ⇒ red.</summary>
        [Fact]
        public void CE2436_AUnitBelowItsLine_StillCommands_WhileHeRunsItsOrder()
        {
            Entity Shattered(World w)
            {
                var unit = w.Repo.CreateEntity();
                w.Repo.AddComponent(unit, new Fdp.Core.CommandHierarchy.UnitEntity());
                w.Repo.AddComponent(unit, new Fdp.Core.CommandHierarchy.UnitRoster());   // an emptied roster: out of the fight
                w.Repo.AddComponent(w.Unit, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = unit });
                return unit;
            }

            var ordered = new World();
            Shattered(ordered);
            ordered.Order("Idle");
            ordered.StartSop();
            ordered.Run(0.3);
            ordered.Remember(true);
            ordered.Sense(SensorChange.Hit);
            ordered.Run(0.1);
            Assert.Equal("Idle", ordered.TaskName);
            Assert.Equal(BehaviorOrigin.Superior, ordered.Task.Origin);

            var free = new World();
            Shattered(free);
            free.StartSop();
            free.Run(0.3);
            free.Remember(true);
            free.Sense(SensorChange.Hit);
            free.Run(0.1);
            Assert.Equal("FightFromCover", free.TaskName);
        }

        /// <summary>⭐ <c>CE-4035</c> G-11 — a man in a WEAPON SYSTEM's seat (a manned M2) is not "aboard": the weapon has no brain (W-D), so
        /// his own rows stay his — idle with an enemy in sight, he engages. 📐 Live `ua-m2-engage`: the gunner watched the intruder for 50 s.
        /// ✅ Red-proof: <c>Aboard</c> true for any carrier ⇒ he stays Idle ⇒ red.</summary>
        [Fact]
        public void CE4035_AGunnerInAWeaponSystem_IsNotAboard_HeEngagesWhatHeSees()
        {
            var w = new World();
            var m2 = w.Repo.CreateEntity();
            w.Repo.AddComponent(m2, new Fdp.Toolkit.Combat.WeaponSystemParams()); w.Repo.AddComponent(m2, new Fdp.Toolkit.Combat.Mountable());
            w.Repo.AddComponent(w.Unit, new IsEmbarkedTag { VehicleEntity = m2 });
            w.StartSop();
            w.Run(0.3);
            Assert.Equal("Idle", w.TaskName);
            w.RememberEnemy(seen: true);
            w.Tick();
            w.KeepSeeing();
            w.Run(0.5);
            Assert.Equal("FightFromCover", w.TaskName);
        }

        // ── ⭐ CE-3454 (Motorcycle PF-3) — a man in an open FIRING seat engages on his own, inside his seat's arc ─────────────────────

        /// <summary>A bike at the origin facing +X; <paramref name="firing"/> = seat 1 is a firing seat whose arc is 30°…330° (both sides and the
        /// rear, never ahead — the pillion's); the unit sits in seat 1. The enemy is remembered SEEN at <paramref name="enemyAt"/>.</summary>
        private static unsafe World Seated(bool firing, System.Numerics.Vector3 enemyAt, bool stoppedOnly = false)
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new SimTransform { Rotation = System.Numerics.Quaternion.Identity });   // he sits at the bike (R-2136's range)
            var bike = w.Repo.CreateEntity();
            w.Repo.AddComponent(bike, new SimTransform { Rotation = System.Numerics.Quaternion.Identity });
            var stations = new CarrierStations();
            if (firing)
            {
                stations.FiringMask = 1 << 1;
                if (stoppedOnly) stations.StoppedOnlyMask = 1 << 1;
                stations.Seats[1] = new StationSeat
                {
                    HasArc = 1,
                    TraverseMin = (Angle)(30f * MathF.PI / 180f), TraverseMax = (Angle)(330f * MathF.PI / 180f),
                    ElevationMin = (Angle)(-1.4f), ElevationMax = (Angle)1.4f,
                };
            }
            w.Repo.AddComponent(bike, stations);
            w.Repo.AddComponent(w.Unit, new IsEmbarkedTag { VehicleEntity = bike, Seat = 1 });
            // as boarding leaves it (Embarkation.StrippedOn): a firing seat keeps CanShoot, a closed seat strips it
            w.Repo.AddComponent(w.Unit, new ActorCapabilityState { Capabilities = firing ? ActorCapabilities.CanShoot : ActorCapabilities.None });
            w.StartSop();
            w.Run(0.3);
            Assert.Null(w.TaskName);                                         // aboard: the Aboard row orders nothing (CE-2322)
            w.RememberEnemy(seen: true);
            ref var mem = ref w.Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(w.Unit);
            mem.PositionsX[0] = enemyAt.X; mem.PositionsY[0] = enemyAt.Y; mem.PositionsZ[0] = enemyAt.Z;
            return w;
        }

        /// <summary>⭐ CE-3454 — the pillion, idle, sees an enemy BESIDE the bike (inside his arc): he reacts <c>FireFromSeat</c> (Contact) and
        /// his weapon is aimed at that enemy. 📐 Live: the war-horse rider held both targets for 45 s and never fired (<c>ua-animal-horse-rider</c>).
        /// ✅ Red-proof: the row removed ⇒ the Aboard row stands him aside ⇒ no task ⇒ red.</summary>
        [Fact]
        public void CE3454_AFiringSeatMan_SeesAnEnemyInHisArc_FiresFromTheSeat()
        {
            var w = Seated(firing: true, enemyAt: new System.Numerics.Vector3(0f, 40f, 0f));   // abeam left
            w.Tick();
            w.KeepSeeing();
            w.Run(0.5);
            Assert.Equal("FireFromSeat", w.TaskName);
            Assert.Equal(BehaviorOrigin.Reaction, w.Task.Origin);
            Assert.Equal(ReactionUrgency.Contact, w.Task.Urgency);
            var channel = w.Repo.GetComponent<WeaponChannel>(w.Unit);
            Assert.Equal(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, channel.ActiveAction);
        }

        /// <summary>⭐ CE-3454 — the same enemy DEAD AHEAD (outside the pillion's arc, across the rider): no reaction, no task — and a
        /// closed hull's passenger (no firing seat) gets none either with the enemy beside him. ✅ Red-proof: the arc test dropped from
        /// <c>TargetInArc</c> ⇒ the ahead case reacts ⇒ red.</summary>
        [Fact]
        public void CE3454_OutsideTheArc_OrInAClosedSeat_HeDoesNotReact()
        {
            var ahead = Seated(firing: true, enemyAt: new System.Numerics.Vector3(40f, 0f, 0f));
            ahead.Tick(); ahead.KeepSeeing(); ahead.Run(0.5);
            Assert.Null(ahead.TaskName);

            var closed = Seated(firing: false, enemyAt: new System.Numerics.Vector3(0f, 40f, 0f));
            closed.Tick(); closed.KeepSeeing(); closed.Run(0.5);
            Assert.Null(closed.TaskName);

            var far = Seated(firing: true, enemyAt: new System.Numerics.Vector3(0f, FireFromSeatBehavior.OwnInitiativeRangeM + 20f, 0f));   // R-2136
            far.Tick(); far.KeepSeeing(); far.Run(0.5);
            Assert.Null(far.TaskName);

            var rider = Seated(firing: true, enemyAt: new System.Numerics.Vector3(0f, 40f, 0f), stoppedOnly: true);   // PF-3/PF-4: the rider only drives
            rider.Tick(); rider.KeepSeeing(); rider.Run(0.5);
            Assert.Null(rider.TaskName);
        }

        /// <summary>⭐ CE-3454 — a TASKED firing-seat man (a drive-by's ordered fire) keeps his task: the row needs no task. And once the enemy
        /// is no longer seen, the reaction ends after <see cref="FireFromSeatBehavior.QuietSeconds"/>. ✅ Red-proofs:
        /// <c>NoTask</c> dropped from the row ⇒ the order is replaced ⇒ red; the quiet end removed ⇒ still FireFromSeat ⇒ red.</summary>
        [Fact]
        public void CE3454_ATaskedMan_KeepsHisTask_AndTheReactionEndsWhenQuiet()
        {
            var tasked = Seated(firing: true, enemyAt: new System.Numerics.Vector3(0f, 40f, 0f));
            tasked.Order("HoldStance");
            tasked.Run(0.2);
            for (int i = 0; i < 40; i++) { tasked.KeepSeeing(); tasked.Tick(); }
            Assert.Equal("HoldStance", tasked.TaskName);

            var w = Seated(firing: true, enemyAt: new System.Numerics.Vector3(0f, 40f, 0f));
            for (int i = 0; i < 40; i++) { w.KeepSeeing(); w.Tick(); }
            Assert.Equal("FireFromSeat", w.TaskName);
            w.Run(FireFromSeatBehavior.QuietSeconds + 1.5);                   // no longer seen (nobody re-stamps the sighting)
            Assert.Null(w.TaskName);                                         // the reaction ended; aboard, nothing else is ordered
        }

        // ── ⭐ CE-2331 / CE-2332 / CE-2333 (AQ98, R-2070) — the cover reaction fights back and ends; an idle man engages what he sees ────

        /// <summary>⭐ AQ98 B — the reaction ENDS after a quiet lapse though the threat is still remembered (the hide-only rule lasted while
        /// a threat was remembered — in a battle, always), and the paused task resumes. Here there is no cover anywhere (no EQS in this world),
        /// so it runs its open-ground half (AQ96 C: down in the open) and ends the same way. ✅ Red-proof: <c>Quiet</c> always false ⇒ the
        /// reaction lasts ⇒ red.</summary>
        [Fact]
        public void CE2331_TheReaction_EndsAfterAQuietLapse_WithTheThreatStillRemembered()
        {
            var w = new World();
            w.Order("Idle");
            w.StartSop();
            w.Run(0.3);
            w.Remember(true);
            w.Sense(SensorChange.Hit);
            w.Run(0.1);
            Assert.Equal("FightFromCover", w.TaskName);

            w.Run(FightFromCoverBehavior.DefaultQuietSeconds - 1.5);
            Assert.Equal("FightFromCover", w.TaskName);                        // not yet quiet
            w.Run(2.5);
            Assert.Equal("Idle", w.TaskName);                                  // quiet: the task is back …
            Assert.Equal(BehaviorOrigin.Superior, w.Task.Origin);
            Assert.True(w.Repo.GetComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(w.Unit).Count > 0);   // … the threat still remembered
        }

        /// <summary>⭐ AQ98 B — rounds still landing keep him in the reaction: a near miss 5 s in restarts the quiet clock.
        /// ✅ Red-proof: <c>Quiet</c> reading only <c>Hit</c> ⇒ it ends at 8 s ⇒ red.</summary>
        [Fact]
        public void CE2331_RoundsStillLanding_KeepTheReactionGoing()
        {
            var w = new World();
            w.Order("Idle");
            w.StartSop();
            w.Run(0.3);
            w.Remember(true);
            w.Sense(SensorChange.Hit);
            w.Run(5.0);
            w.Sense(SensorChange.NearMiss);
            w.Run(4.5);                                                        // 9.5 s after the hit, 4.5 s after the near miss
            Assert.Equal("FightFromCover", w.TaskName);
            w.Run(4.5);
            Assert.Equal("Idle", w.TaskName);
        }

        /// <summary>⭐ AQ98 B — the in-cover end, on the node itself: at his hide point and quiet ⇒ Success; a hit within the lapse ⇒ he fights
        /// on. Not in cover yet ⇒ it never ends on quiet (he would be walked back out into the fire). ✅ Red-proof: drop <c>InCover</c> from
        /// the end ⇒ the not-in-cover case ends ⇒ red.</summary>
        [Fact]
        public void CE2331_InCoverAndQuiet_Ends_NotInCover_FightsOn()
        {
            var w = new World();
            w.Remember(true);
            var p = new FightFromCoverParams { QuietSeconds = 8f, Open = FightFromCoverBehavior.OpenDefaults };

            var hidden = new FightFromCoverState();
            hidden.Peek.Phase = Fdp.Toolkit.Combat.PeekPhase.Hidden;
            Assert.Equal(Fbt.NodeStatus.Success, FightFromCoverBehavior.Run(ref p, ref hidden, w.Unit, w.Repo));

            var choosing = new FightFromCoverState();
            Assert.NotEqual(Fbt.NodeStatus.Success, FightFromCoverBehavior.Run(ref p, ref choosing, w.Unit, w.Repo));

            w.Sense(SensorChange.Hit);
            w.Tick();
            var shotAt = new FightFromCoverState();
            shotAt.Peek.Phase = Fdp.Toolkit.Combat.PeekPhase.Hidden;
            Assert.NotEqual(Fbt.NodeStatus.Success, FightFromCoverBehavior.Run(ref p, ref shotAt, w.Unit, w.Repo));
        }

        /// <summary>⭐ <c>CE-2357</c> (AQ102 E, R-2072) — in cover, the enemy he SAW out of sight for <c>FlankAfterSeconds</c>: a trained man
        /// FLANKS (the fight releases PeekAndFire and runs the flank) instead of peeking at the same spot for ever. Not too soon, not before he
        /// is in cover, not on a post, not past his flank budget, not a militiaman. ✅ Red-proof: drop any one guard ⇒ its case flanks ⇒ red.</summary>
        [Fact]
        public void CE2357_TheEnemyOutOfSightTooLong_ATrainedManInCoverFlanks()
        {
            var w = new World();
            w.RememberEnemy(seen: true);
            var p = new FightFromCoverParams
            {
                QuietSeconds = 8f, Open = FightFromCoverBehavior.OpenDefaults,
                Flank = FightFromCoverBehavior.FlankDefaults, FlankAfterSeconds = FightFromCoverBehavior.DefaultFlankAfterSeconds,
            };
            var hidden = new FightFromCoverState();
            hidden.Peek.Phase = Fdp.Toolkit.Combat.PeekPhase.Hidden;
            Assert.False(FightFromCoverBehavior.ShouldFlank(in p, in hidden, w.Unit, w.Repo));        // seen just now

            w.Run(FightFromCoverBehavior.DefaultFlankAfterSeconds + 1);
            Assert.True(FightFromCoverBehavior.ShouldFlank(in p, in hidden, w.Unit, w.Repo));

            var choosing = new FightFromCoverState();
            Assert.False(FightFromCoverBehavior.ShouldFlank(in p, in choosing, w.Unit, w.Repo));      // not in cover yet
            var posted = p;
            posted.Peek.AnchorRadius = 1.5f;
            Assert.False(FightFromCoverBehavior.ShouldFlank(in posted, in hidden, w.Unit, w.Repo));   // a post is held (DA-44)
            var spent = hidden;
            spent.FlanksTried = FightFromCoverBehavior.MaxFlanks;
            Assert.False(FightFromCoverBehavior.ShouldFlank(in p, in spent, w.Unit, w.Repo));         // the run's flanks are used up

            var run = hidden;
            Assert.Equal(Fbt.NodeStatus.Running, FightFromCoverBehavior.Run(ref p, ref run, w.Unit, w.Repo));
            Assert.Equal(1, run.Flanking);                                                             // the fight flanks, it does not end quiet
            Assert.Equal(1, run.FlanksTried);
            FightFromCoverBehavior.Deactivate_Run(ref p, ref run, w.Unit, w.Repo);
            var second = hidden;
            second.FlanksTried = 1;                                                                    // the run's second escalation …
            Assert.Equal(Fbt.NodeStatus.Running, FightFromCoverBehavior.Run(ref p, ref second, w.Unit, w.Repo));
            Assert.Equal(2, second.Flanking);                                                          // … closes in on the remembered point
            FightFromCoverBehavior.Deactivate_Run(ref p, ref second, w.Unit, w.Repo);

            var justMoved = hidden;
            justMoved.EscalatedAt = (SimInstant)(w.Repo.SimulationTime - 1.0);
            Assert.False(FightFromCoverBehavior.ShouldFlank(in p, in justMoved, w.Unit, w.Repo));     // the last escalation was a second ago

            Assert.True(FightFromCoverBehavior.ShouldFlank(in p, in hidden, w.Unit, w.Repo));         // nothing paused: he may
            w.Order("HoldStance");                                                                     // an ORDERED task …
            w.Run(0.1);
            w.Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = w.Unit, BehaviorName = "FightFromCover", JsonParams = "{}",
                                                                  Origin = BehaviorOrigin.Reaction, Urgency = ReactionUrgency.Hit });
            w.Run(0.1);
            Assert.Equal("HoldStance", BehaviorIngressSystem.PausedTaskOf(w.Repo, w.Unit)!.BehaviorName);   // … the fight paused it
            Assert.False(FightFromCoverBehavior.ShouldFlank(in p, in hidden, w.Unit, w.Repo));        // it waits for him: no flank away from it

            w.Repo.AddComponent(w.Unit, new CombatProficiency { Authored = ProficiencyOf.Militia, Set = CombatProficiency.AuthoredSet });
            Assert.False(FightFromCoverBehavior.ShouldFlank(in p, in hidden, w.Unit, w.Repo));        // militia only duck and fire blind (J)
        }

        /// <summary>⭐ AQ98 C — a man with no task who SEES an enemy engages from where he is (React FightFromCover, Contact) instead of
        /// standing idle — and, with no task to resume, a quiet lapse does not end it (no restart churn). ✅ Red-proofs: the row removed from
        /// the asset ⇒ he stays Idle ⇒ red; <c>WouldReturn</c> always false ⇒ a new run after 8 s ⇒ red.</summary>
        [Fact]
        public void CE2332_NoTask_AnEnemyInSight_HeFightsFromCover()
        {
            var w = new World();
            w.StartSop();
            w.Run(0.3);
            Assert.Equal("Idle", w.TaskName);
            w.RememberEnemy(seen: true);
            w.Tick();
            w.KeepSeeing();
            w.Run(0.5);                                                        // the SOP re-reads its rows every 0.2 s (+ stagger)
            Assert.Equal("FightFromCover", w.TaskName);
            Assert.Equal(BehaviorOrigin.Reaction, w.Task.Origin);
            Assert.Equal(ReactionUrgency.Contact, w.Task.Urgency);

            // ⭐ no task to resume and the enemy still in sight ⇒ the quiet lapse does not end it (it would only restart a frame later)
            uint run = w.Task.InstanceId;
            for (int i = 0; i < 700; i++) { w.KeepSeeing(); w.Tick(); }     // > QuietSeconds, nothing landing near him
            Assert.Equal("FightFromCover", w.TaskName);
            Assert.Equal(run, w.Task.InstanceId);
        }

        /// <summary>⭐ <c>CE-1269</c> — the same, once he HIDES and stops seeing the enemy (the eye stamps nothing while he is down in cover):
        /// the enemy is still remembered, so the quiet lapse still does not end it — ending would only let the row restart him the moment he
        /// looks out (📐 measured live: a restart every 1–3 s). ✅ Red-proof: <c>WouldReturn</c> reading <c>EnemyInSight</c> again ⇒ it ends
        /// after the quiet lapse ⇒ red.</summary>
        [Fact]
        public void CE1269_HiddenAndNoLongerSeeing_TheRememberedEnemy_StillKeepsTheReaction()
        {
            var w = new World();
            w.StartSop();
            w.Run(0.3);
            w.RememberEnemy(seen: true);
            w.Tick();
            w.KeepSeeing();
            w.Run(0.5);
            Assert.Equal("FightFromCover", w.TaskName);

            uint run = w.Task.InstanceId;
            w.Run(FightFromCoverBehavior.DefaultQuietSeconds + 3);             // hidden: nothing seen, nothing landing near him
            Assert.Equal("FightFromCover", w.TaskName);
            Assert.Equal(run, w.Task.InstanceId);
        }

        /// <summary>⭐ <c>CE-3306</c> — under HOLD FIRE the enemy-in-sight row stands aside: a man with no task who sees an enemy, the
        /// first-threat edge not yet read, does NOT start fighting from cover — and once the edge arrives the withdraw row takes him
        /// (FallBack, D5). 📐 Live (the in-process cluster): row C took him a frame before the edge. ✅ Red-proof: the <c>MayFire</c>
        /// condition removed from the asset ⇒ FightFromCover ⇒ red.</summary>
        [Fact]
        public void CE3306_UnderHoldFire_AnEnemyInSight_DoesNotStartTheFight_TheWithdrawRowDecides()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new Roe { Fire = RoeFire.HoldFire, SetBy = BehaviorOrigin.Superior });
            w.StartSop();
            w.Run(0.3);
            w.RememberEnemy(seen: true);
            w.Tick();
            w.KeepSeeing();
            w.Run(0.5);
            Assert.Equal("Idle", w.TaskName);                                  // seen, but no fight under Hold Fire
            w.Sense(SensorChange.FirstThreat);
            w.KeepSeeing();
            w.Run(0.3);
            Assert.Equal("FallBack", w.TaskName);                              // row 2 (ROE ≤ HoldFire)
        }

        /// <summary>⭐ AQ98 C — the row's limits: a man WITH a task keeps it (the task decides the fight); only HEARD (not seen) ⇒ nothing;
        /// a neutral in sight ⇒ nothing. ✅ Red-proof: <c>NoTask</c> always true ⇒ the tasked man reacts ⇒ red.</summary>
        [Fact]
        public void CE2332_ATaskedMan_AHeardEnemy_OrANeutral_DoNotFireTheRow()
        {
            var tasked = new World();
            tasked.Order("HoldStance");
            tasked.StartSop();
            tasked.Run(0.3);
            string? task = tasked.TaskName;
            Assert.NotNull(task);
            Assert.NotEqual("Idle", task);
            tasked.RememberEnemy(seen: true);
            tasked.KeepSeeing();
            tasked.Run(0.5);
            Assert.Equal(task, tasked.TaskName);

            var heard = new World();
            heard.StartSop();
            heard.Run(0.3);
            heard.RememberEnemy(seen: false);
            heard.Run(0.5);
            Assert.Equal("Idle", heard.TaskName);

            var neutral = new World();
            neutral.StartSop();
            neutral.Run(0.3);
            neutral.RememberEnemy(seen: true, ForceId.Neutral);
            neutral.KeepSeeing();
            neutral.Run(0.5);
            Assert.Equal("Idle", neutral.TaskName);
        }

        /// <summary>⭐ AQ97 C — <c>InertSop</c>, the explicit none: no idle choice and no reaction, whatever he senses.</summary>
        [Fact]
        public void CE2322_InertSop_OrdersNothing_NeitherIdleNorAReaction()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId("InertSop", out _));
            w.StartSop("InertSop");
            w.Run(0.3);
            Assert.Null(w.TaskName);
            w.Remember(true);
            w.Sense(SensorChange.Hit);
            w.Sense(SensorChange.NearMiss);
            w.Run(0.3);
            Assert.Null(w.TaskName);
            Assert.Equal(0, w.Repo.GetComponent<SopState>(w.Unit).SopFaulted);
        }

        /// <summary>⭐ AQ97 A / D2 — the TKB names the defaults: every armed soldier type of the urban catalogue <c>BasicInfantrySop</c>, every
        /// civilian <c>CivilianSop</c> (beside his routine HSM).</summary>
        [Fact]
        public void CE2322_TheTkb_NamesTheDefaults_SoldiersAndCivilians()
        {
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Core.Tkb.UrbanCombatTkbCatalog.RegisterAll(db);
            Hrot.Core.Tkb.CivilianTkbCatalog.RegisterAll(db);
            string? SopOf(long type) => db.GetByType(type)?.GetDescriptor<Fdp.Toolkit.Tkb.Domain.BehaviorProfileDto>()?.DefaultSop;
            Assert.Equal("BasicInfantrySop", SopOf(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbInfantrySoldier));
            Assert.Equal("BasicInfantrySop", SopOf(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbInsurgent));
            foreach (long civ in new long[] { 1101, 1102, 1103, 1104 })
                Assert.Equal("CivilianSop", SopOf(civ));
        }

        /// <summary>⭐ <c>CE-2336</c> (AQ99 A, R-2065) — the TKB names how close a person stands: soldiers, fighters and the security forces
        /// the navmesh's body (0.3 m — a stack, a line); civilians and the unarmed crowd fighter state nothing, so they keep the Pedestrian
        /// preset's distance (a crowd is not a horde).</summary>
        [Fact]
        public void CE2336_TheTkb_NamesWhoStandsClose_SoldiersAndSecurity_NotCrowds()
        {
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Core.Tkb.UrbanCombatTkbCatalog.RegisterAll(db);
            Hrot.Core.Tkb.CivilianTkbCatalog.RegisterAll(db);
            float? Radius(long type) => db.GetByType(type)?.GetDescriptor<Fdp.Toolkit.Tkb.Domain.VehicleParametersDto>()?.AvoidanceRadius;
            const float close = Fdp.Toolkit.Tkb.Domain.VehicleParametersDto.CloseOrderAvoidanceRadius;
            foreach (long t in new long[] { 2002, 2003, 2004, 2005, 2006, 2007, 1201, 1202, 1203 })
                Assert.True(Radius(t) == close, $"type {t}: {Radius(t)}");
            foreach (long t in new long[] { 1101, 1102, 1103, 1104, 2008 })
                Assert.True(Radius(t) == 0f, $"type {t}: {Radius(t)}");   // unstated ⇒ the preset's
        }
    }
}
