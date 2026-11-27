using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1b-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.3, D-73, R4-2, R7-1, R6-1) — WHO OPERATES A DEVICE and its
    /// <see cref="PossessionMode.Operated"/> row: carried by a possessed man, manned in its own gunner seat, or in a host station operated by a crew
    /// seat ⇒ the row names the station of that operator's BODY; no operator ⇒ no row; another station's body ⇒ a new epoch and a clean input. The
    /// station's body stays the man (<see cref="StationBody"/> ignores the device), and the possession core never releases a device's row.
    /// </summary>
    public class DeviceOperatorTests
    {
        private static readonly StationId Five = (StationId)5, Seven = (StationId)7;

        private static EntityRepository World()
        {
            var w = TestWorldFactory.Create();
            w.RegisterComponent<DeviceInfo>();
            w.RegisterComponent<PossessedBy>();
            w.RegisterComponent<OperatorInput>();
            w.RegisterComponent<PersonCarrier>();
            w.RegisterComponent<CarrierSeats>();
            w.RegisterComponent<CarrierStations>();
            return w;
        }

        private static Entity Device(EntityRepository w)
        {
            var d = w.CreateEntity();
            w.AddComponent(d, new BehaviorState());
            w.AddComponent(d, new DeviceInfo { DeviceType = 77 });
            return d;
        }

        private static Entity Man(EntityRepository w, StationId? heldBy = null)
        {
            var m = w.CreateEntity();
            w.AddComponent(m, new PersonCarrier());
            w.AddComponent(m, new PassengerBuffer());
            if (heldBy is { } s) w.AddComponent(m, new PossessedBy { StationNodeId = s, Mode = PossessionMode.Fps, Epoch = 1 });
            return m;
        }

        private static void Carry(EntityRepository w, Entity man, Entity device, int seat)
        {
            w.GetComponentRW<PassengerBuffer>(man).Passengers[seat] = device;
            w.AddComponent(device, new IsEmbarkedTag { VehicleEntity = man, Seat = (byte)seat });
        }

        [Fact]
        public void S1b1_ADeviceCarriedByAPossessedMan_IsOperatedByHisStation_WhileHisBodyStaysTheMan()
        {
            using var w = World();
            var man = Man(w, Five);
            var d = Device(w);
            Carry(w, man, d, 3);

            DeviceOperators.Sync(w, d);
            var row = w.GetComponentRO<PossessedBy>(d);
            Assert.Equal(PossessionMode.Operated, row.Mode);
            Assert.Equal(Five, row.StationNodeId);
            Assert.Equal(1u, row.Epoch);
            Assert.Equal((int?)(int)Five, PossessionSystem.HolderOf(w, d));   // ⇒ the existing HeldBy gate authorises its frames
            Assert.Equal(man, StationBody.Driven(w, (int)Five));              // the device is never the body
            Assert.Equal(man, StationBody.Own(w, (int)Five));
        }

        [Fact]
        public void S1b1_NoOperatorStation_NoRow_AndAnotherStationsBody_IsANewEpochWithACleanInput()
        {
            using var w = World();
            var man = Man(w, Five);
            var d = Device(w);
            Carry(w, man, d, 3);
            DeviceOperators.Sync(w, d);
            w.AddComponent(d, new OperatorInput { Seq = 42 });

            w.SetComponent(man, new PossessedBy { StationNodeId = Seven, Mode = PossessionMode.Fps, Epoch = 2 });   // another player takes the man
            DeviceOperators.Sync(w, d);
            Assert.Equal(Seven, w.GetComponentRO<PossessedBy>(d).StationNodeId);
            Assert.Equal(2u, w.GetComponentRO<PossessedBy>(d).Epoch);
            Assert.False(w.HasComponent<OperatorInput>(d));                    // the new station starts clean (no phantom press)

            w.RemoveComponent<PossessedBy>(man);                               // nobody holds the man
            DeviceOperators.Sync(w, d);
            Assert.False(w.HasComponent<PossessedBy>(d));
        }

        [Fact]
        public void S1b1_AMannedDevice_AndAVehicleDeviceStation_AreOperatedByTheirSeat()
        {
            using var w = World();
            // manned: the device's own gunner seat
            var tripod = Device(w);
            w.AddComponent(tripod, new PassengerBuffer());
            var seats = new CarrierSeats { SeatCount = 1 };
            seats.Roles[0] = SeatRole.Gunner;
            w.AddComponent(tripod, seats);
            var gunner = Man(w, Five);
            w.GetComponentRW<PassengerBuffer>(tripod).Passengers[0] = gunner;
            DeviceOperators.Sync(w, tripod);
            Assert.Equal(Five, w.GetComponentRO<PossessedBy>(tripod).StationNodeId);

            // a vehicle's device station 3, operated by crew seat 1
            var hull = w.CreateEntity();
            w.AddComponent(hull, new PassengerBuffer());
            var stations = new CarrierStations { StationMask = 1 << 3 };
            stations.Seats[3] = new StationSeat { OperatedBy = 1 };
            w.AddComponent(hull, stations);
            var commander = Man(w, Seven);
            w.GetComponentRW<PassengerBuffer>(hull).Passengers[1] = commander;
            var rangefinder = Device(w);
            w.GetComponentRW<PassengerBuffer>(hull).Passengers[3] = rangefinder;
            w.AddComponent(rangefinder, new IsEmbarkedTag { VehicleEntity = hull, Seat = 3 });
            DeviceOperators.Sync(w, rangefinder);
            Assert.Equal(Seven, w.GetComponentRO<PossessedBy>(rangefinder).StationNodeId);
        }

        /// <summary>⭐ R6-1 — releasing a station never releases a device's row (it would clear the device's behaviour): the device tick's walk owns it.
        /// 🔴 Mutation: drop the guard in <c>ReleaseStation</c> ⇒ the device's row is gone after the release.</summary>
        [Fact]
        public void S1b1_ReleasingTheStation_LeavesTheDevicesRowToTheDeviceTick()
        {
            using var w = World();
            var man = Man(w, Five);
            var d = Device(w);
            Carry(w, man, d, 3);
            DeviceOperators.Sync(w, d);
            w.AddComponent(man, new BehaviorState());
            w.SetAuthority<BehaviorState>(man, true);
            w.SetAuthority<BehaviorState>(d, true);

            PossessionSystem.ReleaseStation(w, Five);
            Assert.False(w.HasComponent<PossessedBy>(man));   // the body is released
            Assert.True(w.HasComponent<PossessedBy>(d));      // ...the device row is NOT (no clear of its logic)
            DeviceOperators.Sync(w, d);
            Assert.False(w.HasComponent<PossessedBy>(d));     // the walk removes it: nobody's body operates it now
        }
    }
}
