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
            w.RegisterComponent<DeviceInput>();
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
            w.AddComponent(d, new DeviceInput { Seq = 42 });

            w.SetComponent(man, new PossessedBy { StationNodeId = Seven, Mode = PossessionMode.Fps, Epoch = 2 });   // another player takes the man
            DeviceOperators.Sync(w, d);
            Assert.Equal(Seven, w.GetComponentRO<PossessedBy>(d).StationNodeId);
            Assert.Equal(2u, w.GetComponentRO<PossessedBy>(d).Epoch);
            Assert.False(w.HasComponent<DeviceInput>(d));                      // the new station starts clean (no phantom press)

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

        /// <summary>⭐ S1b-2 (§3.4, D-72) — a control frame aimed at an operated device, from ITS station, becomes the device's own input — on a PAUSED
        /// frame too (the device tick runs every frame); another station's frame is ignored; an older Seq never overwrites; the body's frame / input are untouched (their recorded layout is pinned). 🔴 Mutation: the ingest drops the HeldBy check ⇒ station 7's frame lands.</summary>
        [Fact]
        public void S1b2_ADeviceTakesItsOperatorsFrame_EvenPaused_AndNoOtherStationsFrame()
        {
            using var w = World();
            var registry = new BehaviorRegistry();
            var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            var tick = new DeviceTickSystem(registry, ingress);
            if (!w.Bus.IsRegistered<DeviceFrameEvent>()) w.Bus.Register<DeviceFrameEvent>();
            var man = Man(w, Five);
            var d = Device(w);
            w.SetAuthority<BehaviorState>(d, true);
            Carry(w, man, d, 3);
            tick.Execute(w, 0f);   // the walk writes the row

            DeviceFrameEvent Frame(StationId from, uint seq, byte press)
            {
                var f = new DeviceFrameEvent { Device = d, StationNodeId = from, Seq = seq };
                f.Control.Presses[2] = press;
                f.Control.Axes[0] = 0.5f;
                return f;
            }
            w.Bus.Publish(Frame(Five, 4, 1));
            w.Bus.Publish(Frame(Seven, 9, 7));   // another station: not the device's operator
            w.Bus.SwapBuffers();
            tick.Execute(w, 0f);                 // a paused frame
            var input = w.GetComponentRO<DeviceInput>(d);
            Assert.Equal(4u, input.Seq);
            Assert.Equal((byte)1, input.Control.Presses[2]);
            Assert.Equal(0.5f, input.Control.Axes[0]);

            w.Bus.Publish(Frame(Five, 3, 9));    // an older frame
            w.Bus.SwapBuffers();
            tick.Execute(w, 0f);
            Assert.Equal(4u, w.GetComponentRO<DeviceInput>(d).Seq);
        }

        /// <summary>⭐ S1b-3 (D-48) — a device's world pose is its own placed transform (EmbarkedStateSystem puts it at its station) composed with its
        /// <see cref="DevicePose"/>: an offset in the mount's frame, an orientation relative to it.</summary>
        [Fact]
        public void S1b3_ADevicesWorldPose_IsItsPlacedTransform_ComposedWithItsPose()
        {
            using var w = World();
            w.RegisterComponent<DevicePose>();
            var d = Device(w);
            var yaw90 = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, System.MathF.PI / 2f);
            w.AddComponent(d, new SimTransform { Position = new System.Numerics.Vector3(10f, 0f, 0f), Rotation = yaw90 });
            w.AddComponent(d, new DevicePose { Orientation = yaw90, Offset = new System.Numerics.Vector3(1f, 0f, 0f) });
            Assert.True(DevicePoses.TryWorld(w, d, out var p, out var r));
            Assert.Equal(10f, p.X, 3); Assert.Equal(1f, p.Y, 3);   // the offset turned with the mount (x forward → +y)
            var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, r);
            Assert.Equal(-1f, forward.X, 3);                       // two quarter turns: it points backwards
        }
    }
}
