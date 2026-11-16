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
        private const long CowType = 700, DogType = 705, FlockType = 707;
        private static readonly AnimalTemperamentDto Cattle = new()
        {
            Species = (byte)AnimalSpecies.Cattle, FlightZoneM = 25f, PanicZoneM = 10f, FleeSpeed = 9f, FleeDistanceM = 200f,
            RecoverySeconds = 8f, HerdCohesion = 0.4f, WalkSpeed = 1f,
        };
        private static readonly AnimalTemperamentDto Dog = new()
        {
            Species = (byte)AnimalSpecies.Dog, Fearless = true, Territorial = true, FlightZoneM = 30f, PanicZoneM = 8f, FleeSpeed = 10f,
            FleeDistanceM = 30f, RecoverySeconds = 20f, WalkSpeed = 1.2f,
        };
        private static readonly AnimalTemperamentDto Flock = new()
        {
            Species = (byte)AnimalSpecies.BirdFlock, FlightZoneM = 40f, PanicZoneM = 15f, FleeSpeed = 12f, FleeDistanceM = 400f, RecoverySeconds = 8f,
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
                Reg<AnimalState>(); Reg<Fear>(); Reg<HerdMember>(); Reg<HerdState>(); Reg<AnimalHome>(); Reg<FlockState>(); Reg<TkbIdentity>();
                if (!Repo.IsComponentTypeRegistered<StanceIntent>()) Repo.RegisterComponent<StanceIntent>();
                if (!Repo.IsComponentTypeRegistered<InjuryThresholds>()) Repo.RegisterComponent<InjuryThresholds>();
                Repo.SetSingletonManaged<IGeographicTransform>(UtmTransform.AtOrigin(0.0, 0.0, 0.0));
                var db = new TkbDatabase();
                var cow = new TkbTemplate("Cattle", CowType); cow.AddDescriptor(Cattle); db.Register(cow);
                var dog = new TkbTemplate("Dog", DogType); dog.AddDescriptor(Dog); db.Register(dog);
                var flock = new TkbTemplate("BirdFlock", FlockType); flock.AddDescriptor(Flock); db.Register(flock);
                Repo.SetSingletonManaged<ITkbDatabase>(db);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                Herd = Repo.CreateEntity();
                Repo.AddComponent(Herd, new HerdState { Species = AnimalSpecies.Cattle });
                Repo.AddComponent(Herd, new AnimalHome { Centre = new Vector3(0, 0, 0), Radius = 60f });
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
                Repo.AddComponent(e, new AnimalState { Species = species });
                Repo.AddComponent(e, new Fear());
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
                Repo.AddComponent(e, new AnimalState { Species = AnimalSpecies.Cattle });
                Repo.AddComponent(e, new Fear());
                Repo.AddComponent(e, new HerdMember { Herd = Herd });
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
            Assert.NotEqual(Vector3.Zero, spot);                                         // the herd picked a grazing spot
            var (to, speed, moving) = w.Move(w.A);
            Assert.True(moving);
            Assert.True(Vector2.Distance(new(to.X, to.Y), new(spot.X, spot.Y)) <= AnimalBehavior.TightSpreadMetres + AnimalBehavior.LooseSpreadMetres + 0.1f,
                $"it grazes round the herd's spot {spot}: {to}");
            Assert.Equal(Cattle.WalkSpeed, speed);

            // a fright past panic on cow A only, from the west
            var from = new Vector3(-50, 0, 0);
            var fear = new Fear { Value = 0.95f, FrightFrom = from, HasFrightFrom = 1, LastFrightAt = w.Repo.SimulationTime };
            w.Repo.GetComponentRW<Fear>(w.A) = fear;
            w.Run(2.0);   // contagion carries B past panic in ~1 s (8 m from A, cattle cohesion 0.4)
            Assert.Equal(AnimalMode.Flee, w.Mode(w.A));
            Assert.Equal(AnimalMode.Flee, w.Mode(w.B));                                  // caught it from A (contagion)
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
            w.Run(Cattle.RecoverySeconds * AnimalBehavior.LookBackShare + 1.0);
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
            w.Repo.AddComponent(dog, new AnimalHome { Centre = new Vector3(100, 0, 0), Radius = 20f });
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
            w.Repo.GetComponentRW<Fear>(flock) = new Fear { Value = 0.9f, FrightFrom = new Vector3(-150, 0, 0), HasFrightFrom = 1, LastFrightAt = w.Repo.SimulationTime };
            w.Run(0.3);
            Assert.Equal(FlockMode.Flying, w.Repo.GetComponentRO<FlockState>(flock).Mode);
            var (to, speed, _) = w.Move(flock);
            Assert.True(to.X > 200f, $"it flies away from the shot: {to}");
            Assert.Equal(Flock.FleeSpeed, speed);
            w.Arrive(flock);
            w.Repo.GetComponentRW<Fear>(flock).Value = 0.3f;
            w.Run(0.3);
            Assert.Equal(FlockMode.Landed, w.Repo.GetComponentRO<FlockState>(flock).Mode);
            w.Repo.GetComponentRW<Fear>(flock).Value = 0f;
            w.Run(Flock.RecoverySeconds * AnimalBehavior.LookBackShare + 0.5);
            Assert.Equal(FlockMode.Perched, w.Repo.GetComponentRO<FlockState>(flock).Mode);
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
            w.Repo.GetComponentRW<Fear>(w.A) = new Fear { Value = 0.95f, FrightFrom = new Vector3(-50, 0, 0), HasFrightFrom = 1, LastFrightAt = w.Repo.SimulationTime };
            w.Run(0.5);
            Assert.Equal(AnimalMode.Flee, w.Mode(w.A));
        }
    }
}
