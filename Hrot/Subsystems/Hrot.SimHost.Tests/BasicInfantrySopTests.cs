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
    /// ⭐⭐ <c>CE-2080</c> — the SHIPPED SOP (<c>Assets/BTrees/Sop/BasicInfantrySop.btree.json</c>, also the BTree recipe) run
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

            public void StartSop()
                => Repo.Bus.PublishManaged(new AssignSopEvent { Entity = Unit, BehaviorName = Sop, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
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
    }
}
