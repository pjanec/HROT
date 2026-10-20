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
        internal sealed class World
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

            /// <summary>⭐ <c>CE-2238</c> part 5 — a worn CHARGE (B-11b: a weapon system with <c>Charge</c>), boarded on him, moved to
            /// <paramref name="seat"/>.</summary>
            public Entity Charge(int seat)
            {
                if (!Repo.Bus.IsRegistered<DetonateChargeEvent>()) Repo.Bus.Register<DetonateChargeEvent>();
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new WeaponState { Ammo = 1 });
                var t = new TkbTemplate("Charge", 4352L);
                t.AddDescriptor(new WeaponSystemDto { MountKinds = MountKinds.Ground | MountKinds.Carried | MountKinds.Cargo, Portable = WeaponPortability.OneMan, Charge = true });
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

            public void Start(string json) => Start(BehaviorNames.StationWeapon, json);

            public void Start(string behaviorName, string json)
            {
                Finished = null;
                Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Man, BehaviorName = behaviorName, JsonParams = json, Origin = BehaviorOrigin.Superior });
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

    /// <summary>
    /// ⭐⭐ <c>CE-2238</c> part 5 (AW-12, B5; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5l BM-1/BM-2) — the SHIPPED <c>DetonateCharge</c>
    /// through the real ingress and brain tick: a man with a charge HIDDEN stands still <c>Seconds</c>, then asks it to go off (one
    /// <see cref="DetonateChargeEvent"/>, by him) and finishes with Success; the charge never leaves its hidden station; a man with no charge
    /// fails.
    /// </summary>
    public sealed class DetonateChargeBehaviorTests
    {
        private static int Asks(StationWeaponBehaviorTests.World w, Entity charge, Entity by)
        {
            int n = 0;
            foreach (var e in w.Repo.Bus.Read<DetonateChargeEvent>()) if (e.Charge == charge && e.By == by) n++;
            return n;
        }

        /// <summary>⭐ HIDDEN vest, 1 s: no ask before the second has passed, then ONE ask by him, Success; the vest stays hidden (never drawn).
        /// ✅ Red-proofs: the <c>Seconds</c> hold removed ⇒ the ask comes on the first tick ⇒ red; <c>by</c> = null ⇒ the ask names no one ⇒ red.</summary>
        [Fact]
        public void CE2238_DetonateCharge_StandsStillSeconds_ThenAsksHisHiddenChargeOnce_AndSucceeds()
        {
            var w = new StationWeaponBehaviorTests.World();
            var vest = w.Charge(CarryStations.Hidden(w.Repo, w.Man));
            w.Start(BehaviorNames.DetonateCharge, "{\"Seconds\":1}");
            int asked = 0;
            for (int i = 0; i < 5; i++) { w.Tick(); asked += Asks(w, vest, w.Man); }
            Assert.Equal(0, asked);
            Assert.Null(w.Finished);
            for (int i = 0; i < 10; i++) { w.Tick(); asked += Asks(w, vest, w.Man); }
            Assert.Equal(1, asked);
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.True(CarryStations.IsConcealed(w.Repo, vest), "never drawn: it stays hidden until it goes off");
        }

        /// <summary>⭐ Nothing worn: the behaviour FAILS, it does not hang. ✅ Red-proof: <c>ChargeOf</c> null ⇒ Running ⇒ red.</summary>
        [Fact]
        public void CE2238_DetonateCharge_NoCharge_Fails()
        {
            var w = new StationWeaponBehaviorTests.World();
            w.Start(BehaviorNames.DetonateCharge, "{}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.Equal(NodeStatus.Failure, w.Finished);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2306</c> (DA-93…DA-95; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8e) — the SHIPPED <c>TriggerIed</c>
    /// through the real ingress and brain tick: the trigger man takes his phone in hand and sets off the placed charge when a hostile VEHICLE
    /// he remembers is inside the kill zone — not a man on foot, not a neutral car, not a vehicle outside it; with <c>AnyTarget</c> the man on
    /// foot sets it off too; no phone or no charge ⇒ Failure.
    /// </summary>
    public sealed class TriggerIedBehaviorTests
    {
        private const long TriggerManType = 4360L;

        private sealed class Ambush
        {
            public readonly StationWeaponBehaviorTests.World W = new();
            public readonly Entity Ied;

            public Ambush(bool phone = true)
            {
                var repo = W.Repo;
                foreach (var reg in new System.Action[]
                {
                    () => { if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(); },
                    () => { if (!repo.IsComponentTypeRegistered<WeaponPosture>()) repo.RegisterComponent<WeaponPosture>(); },
                    () => { if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()) repo.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(); },
                    () => { if (!repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>()) repo.RegisterComponent<global::CarKinem.Core.VehicleState>(); },
                    () => { if (!repo.Bus.IsRegistered<DetonateChargeEvent>()) repo.Bus.Register<DetonateChargeEvent>(); },
                    () => { if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(); },
                }) reg();
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                var man = new TkbTemplate("Trigger man", TriggerManType);
                man.AddDescriptor(CarrierDefDto.PersonCarrier());
                if (phone) man.AddDescriptor(new PocketItemsDto { Phone = true });
                db.Register(man);
                repo.SetSingletonManaged<ITkbDatabase>(db);
                repo.AddComponent(W.Man, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TriggerManType });
                if (!repo.HasComponent<WeaponPosture>(W.Man)) repo.AddComponent(W.Man, new WeaponPosture());
                repo.GetComponentRW<EntityInfo>(W.Man).ForceId = ForceId.Hostile;
                repo.AddComponent(W.Man, new Fdp.Toolkit.Perception.Components.TargetMemory());

                Ied = repo.CreateEntity();   // a placed charge, on the road 40 m north of him
                repo.AddComponent(Ied, new SimTransform { Position = new Vector3(0f, 40f, 0f), Rotation = Quaternion.Identity });
                repo.AddComponent(Ied, new WeaponState { Ammo = 1 });
                var t = new TkbTemplate("IED", 4361L);
                t.AddDescriptor(new WeaponSystemDto { MountKinds = MountKinds.Ground | MountKinds.Carried | MountKinds.Cargo, Portable = WeaponPortability.OneMan, Charge = true });
                new WeaponSystemTkbTranslator().Inject(repo, Ied, t);
                repo.AddComponent(Ied, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = IedNetId });
            }

            public const long IedNetId = 5501;

            /// <summary>A thing he SEES at <paramref name="at"/>: a vehicle or a man on foot, of <paramref name="force"/>.</summary>
            public Entity Sees(Vector3 at, bool vehicle, ForceId force = ForceId.Friend)
            {
                var repo = W.Repo;
                var e = repo.CreateEntity();
                repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                repo.AddComponent(e, new EntityInfo { ForceId = force });
                if (vehicle) repo.AddComponent(e, new global::CarKinem.Core.VehicleState());
                ref var mem = ref repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(W.Man);
                Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)e.PackedValue, at.X, at.Y, 1f, repo.SimulationTick);
                return e;
            }

            public int Asks()
            {
                int n = 0;
                foreach (var e in W.Repo.Bus.Read<DetonateChargeEvent>()) if (e.Charge == Ied && e.By == W.Man) n++;
                return n;
            }

            public int Run(int ticks)
            {
                int asked = 0;
                for (int i = 0; i < ticks; i++) { W.Tick(); asked += Asks(); }
                return asked;
            }

            public void Start(bool anyTarget = false)
                => W.Start(BehaviorNames.TriggerIed, "{\"Charge\":" + IedNetId + ",\"KillZoneMetres\":10" + (anyTarget ? ",\"AnyTarget\":true" : "") + "}");
        }

        /// <summary>⭐⭐ DA-93/DA-94 — a man on foot in the zone, a NEUTRAL car in the zone and a hostile vehicle 30 m off it change nothing (he
        /// holds his phone, still Running); the hostile vehicle inside the zone ⇒ ONE ask, by him, Success. ✅ Red-proofs: the vehicle test
        /// dropped ⇒ the man on foot sets it off · the hostility test dropped ⇒ the neutral car · the zone ignored ⇒ the far vehicle.</summary>
        [Fact]
        public void CE2306_TriggerIed_SetsItOffWhenAHostileVehicleHeSeesIsInTheKillZone_NothingElse()
        {
            var a = new Ambush();
            a.Start();
            a.Sees(new Vector3(2f, 42f, 0f), vehicle: false);                       // a soldier on foot, in the zone
            a.Sees(new Vector3(-3f, 38f, 0f), vehicle: true, ForceId.Neutral);      // a civilian car, in the zone
            var far = a.Sees(new Vector3(0f, 75f, 0f), vehicle: true);              // the convoy's lead, 35 m off
            Assert.Equal(0, a.Run(5));
            Assert.Null(a.W.Finished);
            Assert.Equal(HeldItemKind.Phone, HeldItems.Of(a.W.Repo, a.W.Man));
            ref var mem = ref a.W.Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(a.W.Man);
            Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)far.PackedValue, 1f, 44f, 1f, a.W.Repo.SimulationTick);   // it drives in
            Assert.Equal(1, a.Run(4));
            Assert.Equal(NodeStatus.Success, a.W.Finished);
        }

        /// <summary>⭐ DA-94 — with <c>AnyTarget</c> the man on foot sets it off. ✅ Red-proof: the flag not read ⇒ nothing.</summary>
        [Fact]
        public void CE2306_TriggerIed_AnyTarget_TakesAManOnFootToo()
        {
            var a = new Ambush();
            a.Start(anyTarget: true);
            a.Sees(new Vector3(2f, 42f, 0f), vehicle: false);
            Assert.Equal(1, a.Run(4));
            Assert.Equal(NodeStatus.Success, a.W.Finished);
        }

        /// <summary>⭐ DA-93 — no phone ⇒ Failure (he cannot set it off); the charge gone ⇒ Failure. ✅ Red-proof: the phone test dropped ⇒
        /// he runs on (Running) ⇒ red.</summary>
        [Fact]
        public void CE2306_TriggerIed_NoPhone_OrNoCharge_Fails()
        {
            var a = new Ambush(phone: false);
            a.Start();
            a.Run(3);
            Assert.Equal(NodeStatus.Failure, a.W.Finished);

            var b = new Ambush();
            b.W.Repo.DestroyEntity(b.Ied);
            b.W.Start(BehaviorNames.TriggerIed, "{\"Charge\":987654}");
            b.Run(3);
            Assert.Equal(NodeStatus.Failure, b.W.Finished);
        }
    }
}
