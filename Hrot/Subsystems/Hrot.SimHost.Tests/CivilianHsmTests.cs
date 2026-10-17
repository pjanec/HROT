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

        /// <summary>A forces rifleman at <paramref name="at"/> whose weapon posture is <paramref name="kind"/> at <paramref name="target"/>.</summary>
        private static Entity Covering(World w, Vector3 at, Entity target, WeaponPostureKind kind = WeaponPostureKind.Aimed)
        {
            var e = Policeman(w, at);
            w.Repo.AddComponent(e, new WeaponPosture { Kind = kind, Target = target });
            return e;
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
            Assert.True(HostileActs.Commit(w.Repo, w.Unit));                         // his blow (as §5i lands it)
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

            w.Repo.GetComponentRW<WeaponPosture>(rifle).Kind = WeaponPostureKind.Aimed;   // covered again ...
            for (int i = 0; i < 30; i++) w.Tick();
            Assert.Equal("Surrender", w.State());
            w.Repo.GetComponentRW<Health>(w.Unit).Current = 40f;                      // ... and wounded: the wound wins
            for (int i = 0; i < 4; i++) w.Tick();
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
            Assert.True(HostileActs.Commit(w.Repo, w.Unit));
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
