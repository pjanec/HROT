using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1152</c> (docs/DESIGN_Entity_Control.md §8h U-1…U-7, §8h.6 ①) — the ONE rule for the FPS use key: for each kind of target
    /// (a door, a man who is down, a vehicle, a standalone weapon, aboard) it names the primary action and the hold-E list, says whether each
    /// can be done now and why not, and packs the parameters the executor reads. And <see cref="Embarkation.CanEmbark"/> answers exactly what
    /// <see cref="Embarkation.TryEmbark"/> does, seating nobody.
    /// </summary>
    public sealed class OperatorUseActionsTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public OperatorUseActionsTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<CarrierSeats>();
            _world.RegisterComponent<CarrierStations>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterComponent<PersonCarrier>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<IsAssistingTag>();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<InjuryThresholds>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<WeaponSystemParams>();
            _world.RegisterComponent<WeaponSystemState>();
            _world.RegisterComponent<DoorState>();
            _world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
            _world.RegisterEvent<DamageAssessedEvent>();
            if (!_world.IsComponentTypeRegistered<EntityInfo>()) _world.RegisterComponent<EntityInfo>();
        }

        public void Dispose() => _world.Dispose();

        private Entity OfType(Vector3 at, CarrierDefDto? carrier = null, WeaponSystemDto? weapon = null, InjuryDefDto? injury = null)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            var t = new TkbTemplate("T", 990L);
            if (carrier != null) t.AddDescriptor(carrier);
            if (weapon != null) t.AddDescriptor(weapon);
            if (injury != null) t.AddDescriptor(injury);
            new Translators.CarrierTkbTranslator().Inject(_world, e, t);
            new WeaponSystemTkbTranslator().Inject(_world, e, t);
            new InjuryTkbTranslator().Inject(_world, e, t);
            return e;
        }

        /// <summary>A soldier: a person carrier with an injury band, at <paramref name="health"/> of 100.</summary>
        private Entity Soldier(Vector3 at, float health = 100f)
        {
            var e = OfType(at, CarrierDefDto.PersonCarrier(), injury: new InjuryDefDto());
            _world.AddComponent(e, new Health { Current = health, Max = 100f });
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot | ActorCapabilities.CanInteract });
            return e;
        }

        private Entity Vehicle(Vector3 at) => OfType(at, new CarrierDefDto
        {
            Seats = { new SeatDto { Role = SeatRole.Driver }, new SeatDto { Role = SeatRole.Passenger } },
        });

        private Entity Door(Vector3 at, TerrainDoorState state)
        {
            var d = _world.CreateEntity();
            _world.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(d, new DoorState { State = state });
            return d;
        }

        private UseAction[] For(Entity actor, Entity target) => OperatorUseActions.For(_world, actor, target);

        [Fact]
        public void CE1152_ADoor_ThePrimaryTogglesByItsState_ALockedDoorSaysLocked_AndReachIsTheDoorsOwn()
        {
            var me = Soldier(new Vector3(0, 0, 0));
            var closed = For(me, Door(new Vector3(1, 0, 0), TerrainDoorState.Closed));
            Assert.Equal((UseVerbKind.OpenDoor, true, BehaviorConstants.ActionIdOpenDoor), (closed[0].Verb, closed[0].Available, closed[0].ActionId));
            Assert.Contains(closed, a => a.Verb == UseVerbKind.LockDoor && a.Available);

            var open = For(me, Door(new Vector3(1, 0, 0), TerrainDoorState.Open));
            Assert.Equal(UseVerbKind.CloseDoor, open[0].Verb);

            var locked = For(me, Door(new Vector3(1, 0, 0), TerrainDoorState.Locked));
            Assert.Equal((UseVerbKind.OpenDoor, false, UseReason.Locked), (locked[0].Verb, locked[0].Available, locked[0].Reason));
            Assert.Contains(locked, a => a.Verb == UseVerbKind.UnlockDoor && a.Available);
            Assert.Contains(locked, a => a.Verb == UseVerbKind.BreachDoor && a.Available);

            var far = For(me, Door(new Vector3(DoorRules.ReachMetres + 1f, 0, 0), TerrainDoorState.Closed));
            Assert.Equal((false, UseReason.OutOfReach), (far[0].Available, far[0].Reason));
        }

        [Fact]
        public void CE1152_AManDown_IsDraggedOrCarried_AManStanding_IsNotDown_AndPutDownComesFirstOnceCarried()
        {
            var me = Soldier(new Vector3(0, 0, 0));
            var down = Soldier(new Vector3(1, 0, 0), health: 10f);
            var standing = Soldier(new Vector3(1, 0, 0), health: 90f);

            var onDown = For(me, down);
            Assert.Equal((UseVerbKind.Drag, true, BehaviorConstants.ActionIdPickUp, CarryMode.Dragged),
                (onDown[0].Verb, onDown[0].Available, onDown[0].ActionId, onDown[0].Mode));
            Assert.Equal((UseVerbKind.Carry, true, CarryMode.Carried), (onDown[1].Verb, onDown[1].Available, onDown[1].Mode));

            var onStanding = For(me, standing);
            Assert.Equal((false, UseReason.NotDown), (onStanding[0].Available, onStanding[0].Reason));

            var far = Soldier(new Vector3(PickUpExecutor.DefaultReach + 1f, 0, 0), health: 10f);
            Assert.Equal(UseReason.OutOfReach, For(me, far)[0].Reason);

            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, down, me, mode: CarryMode.Dragged));
            var carrying = For(me, standing);
            Assert.Equal((UseVerbKind.PutDown, BehaviorConstants.ActionIdPutDown, down), (carrying[0].Verb, carrying[0].ActionId, carrying[0].Target));
        }

        [Fact]
        public void CE1152_AVehicle_DriveFirst_ASecondHumanBoards_AndAFullOneSaysSeatTaken_AboardItIsGetOut()
        {
            var bmp = Vehicle(new Vector3(0, 0, 0));
            var me = Soldier(new Vector3(2, 0, 0));
            var first = For(me, bmp);
            Assert.Equal((UseVerbKind.Drive, true, SeatRole.Driver, BehaviorConstants.ActionIdEmbark),
                (first[0].Verb, first[0].Available, first[0].Role, first[0].ActionId));

            var driver = Soldier(new Vector3(2, 1, 0));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, driver, bmp, SeatRole.Driver));
            var second = For(me, bmp);
            Assert.Equal((UseVerbKind.Board, true), (second[0].Verb, second[0].Available));   // O-4: the free seat goes first
            Assert.Equal((UseVerbKind.Drive, UseReason.SeatTaken), (second[1].Verb, second[1].Reason));

            var passenger = Soldier(new Vector3(2, -1, 0));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, passenger, bmp));
            Assert.All(For(me, bmp), a => Assert.Equal(UseReason.SeatTaken, a.Reason));

            var aboard = For(driver, Door(new Vector3(1, 0, 0), TerrainDoorState.Closed));   // whatever the crosshair rests on
            Assert.Equal((UseVerbKind.GetOut, BehaviorConstants.ActionIdDisembark, bmp), (aboard.Single().Verb, aboard[0].ActionId, aboard[0].Target));

            var far = Soldier(new Vector3(OperatorUseActions.VehicleReach + 1f, 0, 0));
            Assert.Equal(UseReason.OutOfReach, For(far, Vehicle(new Vector3(0, 0, 0)))[0].Reason);
        }

        [Fact]
        public void CE1152_AStandaloneWeapon_IsManned_InItsGunnerSeat_OnceOnly()
        {
            var tripod = OfType(new Vector3(0, 0, 0), new CarrierDefDto { Seats = { new SeatDto { Role = SeatRole.Gunner } } },
                new WeaponSystemDto { NeedsOperator = true });
            var me = Soldier(new Vector3(1, 0, 0));
            var man = For(me, tripod).Single();
            Assert.Equal((UseVerbKind.Man, true, SeatRole.Gunner), (man.Verb, man.Available, man.Role));

            var other = Soldier(new Vector3(1, 1, 0));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, other, tripod, SeatRole.Gunner));
            Assert.Equal(UseReason.SeatTaken, For(me, tripod).Single().Reason);
        }

        [Fact]
        public unsafe void CE1152_WriteParams_PacksWhatEachExecutorReads()
        {
            Span<byte> buf = stackalloc byte[BehaviorConstants.ActionParamsByteSize];
            var me = Soldier(new Vector3(0, 0, 0));
            var door = Door(new Vector3(1, 0, 0), TerrainDoorState.Closed);
            OperatorUseActions.WriteParams(For(me, door)[0], buf);
            Assert.Equal(door, System.Runtime.InteropServices.MemoryMarshal.Read<OpenDoorParams>(buf).TargetDoor);

            var bmp = Vehicle(new Vector3(2, 0, 0));
            OperatorUseActions.WriteParams(For(me, bmp)[0], buf);
            var embark = System.Runtime.InteropServices.MemoryMarshal.Read<EmbarkParams>(buf);
            Assert.Equal((bmp, SeatRole.Driver, OperatorUseActions.VehicleReach, Entity.Null),
                (embark.VehicleEntity, embark.Role, embark.MaxBoardingRange, embark.Passenger));

            var down = Soldier(new Vector3(1, 0, 0), health: 10f);
            OperatorUseActions.WriteParams(For(me, down)[1], buf);
            var pick = System.Runtime.InteropServices.MemoryMarshal.Read<PickUpParams>(buf);
            Assert.Equal((down, CarryMode.Carried, PickUpExecutor.DefaultReach), (pick.Casualty, pick.Mode, pick.Reach));
        }

        /// <summary>⭐ One rule, no copy: <see cref="Embarkation.CanEmbark"/> answers what <see cref="Embarkation.TryEmbark"/> then does, and seats
        /// nobody — for a seat, a full vehicle, a unit, a weapon onto a station, a man down and a man standing onto a carrier.</summary>
        [Fact]
        public void CE1152_CanEmbark_AnswersWhatTryEmbarkDoes_AndSeatsNobody()
        {
            var bmp = Vehicle(new Vector3(0, 0, 0));
            var carrier = Soldier(new Vector3(0, 0, 0));
            var cases = new (Func<Entity> Passenger, Entity Vehicle, SeatRole Role, CarryMode Mode)[]
            {
                (() => Soldier(default), bmp, SeatRole.Driver, CarryMode.Seated),                 // a free driver seat
                (() => Soldier(default), bmp, SeatRole.Driver, CarryMode.Seated),                 // now taken
                (() => Soldier(default), bmp, SeatRole.Passenger, CarryMode.Seated),
                (() => Soldier(default), bmp, SeatRole.Passenger, CarryMode.Seated),              // full
                (() => Soldier(default), bmp, SeatRole.Station, CarryMode.Seated),                // a man onto a station
                (() => { var u = _world.CreateEntity(); _world.AddComponent(u, new Fdp.Core.CommandHierarchy.UnitEntity()); return u; },
                    bmp, SeatRole.Passenger, CarryMode.Seated),                                    // a unit
                (() => Soldier(default, 90f), carrier, SeatRole.Passenger, CarryMode.Dragged),   // a man standing
                (() => Soldier(default, 10f), carrier, SeatRole.Passenger, CarryMode.Dragged),   // a man down
            };
            foreach (var (make, vehicle, role, mode) in cases)
            {
                var man = make();
                int before = _world.GetComponentRO<PassengerBuffer>(vehicle).Count;
                var can = Embarkation.CanEmbark(_world, man, vehicle, role, mode);
                Assert.Equal(before, _world.GetComponentRO<PassengerBuffer>(vehicle).Count);   // seated nobody
                Assert.False(_world.HasComponent<IsEmbarkedTag>(man));
                Assert.Equal(can, Embarkation.TryEmbark(_world, man, vehicle, role, mode));
            }
        }
    }
}
