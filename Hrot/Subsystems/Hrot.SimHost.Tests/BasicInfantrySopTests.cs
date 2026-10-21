using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Perception.Events;
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
                if (!Repo.Bus.IsRegistered<SensorChangedEvent>()) Repo.Bus.Register<SensorChangedEvent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
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
                Repo.Bus.SwapBuffers();
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
            Assert.Equal("TakeCover", w.TaskName);                             // row 1: hit → React(cover, Hit)
            Assert.Equal(BehaviorOrigin.Reaction, w.Task.Origin);
            Assert.Equal(ReactionUrgency.Hit, w.Task.Urgency);
            Assert.Equal("Idle", BehaviorIngressSystem.PausedTaskOf(w.Repo, w.Unit)!.BehaviorName);

            w.Run(1.3);
            Assert.Equal("TakeCover", w.TaskName);                             // cover lasts while a threat is remembered
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
        public void CE2080_AContactUnderHoldFire_Withdraws_OtherwiseTakesCover()
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
            Assert.Equal("TakeCover", free.TaskName);                          // row 3
            Assert.Equal(ReactionUrgency.Contact, free.Task.Urgency);
        }
    
        // ── ⭐ CE-2322 (AQ97, R-2062) — every lone soldier reacts by default; "no response" is said explicitly ────────────────────

        /// <summary>⭐ AQ97 B — rounds passing near a lone soldier are reason enough: React(TakeCover, UnderFire). ✅ Red-proof: the
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
            Assert.Equal("TakeCover", w.TaskName);
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
            Assert.Equal("TakeCover", w.TaskName);

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
