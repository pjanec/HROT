using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Civilians
{
    /// <summary>
    /// ⭐⭐ <c>CE-3256</c> (BR-4, Building Clearing CB-19, R-2032; docs/DESIGN_Entity_Interactions.md §7.6) — CUSTODY: a soldier searches,
    /// detains, restrains and releases a man; the man's owner applies it. A search takes 3 s in reach of a complying man and puts every hidden
    /// weapon down at his feet; a detained or restrained man is out of the fight; a restrained man fires nothing. New feature, no suite before.
    /// </summary>
    [Collection(Fdp.Toolkit.Tests.Combat.ArmamentCounterCollection.Name)]
    public sealed class CustodyTests : IDisposable
    {
        private const long ManType = 9811, PistolType = 9812, PistolAmmo = 9890, CarType = 9813;

        private readonly EntityRepository _world = new();
        private readonly TkbDatabase _db = new();
        private readonly CustodySystem _system = new();
        private double _t;

        public CustodyTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<AmmoLoadout>();
            _world.RegisterComponent<WeaponSystemParams>(); _world.RegisterComponent<Mountable>();
            _world.RegisterComponent<WeaponSystemState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<CarrierSeats>();
            _world.RegisterComponent<CarrierStations>();
            _world.RegisterComponent<PersonCarrier>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<WeaponPosture>();
            _world.RegisterComponent<Fdp.Toolkit.Creatures.Demeanour>();
            _world.RegisterComponent<Custody>();
            _world.RegisterEvent<CustodyEvent>();
            _world.SetSingleton(new GlobalTime());

            var man = new TkbTemplate("Civilian", ManType);
            man.AddDescriptor(CarrierDefDto.PersonCarrier());
            _db.Register(man);
            var pistol = new TkbTemplate("Pistol", PistolType);
            pistol.AddDescriptor(new WeaponSystemDto { Portable = WeaponPortability.OneMan, MountKinds = MountKinds.Ground | MountKinds.Carried | MountKinds.Cargo });
            pistol.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = 12, AmmoGuid = (ulong)PistolAmmo, Category = WeaponCategory.SmallArms } } });
            _db.Register(pistol);
            _db.Register(new TkbTemplate("Pistol round", PistolAmmo));
            var car = new TkbTemplate("Car", CarType);   // ⭐ CE-4622 K-6 — a driver's seat and a concealed boot
            car.AddDescriptor(new CarrierDefDto { Seats = new() { new SeatDto(), new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Cargo },
                new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Cargo, Stowed = true, Concealed = true, Exposed = false } } });
            _db.Register(car);
            _world.RegisterComponent<global::CarKinem.Core.VehicleState>();
            _world.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _world.Dispose();

        private Entity Spawn(long type, Vector3 at)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            var t = _db.GetByType(type);
            new CarrierTkbTranslator().Inject(_world, e, t);
            new WeaponSystemTkbTranslator().Inject(_world, e, t);
            new CombatTkbTranslator().Inject(_world, e, t);
            return e;
        }

        private (Entity Man, Entity Pistol, Entity Soldier) ManWithAHiddenPistol(float soldierAt = 1f)
        {
            var man = Spawn(ManType, Vector3.Zero);
            var pistol = Spawn(PistolType, Vector3.Zero);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, pistol, man));
            Assert.True(CarryStations.Move(_world, man, pistol, CarryStations.Hidden(_world, man)));
            Assert.True(CarryStations.IsConcealed(_world, pistol));
            var soldier = _world.CreateEntity();
            _world.AddComponent(soldier, new SimTransform { Position = new Vector3(soldierAt, 0f, 0f), Rotation = Quaternion.Identity });
            return (man, pistol, soldier);
        }

        private void Step(double seconds)
        {
            _t += seconds;
            _world.SetSingleton(new GlobalTime { TotalTime = _t, DeltaTime = (float)seconds });
            _world.Bus.SwapBuffers();
            _system.Execute(_world, (float)seconds);
        }

        private Custody Of(Entity man) => _world.GetComponentRO<Custody>(man);

        /// <summary>
        /// CU-3 — a search of a man showing his hands, 1 m away: Searching for 3 s, then his hidden pistol is put down (out of his list, no
        /// longer concealed) and he reads Searched. ✅ Red-proof: <c>RevealHidden</c> skipped ⇒ the pistol stays hidden ⇒ red.
        /// </summary>
        [Fact]
        public void CE3256_ASearch_OfAComplyingMan_PutsHisHiddenPistolDown_AfterThreeSeconds()
        {
            var (man, pistol, soldier) = ManWithAHiddenPistol();
            Gestures.Set(_world, man, GestureKind.HandsUp);

            Assert.True(CustodyRules.Ask(_world, man, soldier, CustodyVerb.Search));
            Step(0.1);
            Assert.Equal((byte)1, Of(man).Searching);
            Step(2.0);
            Assert.True(CarryStations.IsConcealed(_world, pistol), "not before 3 s");
            Step(1.0);
            Assert.Equal(((byte)0, (byte)1), (Of(man).Searching, Of(man).Searched));
            Assert.False(CarryStations.IsConcealed(_world, pistol));
            Assert.All(Enumerable.Range(0, PassengerBuffer.Capacity), i => Assert.NotEqual(pistol, _world.GetComponentRO<PassengerBuffer>(man).Passengers[i]));
            Assert.Equal(1, _system.Searched);
        }

        /// <summary>CU-3 / CU-4 — refused: a man not complying, or a soldier out of reach; aborted: the soldier walks off mid-search.</summary>
        [Fact]
        public void CE3256_ASearch_IsRefusedWithoutCompliance_OrReach_AndAbortedWhenTheSoldierLeaves()
        {
            var (man, pistol, soldier) = ManWithAHiddenPistol();
            CustodyRules.Ask(_world, man, soldier, CustodyVerb.Search);
            Step(0.1);
            Assert.Equal(1, _system.Refused);                                     // no hands up

            Gestures.Set(_world, man, GestureKind.HandsUp);
            var far = _world.CreateEntity();
            _world.AddComponent(far, new SimTransform { Position = new Vector3(5f, 0f, 0f), Rotation = Quaternion.Identity });
            CustodyRules.Ask(_world, man, far, CustodyVerb.Search);
            Step(0.1);
            Assert.Equal(2, _system.Refused);                                     // 5 m off

            CustodyRules.Ask(_world, man, soldier, CustodyVerb.Search);
            Step(0.1);
            _world.GetComponentRW<SimTransform>(soldier).Position = new Vector3(4f, 0f, 0f);   // he walks off
            Step(0.5);
            Assert.Equal(1, _system.Aborted);
            Step(4.0);
            Assert.True(CarryStations.IsConcealed(_world, pistol));
        }

        /// <summary>
        /// CU-5 / CU-6 — Detain and Restrain set the flags; either puts him out of the fight; a restrained man's fire holds (HoldRestrained);
        /// Release clears both. ✅ Red-proof: <c>IsOutOfTheFight</c> without custody ⇒ the detained man is still in the fight ⇒ red.
        /// </summary>
        [Fact]
        public void CE3256_DetainAndRestrain_PutHimOutOfTheFight_HoldHisFire_AndReleaseClears()
        {
            var (man, _, soldier) = ManWithAHiddenPistol();
            Gestures.Set(_world, man, GestureKind.HandsUp);
            Assert.False(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, man));

            CustodyRules.Ask(_world, man, soldier, CustodyVerb.Detain);
            Step(0.1);
            Assert.True(CustodyRules.IsDetained(_world, man));
            Assert.True(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, man));

            CustodyRules.Ask(_world, man, soldier, CustodyVerb.Restrain);
            Step(0.1);
            Assert.True(CustodyRules.IsRestrained(_world, man));
            var rifle = new WeaponMountDto { Category = WeaponCategory.SmallArms };
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldRestrained,
                         Fdp.Toolkit.Combat.Executors.AimAndFireExecutor.HoldOf(_world, man, soldier, rifle, 0));

            CustodyRules.Ask(_world, man, soldier, CustodyVerb.ReleaseCustody);
            Step(0.1);
            Assert.False(CustodyRules.InCustody(_world, man));
        }

        /// <summary>
        /// ⭐ <c>CE-4622</c> K-6 (Checkpoint CP-4) — a VEHICLE search: a soldier 3 m from a STOPPED car (its side — the vehicle reach is 4 m) searches it
        /// for 3 s and what its concealed boot holds is put down (a charge stands in as the pistol); a MOVING car is refused.
        /// ✅ Red-proof: the stopped-vehicle compliance dropped ⇒ the search is refused ⇒ red.
        /// </summary>
        [Fact]
        public void CE4622_K6_AVehicleSearch_OfAStoppedCar_RevealsItsBoot_AMovingCarIsRefused()
        {
            var car = Spawn(CarType, Vector3.Zero);
            _world.AddComponent(car, new global::CarKinem.Core.VehicleState { Speed = 5f });
            var charge = Spawn(PistolType, Vector3.Zero);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, charge, car));
            Assert.True(CarryStations.Move(_world, car, charge, 2));   // the boot (station 2)
            Assert.True(CarryStations.IsConcealed(_world, charge));
            var soldier = _world.CreateEntity();
            _world.AddComponent(soldier, new SimTransform { Position = new Vector3(0f, 3f, 0f), Rotation = Quaternion.Identity });

            CustodyRules.Ask(_world, car, soldier, CustodyVerb.Search);
            Step(0.1);
            Assert.Equal(1, _system.Refused);                                      // it is moving
            _world.GetComponentRW<global::CarKinem.Core.VehicleState>(car).Speed = 0f;
            CustodyRules.Ask(_world, car, soldier, CustodyVerb.Search);
            Step(0.1);
            Assert.Equal((byte)1, Of(car).Searching);
            Step(3.0);
            Assert.Equal((byte)1, Of(car).Searched);
            Assert.False(CarryStations.IsConcealed(_world, charge));               // the boot's charge is out
        }
    }
}
