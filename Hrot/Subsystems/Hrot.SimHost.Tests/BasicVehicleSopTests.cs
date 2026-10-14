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
    /// ⭐⭐ <c>CE-2165</c> V8 — the shipped VEHICLE SOP (<c>Assets/BTrees/Library/Sop/BasicVehicleSop.btree.json</c>, DESIGN_Vehicle_Exposure_Cycle
    /// §8) run from the PRODUCTION registry through the real ingress and brain, the harness of <see cref="BasicInfantrySopTests"/>: a drone
    /// first (ShelterFromAir, UnderFire), then a hit (VehicleFightFromCover, Hit), then contact (VehicleFightFromCover, Contact), else Idle.
    /// </summary>
    public sealed class BasicVehicleSopTests
    {
        private const string Sop = "BasicVehicleSop";

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
                Repo.AddComponent(Unit, new SimTransform { Position = System.Numerics.Vector3.Zero, Rotation = System.Numerics.Quaternion.Identity });
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

            public void StartSop()
                => Repo.Bus.PublishManaged(new AssignSopEvent { Entity = Unit, BehaviorName = Sop, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
        }

        [Fact]
        public void V8_TheShippedVehicleSop_IsRegistered_AndIdles()
        {
            var w = new World();
            Assert.True(w.Registry.TryGetId(Sop, out _), "the shipped vehicle SOP must be compiled and registered so a TKB template can name it");
            w.StartSop();
            w.Run(0.3);
            Assert.Equal(0, w.Repo.GetComponent<SopState>(w.Unit).SopFaulted);
            Assert.Equal("Idle", w.TaskName);
        }

        /// <summary>⭐⭐ The rows in order: an air threat shelters (UnderFire); a hit or a first contact fights from cover (Hit / Contact); and
        /// with a drone AND a hit both fresh the drone row wins — row order picks the reaction.</summary>
        [Fact]
        public void V8_ADrone_Shelters_AHitOrContact_FightsFromCover_AndTheDroneRowComesFirst()
        {
            ReactionUrgency? Urgency(World w) => w.Task.Origin == BehaviorOrigin.Reaction ? w.Task.Urgency : null;

            var air = new World();
            air.Order("Idle"); air.StartSop(); air.Run(0.3);
            air.Remember(true);
            air.Sense(SensorChange.AirThreat);
            air.Run(0.1);
            Assert.Equal("ShelterFromAir", air.TaskName);
            Assert.Equal(ReactionUrgency.UnderFire, Urgency(air));
            Assert.Equal("Idle", BehaviorIngressSystem.PausedTaskOf(air.Repo, air.Unit)!.BehaviorName);

            var hit = new World();
            hit.Order("Idle"); hit.StartSop(); hit.Run(0.3);
            hit.Remember(true);
            hit.Sense(SensorChange.Hit);
            hit.Run(0.1);
            Assert.Equal("VehicleFightFromCover", hit.TaskName);
            Assert.Equal(ReactionUrgency.Hit, Urgency(hit));

            var contact = new World();
            contact.Order("Idle"); contact.StartSop(); contact.Run(0.3);
            contact.Remember(true);
            contact.Sense(SensorChange.FirstThreat);
            contact.Run(0.1);
            Assert.Equal("VehicleFightFromCover", contact.TaskName);
            Assert.Equal(ReactionUrgency.Contact, Urgency(contact));

            var both = new World();
            both.Order("Idle"); both.StartSop(); both.Run(0.3);
            both.Remember(true);
            both.Sense(SensorChange.Hit);
            both.Sense(SensorChange.AirThreat);
            both.Run(0.1);
            Assert.Equal("ShelterFromAir", both.TaskName);
        }
    }
}
