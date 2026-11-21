using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Creatures;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-4 (docs/designs/animals/DESIGN_Animals.md §6.2 AB-4, AN-1, AR-9) — <c>AnimalHsm</c> over a herd: two cows GRAZE round
    /// their herd's grazing spot (the <see cref="HerdSystem"/> picks it); a fright past panic on ONE spreads to the other (contagion) and both FLEE
    /// AWAY from where it came from at the species' flee speed; after the run they RECOVER, then graze again — and the herd's next grazing spot is
    /// on the far side, away from the fright; at night the herd RESTS. 🔴 Red-proofs: the contagion removed from the HerdSystem ⇒ the second cow
    /// never flees; the grazing pick ignoring the fright ⇒ the "away" assert red.
    /// </summary>
    [Collection("MeleeStrikes")]
    public sealed class AnimalHsmTests
    {
        private const long CowType = 700, DogType = 705, FlockType = 707, RabbitType = 715, HuntingDogType = 716;
        private static readonly AnimalTemperamentDto HuntingDog = new()
        {
            Species = (byte)AnimalSpecies.HuntingDog, Fearless = true, Ranges = true, FlightZoneM = 30f, PanicZoneM = 8f, FleeSpeed = 8f, FleeDistanceM = 30f, Recovery = 15f, WalkSpeed = 2.5f,
        };
        private static readonly AnimalTemperamentDto Rabbit = new()
        {
            Species = (byte)AnimalSpecies.Rabbit, FlightZoneM = 25f, PanicZoneM = 12f, FleeSpeed = 14f, FleeDistanceM = 70f, Recovery = 30f, WalkSpeed = 0.8f, FleeStyle = 1, Freezes = true,
        };
        private static readonly AnimalTemperamentDto Cattle = new()
        {
            Species = (byte)AnimalSpecies.Cattle, FlightZoneM = 25f, PanicZoneM = 10f, FleeSpeed = 9f, FleeDistanceM = 200f,
            Recovery = 8f, HerdCohesion = 0.4f, WalkSpeed = 1f,
        };
        private static readonly AnimalTemperamentDto Dog = new()
        {
            Species = (byte)AnimalSpecies.Dog, Fearless = true, Territorial = true, FlightZoneM = 30f, PanicZoneM = 8f, FleeSpeed = 10f,
            FleeDistanceM = 30f, Recovery = 20f, WalkSpeed = 1.2f,
        };
        private static readonly AnimalTemperamentDto Flock = new()
        {
            Species = (byte)AnimalSpecies.BirdFlock, FlightZoneM = 40f, PanicZoneM = 15f, FleeSpeed = 12f, FleeDistanceM = 400f, Recovery = 8f,
            HerdCohesion = 1f, WalkSpeed = 5f,
        };

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Herd, A, B;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            public readonly HerdSystem Herds = new();
            private double _time;
            private long _frame;

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                foreach (var t in new AnimalTkbTranslator().GetProducedComponents()) { }
                Reg<AnimalState>(); Reg<Fear>(); Reg<AnimalTasks>(); Reg<GroupMember>(); Reg<Group>(); Reg<HerdState>(); Reg<Home>(); Reg<FlockState>(); Reg<TkbIdentity>();
                Reg<Fdp.Toolkit.Civilians.Utterance>();
                if (!Repo.IsComponentTypeRegistered<StanceIntent>()) Repo.RegisterComponent<StanceIntent>();
                if (!Repo.IsComponentTypeRegistered<InjuryThresholds>()) Repo.RegisterComponent<InjuryThresholds>();
                Repo.SetSingletonManaged<IGeographicTransform>(UtmTransform.AtOrigin(0.0, 0.0, 0.0));
                var db = new TkbDatabase();
                var cow = new TkbTemplate("Cattle", CowType); cow.AddDescriptor(Cattle); db.Register(cow);
                var dog = new TkbTemplate("Dog", DogType); dog.AddDescriptor(Dog); db.Register(dog);
                var flock = new TkbTemplate("BirdFlock", FlockType); flock.AddDescriptor(Flock); db.Register(flock);
                var rabbit = new TkbTemplate("Rabbit", RabbitType); rabbit.AddDescriptor(Rabbit); db.Register(rabbit);
                var hdog = new TkbTemplate("HuntingDog", HuntingDogType); hdog.AddDescriptor(HuntingDog); db.Register(hdog);
                Repo.SetSingletonManaged<ITkbDatabase>(db);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                Herd = Repo.CreateEntity();
                Repo.AddComponent(Herd, new HerdState { Species = AnimalSpecies.Cattle });
                Repo.AddComponent(Herd, new Group { Kind = GroupKind.Herd });   // ⭐ CE-4628 — the herd's header (centre, lead, count)
                Repo.AddComponent(Herd, new Home { Centre = new Vector3(0, 0, 0), Radius = 60f });
                A = Cow(new Vector3(0, 0, 0));
                B = Cow(new Vector3(8, 0, 0));
                Tick();
            }

            public bool WithFear;
            private readonly Fdp.Toolkit.Civilians.CivilianFearSystem _fear = new();

            /// <summary>Any animal of <paramref name="type"/> (no herd).</summary>
            public Entity Animal(long type, Vector3 at, AnimalSpecies species)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new BehaviorState());
                Repo.AddComponent(e, new LocomotionChannel());
                Repo.AddComponent(e, new Health { Current = 80f, Max = 80f });
                Repo.AddComponent(e, new InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
                Repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
                Repo.AddComponent(e, new AnimalState());   // ⭐ CE-4628 — its species is its TKB type's (the template above)
                Repo.AddComponent(e, new Fear());
                Repo.AddComponent(e, new AnimalTasks());
                Repo.AddComponent(e, new Fdp.Toolkit.Civilians.Utterance());
                if (!Repo.IsComponentTypeRegistered<EntityInfo>()) Repo.RegisterComponent<EntityInfo>();
                Repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Neutral });   // spawned neutral (AR-6)
                return e;
            }

            /// <summary>A man on foot (what the flight zone and the dog see).</summary>
            public Entity Man(Vector3 at)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new CarKinem.Core.VehicleParams { Class = CarKinem.Core.VehicleClass.Pedestrian });
                Repo.AddComponent(e, new Health { Current = 100f, Max = 100f });
                Repo.AddComponent(e, new InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
                return e;
            }

            private void Reg<T>() where T : unmanaged { if (!Repo.IsComponentTypeRegistered<T>()) Repo.RegisterComponent<T>(); }

            private Entity Cow(Vector3 at)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new BehaviorState());
                Repo.AddComponent(e, new LocomotionChannel());
                Repo.AddComponent(e, new Health { Current = 400f, Max = 400f });
                Repo.AddComponent(e, new InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
                Repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)CowType });
                Repo.AddComponent(e, new AnimalState());
                Repo.AddComponent(e, new Fear());
                Repo.AddComponent(e, new AnimalTasks());
                Repo.AddComponent(e, new GroupMember { Group = Herd });
                Repo.AddComponent(e, new Fdp.Toolkit.Civilians.Utterance());   // its voice, from spawn (the translator's)
                return e;
            }

            public void Tick()
            {
                _time += 0.05;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.05f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                if (Fdp.Core.ParallelReadCheck.HasDeclarations) Repo.Tick();
                Repo.Bus.SwapBuffers();
                if (WithFear) _fear.Execute(Repo, 0.05f);   // the CGF order: the fear system, the herds, then the brains
                Herds.Execute(Repo, 0.05f);
                if (Fdp.Core.ParallelReadCheck.HasDeclarations) Repo.CaptureLastTick();
                _ingress.Execute(Repo, 0.05f);
                _brain.Execute(Repo, 0.05f);
                Repo.FlushCommandBuffers();
            }

            public void Start(Entity e)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = BehaviorNames.AnimalHsm, JsonParams = "{}", Origin = BehaviorOrigin.Sop });

            public void Run(double seconds) { for (double t = 0; t < seconds; t += 0.05) Tick(); }
            public AnimalMode Mode(Entity e) => Repo.GetComponentRO<AnimalState>(e).Mode;

            public unsafe (Vector3 To, float Speed, bool Moving) Move(Entity e)
            {
                ref readonly var loco = ref Repo.GetComponentRO<LocomotionChannel>(e);
                if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo) return (default, 0f, false);
                fixed (byte* p = loco.Params) return (((MoveToParams*)p)->Destination, ((MoveToParams*)p)->Speed, true);
            }

            /// <summary>The move ends: the animal is where it went (what the locomotion executor would report).</summary>
            public void Arrive(Entity e)
            {
                var (to, _, moving) = Move(e);
                if (!moving) return;
                Repo.GetComponentRW<SimTransform>(e).Position = to;
                Repo.GetComponentRW<LocomotionChannel>(e).Status = Fbt.NodeStatus.Success;
            }
        }

        [Fact]
        public void CE4623_AHerd_Grazes_FleesAwayTogether_Recovers_AndGrazesAgainAwayFromTheFright()
        {
            var w = new World();
            w.Start(w.A); w.Start(w.B);
            w.Run(0.5);
            Assert.Equal(AnimalMode.Calm, w.Mode(w.A));
            var spot = w.Repo.GetComponentRO<HerdState>(w.Herd).NextGrazing;
            Assert.NotEqual(Vector3.Zero, (Vector3)spot);                                         // the herd picked a grazing spot
            var (to, speed, moving) = w.Move(w.A);
            Assert.True(moving);
            Assert.True(Vector2.Distance(new(to.X, to.Y), new(spot.X, spot.Y)) <= AnimalBehavior.TightSpreadMetres + AnimalBehavior.LooseSpreadMetres + 0.1f,
                $"it grazes round the herd's spot {spot}: {to}");
            Assert.Equal(Cattle.WalkSpeed, speed);

            // a fright past panic on cow A only, from the west
            var from = new Vector3(-50, 0, 0);
            var fear = new Fear { Value = 0.95f, FrightFrom = from, HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            w.Repo.GetComponentRW<Fear>(w.A) = fear;
            w.Run(2.0);   // contagion carries B past panic in ~1 s (8 m from A, cattle cohesion 0.4)
            Assert.Equal(AnimalMode.Flee, w.Mode(w.A));
            Assert.Equal(AnimalMode.Flee, w.Mode(w.B));                                  // caught it from A (contagion)
            Assert.Equal(Fdp.Toolkit.Civilians.UtteranceKind.AnimalAlarm,                  // ⭐ AB-8 — the alarm as it bolted
                w.Repo.GetComponentRO<Fdp.Toolkit.Civilians.Utterance>(w.B).Kind);
            foreach (var c in new[] { w.A, w.B })
            {
                var (dest, s, m) = w.Move(c);
                Assert.True(m);
                Assert.Equal(Cattle.FleeSpeed, s);
                Assert.True(dest.X > 100f, $"it runs AWAY from the fright (east): {dest}");
            }

            // they arrive; the fear fades (no new fright); they recover, then graze again
            w.Arrive(w.A); w.Arrive(w.B);
            w.Repo.GetComponentRW<Fear>(w.A).Value = 0.5f;
            w.Repo.GetComponentRW<Fear>(w.B).Value = 0.5f;
            w.Run(0.5);
            Assert.Equal(AnimalMode.Recover, w.Mode(w.A));
            w.Repo.GetComponentRW<Fear>(w.A).Value = 0f;
            w.Repo.GetComponentRW<Fear>(w.B).Value = 0f;
            w.Run(Cattle.Recovery * AnimalBehavior.LookBackShare + 1.0);
            Assert.Equal(AnimalMode.Calm, w.Mode(w.A));
            var next = w.Repo.GetComponentRO<HerdState>(w.Herd).NextGrazing;
            var home = new Vector2(0, 0);
            Assert.True(next.X > 0f, $"the next grazing spot is on the far side of the home from the fright (west): {next}");
            Assert.True(Vector2.Distance(new(next.X, next.Y), home) <= 60f, $"and inside the home: {next}");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-5 (AN-5, AR-6) — the GUARD DOG: a man walks onto its ground ⇒ it BARKS (Guard, Calling) standing between him and
        /// its home, never leaving it; he comes close inside its home ⇒ it ATTACKS and BITES (a blow of the bite kind), marked DANGEROUS — no
        /// longer held as a neutral, and firing at it is self-defence; he backs off out of its home ⇒ it barks again (no longer dangerous); he
        /// leaves ⇒ it settles. 🔴 Red-proofs: the intruder search skipped ⇒ no bark; the dangerous mark not honoured by IsNeutral ⇒ red.
        /// </summary>
        [Fact]
        public void CE4623_TheGuardDog_Barks_BitesOnItsGround_IsDangerousWhileItDoes_AndSettles()
        {
            var w = new World { WithFear = true };
            if (!w.Repo.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>()) w.Repo.RegisterComponent<CarKinem.Core.VehicleParams>();
            var dog = w.Animal(DogType, new Vector3(100, 0, 0), AnimalSpecies.Dog);
            w.Repo.AddComponent(dog, new Home { Centre = new Vector3(100, 0, 0), Radius = 20f });
            w.Start(dog);
            var man = w.Man(new Vector3(100, 40, 0));                // outside its ground (home 20 m + 15 m)
            w.Run(1.0);
            Assert.Equal(AnimalMode.Calm, w.Mode(dog));

            w.Repo.GetComponentRW<SimTransform>(man).Position = new Vector3(100, 30, 0);   // 10 m outside its fence: it barks from inside
            w.Run(1.5);
            Assert.Equal(AnimalMode.Guard, w.Mode(dog));
            var flags = w.Repo.GetComponentRO<AnimalState>(dog).Flags;
            Assert.True((flags & AnimalFlags.Calling) != 0);
            Assert.True((flags & AnimalFlags.Dangerous) == 0);
            Assert.True(Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(w.Repo, dog));   // a barking dog is still a neutral
            Assert.Equal(Fdp.Toolkit.Civilians.UtteranceKind.Bark,                                      // ⭐ AB-8 — and it is heard barking
                Fdp.Toolkit.Civilians.Utterances.Of(w.Repo, dog, w.Repo.SimulationTime, out _));
            var (to, _, moving) = w.Move(dog);
            Assert.True(moving);
            Assert.True(Vector2.Distance(new(to.X, to.Y), new(100, 0)) <= 20.01f, $"it stays inside its ground: {to}");

            w.Repo.GetComponentRW<SimTransform>(man).Position = new Vector3(100, 15, 0);   // inside its home
            w.Repo.GetComponentRW<SimTransform>(dog).Position = new Vector3(100, 11, 0);   // it stood its ground near him
            int bites = Fdp.Toolkit.Combat.Melee.Strikes;
            w.Run(1.0);
            Assert.Equal(AnimalMode.Guard, w.Mode(dog));
            Assert.True((w.Repo.GetComponentRO<AnimalState>(dog).Flags & AnimalFlags.Dangerous) != 0, "attacking: dangerous");
            Assert.False(Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(w.Repo, dog));
            Assert.True(Fdp.Toolkit.Combat.ForceLevels.SelfDefence(w.Repo, man, dog));
            w.Repo.GetComponentRW<SimTransform>(dog).Position = new Vector3(100, 14, 0);   // it reached him
            w.Run(0.5);
            Assert.True(Fdp.Toolkit.Combat.Melee.Strikes > bites, "it bit him");

            w.Repo.GetComponentRW<SimTransform>(man).Position = new Vector3(100, 30, 0);   // he backs out of its home
            w.Run(1.0);
            Assert.True((w.Repo.GetComponentRO<AnimalState>(dog).Flags & AnimalFlags.Dangerous) == 0, "no longer dangerous");
            Assert.False(Fdp.Toolkit.Combat.ForceLevels.SelfDefence(w.Repo, man, dog));
            w.Repo.GetComponentRW<SimTransform>(man).Position = new Vector3(100, 80, 0);   // he leaves
            w.Run(1.0);
            Assert.Equal(AnimalMode.Calm, w.Mode(dog));
            Assert.True((w.Repo.GetComponentRO<AnimalState>(dog).Flags & AnimalFlags.Calling) == 0);
        }

        /// <summary>⭐ <c>CE-4623</c> AB-5 — a BIRD FLOCK sits perched; a fright flushes it (Flying) and it flies its flee distance away; it comes
        /// down (Landed) and, calm, perches again.</summary>
        [Fact]
        public void CE4623_ABirdFlock_IsPerched_FlushesOnAFright_LandsAndPerchesAgain()
        {
            var w = new World();
            var flock = w.Animal(FlockType, new Vector3(-100, 0, 10), AnimalSpecies.BirdFlock);
            w.Repo.AddComponent(flock, new FlockState { Count = 40, InitialCount = 40, Mode = FlockMode.Landed });
            w.Start(flock);
            w.Run(0.5);
            Assert.Equal(FlockMode.Perched, w.Repo.GetComponentRO<FlockState>(flock).Mode);
            Assert.False(w.Move(flock).Moving);                                              // a perched flock does not wander
            w.Repo.GetComponentRW<Fear>(flock) = new Fear { Value = 0.9f, FrightFrom = new Vector3(-150, 0, 0), HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            w.Run(0.3);
            Assert.Equal(FlockMode.Flying, w.Repo.GetComponentRO<FlockState>(flock).Mode);
            Assert.True((w.Repo.GetComponentRO<AnimalState>(flock).Flags & AnimalFlags.InFlight) != 0);   // ⭐ CE-11008 (RR-8) — "flying" is state, on its flags too
            var (to, speed, _) = w.Move(flock);
            Assert.True(to.X > 200f, $"it flies away from the shot: {to}");
            Assert.Equal(Flock.FleeSpeed, speed);
            w.Arrive(flock);
            w.Repo.GetComponentRW<Fear>(flock).Value = 0.3f;
            w.Run(0.3);
            Assert.Equal(FlockMode.Landed, w.Repo.GetComponentRO<FlockState>(flock).Mode);
            Assert.Equal(AnimalFlags.None, w.Repo.GetComponentRO<AnimalState>(flock).Flags & AnimalFlags.InFlight);   // ⭐ CE-11008 — landed: cleared
            w.Repo.GetComponentRW<Fear>(flock).Value = 0f;
            w.Run(Flock.Recovery * AnimalBehavior.LookBackShare + 0.5);
            Assert.Equal(FlockMode.Perched, w.Repo.GetComponentRO<FlockState>(flock).Mode);
        }

        /// <summary>⭐ <c>CE-4649</c> part C ② (AN-20a) — a LONE bird that is only FRIGHTENED (fear 0.5, band 2, below panic) TAKES OFF: it flies to a point beyond the flight model's descent distance
        /// (so it climbs to cruise) and is Flying — a 15 m hop never left the ground. A cow at the same fear still only moves off 15 m. ✅ Red-proof: the bird branch removed ⇒ 15 m, not flying ⇒ red.</summary>
        [Fact]
        public void CE4649_AFrightenedLoneBird_TakesOff_ACowOnlyMovesOff()
        {
            var w = new World();
            var bird = w.Animal(FlockType, new Vector3(-100, 0, 10), AnimalSpecies.Bird);
            w.Repo.AddComponent(bird, new FlockState { Count = 1, InitialCount = 1, Mode = FlockMode.Landed });
            w.Start(bird);
            w.Run(0.5);
            w.Repo.GetComponentRW<Fear>(bird) = new Fear { Value = 0.5f, FrightFrom = new Vector3(-150, 0, 0), HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            w.Run(0.3);
            var (to, _, moving) = w.Move(bird);
            Assert.True(moving);
            Assert.True(Vector3.Distance(new Vector3(-100, 0, 10), to) > Fdp.Toolkit.Motion.BirdFlightModel.DescendWithinM, $"it flies past the descent distance: {to}");
            Assert.Equal(FlockMode.Flying, w.Repo.GetComponentRO<FlockState>(bird).Mode);
            Assert.True((w.Repo.GetComponentRO<AnimalState>(bird).Flags & AnimalFlags.InFlight) != 0);   // ⭐ CE-11008 (RR-8) — a lone bird says it on its own flags
        }

        /// <summary>⭐ <c>CE-4649</c> part C ② (AN-20a) — a lone BIRD's collider is its BODY (radius 0.15 m = half its 0.3 m width, height 0.15 m), not the 2.5 m fallback circle that made it a 5 m barn door; (the round\'s height band follows it: RaycastSolverSystemTests CE4649). ✅ Red-proof: the animal arm of the translator removed ⇒ radius 2.5 ⇒ red.</summary>
        [Fact]
        public void CE4649_ALoneBirdsCollider_IsItsBody_AndARoundAt25mHitsIt()
        {
            var db = new TkbDatabase();
            Hrot.Core.Tkb.AnimalTkbCatalog.RegisterAll(db);
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>(); repo.RegisterComponent<Health>(); repo.RegisterComponent<PhysicsCollider>();
            var bird = repo.CreateEntity();
            repo.AddComponent(bird, new SimTransform { Position = new Vector3(40f, 0f, 25f), Rotation = Quaternion.Identity });
            new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(repo, bird, db.GetByType((long)Hrot.Map.Common.TkbEntityTypes.Animal_Bird)!);
            ref readonly var col = ref repo.GetComponentRO<PhysicsCollider>(bird);
            Assert.Equal(0.15f, col.Radius, 3);
            Assert.Equal(0.15f, col.Height, 3);
        }

        /// <summary>⭐ <c>CE-4649</c> part D (AN-21b/c) — a RABBIT: frightened from afar (fear 0.5, band 2) it FREEZES — mode Crouch, its stance Crouched (the signature a crouching man has), not moving; panicked (0.9) it BOLTS in a ZIGZAG:
        /// leg after leg, ≥ 4 direction changes of ≥ 30° across the run, the legs 0.6–1.2 s long, ending when it has run its distance; standing again. ✅ Red-proof: the freeze branch removed ⇒ it walks off ⇒ red.</summary>
        [Fact]
        public void CE4649_ARabbit_FreezesCrouched_ThenBoltsInAZigzag()
        {
            var w = new World();
            var r = w.Animal(RabbitType, new Vector3(0, 0, 0), AnimalSpecies.Rabbit);
            w.Repo.AddComponent(r, new StanceIntent { TargetStance = Fdp.Toolkit.Tkb.Domain.StanceId.Standing });
            w.Start(r);
            w.Run(0.5);
            w.Repo.GetComponentRW<Fear>(r) = new Fear { Value = 0.5f, FrightFrom = new Vector3(-30, 0, 0), HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            w.Run(0.5);
            Assert.Equal(AnimalMode.Crouch, w.Mode(r));
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, w.Repo.GetComponentRO<StanceIntent>(r).TargetStance);
            Assert.False(w.Move(r).Moving);                                              // frozen: no leg
            w.Repo.GetComponentRW<Fear>(r) = new Fear { Value = 0.9f, FrightFrom = new Vector3(-8, 0, 0), HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            var heading = new System.Collections.Generic.List<float>();
            int turns = 0;
            float? last = null;
            for (int i = 0; i < 40; i++)
            {
                w.Run(0.1);
                var (to, speed, moving) = w.Move(r);
                if (!moving) { if (w.Mode(r) == AnimalMode.Flee && heading.Count > 2) break; continue; }
                var from = w.Repo.GetComponentRO<SimTransform>(r).Position;
                float a = MathF.Atan2(to.Y - from.Y, to.X - from.X);
                if (last is { } l)
                {
                    float d = MathF.Abs(MathF.IEEERemainder(a - l, MathF.Tau));
                    if (d >= 30f * MathF.PI / 180f - 1e-3f) turns++;
                }
                last = a; heading.Add(a);
                Assert.Equal(Rabbit.FleeSpeed, speed);
                w.Arrive(r);
            }
            Assert.Equal(AnimalMode.Flee, w.Mode(r));
            Assert.True(turns >= 4, $"a zigzag: {turns} turns of ≥ 30° over {heading.Count} legs");
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, w.Repo.GetComponentRO<StanceIntent>(r).TargetStance);
        }

        /// <summary>⭐ <c>CE-4649</c> part D (AN-21c) — the zigzag's leg is pure: 30–50° off the line, 0.6–1.2 s × speed long, the side flips each leg except for a seeded double jink; the same inputs give the same leg; and it allocates NOTHING.
        /// ✅ Red-proof: the jink angle fixed at 0 ⇒ a straight line ⇒ red.</summary>
        [Fact]
        public void CE4649_TheZigzagLeg_IsDeterministic_BoundedAndAllocatesNothing()
        {
            var line = new Vector3(1, 0, 0);
            byte side = 0;
            float lo = 14f * AnimalBehavior.LegMinSeconds, hi = 14f * AnimalBehavior.LegMaxSeconds;
            int flips = 0; byte prev = 0;
            for (int leg = 0; leg < 40; leg++)
            {
                var to = AnimalBehavior.ZigzagTarget(Vector3.Zero, line, 77, leg, leg * 0.8, 14f, ref side, out float len);
                float ang = MathF.Abs(MathF.Atan2(to.Y, to.X));
                Assert.InRange(ang, AnimalBehavior.JinkMinRad - 1e-3f, AnimalBehavior.JinkMaxRad + 1e-3f);
                Assert.InRange(len, lo - 1e-3f, hi + 1e-3f);
                if (prev != 0 && side != prev) flips++;
                prev = side;
                byte side2 = (byte)(leg == 0 ? 0 : 0);
            }
            Assert.InRange(flips, 20, 39);                                                    // mostly alternating, some double jinks
            byte sa = 1, sb = 1;
            Assert.Equal(AnimalBehavior.ZigzagTarget(Vector3.Zero, line, 5, 3, 2.5, 14f, ref sa, out _), AnimalBehavior.ZigzagTarget(Vector3.Zero, line, 5, 3, 2.5, 14f, ref sb, out _));
            float sink = 0f; byte sd = 1;
            for (int i = 0; i < 100; i++) sink += AnimalBehavior.ZigzagTarget(Vector3.Zero, line, 5, i, i * 0.5, 14f, ref sd, out _).X;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) sink += AnimalBehavior.ZigzagTarget(Vector3.Zero, line, 5, i, i * 0.5, 14f, ref sd, out _).X;
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.NotEqual(0f, sink);
        }

        /// <summary>⭐ <c>CE-4649</c> part D (AN-21f) — the HUNTING DOG ranges AHEAD of its handler in sweeps (each point ≥ 15 m ahead of him, the side alternating), CHASES a flushed animal (its quarry, in flight) a short way — BARKING — and comes
        /// back to its sweeps; and it NEVER attacks a person: a man standing against it draws no bite, no bark, no attack mode. ✅ Red-proof: the Ranges branch removed ⇒ it grazes ⇒ red.</summary>
        [Fact]
        public void CE4649_AHuntingDog_RangesAheadOfItsHandler_ChasesAFlushedAnimalBarking_AndNeverAttacksAPerson()
        {
            var w = new World();
            var handler = w.Man(new Vector3(100, 0, 0));
            var dog = w.Animal(HuntingDogType, new Vector3(100, 0, 0), AnimalSpecies.HuntingDog);
            w.Repo.GetComponentRW<AnimalTasks>(dog).Handler = handler;
            w.Start(dog);
            var sides = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 6; i++)
            {
                w.Run(0.3);
                var (to, _, moving) = w.Move(dog);
                if (!moving) continue;
                Assert.True(to.X > 100f + 10f, $"ahead of him: {to}");
                sides.Add(to.Y);
                w.Arrive(dog);
            }
            Assert.True(sides.Count >= 3 && sides.Exists(y => y > 5f) && sides.Exists(y => y < -5f), "sweeps to both sides");
            // a rabbit flushed ahead: the dog chases it a short way, barking
            var rabbit = w.Animal(RabbitType, new Vector3(140, 10, 0), AnimalSpecies.Rabbit);
            w.Repo.GetComponentRW<AnimalState>(rabbit).Mode = AnimalMode.Flee;
            w.Repo.GetComponentRW<AnimalTasks>(dog).Quarry = rabbit;
            w.Run(0.4);
            var chase = w.Move(dog);
            Assert.True(chase.Moving && chase.Speed == HuntingDog.FleeSpeed, "it gives chase at its flee speed");
            Assert.Equal(Fdp.Toolkit.Civilians.UtteranceKind.Bark, Fdp.Toolkit.Civilians.Utterances.Of(w.Repo, dog, w.Repo.SimulationTime, out _));
            w.Repo.GetComponentRW<AnimalTasks>(dog).Quarry = Entity.Null;
            w.Run(AnimalBehavior.ChaseSeconds + 1.0);
            // back to the sweeps: ahead of the handler again
            var (to2, _, m2) = w.Move(dog);
            Assert.True(!m2 || to2.X > 100f + 10f, $"back to ranging: {to2}");
            // never a person: a man at its nose draws no attack
            var man = w.Man(new Vector3(100, 1, 0));
            w.Run(3);
            Assert.NotEqual(AnimalMode.Guard, w.Mode(dog));
            Assert.Equal(100f, w.Repo.GetComponentRO<Health>(man).Current);
        }

        /// <summary>⭐ <c>CE-4623</c> AB-9 — an ENCLOSED pen holds its animals: a frightened one runs only to the pen's edge, where one in an open
        /// area runs its full flee distance. 🔴 Red-proof: the Enclosed clamp dropped from Flee ⇒ the penned one's run leaves the pen ⇒ red.</summary>
        [Fact]
        public void CE4623_AnEnclosedPen_HoldsAFrightenedAnimal_AnOpenAreaDoesNot()
        {
            var w = new World();
            var penned = w.Animal(CowType, new Vector3(-200, 50, 0), AnimalSpecies.Cattle);
            w.Repo.AddComponent(penned, new Home { Centre = new Vector3(-200, 50, 0), Radius = 10f, Enclosed = 1 });
            var open = w.Animal(CowType, new Vector3(-200, -50, 0), AnimalSpecies.Cattle);
            w.Repo.AddComponent(open, new Home { Centre = new Vector3(-200, -50, 0), Radius = 10f, Enclosed = 0 });
            w.Start(penned); w.Start(open);
            w.Run(0.3);
            foreach (var (e, y) in new[] { (penned, 50f), (open, -50f) })
                w.Repo.GetComponentRW<Fear>(e) = new Fear { Value = 0.95f, FrightFrom = new Vector3(-260, y, 0), HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            w.Run(0.3);
            var (pen, _, _) = w.Move(penned);
            var (field, _, _) = w.Move(open);
            Assert.True(Vector2.Distance(new(pen.X, pen.Y), new(-200, 50)) <= 10.01f, $"the pen holds it: {pen}");
            Assert.True(field.X > -100f, $"in the open it runs its distance: {field}");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1958</c> (pigs stuck in their pen's wall) — a HERD member's grazing spot (the herd's spot plus its spread — up to 11 m for a loose herd)
        /// stays inside its HOME: two penned cows with a 6.5 m home, the herd's spot near its edge, walk to spot after spot and every one is inside.
        /// 🔴 Red-proof: no KeepHome on the member's spot ⇒ spots up to ~20 m out, past the pen's walls.
        /// </summary>
        [Fact]
        public void CE1958_AHerdMembersGrazingSpot_StaysInItsHome()
        {
            var w = new World();
            var home = new Home { Centre = new Vector3(0, 0, 0), Radius = 6.5f, Enclosed = 1 };
            w.Repo.GetComponentRW<Home>(w.Herd) = home;
            foreach (var cow in new[] { w.A, w.B })
            {
                w.Repo.AddComponent(cow, home);
                w.Repo.GetComponentRW<SimTransform>(cow).Position = new Vector3(cow == w.A ? -2f : 2f, 0f, 0f);
                w.Start(cow);
            }
            float farthest = 0f;
            int spots = 0;
            for (int leg = 0; leg < 150; leg++)   // 150 s: grazing pauses 5–20 s a spot
            {
                w.Run(1.0);
                foreach (var cow in new[] { w.A, w.B })
                {
                    var (to, _, moving) = w.Move(cow);
                    if (!moving) continue;
                    spots++;
                    farthest = MathF.Max(farthest, new Vector2(to.X, to.Y).Length());
                    w.Arrive(cow);
                }
            }
            Assert.True(spots >= 4, $"they grazed ({spots} spots)");
            Assert.True(farthest <= home.Radius + 0.01f, $"a grazing spot {farthest:F1} m from the pen's centre (its home {home.Radius} m)");
        }

        [Fact]
        public void CE4623_AtNight_TheHerdRests_AndAFrightStillWakesIt()
        {
            var w = new World();
            // midnight at 0°/0°
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Weather.ScenarioClock>()) w.Repo.RegisterComponent<Fdp.Toolkit.Weather.ScenarioClock>();
            var env = w.Repo.CreateEntity();   // the World Environment's clock
            w.Repo.AddComponent(env, new Fdp.Toolkit.Weather.ScenarioClock { StartUtcTicks = new System.DateTime(2025, 6, 21, 0, 0, 0, System.DateTimeKind.Utc).Ticks });
            w.Start(w.A); w.Start(w.B);
            w.Run(HerdSystem.NightLookSeconds + 1.0);
            Assert.True(w.Herds.Night);
            Assert.Equal(AnimalMode.Rest, w.Mode(w.A));
            Assert.False(w.Move(w.A).Moving);
            w.Repo.GetComponentRW<Fear>(w.A) = new Fear { Value = 0.95f, FrightFrom = new Vector3(-50, 0, 0), HasFrightFrom = 1, LastFrightAt = (Fdp.Core.SimInstant)w.Repo.SimulationTime };
            w.Run(0.5);
            Assert.Equal(AnimalMode.Flee, w.Mode(w.A));
        }
    }
}
