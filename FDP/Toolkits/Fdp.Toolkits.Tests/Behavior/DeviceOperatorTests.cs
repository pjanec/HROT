using System.Linq;
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
            w.AddComponent(d, new DeviceInput());

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

        /// <summary>⭐ S1b-2 (§3.4, D-72) / ⭐ SD-S5v (D-138) — an input event aimed at an operated device, from ITS station, becomes the device's own input — on
        /// a PAUSED frame too (the device tick runs every frame); another station's event is ignored; a duplicate (an older SourceSeq) never lands. 🔴 Mutation:
        /// the ingest drops the HeldBy check ⇒ station 7's press lands.</summary>
        [Fact]
        public void S1b2_ADeviceTakesItsOperatorsInput_EvenPaused_AndNoOtherStationsInput()
        {
            using var w = World();
            var registry = new BehaviorRegistry();
            var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            var tick = new DeviceTickSystem(registry, ingress);
            if (!w.Bus.IsRegistered<DeviceInputEvent>()) w.Bus.Register<DeviceInputEvent>();
            var man = Man(w, Five);
            var d = Device(w);
            w.SetAuthority<BehaviorState>(d, true);
            Carry(w, man, d, 3);
            tick.Execute(w, 0f);   // the walk writes the row

            DeviceInputEvent Event(StationId from, uint seq, ushort press)
            {
                var f = new DeviceInputEvent { Device = d, Station = from, Source = DeviceInputSource.Virtual, SourceSeq = seq, Count = 2 };
                f.Changes[0] = new InputChange { Kind = InputChangeKind.ButtonDown, Element = 2, Count = press };
                f.Changes[1] = new InputChange { Kind = InputChangeKind.Axis, Element = 0, Value = 0.5f };
                return f;
            }
            w.Bus.Publish(Event(Five, 4, 1));
            w.Bus.Publish(Event(Seven, 9, 7));   // another station: not the device's operator
            w.Bus.SwapBuffers();
            tick.Execute(w, 0f);                 // a paused frame
            var input = w.GetComponentRO<DeviceInput>(d);
            Assert.Equal((byte)1, input.Control.Presses[2]);
            Assert.Equal(0.5f, input.Control.Axes[0]);

            w.Bus.Publish(Event(Five, 3, 9));    // an older event of the same source: a duplicate
            w.Bus.SwapBuffers();
            tick.Execute(w, 0f);
            Assert.Equal((byte)1, w.GetComponentRO<DeviceInput>(d).Control.Presses[2]);
            Assert.Equal(1, tick.Merge.Duplicates);
        }

        // ── ⭐⭐⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-137 – D-146) — SPARSE input events, merged by the DEVICE ──────────────

        /// <summary>A device operated by station Five, its tick, and a wall clock the rail moves.</summary>
        private sealed class MergeRig : System.IDisposable
        {
            public readonly EntityRepository W = World();
            public readonly DeviceTickSystem Tick;
            public readonly Entity D;
            public double Now;

            public MergeRig()
            {
                var registry = new BehaviorRegistry();
                Tick = new DeviceTickSystem(registry, new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry));
                Tick.Merge.Clock = () => Now;
                if (!W.Bus.IsRegistered<DeviceInputEvent>()) W.Bus.Register<DeviceInputEvent>();
                var man = Man(W, Five);
                D = Device(W);
                W.SetAuthority<BehaviorState>(D, true);
                Carry(W, man, D, 3);
                Tick.Execute(W, 0f);   // the walk writes the row
            }

            public DeviceInputEncoder Encoder(DeviceInputSource source) => new(source) { Clock = () => Now };

            public void Flush(params DeviceInputEncoder[] sources) { foreach (var e in sources) e.Flush(W, Five, new[] { D }); }

            public void Frame() { W.Bus.SwapBuffers(); Tick.Execute(W, 0f); }

            public DeviceControl Input => W.HasComponent<DeviceInput>(D) ? W.GetComponentRO<DeviceInput>(D).Control : default;

            public DeviceInputEvent[] Sent() { W.Bus.SwapBuffers(); var e = W.Bus.Read<DeviceInputEvent>().ToArray(); return e; }

            public void Dispose() => W.Dispose();
        }

        /// <summary>⭐ D-138 / D-141 — two sources press the same button in ONE frame (the replica's button, a HUD click beside it): the DEVICE counts BOTH; the HUD's
        /// press-only event leaves the replica's axis and the look untouched. 🔴 Red-proof (compiles): the merge SETS the counter (<c>= Count</c>) instead of adding ⇒ 1.</summary>
        [Fact]
        public void SDS5v_TwoSourcesPresses_AddUp_AndAPressOnlyEvent_OverwritesNothing()
        {
            using var r = new MergeRig();
            var replica = r.Encoder(DeviceInputSource.Replica);
            var hud = r.Encoder(DeviceInputSource.Virtual);
            replica.SetAxis(r.D, 0, 0.25f);
            replica.SetOrientation(r.D, System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 0.4f));
            r.Flush(replica); r.Frame();
            r.Now = 2;   // past any hold
            replica.Press(r.D, 1);
            hud.Press(r.D, 1);
            r.Flush(replica, hud); r.Frame();
            var c = r.Input;
            Assert.Equal((byte)2, c.Presses[1]);                // both presses counted on the device
            Assert.Equal(0.25f, c.Axes[0]);                    // the HUD's event carried no axis …
            Assert.Equal(0.4f, 2f * System.MathF.Asin(c.Orientation.Z), 4);   // … and no look
            Assert.Equal((ushort)0, c.Held);                   // a click is a press AND its release
        }

        /// <summary>⭐ D-139 — a field is last-writer-wins UNLESS a higher-priority source wrote it within 0.5 s (wall): a live tracker keeps the look against the
        /// mouse, which takes it the moment the tracker goes quiet; a lower source's PRESS is never held off. 🔴 Red-proof (compiles): <c>May</c> always true ⇒ the
        /// mouse's look lands at 0.2 s.</summary>
        [Fact]
        public void SDS5v_AHigherPrioritySource_HoldsAField_ForHalfASecond()
        {
            using var r = new MergeRig();
            var tracker = r.Encoder(DeviceInputSource.Tracker);
            var mouse = r.Encoder(DeviceInputSource.Virtual);
            var a = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 0.3f);
            var b = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, -0.6f);
            tracker.SetOrientation(r.D, a); r.Flush(tracker); r.Frame();
            r.Now = 0.2;
            mouse.SetOrientation(r.D, b); mouse.Press(r.D, 0); r.Flush(mouse); r.Frame();
            Assert.Equal(a, r.Input.Orientation);               // held by the tracker
            Assert.Equal((byte)1, r.Input.Presses[0]);           // the press is never held off
            Assert.Equal(1, r.Tick.Merge.Held);
            r.Now = 0.8;
            mouse.SetOrientation(r.D, a * b); r.Flush(mouse); r.Frame();
            Assert.Equal(a * b, r.Input.Orientation);           // the tracker went quiet: the mouse takes it
        }

        /// <summary>⭐ D-140 / D-143 — a held button whose <c>ButtonUp</c> is LOST stays held only until the source's 1 Hz heal: the snapshot clears it; another
        /// source's held button is not touched by that heal (it speaks only for its own). 🔴 Red-proof (compiles): no heal in <c>Flush</c> ⇒ still held.</summary>
        [Fact]
        public void SDS5v_ALostButtonUp_HealsOnTheSnapshot()
        {
            using var r = new MergeRig();
            var replica = r.Encoder(DeviceInputSource.Replica);
            var pad = r.Encoder(DeviceInputSource.Virtual);
            replica.Hold(r.D, 2, true); pad.Hold(r.D, 4, true);
            r.Flush(replica, pad); r.Frame();
            Assert.Equal((ushort)((1 << 2) | (1 << 4)), r.Input.Held);
            Assert.Equal((byte)1, r.Input.Presses[2]);          // a held level going down is a press
            replica.Hold(r.D, 2, false);
            r.Flush(replica); r.W.Bus.SwapBuffers();             // the ButtonUp is LOST (the frame's events are never ingested)
            r.W.Bus.SwapBuffers();
            r.Tick.Execute(r.W, 0f);
            Assert.Equal((ushort)((1 << 2) | (1 << 4)), r.Input.Held);
            r.Now = 1.1;                                          // a second later: the heal
            r.Flush(replica); r.Frame();
            Assert.Equal((ushort)(1 << 4), r.Input.Held);       // released — and the pad's button still held
            Assert.Equal((byte)1, r.Input.Presses[2]);          // a heal presses nothing
        }

        /// <summary>⭐ D-145 — a duplicate (a <c>SourceSeq</c> not above that source's last) is dropped; another source's lower number is its own series.</summary>
        [Fact]
        public void SDS5v_ADuplicate_IsDroppedBySourceSeq()
        {
            using var r = new MergeRig();
            DeviceInputEvent Press(DeviceInputSource src, uint seq)
            {
                var e = new DeviceInputEvent { Device = r.D, Station = Five, Source = src, SourceSeq = seq, Count = 1 };
                e.Changes[0] = new InputChange { Kind = InputChangeKind.ButtonDown, Element = 0, Count = 1 };
                return e;
            }
            r.W.Bus.Publish(Press(DeviceInputSource.Replica, 10));
            r.W.Bus.Publish(Press(DeviceInputSource.Replica, 10));    // the same event twice
            r.W.Bus.Publish(Press(DeviceInputSource.Virtual, 3));     // another source: its own series
            r.Frame();
            Assert.Equal((byte)2, r.Input.Presses[0]);
            Assert.Equal(1, r.Tick.Merge.Duplicates);
        }

        /// <summary>⭐ D-137 / D-144 — the encoder sends ONLY changes, at most eight per event: ten buttons in one frame ⇒ three events (8, 8, 4 changes), the pose on
        /// the first only, consecutive <c>SourceSeq</c>; the device applies them in order (every press counted, none left held); a second flush with nothing changed
        /// sends nothing; one axis moved ⇒ one event, one change. 🔴 Red-proof (compiles): the split's <c>Math.Min</c> dropped ⇒ an event with Count 20 (the merge
        /// clamps at 8 ⇒ red).</summary>
        [Fact]
        public void SDS5v_TheEncoder_SendsOnlyChanges_AtMostEightPerEvent()
        {
            using var r = new MergeRig();
            var enc = r.Encoder(DeviceInputSource.Virtual);
            enc.SetOrientation(r.D, System.Numerics.Quaternion.Identity);
            enc.SetViewed(r.D, true);
            for (int b = 0; b < 10; b++) enc.Press(r.D, b);
            r.Flush(enc);
            var sent = r.Sent();
            Assert.Equal(new byte[] { 8, 8, 4 }, sent.Select(e => e.Count).ToArray());
            Assert.Equal(new byte[] { DeviceInputEvent.OrientationPresent | DeviceInputEvent.ViewedPresent | DeviceInputEvent.ViewedOn, 0, 0 }, sent.Select(e => e.PoseMask).ToArray());
            Assert.Equal(sent[0].SourceSeq + 1, sent[1].SourceSeq);
            Assert.Equal(sent[1].SourceSeq + 1, sent[2].SourceSeq);
            r.Tick.Execute(r.W, 0f);
            for (int b = 0; b < 10; b++) Assert.Equal((byte)1, r.Input.Presses[b]);
            Assert.Equal((ushort)0, r.Input.Held);
            Assert.Equal((byte)1, r.Input.Viewed);

            r.Flush(enc);                                         // nothing changed
            Assert.Empty(r.Sent());
            enc.SetAxis(r.D, 3, -0.5f);
            r.Flush(enc);
            var one = Assert.Single(r.Sent());
            Assert.Equal((byte)1, one.Count);
            Assert.Equal((InputChangeKind.Axis, (byte)3, -0.5f), (one.Changes[0].Kind, one.Changes[0].Element, one.Changes[0].Value));
            Assert.Equal((byte)0, one.PoseMask);
        }

        /// <summary>⭐ R-2084 — a steady frame of the whole path (the encoder staging the look and a press, the bus, the device's merge) allocates nothing.</summary>
        [Fact]
        public void SDS5v_ASteadyFrame_OfTheEncoderAndTheMerge_AllocatesNothing()
        {
            using var r = new MergeRig();
            var enc = r.Encoder(DeviceInputSource.Virtual);
            var devices = new[] { r.D };
            void Step(int i)
            {
                enc.SetOrientation(r.D, System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, i * 0.01f));
                enc.SetViewed(r.D, true);
                enc.Press(r.D, i & 3);
                enc.Flush(r.W, Five, devices);
                r.Frame();
            }
            for (int i = 0; i < 20; i++) Step(i);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 20; i < 120; i++) Step(i);
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
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
