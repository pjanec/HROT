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
        private const long CowType = 700;
        private static readonly AnimalTemperamentDto Cattle = new()
        {
            Species = (byte)AnimalSpecies.Cattle, FlightZoneM = 25f, PanicZoneM = 10f, FleeSpeed = 9f, FleeDistanceM = 200f,
            RecoverySeconds = 8f, HerdCohesion = 0.4f, WalkSpeed = 1f,
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
                Herds.Execute(Repo, 0.05f);   // serial, before the brains (the CGF order)
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
