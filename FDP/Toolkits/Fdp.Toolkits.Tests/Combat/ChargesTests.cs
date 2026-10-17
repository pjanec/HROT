using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3233</c> (civilians B-11b, AW-12; docs/DESIGN_Civilians_And_Crowd_Control.md §7.13 ST-8/ST-9) — an explosive CHARGE is never
    /// fired; set off, its round bursts WHERE IT STANDS — worn, that is at the man — on the node that owns its position, and it is gone. In a
    /// car's boot it arms nobody and is no weapon the car fires.
    /// </summary>
    [Collection(ArmamentCounterCollection.Name)]   // ⭐ Armament reads move the process-wide conduct counter
    public sealed class ChargesTests : IDisposable
    {
        private const long ManType = 9711, ChargeType = 9712, CarType = 9713, RifleType = 9714, TruckType = 9715, ChargeAmmo = 9790;
        private const int Here = 1, Elsewhere = 2;

        private readonly EntityRepository _world = new();
        private readonly TkbDatabase _db = new();

        public ChargesTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterComponent<AmmoLoadout>();
            _world.RegisterComponent<WeaponSystemParams>();
            _world.RegisterComponent<WeaponSystemState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<CarrierSeats>();
            _world.RegisterComponent<CarrierStations>();
            _world.RegisterComponent<PersonCarrier>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterManagedComponent<DescriptorOwnership>();
            _world.RegisterEvent<DetonationNotification>();
            _world.RegisterEvent<DetonateChargeEvent>();

            var man = new TkbTemplate("Bomber", ManType);
            man.AddDescriptor(CarrierDefDto.PersonCarrier());
            _db.Register(man);
            var charge = new TkbTemplate("Charge", ChargeType);
            charge.AddDescriptor(new WeaponSystemDto { Portable = WeaponPortability.OneMan, Charge = true,
                                                       MountKinds = MountKinds.Ground | MountKinds.Carried | MountKinds.Cargo });
            charge.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = 1, AmmoGuid = (ulong)ChargeAmmo, Category = WeaponCategory.Grenade } } });
            _db.Register(charge);
            _db.Register(new TkbTemplate("Charge round", ChargeAmmo));
            var car = new TkbTemplate("Car", CarType);
            var seats = CarrierDefDto.PassengerSeats(4);
            seats.Seats.Add(new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Cargo, Stowed = true, Concealed = true, Exposed = false });
            car.AddDescriptor(seats);
            _db.Register(car);
            var rifle = new TkbTemplate("Rifle", RifleType);   // a weapon system that is NOT a charge, with a round
            rifle.AddDescriptor(new WeaponSystemDto { Portable = WeaponPortability.OneMan, MountKinds = MountKinds.Ground | MountKinds.Carried });
            rifle.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = 30, AmmoGuid = (ulong)ChargeAmmo, Category = WeaponCategory.SmallArms } } });
            _db.Register(rifle);
            var truck = new TkbTemplate("Truck", TruckType);   // one OPEN ground station — its crew fires what stands there
            var bed = CarrierDefDto.PassengerSeats(1);
            bed.Seats.Add(new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Ground });
            truck.AddDescriptor(bed);
            _db.Register(truck);
            _world.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _world.Dispose();

        private Entity Spawn(long type, Vector3 at)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TkbIdentity { TkbType = type });
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            var t = _db.GetByType(type);
            new CarrierTkbTranslator().Inject(_world, e, t);
            new WeaponSystemTkbTranslator().Inject(_world, e, t);
            new CombatTkbTranslator().Inject(_world, e, t);
            return e;
        }

        /// <summary>Position owned by another node.</summary>
        private void PositionElsewhere(Entity e)
        {
            _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: Elsewhere, localNodeId: Here));
            var ownership = new DescriptorOwnership();
            ownership.Map[Embarkation.PositionKey] = Elsewhere;
            _world.SetManagedComponent(e, ownership);
        }

        /// <summary>
        /// ⭐ ST-8 — a vest WORN in his hidden station: it is no weapon he fires, he reads unarmed; set off, ONE detonation of its round bursts AT
        /// HIM (a carried thing stands at its carrier), named his; the vest is gone and out of his list.
        /// </summary>
        [Fact]
        public void CE3233_AVestWornHidden_ArmsNobody_FiresNothing_AndSetOffBurstsAtTheMan()
        {
            var man = Spawn(ManType, new Vector3(10f, 20f, 0f));
            var vest = Spawn(ChargeType, Vector3.Zero);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, vest, man));
            Assert.True(CarryStations.Move(_world, man, vest, CarryStations.Hidden(_world, man)));
            new Fdp.Toolkit.Behavior.Systems.EmbarkedStateSystem().Execute(_world, 0.1f);   // it stands at its carrier
            Assert.Equal(new Vector3(10f, 20f, 0f), _world.GetComponentRO<SimTransform>(vest).Position);

            Assert.True(Charges.IsCharge(_world, vest));
            Assert.False(WeaponSystems.CanFire(_world, vest));
            Assert.False(Armament.ArmedByStatus(_world, man));

            Assert.True(Charges.Detonate(_world, vest, man));
            _world.Bus.SwapBuffers();
            var burst = Assert.Single(_world.Bus.Read<DetonationNotification>().ToArray());
            Assert.Equal((10f, 20f, ChargeAmmo, man), (burst.HitX, burst.HitY, burst.Ammo, burst.Shooter));
            Assert.False(_world.IsAlive(vest));
            Assert.All(Enumerable.Range(0, PassengerBuffer.Capacity), i => Assert.NotEqual(vest, _world.GetComponentRO<PassengerBuffer>(man).Passengers[i]));
        }

        /// <summary>
        /// ⭐ ST-8 — the ASK reaches the charge's position owner: here (one world) <c>ChargeSystem</c> sets it off; a node that does not own its
        /// position does nothing (the transport carries the ask, kind 12); a rifle is no charge. ✅ Red-proofs: <c>Detonate</c> without the
        /// ownership check ⇒ a replica bursts too ⇒ red; without the charge check ⇒ the rifle bursts ⇒ red.
        /// </summary>
        [Fact]
        public void CE3233_OnlyThePositionOwner_SetsACharge_Off_AndOnlyACharge()
        {
            var placed = Spawn(ChargeType, new Vector3(5f, 0f, 0f));   // an IED on the ground
            var replica = Spawn(ChargeType, new Vector3(6f, 0f, 0f));
            PositionElsewhere(replica);
            var rifle = Spawn(RifleType, Vector3.Zero);

            Assert.False(Charges.Detonate(_world, replica));
            Assert.False(Charges.Detonate(_world, rifle));
            Assert.True(Charges.Ask(_world, placed, Entity.Null));
            _world.Bus.SwapBuffers();
            var system = new ChargeSystem();
            system.Execute(_world, 0.1f);
            Assert.Equal(1, system.Detonated);
            Assert.False(_world.IsAlive(placed));
            Assert.True(_world.IsAlive(replica));
            Assert.True(_world.IsAlive(rifle));
        }

        /// <summary>
        /// ⭐ ST-8 — a charge on a vehicle's OPEN ground station (where the crew would fire a gun) is still no weapon anyone fires.
        /// ✅ Red-proof: <c>CanFire</c> without the charge gate ⇒ the truck "fires" it ⇒ red.
        /// </summary>
        [Fact]
        public void CE3233_AChargeOnAnOpenStation_IsStillNoWeaponItsCarrierFires()
        {
            var truck = Spawn(TruckType, Vector3.Zero);
            var charge = Spawn(ChargeType, Vector3.Zero);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, charge, truck));
            Assert.Equal(truck, WeaponSystems.ControllerOf(_world, charge));   // the station's crew would fire what stands there
            Assert.False(WeaponSystems.CanFire(_world, charge, out var hold));
            Assert.Equal(WeaponHold.NotDeployed, hold);
        }

        /// <summary>
        /// ⭐ ST-9 — a car's BOOT takes the charge (a cargo station) and it rides there unseen: the car is not armed by it and never fires it.
        /// ✅ Red-proof: the boot not concealed ⇒ the car reads armed ⇒ red.
        /// </summary>
        [Fact]
        public void CE3233_ACarBomb_RidesInTheBoot_TheCarIsNotArmed_AndFiresNothing()
        {
            var car = Spawn(CarType, Vector3.Zero);
            var load = Spawn(ChargeType, Vector3.Zero);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, load, car));
            Assert.Equal(4, CarryStations.SeatOf(_world, load, out _));   // after the four seats
            Assert.True(CarryStations.IsConcealed(_world, load));
            Assert.False(Armament.ArmedByStatus(_world, car));
            Assert.Equal(0, WeaponSystems.PrimaryMount(_world, car, out _));
            Assert.True(WeaponSystems.WeaponOf(_world, car).IsNull);
        }
    }
}
