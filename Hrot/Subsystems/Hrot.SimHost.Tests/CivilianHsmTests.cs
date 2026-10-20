using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2235</c> (civilians CIV-1, CV-3; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — the SHIPPED <c>CivilianHsm</c> from
    /// the production registry, through the real ingress and brain tick: he starts in his routine and walks about his home; fear past
    /// <c>AlarmAt</c> stops and crouches him; calm again, he goes back to his routine; wounded — from his routine or his alarm — he lies
    /// down and stays a casualty however frightened he becomes. And the new civilian types start it by default.
    /// </summary>
    // ⭐ CE-2241 ② — one collection with every class that strikes: their rails read the process-wide Melee.Strikes counter, so they
    //   must not run in parallel (a BatonCharge rail in another class made a CivilianHsm rail count one blow too many)
    [Collection("MeleeStrikes")]
    public sealed class CivilianHsmTests
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
                if (!Repo.IsComponentTypeRegistered<StanceIntent>()) Repo.RegisterComponent<StanceIntent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new SimTransform { Position = new Vector3(100, 200, 0), Rotation = Quaternion.Identity });
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.AddComponent(Unit, new LocomotionChannel());
                Repo.AddComponent(Unit, new StanceIntent());
                Repo.AddComponent(Unit, new Health { Current = 100f, Max = 100f });
                // ⭐ the type's injury band, as InjuryTkbTranslator stamps it on every node (a civilian's InjuryDefDto defaults)
                if (!Repo.IsComponentTypeRegistered<InjuryThresholds>()) Repo.RegisterComponent<InjuryThresholds>();
                Repo.AddComponent(Unit, new InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
                Tick();
            }

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

            public void Start(string json = "{}")
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = BehaviorNames.CivilianHsm, JsonParams = json, Origin = BehaviorOrigin.Superior });

            public unsafe string State()
            {
                Assert.True(Registry.TryGetId(BehaviorNames.CivilianHsm, out int id));
                Assert.True(Registry.TryGetDefinition(id, out var def));
                if (!RootHsmAccess.TryGetInstance(Repo, Unit, out byte* ptr, out int size)) return "-";
                ushort* leaves = Fhsm.Kernel.HsmKernel.GetActiveLeafIds(ptr, size, out int count);
                return count == 0 || leaves[0] == 0xFFFF ? "-" : def!.HsmMetadata?.GetStateName(leaves[0]) ?? $"State_{leaves[0]}";
            }

            public CivilianMode Mode => CivilianMind.ModeOf(Repo, Unit);
            public void Fear(float fear) => Repo.GetComponentRW<CivilianState>(Unit).Fear = fear;
            public ref readonly LocomotionChannel Loco => ref Repo.GetComponentRO<LocomotionChannel>(Unit);
            public StanceId Stance => Repo.GetComponentRO<StanceIntent>(Unit).TargetStance;
            public void Health(float current) => Repo.GetComponentRW<Health>(Unit).Current = current;

            /// <summary>⭐ CE-2322 — what his task slot runs now (a task or a reaction).</summary>
            public string? TaskName => Registry.TryGetName(Repo.GetComponent<BehaviorState>(Unit).ActiveBehaviorHash, out var n) ? n : null;
            public BehaviorOrigin TaskOrigin => Repo.GetComponent<BehaviorState>(Unit).Origin;
            public void Order(string behaviour)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = behaviour, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
            public void StartSop(string name)
                => Repo.Bus.PublishManaged(new AssignSopEvent { Entity = Unit, BehaviorName = name, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
            public void Run(double seconds) { for (double t = 0; t < seconds; t += 0.016) Tick(); }
        }

        // ── ⭐ CE-2322 (AQ97 D, R-2062) — a civilian's fear reacts on ANY task ─────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ AQ97 D2 / D3 — a civilian ORDERED to do something else (here: hold, Idle) still reacts: frightened past the alarm his SOP
        /// pauses the order with <c>CivilianReact</c> (he stops and crouches — the HSM's own Alarmed); calm again, the reaction is done and his
        /// order restarts. 🔴 Red before: nothing but his HSM read his fear, so an ordered civilian walked on through gunfire.
        /// ✅ Red-proof: the SOP's react row removed ⇒ he keeps holding upright ⇒ red.
        /// </summary>
        [Fact]
        public void CE2322_AnOrderedCivilian_StillReacts_AndHisOrderRestartsWhenHeCalms()
        {
            var w = new World();
            w.Order("Idle");
            w.StartSop("CivilianSop");
            w.Run(0.3);
            Assert.Equal("Idle", w.TaskName);
            CivilianMind.Ensure(w.Repo, w.Unit);
            w.Fear(0.4f);                                                       // past the alarm (0.3), short of panic (0.6)
            w.Run(0.2);
            Assert.Equal(BehaviorNames.CivilianReact, w.TaskName);
            Assert.Equal(BehaviorOrigin.Reaction, w.TaskOrigin);
            Assert.Equal(CivilianMode.Alarmed, w.Mode);
            Assert.Equal(StanceId.Crouched, w.Stance);

            w.Fear(0.05f);                                                      // calm (below 0.1)
            w.Run(0.3);
            Assert.Equal("Idle", w.TaskName);                                   // the order is back, at its own rank
            Assert.Equal(BehaviorOrigin.Superior, w.TaskOrigin);
        }

        /// <summary>⭐ AQ97 D2 — on his routine the HSM keeps his fear itself: the SOP stands aside (no second reaction on top of the HSM's).</summary>
        [Fact]
        public void CE2322_OnHisRoutine_HisSopStandsAside_TheHsmReacts()
        {
            var w = new World();
            w.Start();
            w.StartSop("CivilianSop");
            w.Run(0.3);
            w.Fear(0.4f);
            w.Run(0.3);
            Assert.Equal(BehaviorNames.CivilianHsm, w.TaskName);
            Assert.Equal("Alarmed", w.State());
        }

        /// <summary>⭐ AQ97 D3 — the reaction is the HSM's fear, not a copy: every edge among the fear states of <c>CivilianHsm</c> exists in
        /// <c>CivilianReact</c> with the same guard (its way back to the routine is the reaction's end, Done), and every state runs the same
        /// activity. ✅ Red-proof: an edge dropped from the reaction ⇒ red.</summary>
        [Fact]
        public void CE2322_TheReaction_HasTheHsmsFearEdges()
        {
            var fear = new System.Collections.Generic.HashSet<string> { "Alarmed", "Panic", "Shelter", "Inside", "Cower", "Casualty" };
            System.Collections.Generic.HashSet<string> Edges(Fhsm.Compiler.Graph.StateMachineGraph g, string home)
            {
                var set = new System.Collections.Generic.HashSet<string>();
                foreach (var st in g.States.Values)
                {
                    if (!fear.Contains(st.Name)) continue;
                    foreach (var t in st.Transitions)
                    {
                        string to = t.Target?.Name ?? "?";
                        if (to == home) to = "Done";
                        // the guard's METHOD (its name carries the state's byte offset in its own blackboard — "@0@104" vs "@0@72")
                        string guard = (t.GuardFunction ?? "").Split('@')[0];
                        if (fear.Contains(to) || to == "Done") set.Add($"{st.Name}->{to}:{guard}");
                    }
                }
                return set;
            }
            var hsm = Edges(Hrot.AI.Behaviors.Brains.CivilianBehavior.BuildCivilianMachine(), "Routine");
            var react = Edges(Hrot.AI.Behaviors.Brains.CivilianBehavior.BuildCivilianReact(), "Done");
            Assert.NotEmpty(hsm);
            foreach (var e in hsm) Assert.True(react.Contains(e), $"missing {e}; the reaction has: {string.Join(" | ", react)}");
        }

        [Fact]
        public void CE2235_HeStartsInHisRoutine_AndWalksToASpotNearHome()
        {
            var w = new World();
            w.Start("{\"WanderRadius\":8}");
            for (int i = 0; i < 4; i++) w.Tick();

            Assert.Equal("Routine", w.State());
            Assert.Equal(CivilianMode.Routine, w.Mode);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
            ref readonly var mind = ref w.Repo.GetComponentRO<CivilianState>(w.Unit);
            Assert.Equal(new Vector3(100, 200, 0), mind.Home);
        }

        [Fact]
        public void CE2235_Frightened_HeStopsAndCrouches_Calm_HeGoesBackToHisRoutine()
        {
            var w = new World();
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Routine", w.State());

            w.Fear(CivilianBehavior.DefaultAlarmAt - 0.01f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Routine", w.State());   // just under the threshold: not yet

            w.Fear(CivilianBehavior.DefaultAlarmAt);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());
            Assert.Equal(CivilianMode.Alarmed, w.Mode);
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
            Assert.Equal(StanceId.Crouched, w.Stance);

            w.Fear(CivilianBehavior.DefaultCalmBelow);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());   // at the calm line: still alarmed

            w.Fear(0f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Routine", w.State());
            Assert.Equal(StanceId.Standing, w.Stance);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);   // walking again
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CE2235_Wounded_HeLiesDown_AndStaysACasualtyHoweverFrightened(bool alarmedFirst)
        {
            var w = new World();
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();
            if (alarmedFirst)
            {
                w.Fear(CivilianBehavior.DefaultAlarmAt);   // ⭐ CE-2236 — alarmed, not panicked (fear below PanicAt)
                for (int i = 0; i < 3; i++) w.Tick();
                Assert.Equal("Alarmed", w.State());
            }

            w.Health(40f);   // below the default WoundedBelow (0.6)
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Casualty", w.State());
            Assert.Equal(CivilianMode.Casualty, w.Mode);
            Assert.Equal(StanceId.Prone, w.Stance);
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);

            w.Fear(1f);
            for (int i = 0; i < 5; i++) w.Tick();
            w.Fear(0f);
            for (int i = 0; i < 5; i++) w.Tick();
            Assert.Equal("Casualty", w.State());
        }

        /// <summary>⭐ <c>CE-2236</c> (§5e) — terrified with no building in reach (this world has no terrain, so the shelter query never
        /// answers), he panics, waits for the answer, then cowers where he is: crouched, cowering on every map. His fear falling below
        /// panic ends the cowering, back to alarmed. Wounded while cowering, he is a casualty.</summary>
        [Fact]
        public void CE2236_PanickedWithNoShelter_HeCowers_UntilHisFearFallsBelowPanic()
        {
            var w = new World();
            Assert.True(w.Repo.IsComponentTypeRegistered<EffectState>(), "the cowering flag needs EffectState on this node");
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();

            w.Fear(1f);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("Panic", w.State());
            Assert.Equal(CivilianMode.Panic, w.Mode);
            Assert.Equal(StanceId.Standing, w.Stance);

            int waited = 0;
            while (w.State() == "Panic" && waited < 400) { w.Tick(); waited++; }
            Assert.Equal("Cower", w.State());
            Assert.InRange(waited * 0.016, CivilianBehavior.ShelterWaitSeconds - 0.2, CivilianBehavior.ShelterWaitSeconds + 0.2);
            Assert.Equal(CivilianMode.Cower, w.Mode);
            Assert.Equal(StanceId.Crouched, w.Stance);
            Assert.True(Effects.IsCowering(w.Repo, w.Unit));
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);

            w.Fear(CivilianBehavior.DefaultPanicAt);
            for (int i = 0; i < 5; i++) w.Tick();
            Assert.Equal("Cower", w.State());   // at the panic line: still cowering

            w.Fear(CivilianBehavior.DefaultPanicAt - 0.05f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());
            Assert.False(Effects.IsCowering(w.Repo, w.Unit), "the cowering ends with the state");

            w.Fear(1f);
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Equal("Panic", w.State());   // a second panic starts afresh: waits again
            for (int i = 0; i < 400 && w.State() == "Panic"; i++) w.Tick();
            Assert.Equal("Cower", w.State());
            w.Health(40f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Casualty", w.State());
            Assert.False(Effects.IsCowering(w.Repo, w.Unit));
            Assert.Equal(StanceId.Prone, w.Stance);
        }

        /// <summary>⭐ <c>CE-2236</c> (R-4035) — inside, he hides for a time drawn from [HideMin, HideMax], then deletes himself: ONE
        /// <c>DestroyEntityCommand</c> naming his network id, not one per frame.</summary>
        [Fact]
        public void CE2236_Inside_HeHidesHisTime_ThenDeletesHimselfOnce()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4242));
            var p = CivilianBehavior.Defaults();
            p.HideMinSeconds = 1f;
            p.HideMaxSeconds = 1.5f;
            var ws = new CivilianBehavior.PanicState();
            int destroys = 0;
            double start = w.Repo.SimulationTime, deletedAt = 0;
            for (int i = 0; i < 150; i++)
            {
                CivilianBehavior.Inside(ref p, ref ws, w.Unit, w.Repo);
                w.Tick();
                foreach (var c in w.Repo.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>())
                {
                    Assert.Equal(4242L, c.NetworkId);
                    destroys++;
                    deletedAt = w.Repo.SimulationTime;
                }
            }
            Assert.Equal(1, destroys);
            Assert.InRange(deletedAt - start, 1.0, 1.5 + 0.05);
            Assert.Equal(CivilianMode.Inside, w.Mode);
        }

        /// <summary>⭐ <c>CE-2237</c> (§5f) — drawn by an incident (his <c>Focus</c>, which the crowd system sets), a calm civilian
        /// leaves his routine and walks to a spot on a ring 4–9 m round it; the focus gone, he goes back to his routine; frightened while
        /// looking on, he is alarmed instead — fear outranks curiosity.</summary>
        [Fact]
        public unsafe void CE2237_AnIncidentDrawsHim_ToARingRoundIt_GoneHeGoesBack_FrightenedHeAlarms()
        {
            var w = new World();
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Routine", w.State());

            var focus = new Vector3(130, 200, 0);   // 30 m east of him
            void Draw(bool on)
            {
                ref var mind = ref w.Repo.GetComponentRW<CivilianState>(w.Unit);
                mind.Focus = focus;
                mind.HasFocus = on ? (byte)1 : (byte)0;
            }
            Draw(true);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Gather", w.State());
            Assert.Equal(CivilianMode.Gather, w.Mode);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
            var loco = w.Loco;
            var to = ((MoveToParams*)loco.Params)->Destination;
            float ring = Vector2.Distance(new Vector2(to.X, to.Y), new Vector2(focus.X, focus.Y));
            Assert.InRange(ring, CrowdRules.RingMetres - 0.01f, CrowdRules.RingMetres + CrowdRules.RingJitterMetres + 0.01f);
            Assert.True(to.X < focus.X, $"he stands on HIS side of it (from the west), not across it: {to}");

            Draw(false);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Routine", w.State());

            Draw(true);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Gather", w.State());
            w.Fear(CivilianBehavior.DefaultAlarmAt);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
        }

        /// <summary>⭐ <c>CE-2240</c> (§5g) — told to protest at a point, he walks to a spot on a ring 8–15 m round it and shows it: a
        /// sign held, or his fist raised (the replicated <c>Gesture</c>); a fright short of panic does not end it (no Alarmed); past
        /// panic he panics and lowers his arms.</summary>
        [Fact]
        public unsafe void CE2240_AProtester_GoesToTheRingAndShowsIt_OnlyPanicEndsIt_AndHisArmsComeDown()
        {
            var w = new World();
            Assert.True(w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Gesture>(), "the gesture needs its component on this node");
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0]}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Protest", w.State());
            Assert.Equal(CivilianMode.Protest, w.Mode);
            var loco = w.Loco;
            Assert.Equal(NavigationConstants.ActionIdMoveTo, loco.ActiveAction);
            var to = ((MoveToParams*)loco.Params)->Destination;
            float ring = Vector2.Distance(new Vector2(to.X, to.Y), new Vector2(130, 200));
            Assert.InRange(ring, CivilianBehavior.ProtestRingMetres - 0.01f, CivilianBehavior.ProtestRingMetres + CivilianBehavior.ProtestRingJitterMetres + 0.01f);
            Assert.Contains(Gestures.Of(w.Repo, w.Unit), new[] { GestureKind.HoldSign, GestureKind.FistRaised });

            w.Fear(CivilianBehavior.DefaultPanicAt - 0.01f);   // frightened, short of panic: a protester stays
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Protest", w.State());

            w.Fear(CivilianBehavior.DefaultPanicAt);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Panic", w.State());
            Assert.Equal(GestureKind.None, Gestures.Of(w.Repo, w.Unit));
        }

        /// <summary>A forces person (Friend, a pedestrian with a collider and health) at <paramref name="at"/>.</summary>
        private static Entity Policeman(World w, Vector3 at)
        {
            var e = w.Repo.CreateEntity();
            w.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.Repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Friend });
            w.Repo.AddComponent(e, new Health { Current = 100f, Max = 100f });
            w.Repo.AddComponent(e, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.3f });
            w.Repo.AddComponent(e, global::CarKinem.Core.VehiclePresets.GetPreset(global::CarKinem.Core.VehicleClass.Pedestrian));
            return e;
        }

        /// <summary>⭐ <c>CE-2238</c> part 1 (§5i) — a protester whose anger reaches <c>RiotAt</c> riots: fist raised, he runs at the nearest
        /// forces person; in reach he strikes (a Blunt <c>DamageAssessedEvent</c>), and that first blow is his hostile act — his side flips
        /// to Hostile, once; a knocked-down man is not struck again; calm again (anger below half) he goes back to the protest.</summary>
        /// <summary>⭐ <c>CE-2241</c> ② (§2.2 "Riot → Panic : fear beats anger", §5m PV-6) — a rioter at anger 1 with fear 0.9 (the most the
        /// crowd's contagion can give) keeps rioting; at fear 1 (force on HIM) he panics; a rioter at anger 0.7 panics at fear 0.7.
        /// ✅ Red-proof: the anger comparison removed ⇒ he panics at 0.9 ⇒ red.</summary>
        [Fact]
        public void CE2241_ARioter_PanicsOnlyWhenFearBeatsAnger()
        {
            var w = new World();
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0]}");
            for (int i = 0; i < 4; i++) w.Tick();
            w.Repo.GetComponentRW<CivilianState>(w.Unit).Anger = 1f;
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Riot", w.State());
            w.Repo.GetComponentRW<CivilianState>(w.Unit).Fear = 0.9f;
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Riot", w.State());                                  // 0.9 < his anger 1: he keeps rioting
            w.Repo.GetComponentRW<CivilianState>(w.Unit).Fear = 1f;
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Panic", w.State());

            var v = new World();
            v.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0]}");
            for (int i = 0; i < 4; i++) v.Tick();
            v.Repo.GetComponentRW<CivilianState>(v.Unit).Anger = 0.7f;
            for (int i = 0; i < 4; i++) v.Tick();
            Assert.Equal("Riot", v.State());
            v.Repo.GetComponentRW<CivilianState>(v.Unit).Fear = 0.7f;
            for (int i = 0; i < 4; i++) v.Tick();
            Assert.Equal("Panic", v.State());
        }

        /// <summary>⭐ <c>CE-2241</c> ② (§5m PV-3) — his ATTITUDE sets his threshold: a friendly protester (−1) stays at the protest at anger 1; a
        /// hostile-at-heart one (+1) riots at 0.15; the HSM writes his attitude on his mind.</summary>
        [Fact]
        public void CE2241_HisAttitude_SetsHisRiotThreshold_AFriendlyManNeverRiots_AHostileOneRiotsEarly()
        {
            var friendly = new World();
            friendly.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0],\"Attitude\":-1}");
            for (int i = 0; i < 4; i++) friendly.Tick();
            Assert.Equal(-1f, friendly.Repo.GetComponentRO<CivilianState>(friendly.Unit).Attitude);
            friendly.Repo.GetComponentRW<CivilianState>(friendly.Unit).Anger = 1f;
            for (int i = 0; i < 10; i++) friendly.Tick();
            Assert.Equal("Protest", friendly.State());

            var hostile = new World();
            hostile.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0],\"Attitude\":1}");
            for (int i = 0; i < 4; i++) hostile.Tick();
            hostile.Repo.GetComponentRW<CivilianState>(hostile.Unit).Anger = 0.09f;
            for (int i = 0; i < 4; i++) hostile.Tick();
            Assert.Equal("Protest", hostile.State());                         // under 0.1: not yet
            hostile.Repo.GetComponentRW<CivilianState>(hostile.Unit).Anger = 0.15f;
            for (int i = 0; i < 4; i++) hostile.Tick();
            Assert.Equal("Riot", hostile.State());
            hostile.Repo.GetComponentRW<CivilianState>(hostile.Unit).Anger = 0.04f;   // below half of HIS 0.1
            for (int i = 0; i < 4; i++) hostile.Tick();
            Assert.Equal("Protest", hostile.State());
        }

        [Fact]
        public unsafe void CE2238_AnAngryProtester_Riots_StrikesTheNearestForcesPerson_AndHisFirstBlowFlipsHisSide()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);   // this node owns his record (CGF, for a scenario cast) ⇒ the flip is Direct
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0]}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Protest", w.State());

            var police = Policeman(w, new Vector3(110, 200, 0));
            w.Repo.GetComponentRW<CivilianState>(w.Unit).Anger = CivilianBehavior.DefaultRiotAt - 0.01f;
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Protest", w.State());   // just under: not yet

            w.Repo.GetComponentRW<CivilianState>(w.Unit).Anger = CivilianBehavior.DefaultRiotAt;
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Riot", w.State());
            Assert.Equal(CivilianMode.Riot, w.Mode);
            Assert.Equal(GestureKind.FistRaised, Gestures.Of(w.Repo, w.Unit));
            var loco = w.Loco;
            Assert.Equal(NavigationConstants.ActionIdMoveTo, loco.ActiveAction);
            var to = ((MoveToParams*)loco.Params)->Destination;
            Assert.True(Vector2.Distance(new Vector2(to.X, to.Y), new Vector2(110, 200)) < 0.01f, $"he runs at the policeman: {to}");
            Assert.Equal(ForceId.Neutral, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);   // ⛔ anger is not an act (AW-16)

            int strikes = Melee.Strikes;
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(111, 200, 0);    // he has reached him
            w.Tick();
            w.Tick();
            Assert.Equal(strikes + 1, Melee.Strikes);
            Assert.True(HostileActs.Committed(w.Repo, w.Unit));
            Assert.Equal(ForceId.Hostile, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);    // the blow is the act: Hostile
            Assert.Equal(ForceId.Friend, w.Repo.GetComponentRO<EntityInfo>(police).ForceId);

            for (int i = 0; i < (int)(Melee.CooldownSeconds / 0.016f) - 5; i++) w.Tick();
            Assert.Equal(strikes + 1, Melee.Strikes);                                            // the cooldown
            for (int i = 0; i < 10; i++) w.Tick();
            Assert.Equal(strikes + 2, Melee.Strikes);

            // a man knocked down is not struck again while he is down (the Health owner stamps it; here by hand)
            if (!w.Repo.HasComponent<EffectState>(police)) w.Repo.AddComponent(police, new EffectState());
            w.Repo.GetComponentRW<EffectState>(police).KnockedDownUntil = HitModel.Now(w.Repo) + 60.0;
            for (int i = 0; i < (int)(Melee.CooldownSeconds * 2 / 0.016f); i++) w.Tick();
            Assert.Equal(strikes + 2, Melee.Strikes);
            w.Repo.GetComponentRW<EffectState>(police).KnockedDownUntil = 0;

            w.Repo.GetComponentRW<CivilianState>(w.Unit).Anger = CivilianBehavior.DefaultRiotAt * 0.5f - 0.01f;
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Protest", w.State());                                                   // calm again
            Assert.Equal(ForceId.Hostile, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);    // sticky (CV-4)
        }

        /// <summary>
        /// ⭐ <c>CE-3238</c> (civilians B-16, HA-2; docs/DESIGN_Civilians_And_Crowd_Control.md §7.16) — the act is the replicated
        /// <see cref="HostileAct"/>: the first stamps its time and flips his side once; a graver one UPGRADES the kind (the time stays); a
        /// lesser one changes nothing; a man another node made act reads committed here from that component alone.
        /// ✅ Red-proofs: <c>Commit</c> without the upgrade ⇒ NonLethal stays ⇒ red; <c>Commit</c> overwriting the kind ⇒ a stone after the
        /// shot downgrades it ⇒ red; <c>Committed</c> without the component ⇒ the replicated actor reads innocent ⇒ red.
        /// </summary>
        [Fact]
        public void CE3238_TheHostileAct_IsReplicated_ItUpgradesNeverDown_AndFlipsOnce()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);   // this node owns his record ⇒ the flip is Direct
            Assert.True(CivilianMind.Ensure(w.Repo, w.Unit));
            Assert.Equal(HostileActKind.None, HostileActs.Of(w.Repo, w.Unit));
            Assert.False(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.None));

            Assert.True(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.NonLethal));                                   // a blow / a stone: non-lethal
            var first = w.Repo.GetComponentRO<HostileAct>(w.Unit);
            Assert.Equal(HostileActKind.NonLethal, first.Kind);
            Assert.True(first.SinceSimTime > 0);
            Assert.True(w.Repo.GetComponentRO<CivilianState>(w.Unit).HostileActAt > 0);                 // the brain's own stamp, in step
            Assert.Equal(ForceId.Hostile, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);

            for (int i = 0; i < 10; i++) w.Tick();
            Assert.False(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.NonLethal));               // no graver: nothing changes
            Assert.True(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.Lethal));                   // a shot: upgraded
            Assert.Equal((first.SinceSimTime, HostileActKind.Lethal),
                (w.Repo.GetComponentRO<HostileAct>(w.Unit).SinceSimTime, w.Repo.GetComponentRO<HostileAct>(w.Unit).Kind));
            Assert.False(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.NonLethal));               // never down
            Assert.Equal(HostileActKind.Lethal, HostileActs.Of(w.Repo, w.Unit));

            var replica = w.Repo.CreateEntity();                                                      // his act, arrived from another node
            w.Repo.AddComponent(replica, new HostileAct { SinceSimTime = 1.0, Kind = HostileActKind.Lethal });
            Assert.True(HostileActs.Committed(w.Repo, replica));
        }

        /// <summary>⭐ <c>CE-2238</c> — a rioter strikes only the FORCES: no forces person within reach ⇒ he stands, fist raised, and strikes
        /// nobody — not a neutral bystander, not a Friend civilian, not a man already down.</summary>
        [Fact]
        public void CE2238_ARioter_StrikesOnlyTheForces_NotABystander_NotAManDown()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0],\"Instigator\":true}");
            var bystander = Policeman(w, new Vector3(101, 200, 0));
            w.Repo.GetComponentRW<EntityInfo>(bystander).ForceId = ForceId.Neutral;
            var down = Policeman(w, new Vector3(100, 201, 0));
            w.Repo.GetComponentRW<Health>(down).Current = 0f;
            var enemy = Policeman(w, new Vector3(99, 200, 0));                       // a combatant, but not of the forces
            w.Repo.GetComponentRW<EntityInfo>(enemy).ForceId = ForceId.Hostile;
            for (int i = 0; i < 8; i++) w.Tick();
            Assert.Equal("Riot", w.State());                       // an instigator's own anger is 1
            int strikes = Melee.Strikes;
            for (int i = 0; i < 200; i++) w.Tick();
            Assert.Equal(strikes, Melee.Strikes);
            Assert.False(HostileActs.Committed(w.Repo, w.Unit));
            Assert.Equal(ForceId.Neutral, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);
        }

        private const long StoneAmmo = 9590, GrenadeAmmo = 9591, CivilianType = 9501;

        /// <summary>⭐ <c>CE-2238</c> part 2 — gives the unit the HANDS production gives every person (backend B-8): a pedestrian TKB type
        /// run through <c>PersonHands.EnsureOnPersons</c>, injected by <c>CombatTkbTranslator</c>; plus <paramref name="stones"/> stones.</summary>
        private static void Hands(World w, int stones, bool phone = false)
        {
            foreach (var reg in new System.Action[]
            {
                () => { if (!w.Repo.IsComponentTypeRegistered<WeaponState>()) w.Repo.RegisterComponent<WeaponState>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<WeaponMountInfo>()) w.Repo.RegisterComponent<WeaponMountInfo>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<AmmoLoadout>()) w.Repo.RegisterComponent<AmmoLoadout>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) w.Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<WeaponChannel>()) w.Repo.RegisterComponent<WeaponChannel>(); },
            }) reg();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var civ = new Fdp.Interfaces.TkbTemplate("Civilian", CivilianType);
            civ.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
            if (phone) civ.AddDescriptor(new PocketItemsDto { Phone = true });   // ⭐ CE-2248 — the civilian types carry one (CivilianTkbCatalog)
            db.Register(civ);
            var stone = new Fdp.Interfaces.TkbTemplate("Stone", StoneAmmo);
            stone.AddDescriptor(new WarheadDto { Kind = WarheadKind.Blunt, Indirect = true });
            stone.AddDescriptor(new HandItemDto { LiesSeconds = 60f });   // ⭐ CE-2238 part 3 — a hand may pick one up (B-13)
            db.Register(stone);
            var grenade = new Fdp.Interfaces.TkbTemplate("Grenade", GrenadeAmmo);
            grenade.AddDescriptor(new WarheadDto { Kind = WarheadKind.Fragmentation, Indirect = true });
            grenade.AddDescriptor(new HandItemDto());
            db.Register(grenade);
            PersonHands.EnsureOnPersons(db);
            w.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            w.Repo.AddComponent(w.Unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = CivilianType });
            if (!w.Repo.HasComponent<WeaponChannel>(w.Unit)) w.Repo.AddComponent(w.Unit, new WeaponChannel());
            db.TryGetByType(CivilianType, out var t);
            new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(w.Repo, w.Unit, t!);
            if (stones > 0) Assert.True(PersonHands.Give(w.Repo, w.Unit, StoneAmmo, stones));
            if (!w.Repo.IsComponentTypeRegistered<WeaponPosture>()) w.Repo.RegisterComponent<WeaponPosture>();
            if (!w.Repo.HasComponent<WeaponPosture>(w.Unit)) w.Repo.AddComponent(w.Unit, new WeaponPosture());   // as BehaviorTkbTranslator adds it
        }

        /// <summary>⭐ <c>CE-2248</c> (§5n HD-1, HD-3) — in a Routine pause he takes his phone out for a share of his pauses (his own seeded
        /// draw), keeps it out while he lingers, puts it away to walk on and when he leaves the routine; a drawn weapon in his hand his
        /// routine never touches.</summary>
        [Fact]
        public void CE2248_InARoutinePause_HeTakesHisPhoneOut_ForAShare_PutsItAwayToWalkOn_AndNeverTouchesADrawnWeapon()
        {
            var w = new World();
            Hands(w, 0, phone: true);
            w.Start("{\"WanderRadius\":8,\"PauseMinSeconds\":0.2,\"PauseMaxSeconds\":0.3}");
            HeldItemKind Held() => HeldItems.Of(w.Repo, w.Unit);
            bool Pause()
            {
                for (int i = 0; i < 3; i++) w.Tick();
                Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
                Assert.Equal(HeldItemKind.None, Held());                                       // walking: no phone
                w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;   // he got there
                w.Tick();
                var first = Held();
                for (int i = 0; i < 5; i++) w.Tick();
                Assert.Equal(first, Held());                                                   // he keeps it out while he lingers
                return first == HeldItemKind.Phone;
            }

            int phones = 0;
            const int pauses = 60;
            for (int k = 0; k < pauses; k++)
            {
                if (Pause()) phones++;
                for (int i = 0; i < 20; i++) w.Tick();                                         // the pause ends: he walks on
            }
            Assert.InRange(phones, (int)(pauses * 0.15f), (int)(pauses * 0.45f));              // a share (0.3), not every pause, not none

            while (!Pause()) for (int i = 0; i < 20; i++) w.Tick();
            w.Fear(CivilianBehavior.DefaultAlarmAt);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Alarmed", w.State());
            Assert.Equal(HeldItemKind.None, Held());                                           // out of the routine: put away

            w.Fear(0f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Routine", w.State());
            w.Repo.GetComponentRW<WeaponPosture>(w.Unit).HeldItem = HeldItemKind.Weapon;      // something else drew his weapon
            for (int k = 0; k < 10; k++)
            {
                for (int i = 0; i < 3; i++) w.Tick();
                w.Repo.GetComponentRW<LocomotionChannel>(w.Unit).Status = Fbt.NodeStatus.Success;
                for (int i = 0; i < 25; i++) w.Tick();
                Assert.Equal(HeldItemKind.Weapon, Held());
            }
        }

        /// <summary>⭐ <c>CE-2248</c> (§5n HD-2) — a rioter with a stone in his hands shows it (<c>Throwable</c>); out of stones his hand is empty
        /// again; leaving the riot he puts it away.</summary>
        [Fact]
        public void CE2248_ARioterWithAStone_ShowsItInHisHand_EmptyHandedOrOutOfTheRiot_Nothing()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            Hands(w, 2);
            Policeman(w, new Vector3(112, 200, 0));
            w.Start("{\"Protest\":true,\"ProtestAt\":[100,200,0],\"Instigator\":true}");
            Assert.Equal(HeldItemKind.None, HeldItems.Of(w.Repo, w.Unit));
            for (int i = 0; i < 8; i++) w.Tick();
            Assert.Equal("Riot", w.State());
            Assert.Equal(HeldItemKind.Throwable, HeldItems.Of(w.Repo, w.Unit));

            PersonHands.Take(w.Repo, w.Unit, StoneAmmo, PersonHands.Rounds(w.Repo, w.Unit, StoneAmmo));
            for (int i = 0; i < 2; i++) w.Tick();
            Assert.Equal(HeldItemKind.None, HeldItems.Of(w.Repo, w.Unit));                    // out of stones

            Assert.True(PersonHands.Give(w.Repo, w.Unit, StoneAmmo, 1));
            for (int i = 0; i < 2; i++) w.Tick();
            Assert.Equal(HeldItemKind.Throwable, HeldItems.Of(w.Repo, w.Unit));
            w.Repo.GetComponentRW<CivilianState>(w.Unit).Anger = 0f;
            w.Fear(1f);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.NotEqual("Riot", w.State());
            Assert.Equal(HeldItemKind.None, HeldItems.Of(w.Repo, w.Unit));                    // out of the riot
        }

        /// <summary>A round of <paramref name="ammo"/> LYING at <paramref name="at"/>, as the node that flies it holds it (B-13: Landed + Lingers).</summary>
        private static Entity Lying(World w, long ammo, Vector3 at)
        {
            if (!w.Repo.IsComponentTypeRegistered<BallisticProjectile>()) w.Repo.RegisterComponent<BallisticProjectile>();
            var e = w.Repo.CreateEntity();
            w.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.Repo.AddComponent(e, new BallisticProjectile { Ammo = ammo, Warhead = (byte)(WarheadRound.Landed | WarheadRound.Lingers), FuzeRemaining = 60f });
            return e;
        }

        /// <summary>⭐ <c>CE-2238</c> part 3 (§5i.3 PK-1…PK-3) — a rioter with EMPTY hands and a policeman 12 m off goes for the nearest LYING
        /// throwable — the stone, not the nearer LIVE grenade — walks to it, asks for it in reach (backend's <c>RoundPickUpSystem</c> grants
        /// it: the stone is gone, in his hands), and after the bend THROWS it at the policeman. With nobody to throw at he leaves it lying.
        /// ✅ Red-proof: the scavenge arm removed ⇒ red; the lethal skip removed from <c>Throwables</c> ⇒ red (he goes for the grenade).</summary>
        [Fact]
        public void CE2238_ARioterWithEmptyHands_PicksTheLyingStoneUp_NotALiveGrenade_AndThrowsIt()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            w.Repo.SetAuthority<BehaviorState>(w.Unit, true);   // his brain is here ⇒ the grant lands in his hands
            Hands(w, 0);
            if (!w.Repo.IsComponentTypeRegistered<HeldFuze>()) w.Repo.RegisterComponent<HeldFuze>();
            if (!w.Repo.Bus.IsRegistered<PickUpRoundEvent>()) w.Repo.Bus.Register<PickUpRoundEvent>();
            if (!w.Repo.Bus.IsRegistered<PickUpGrantEvent>()) w.Repo.Bus.Register<PickUpGrantEvent>();
            var grenade = Lying(w, GrenadeAmmo, new Vector3(102, 200, 0));
            var stone = Lying(w, StoneAmmo, new Vector3(105, 200, 0));
            var pick = new Fdp.Toolkit.Combat.Systems.RoundPickUpSystem();
            void Tick() { w.Tick(); pick.Execute(w.Repo, 0.016f); }

            w.Start("{\"Protest\":true,\"ProtestAt\":[100,200,0],\"Instigator\":true}");
            for (int i = 0; i < 8; i++) Tick();
            Assert.Equal("Riot", w.State());
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);   // nobody to throw at: the stone lies
            Assert.True(w.Repo.IsAlive(stone));

            var police = Policeman(w, new Vector3(112, 200, 0));
            for (int i = 0; i < 80; i++) Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);
            unsafe
            {
                ref var loco = ref w.Repo.GetComponentRW<LocomotionChannel>(w.Unit);
                fixed (byte* p = loco.Params)
                    Assert.True(Vector2.Distance(new Vector2(105, 200), new Vector2(((MoveToParams*)p)->Destination.X, ((MoveToParams*)p)->Destination.Y)) < 0.1f,
                        $"he goes for the STONE, not the nearer live grenade: {((MoveToParams*)p)->Destination}");
            }

            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(104.5f, 200, 0);   // he got there
            for (int i = 0; i < 6; i++) Tick();
            Assert.False(w.Repo.IsAlive(stone), "the stone was granted to him — gone from the ground");
            Assert.Equal(1, PersonHands.Rounds(w.Repo, w.Unit, StoneAmmo));
            Assert.True(w.Repo.IsAlive(grenade), "the live grenade lies where it fell");

            for (int i = 0; i < (int)(CivilianBehavior.BendSeconds / 0.016f) + 10; i++) Tick();
            ref var channel = ref w.Repo.GetComponentRW<WeaponChannel>(w.Unit);
            Assert.Equal(CombatConstants.ActionIdFireAtPoint, channel.ActiveAction);
            Assert.True(Vector2.Distance(new Vector2(ThrowParams(w).Point.X, ThrowParams(w).Point.Y), new Vector2(112, 200)) < 0.01f, "thrown at the policeman");
            Assert.True(HostileActs.Committed(w.Repo, w.Unit));
        }

        private static unsafe Fdp.Toolkit.Combat.Executors.FireAtPointParams ThrowParams(World w)
            => *(Fdp.Toolkit.Combat.Executors.FireAtPointParams*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref w.Repo.GetComponentRW<WeaponChannel>(w.Unit).Params[0]);

        /// <summary>⭐ <c>CE-2238</c> part 2 (§5i.2) — a rioter with stones in his hands, a policeman 12 m off: he stands and THROWS — one
        /// FireAtPoint from his hands mount at the policeman's feet — and that first stone is his hostile act (Hostile); the next stone only
        /// after 3 s; no fist while he has stones and the range is right; out of stones, he closes and strikes.</summary>
        [Fact]
        public void CE2238_ARioterWithStones_ThrowsFromRange_OnePerThreeSeconds_TheFirstIsHisAct_ThenUsesHisFistWhenOut()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            Hands(w, 2);
            var police = Policeman(w, new Vector3(112, 200, 0));
            int strikes = Melee.Strikes;
            w.Start("{\"Protest\":true,\"ProtestAt\":[100,200,0],\"Instigator\":true}");
            for (int i = 0; i < 8; i++) w.Tick();
            Assert.Equal("Riot", w.State());

            ref var channel = ref w.Repo.GetComponentRW<WeaponChannel>(w.Unit);
            Assert.Equal(CombatConstants.ActionIdFireAtPoint, channel.ActiveAction);
            var thrown = ThrowParams(w);
            Assert.Equal(PersonHands.MountIndex(w.Repo, w.Unit), thrown.Mount);
            Assert.Equal(1, thrown.Rounds);
            Assert.True(Vector2.Distance(new Vector2(thrown.Point.X, thrown.Point.Y), new Vector2(112, 200)) < 0.01f, $"at the policeman: {thrown.Point}");
            Assert.True(HostileActs.Committed(w.Repo, w.Unit));
            Assert.Equal(ForceId.Hostile, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);   // the stone is the act
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);            // he stands to throw
            uint first = channel.ActionInstanceId;

            for (int i = 0; i < (int)(CivilianBehavior.ThrowCooldownSeconds / 0.016f) - 10; i++) w.Tick();
            Assert.Equal(first, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActionInstanceId);   // not before 3 s
            for (int i = 0; i < 20; i++) w.Tick();
            Assert.NotEqual(first, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActionInstanceId); // the second stone
            Assert.Equal(strikes, Melee.Strikes);                                                 // no fist while he has stones

            PersonHands.Take(w.Repo, w.Unit, StoneAmmo, PersonHands.Rounds(w.Repo, w.Unit, StoneAmmo));   // out of stones (the executor spends them on CGF)
            for (int i = 0; i < 70; i++) w.Tick();
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);                // he closes ...
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(111, 200, 0);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(strikes + 1, Melee.Strikes);                                             // ... and strikes
        }

        /// <summary>⭐ <c>CE-2238</c> part 2 — a stone only where it reaches and is right: beyond 20 m he closes first; inside 4 m he uses his
        /// fist; a LIVE GRENADE in his hands is not a stone (AW-9: the armed path) — he does not throw it.</summary>
        [Fact]
        public void CE2238_NoStoneBeyond20m_NorInside4m_AndALiveGrenadeIsNotAStone()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            Hands(w, 5);
            var police = Policeman(w, new Vector3(126, 200, 0));                       // 26 m
            w.Start("{\"Protest\":true,\"ProtestAt\":[100,200,0],\"Instigator\":true}");
            for (int i = 0; i < 8; i++) w.Tick();
            Assert.Equal("Riot", w.State());
            Assert.NotEqual(CombatConstants.ActionIdFireAtPoint, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, w.Loco.ActiveAction);   // too far: he closes

            w.Repo.GetComponentRW<SimTransform>(police).Position = new Vector3(102, 200, 0);   // 2 m
            int strikes = Melee.Strikes;
            for (int i = 0; i < 80; i++) w.Tick();
            Assert.NotEqual(CombatConstants.ActionIdFireAtPoint, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(101, 200, 0);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(strikes + 1, Melee.Strikes);                                   // inside 4 m: the fist

            PersonHands.Take(w.Repo, w.Unit, StoneAmmo, 5);
            Assert.True(PersonHands.Give(w.Repo, w.Unit, GrenadeAmmo, 1));
            w.Repo.GetComponentRW<SimTransform>(police).Position = new Vector3(112, 200, 0);   // 12 m again
            w.Repo.GetComponentRW<SimTransform>(w.Unit).Position = new Vector3(100, 200, 0);
            for (int i = 0; i < 300; i++) w.Tick();
            Assert.NotEqual(CombatConstants.ActionIdFireAtPoint, w.Repo.GetComponentRO<WeaponChannel>(w.Unit).ActiveAction);
        }

        /// <summary>A forces rifleman at <paramref name="at"/> whose weapon posture is <paramref name="kind"/> at <paramref name="target"/>.</summary>
        private static Entity Covering(World w, Vector3 at, Entity target, WeaponPostureKind kind = WeaponPostureKind.Aimed)
        {
            var e = Policeman(w, at);
            w.Repo.AddComponent(e, new WeaponPosture { Kind = kind, Target = target });
            if (!w.Repo.IsComponentTypeRegistered<WeaponState>()) w.Repo.RegisterComponent<WeaponState>();
            w.Repo.AddComponent(e, new WeaponState { Ammo = 30, MaxAmmo = 30 });   // ⭐ CE-2239 — a rifle in hand (no TKB ⇒ a weapon is lethal)
            return e;
        }

        /// <summary>⭐ <c>CE-2239</c> (§5j.1) — a COVERING weapon is a lethal one: a rioter who struck (Hostile) with a policeman's BATON
        /// LAUNCHER aimed at him does not raise his hands (the line's force must still be able to break the riot); a rifle aimed at him
        /// does. ✅ Red-proof: the <c>LethalInHand</c> line removed from <c>Covering.By</c> ⇒ red.</summary>
        [Fact]
        public void CE2239_ABatonLauncherAimedAtHim_DoesNotCoverHim_ARifleDoes()
        {
            var w = new World();
            foreach (var reg in new System.Action[]
            {
                () => { if (!w.Repo.IsComponentTypeRegistered<WeaponState>()) w.Repo.RegisterComponent<WeaponState>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<WeaponMountInfo>()) w.Repo.RegisterComponent<WeaponMountInfo>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<AmmoLoadout>()) w.Repo.RegisterComponent<AmmoLoadout>(); },
                () => { if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) w.Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(); },
            }) reg();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Map.Common.HrotEnvironment.RegisterBuiltInTkb(db);
            w.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0],\"Instigator\":true}");
            for (int i = 0; i < 8; i++) w.Tick();
            Assert.True(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.NonLethal));

            var cop = Policeman(w, new Vector3(140, 230, 0));
            w.Repo.AddComponent(cop, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityLauncher });
            Assert.True(db.TryGetByType(Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityLauncher, out var t));
            new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(w.Repo, cop, t!);
            w.Repo.AddComponent(cop, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = w.Unit });
            Assert.False(Fdp.Toolkit.Combat.Armament.LethalInHand(w.Repo, cop));
            for (int i = 0; i < 60; i++) w.Tick();
            Assert.NotEqual("Surrender", w.State());
            Assert.True(Fdp.Toolkit.Combat.Covering.By(w.Repo, w.Unit).IsNull);

            Covering(w, new Vector3(150, 200, 0), w.Unit);   // a rifle
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Surrender", w.State());
        }

        /// <summary>⭐ <c>CE-2241</c> ① (§5h.3, CV-13a) — a rioter who struck a policeman (Hostile) is covered by a rifleman: he stops and
        /// raises his hands, and keeps them up while the rifleman HOLDS at him (Ready, the target kept — what <c>HoldSurrender</c> leaves);
        /// his side stays Hostile; with the rifle lowered for 5 s his hands come down (and not before); a wound still wins.</summary>
        [Fact]
        public void CE2241_ACoveredHostile_RaisesHisHands_KeepsThemUpWhileHeldAt_LowersThemWhenUncovered()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0],\"Instigator\":true}");
            for (int i = 0; i < 8; i++) w.Tick();
            Assert.Equal("Riot", w.State());
            Assert.True(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.NonLethal));                         // his blow (as §5i lands it)
            Assert.Equal(ForceId.Hostile, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);

            var rifle = Covering(w, new Vector3(140, 230, 0), w.Unit);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Surrender", w.State());
            Assert.Equal(CivilianMode.Surrender, w.Mode);
            Assert.Equal(GestureKind.HandsUp, Gestures.Of(w.Repo, w.Unit));
            Assert.True(Fdp.Toolkit.Perception.ThreatDanger.ShowsHandsUp(w.Repo, w.Unit));   // what AimAndFire's HoldOf reads
            Assert.Equal(0, w.Loco.ActiveAction);                                     // he stands still

            w.Repo.GetComponentRW<WeaponPosture>(rifle).Kind = WeaponPostureKind.Ready;   // the rifleman holds at him: still covered
            for (int i = 0; i < (int)(CivilianBehavior.SurrenderHoldSeconds * 2 / 0.016f); i++) w.Tick();
            Assert.Equal("Surrender", w.State());
            Assert.Equal(ForceId.Hostile, w.Repo.GetComponentRO<EntityInfo>(w.Unit).ForceId);   // ⛔ never flips back (CV-13a)

            w.Repo.GetComponentRW<WeaponPosture>(rifle).Kind = WeaponPostureKind.AtEase;  // the rifle comes down
            for (int i = 0; i < (int)(CivilianBehavior.SurrenderHoldSeconds / 0.016f) - 10; i++) w.Tick();
            Assert.Equal("Surrender", w.State());                                     // not before 5 s
            for (int i = 0; i < 20; i++) w.Tick();
            Assert.NotEqual("Surrender", w.State());
            Assert.NotEqual(GestureKind.HandsUp, Gestures.Of(w.Repo, w.Unit));

            w.Repo.GetComponentRW<WeaponPosture>(rifle).Kind = WeaponPostureKind.Aimed;   // covered again AND wounded, the same tick:
            w.Repo.GetComponentRW<Health>(w.Unit).Current = 40f;                      // both ways out are open — the wound wins:
            for (int i = 0; i < 4; i++)                                               // he never passes through Surrender on the way
            {
                w.Tick();
                Assert.NotEqual("Surrender", w.State());
            }
            Assert.Equal("Casualty", w.State());
        }

        /// <summary>⭐ <c>CE-2241</c> ① — only a man who COMMITTED an act raises his hands, and only for the FORCES: a covered protester who
        /// struck nobody does not, and a Hostile gunman (not of the forces) aiming at a man who did strike makes no one surrender.</summary>
        [Fact]
        public void CE2241_OnlyAManWhoCommittedAnAct_RaisesHisHands_AndOnlyToTheForces()
        {
            var w = new World();
            w.Repo.AddComponent(w.Unit, new EntityInfo { ForceId = ForceId.Neutral });
            w.Repo.SetAuthority<EntityInfo>(w.Unit, true);
            w.Start("{\"Protest\":true,\"ProtestAt\":[130,200,0]}");
            for (int i = 0; i < 4; i++) w.Tick();
            var rifle = Covering(w, new Vector3(140, 230, 0), w.Unit);
            for (int i = 0; i < 60; i++) w.Tick();
            Assert.Equal("Protest", w.State());                                       // covered, but he did nothing

            w.Repo.GetComponentRW<EntityInfo>(rifle).ForceId = ForceId.Hostile;      // a gunman, not the forces
            Assert.True(HostileActs.Commit(w.Repo, w.Unit, HostileActKind.NonLethal));
            for (int i = 0; i < 60; i++) w.Tick();
            Assert.NotEqual("Surrender", w.State());
            Assert.NotEqual(GestureKind.HandsUp, Gestures.Of(w.Repo, w.Unit));

            var far = Covering(w, new Vector3(100 + Fdp.Toolkit.Combat.Covering.ReachMetres + 5, 200, 0), w.Unit);   // forces, beyond reach
            for (int i = 0; i < 60; i++) w.Tick();
            Assert.NotEqual("Surrender", w.State());
            w.Repo.GetComponentRW<SimTransform>(far).Position = new Vector3(150, 200, 0);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal("Surrender", w.State());
        }

        /// <summary>⭐ <c>CE-2240</c> — a civilian NOT told to protest never does, and his arms stay down.</summary>
        [Fact]
        public void CE2240_ACivilianNotToldToProtest_NeverDoes()
        {
            var w = new World();
            w.Start();
            for (int i = 0; i < 30; i++) w.Tick();
            Assert.Equal("Routine", w.State());
            Assert.Equal(GestureKind.None, Gestures.Of(w.Repo, w.Unit));
        }

        [Fact]
        public void CE2235_TheCivilianTypesStartCivilianHsm_ByDefault()
        {
            var tkb = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Core.Tkb.CivilianTkbCatalog.RegisterAll(tkb);
            int want = BehaviorHash.FromName(BehaviorNames.CivilianHsm);
            foreach (int type in new[] { Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultMale, Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultFemale,
                                         Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianChild, Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianElderly })
            {
                Assert.True(tkb.TryGetByType(type, out var t));
                Assert.Equal(want, t!.GetDescriptor<BehaviorProfileDto>()!.DefaultBehaviorHash);
            }
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            Assert.True(registry.TryGetId(BehaviorNames.CivilianHsm, out int id));
            Assert.Equal(want, id);
        }
    }
}
