using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
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
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2239</c> part 2 (CV-11, FL-5; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5j) — the SHIPPED <c>HoldTheLine</c> from the
    /// production registry, through the real ingress and brain tick, on a policeman: he fires at the nearest Hostile he remembers within
    /// reach (never a Neutral, a man already down, or one beyond reach), strikes one in arm's reach with the baton instead, and walks back
    /// to his post when drawn from it.
    /// </summary>
    public sealed class HoldTheLineBehaviorTests
    {
        private const long PolicemanType = 9701, BatonRound = 9790;

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Police;
            public readonly List<DamageAssessedEvent> Blows = new();
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
                    () => { if (!Repo.IsComponentTypeRegistered<EntityInfo>()) Repo.RegisterComponent<EntityInfo>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<TargetMemory>()) Repo.RegisterComponent<TargetMemory>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<EffectState>()) Repo.RegisterComponent<EffectState>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<Gesture>()) Repo.RegisterComponent<Gesture>(); },
                    () => { if (!Repo.Bus.IsRegistered<DamageAssessedEvent>()) Repo.Bus.Register<DamageAssessedEvent>(); },
                }) reg();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                var police = new Fdp.Interfaces.TkbTemplate("Policeman", PolicemanType);
                police.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
                police.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BatonRound, InitialAmmunition = 20, Range = 50f } } });
                db.Register(police);
                db.Register(new Fdp.Interfaces.TkbTemplate("Baton round", BatonRound));
                PersonHands.EnsureOnPersons(db);
                Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

                Police = Person(Vector3.Zero, ForceId.Friend);
                Repo.AddComponent(Police, new BehaviorState());
                Repo.SetAuthority<BehaviorState>(Police, true);
                Repo.AddComponent(Police, new LocomotionChannel());
                Repo.AddComponent(Police, new WeaponChannel());
                Repo.AddComponent(Police, new TargetMemory());
                Repo.AddComponent(Police, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = PolicemanType });
                db.TryGetByType(PolicemanType, out var t);
                new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(Repo, Police, t!);
                Tick();
            }

            public Entity Person(Vector3 at, ForceId side)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new EntityInfo { ForceId = side });
                return e;
            }

            /// <summary>The policeman saw <paramref name="who"/> where he stands now.</summary>
            public void Sees(Entity who)
            {
                var at = Repo.GetComponentRO<SimTransform>(who).Position;
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Police);
                TargetMemory.AddOrUpdateTarget(ref mem, (long)who.PackedValue, at.X, at.Y, 1f, Repo.SimulationTick, SensorModality.Visual);
            }

            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                foreach (var blow in Repo.Bus.Read<DamageAssessedEvent>()) Blows.Add(blow);
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                Repo.FlushCommandBuffers();
            }

            public void Start(string json = "{}")
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Police, BehaviorName = BehaviorNames.HoldTheLine, JsonParams = json, Origin = BehaviorOrigin.Superior });

            /// <summary>Whom his weapon channel aims at (<see cref="Entity.Null"/> when it is not firing).</summary>
            public unsafe Entity Aiming
            {
                get
                {
                    ref var channel = ref Repo.GetComponentRW<WeaponChannel>(Police);
                    return channel.ActiveAction == CombatConstants.ActionIdAimAndFire
                        ? Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref channel.Params[0]).Target : Entity.Null;
                }
            }
        }

        /// <summary>⭐ The nearest Hostile in reach is the one he fires at — a nearer NEUTRAL, a nearer Hostile already KNOCKED DOWN, a nearer
        /// one with his HANDS UP and a Hostile beyond reach are not. ✅ Red-proof: the Neutral, knocked-down or hands-up skip removed from <c>Nearest</c> ⇒ red; the reach
        /// bound removed ⇒ red once only the far one is left.</summary>
        [Fact]
        public void CE2239_HoldTheLine_FiresAtTheNearestHostileInReach_NeverANeutral_NorAManDown_NorRaisedHands_NorOneBeyondReach()
        {
            var w = new World();
            var neutral = w.Person(new Vector3(5, 0, 0), ForceId.Neutral);
            var down = w.Person(new Vector3(10, 0, 0), ForceId.Hostile);
            w.Repo.AddComponent(down, new EffectState { KnockedDownUntil = 1e6 });
            var handsUp = w.Person(new Vector3(15, 0, 0), ForceId.Hostile);
            Gestures.Set(w.Repo, handsUp, GestureKind.HandsUp);
            var rioter = w.Person(new Vector3(20, 0, 0), ForceId.Hostile);
            var far = w.Person(new Vector3(0, 60, 0), ForceId.Hostile);
            foreach (var e in new[] { neutral, down, handsUp, rioter, far }) w.Sees(e);
            w.Start();
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(rioter, w.Aiming);

            w.Repo.DestroyEntity(rioter);
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.True(w.Aiming.IsNull, $"the only Hostile left stands 60 m off, beyond the 40 m reach: aiming at {w.Aiming}");
        }

        /// <summary>⭐ A Hostile in arm's reach gets the BATON — a Blunt blow from the policeman — and not a round. ✅ Red-proof: the
        /// <c>Melee.InReach</c> arm removed from <c>Hold</c> ⇒ red (he fires instead, and strikes nobody).</summary>
        [Fact]
        public void CE2239_HoldTheLine_StrikesAHostileInArmsReach_WithTheBaton_AndHoldsHisFire()
        {
            var w = new World();
            var rioter = w.Person(new Vector3(1f, 0, 0), ForceId.Hostile);
            w.Sees(rioter);
            w.Start();
            for (int i = 0; i < 6; i++) w.Tick();
            Assert.Contains(w.Blows, b => b.HitEntity == rioter && b.Source == w.Police && b.Effect == HitEffect.Blunt);
            Assert.True(w.Aiming.IsNull);
        }

        /// <summary>⭐ Drawn from his post (pushed, say) farther than <c>Hold</c>, he walks back to where the line began. ✅ Red-proof: the
        /// return branch removed ⇒ red.</summary>
        [Fact]
        public unsafe void CE2239_HoldTheLine_DrawnFromHisPost_WalksBackToIt()
        {
            var w = new World();
            w.Start("{\"Hold\":2}");
            for (int i = 0; i < 3; i++) w.Tick();
            w.Repo.GetComponentRW<SimTransform>(w.Police).Position = new Vector3(8, 0, 0);
            for (int i = 0; i < 2; i++) w.Tick();
            ref var loco = ref w.Repo.GetComponentRW<LocomotionChannel>(w.Police);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, loco.ActiveAction);
            fixed (byte* p = loco.Params)
                Assert.True(Vector3.Distance(Vector3.Zero, ((MoveToParams*)p)->Destination) < 0.01f, "he walks back to his post");
        }
    }
}
