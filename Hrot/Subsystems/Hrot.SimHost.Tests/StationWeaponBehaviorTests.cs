using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2238</c> part 4 (AW-11, AW-16; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5h.7 CO-2) — the SHIPPED <c>StationWeapon</c>
    /// from the production registry, through the real ingress and brain tick: a man with a pistol HIDDEN draws it to his HANDS, holds still
    /// <c>Seconds</c> and finishes with Success; it slings to his BACK and conceals to HIDDEN the same way; a man with nothing to put there
    /// fails.
    /// </summary>
    public sealed class StationWeaponBehaviorTests
    {
        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Man;
            public NodeStatus? Finished;
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
                    () => { if (!Repo.IsComponentTypeRegistered<PassengerBuffer>()) Repo.RegisterComponent<PassengerBuffer>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<CarrierSeats>()) Repo.RegisterComponent<CarrierSeats>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<CarrierStations>()) Repo.RegisterComponent<CarrierStations>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<PersonCarrier>()) Repo.RegisterComponent<PersonCarrier>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<OpenPassengerCabin>()) Repo.RegisterComponent<OpenPassengerCabin>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<IsEmbarkedTag>()) Repo.RegisterComponent<IsEmbarkedTag>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<ActorCapabilityState>()) Repo.RegisterComponent<ActorCapabilityState>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponSystemParams>()) Repo.RegisterComponent<WeaponSystemParams>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<WeaponState>()) Repo.RegisterComponent<WeaponState>(); },
                    () => { if (!Repo.IsComponentTypeRegistered<EntityInfo>()) Repo.RegisterComponent<EntityInfo>(); },
                    () => { if (!Repo.Bus.IsRegistered<RestationEvent>()) Repo.Bus.Register<RestationEvent>(); },
                }) reg();
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);

                Man = Repo.CreateEntity();
                Repo.AddComponent(Man, new SimTransform { Rotation = Quaternion.Identity });
                Repo.AddComponent(Man, new EntityInfo { ForceId = ForceId.Neutral });
                Repo.AddComponent(Man, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot | ActorCapabilities.CanInteract });
                Repo.AddComponent(Man, new BehaviorState());
                Repo.SetAuthority<BehaviorState>(Man, true);
                Repo.AddComponent(Man, new LocomotionChannel());
                var t = new TkbTemplate("Man", 4350L);
                t.AddDescriptor(CarrierDefDto.PersonCarrier());
                new CarrierTkbTranslator().Inject(Repo, Man, t);
                Tick();
            }

            /// <summary>A pick-up pistol (one man, fires while carried), boarded on him — to his hands, then moved to <paramref name="seat"/>.</summary>
            public Entity Pistol(int seat)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new WeaponState { Ammo = 15 });
                var t = new TkbTemplate("Pistol", 4351L);
                t.AddDescriptor(new WeaponSystemDto { MountKinds = MountKinds.Ground | MountKinds.Carried, Portable = WeaponPortability.OneMan, FireWhileCarried = true });
                new WeaponSystemTkbTranslator().Inject(Repo, e, t);
                Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(Repo, e, Man));
                Assert.True(CarryStations.Move(Repo, Man, e, seat));
                return e;
            }

            public int SeatOf(Entity thing) => CarryStations.SeatOf(Repo, thing, out _);

            public void Tick()
            {
                _time += 0.1;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.1f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                foreach (var f in Repo.Bus.Read<BehaviorFinishedEvent>()) if (f.Entity == Man) Finished = f.Result;
                _ingress.Execute(Repo, 0.1f);
                _brain.Execute(Repo, 0.1f);
                Repo.FlushCommandBuffers();
            }

            public void Start(string json)
            {
                Finished = null;
                Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Man, BehaviorName = BehaviorNames.StationWeapon, JsonParams = json, Origin = BehaviorOrigin.Superior });
            }
        }

        /// <summary>⭐ DRAW: a pistol HIDDEN goes to his HANDS on the first tick, and the behaviour holds until <c>Seconds</c> have passed, then
        /// finishes with Success — the next mission task runs. ✅ Red-proofs: the move removed from <c>Place</c> ⇒ the pistol stays hidden (and
        /// it fails) ⇒ red; the <c>Seconds</c> hold removed ⇒ it finishes at once ⇒ red.</summary>
        [Fact]
        public void CE2238_StationWeapon_DrawsTheHiddenPistolToHisHands_HoldsSeconds_ThenSucceeds()
        {
            var w = new World();
            var pistol = w.Pistol(CarryStations.Hidden(w.Repo, w.Man));
            Assert.True(CarryStations.IsConcealed(w.Repo, pistol));
            w.Start("{\"Station\":\"Hands\",\"Seconds\":1}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(CarryStations.Hands(w.Repo, w.Man), w.SeatOf(pistol));
            Assert.True(WeaponSystems.CanFire(w.Repo, pistol), "drawn: it fires");
            Assert.Null(w.Finished);
            for (int i = 0; i < 12; i++) w.Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);
        }

        /// <summary>⭐ SLING and CONCEAL: from his hands to his BACK (seen, cannot fire), then HIDDEN (concealed). ✅ Red-proof: the station
        /// mapping collapsed to Hands ⇒ the pistol never leaves his hands ⇒ red.</summary>
        [Fact]
        public void CE2238_StationWeapon_SlingsToHisBack_AndConcealsItHidden()
        {
            var w = new World();
            var pistol = w.Pistol(CarryStations.Hands(w.Repo, w.Man));
            w.Start("{\"Station\":\"Back\"}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(CarryStations.Back(w.Repo, w.Man), w.SeatOf(pistol));
            Assert.True(CarryStations.IsStowed(w.Repo, pistol));
            Assert.False(CarryStations.IsConcealed(w.Repo, pistol));
            Assert.Equal(NodeStatus.Success, w.Finished);

            w.Start("{\"Station\":\"Hidden\"}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(CarryStations.Hidden(w.Repo, w.Man), w.SeatOf(pistol));
            Assert.True(CarryStations.IsConcealed(w.Repo, pistol));
            Assert.Equal(NodeStatus.Success, w.Finished);
        }

        /// <summary>⭐ A man who carries no weapon has nothing to draw: the behaviour FAILS (the mission sees it), it does not hang.
        /// ✅ Red-proof: <c>Place</c> returning true with nothing moved ⇒ Running forever ⇒ red.</summary>
        [Fact]
        public void CE2238_StationWeapon_NothingToDraw_Fails()
        {
            var w = new World();
            w.Start("{\"Station\":\"Hands\"}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(NodeStatus.Failure, w.Finished);
        }
    }
}
