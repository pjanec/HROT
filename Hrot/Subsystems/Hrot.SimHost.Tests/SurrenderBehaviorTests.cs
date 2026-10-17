using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2241</c> ① (AW-5, AW-21; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5h.5) — the SHIPPED <c>Surrender</c> from the
    /// production registry, through the real ingress and brain tick, on a rifleman whose rifle comes from his TKB suite: he lays it down
    /// and raises his hands, and so is OUT OF THE FIGHT; after <c>Seconds</c> he takes it up, lowers his hands and the behaviour finishes.
    /// </summary>
    public sealed class SurrenderBehaviorTests
    {
        private const long RiflemanType = 9601, BulletAmmo = 9690;

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Unit;
            public bool Finished;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private double _time;
            private long _frame;

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                foreach (var reg in new System.Action[]
                {
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponState>()) Repo.RegisterComponent<WeaponState>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponMountInfo>()) Repo.RegisterComponent<WeaponMountInfo>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<AmmoLoadout>()) Repo.RegisterComponent<AmmoLoadout>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponChannel>()) Repo.RegisterComponent<WeaponChannel>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<LaidDownWeapons>()) Repo.RegisterComponent<LaidDownWeapons>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<Gesture>()) Repo.RegisterComponent<Gesture>(); },
                }) reg();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                var rifleman = new Fdp.Interfaces.TkbTemplate("Rifleman", RiflemanType);
                rifleman.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
                rifleman.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo, InitialAmmunition = 30 } } });
                db.Register(rifleman);
                db.Register(new Fdp.Interfaces.TkbTemplate("Bullet", BulletAmmo));
                PersonHands.EnsureOnPersons(db);
                Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

                Unit = Repo.CreateEntity();
                Repo.AddComponent(Unit, new SimTransform { Position = new Vector3(100, 200, 0), Rotation = Quaternion.Identity });
                Repo.AddComponent(Unit, new BehaviorState());
                Repo.SetAuthority<BehaviorState>(Unit, true);   // his brain is here ⇒ IsLaidDown reads his own mask
                Repo.AddComponent(Unit, new LocomotionChannel());
                Repo.AddComponent(Unit, new WeaponChannel());
                Repo.AddComponent(Unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = RiflemanType });
                db.TryGetByType(RiflemanType, out var t);
                new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(Repo, Unit, t!);
                Tick();
            }

            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                foreach (var f in Repo.Bus.Read<BehaviorFinishedEvent>())
                    if (f.Entity.Index == Unit.Index) Finished = true;
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                Repo.FlushCommandBuffers();
            }

            public void Start(string json)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = BehaviorNames.Surrender, JsonParams = json, Origin = BehaviorOrigin.Superior });

            public bool Surrendered => WeaponLaying.AllLaidDown(Repo, Unit) && Gestures.Of(Repo, Unit) == GestureKind.HandsUp;
        }

        /// <summary>⭐ Surrender {Seconds: 0}: every weapon laid down, his hands up — ARMED BY NOTHING and out of the fight (AW-21) — and he
        /// stays so (0 = for good, the behaviour never finishes). ✅ Red-proof: the LayDownAll line removed from <c>Surrendered</c> ⇒ red.</summary>
        [Fact]
        public void CE2241_Surrender_LaysEveryWeaponDown_RaisesHisHands_AndIsOutOfTheFight()
        {
            var w = new World();
            Assert.True(Armament.ArmedByStatus(w.Repo, w.Unit));
            Assert.False(ThreatDanger.IsOutOfTheFight(w.Repo, w.Unit));
            w.Start("{\"Seconds\":0}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.True(w.Surrendered);
            Assert.False(Armament.ArmedByStatus(w.Repo, w.Unit));
            Assert.True(ThreatDanger.IsOutOfTheFight(w.Repo, w.Unit));
            for (int i = 0; i < 400; i++) w.Tick();   // 6.4 s
            Assert.True(w.Surrendered);
            Assert.False(w.Finished);
        }

        /// <summary>⭐ The FEINT's half: Surrender {Seconds: 1} holds for its second, then he takes his rifle up, lowers his hands and the
        /// behaviour FINISHES (so the next mission task runs) — armed and in the fight again. ✅ Red-proof: <c>Deactivate_Surrendered</c>'s
        /// <c>LayDownAll(down: false)</c> removed ⇒ red; <c>HeldLongEnough</c> always false ⇒ red.</summary>
        [Fact]
        public void CE2241_Surrender_AfterItsSeconds_TakesHisWeaponUp_LowersHisHands_AndFinishes()
        {
            var w = new World();
            w.Start("{\"Seconds\":1}");
            for (int i = 0; i < 30; i++) w.Tick();   // 0.5 s
            Assert.True(w.Surrendered);
            Assert.False(w.Finished);
            for (int i = 0; i < 60 && !w.Finished; i++) w.Tick();
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.True(w.Finished, "the surrender finishes after its Seconds");
            Assert.False(WeaponLaying.AllLaidDown(w.Repo, w.Unit));
            Assert.Equal(GestureKind.None, Gestures.Of(w.Repo, w.Unit));
            Assert.True(Armament.ArmedByStatus(w.Repo, w.Unit));
            Assert.False(ThreatDanger.IsOutOfTheFight(w.Repo, w.Unit));
        }
    }
}
