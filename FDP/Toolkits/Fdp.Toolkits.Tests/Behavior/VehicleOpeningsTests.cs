using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (vehicle appearance VA-S2, VA-4 / VA-8; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — a vehicle's hatches,
    /// doors and ramps: the class template (the body profile by DIS class) and a type's sparse override; the boarding arbiter opens what serves the seat that
    /// changed hands and closes it once idle; first sight opens nothing; a list owned by another node is left to it; the animation is derived, never stored.
    /// </summary>
    public sealed class VehicleOpeningsTests : IDisposable
    {
        private const long Ifv = 300, Truck = 301, Man = 302;
        private readonly EntityRepository _w = new();
        private readonly VehicleOpeningsSystem _sys = new();
        private double _t;

        public VehicleOpeningsTests()
        {
            _w.RegisterComponent<PassengerBuffer>();
            _w.RegisterComponent<CarrierSeats>();
            _w.RegisterComponent<VehicleOpenings>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterEvent<OpeningRequestEvent>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("IFV", Ifv) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 2 } });
            var truck = new TkbTemplate("Truck", Truck) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 6 } };
            truck.AddDescriptor(new VehicleOpeningsDto
            {
                Openings = new List<OpeningDto>
                {
                    new() { Id = 3, Remove = true },   // no tailgate on this one
                    new() { Id = 1, Kind = OpeningKind.Door, Roles = new() { SeatRole.Driver }, OpenSeconds = 2.5f, Socket = "door_l" },
                },
            });
            db.Register(truck);
            db.Register(new TkbTemplate("Man", Man) { DisType = new DISEntityType { Kind = 3, Domain = 1 } });
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
        }

        public void Dispose() => _w.Dispose();

        private Entity Vehicle(long type, params SeatRole[] roles)
        {
            var v = _w.CreateEntity();
            _w.AddComponent(v, new TkbIdentity { TkbType = (TkbTypeId)type });
            var seats = new CarrierSeats { SeatCount = (byte)roles.Length };
            for (int i = 0; i < roles.Length; i++) seats.Roles[i] = roles[i];
            _w.AddComponent(v, seats);
            _w.AddComponent(v, new PassengerBuffer());
            return v;
        }

        private void Seat(Entity v, int seat, Entity who) => _w.GetComponentRW<PassengerBuffer>(v).Passengers[seat] = who;

        private void Step(double s = 0.5)
        {
            _t += s;
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = _t });
            _sys.Execute(_w, (float)s);
            _w.Bus.SwapBuffers();   // a frame ends: this frame's requests are read once
        }

        private OpeningSlot Slot(Entity v, int i) => _w.GetComponentRO<VehicleOpenings>(v).Slots[i];

        /// <summary>⭐ The IFV class (DIS 1.1.*.2) has a rear ramp for the squad and three crew hatches; a type's override replaces one and drops another; a person has none.</summary>
        [Fact]
        public void CE4670_TheClassTemplate_AndATypesSparseOverride()
        {
            var db = _w.GetSingletonManaged<ITkbDatabase>()!;
            Assert.True(db.TryGetByType(Ifv, out var ifv));
            var o = VehicleOpeningsCatalog.Of(ifv);
            Assert.Equal(4, o.Length);
            Assert.Equal(OpeningKind.Ramp, o[0].Kind);
            Assert.Contains(SeatRole.Passenger, o[0].Roles);
            Assert.Equal(0b0001, VehicleOpeningsCatalog.ServingMask(o, SeatRole.Passenger));
            Assert.Equal(0b0010, VehicleOpeningsCatalog.ServingMask(o, SeatRole.Driver));

            Assert.True(db.TryGetByType(Truck, out var truck));
            var t = VehicleOpeningsCatalog.Of(truck);
            Assert.Equal(2, t.Length);                   // 3 class doors − the tailgate
            Assert.Equal(2.5f, t[0].OpenSeconds);        // the override replaced door 1
            Assert.Equal("door_l", t[0].Socket);

            Assert.True(db.TryGetByType(Man, out var man));
            Assert.Empty(VehicleOpeningsCatalog.Of(man));
        }

        /// <summary>
        /// ⭐ A squad already aboard at first sight opens nothing; a man leaving his passenger seat opens the RAMP (not the hatches) once — Opening since the moment;
        /// a second man leaving a moment later keeps it open without restarting it; idle for its open seconds + the hold, it starts Closing; the progress is
        /// derived. 🔴 Red-proof: drop the first-sight branch ⇒ the loaded squad opens the ramp at once ⇒ red; drop the close ⇒ it stays open ⇒ red.
        /// </summary>
        [Fact]
        public void CE4670_ADismountOpensTheRamp_OnceAndThenClosesIt()
        {
            var v = Vehicle(Ifv, SeatRole.Driver, SeatRole.Commander, SeatRole.Gunner, SeatRole.Passenger, SeatRole.Passenger);
            var a = _w.CreateEntity(); var b = _w.CreateEntity();
            Seat(v, 3, a); Seat(v, 4, b);
            Step();                                                        // first sight: the component, all closed
            Assert.Equal(4, _w.GetComponentRO<VehicleOpenings>(v).Count);
            Step();
            Assert.Equal(OpeningState.Closed, Slot(v, 0).State);

            Seat(v, 3, Entity.Null); Step();                               // t = 1.5: the first man out
            Assert.Equal(OpeningState.Opening, Slot(v, 0).State);
            Assert.Equal(1.5, Slot(v, 0).Since);
            Assert.Equal(OpeningState.Closed, Slot(v, 1).State);           // the driver's hatch stays shut

            Seat(v, 4, Entity.Null); Step();                               // t = 2.0: the second — still the same opening
            Assert.Equal(1.5, Slot(v, 0).Since);
            Assert.Equal(1L, _sys.Opened);
            Assert.Equal(0.125f, _w.GetComponentRO<VehicleOpenings>(v).ProgressAt(0, 4f, 2.0), 3);

            for (int i = 0; i < 11; i++) Step();                           // t = 7.5: idle 5.5 s < 4 + 2
            Assert.Equal(OpeningState.Opening, Slot(v, 0).State);
            Step();                                                        // t = 8.0: idle 6 s ⇒ closing
            Assert.Equal(OpeningState.Closing, Slot(v, 0).State);
            Assert.Equal(8.0, Slot(v, 0).Since);
            Assert.Equal(0.5f, _w.GetComponentRO<VehicleOpenings>(v).ProgressAt(0, 4f, 10.0), 3);
        }

        /// <summary>⭐ Only the boarding arbiter writes: a vehicle whose passenger list another node owns is left alone; and the steady frame allocates nothing.</summary>
        [Fact]
        public void CE4670_OnlyTheArbiterWrites_AndTheSteadyFrameAllocatesNothing()
        {
            var remote = Vehicle(Ifv, SeatRole.Passenger);
            _w.AddComponent(remote, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var mine = Vehicle(Ifv, SeatRole.Passenger);
            var a = _w.CreateEntity();
            Seat(mine, 0, a);
            Step();
            Assert.False(_w.HasComponent<VehicleOpenings>(remote));
            for (int i = 0; i < 5; i++) Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Step();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐ The one progress rule: closed 0, opening grows to 1 over its seconds, open 1, closing falls to 0; 0 s = instant.</summary>
        [Fact]
        public void CE4670_TheProgressRule()
        {
            Assert.Equal(0f, VehicleOpenings.Progress(OpeningState.Closed, 0, 2f, 10));
            Assert.Equal(0.5f, VehicleOpenings.Progress(OpeningState.Opening, 10, 2f, 11), 4);
            Assert.Equal(1f, VehicleOpenings.Progress(OpeningState.Opening, 10, 2f, 99));
            Assert.Equal(1f, VehicleOpenings.Progress(OpeningState.Open, 10, 2f, 10));
            Assert.Equal(0.25f, VehicleOpenings.Progress(OpeningState.Closing, 10, 2f, 11.5), 4);
            Assert.Equal(1f, VehicleOpenings.Progress(OpeningState.Opening, 10, 0f, 10));
        }

        private void Ask(Entity v, Entity man, bool @out)
        {
            Assert.True(VehicleRequests.HeadOut(_w, v, man, @out));
            _w.Bus.SwapBuffers();
        }

        /// <summary>
        /// ⭐ VA-4 — a crewman's head out (a request to the arbiter): the HATCH serving his seat opens and is marked exposed, and the idle close never shuts it
        /// while he is out; head in closes it at once. A passenger's head out (his seat has the ramp, no hatch), a man not aboard and a vehicle whose list another
        /// node owns change nothing. His seat changing hands while he is out clears the mark, and the hatch then closes after the hold.
        /// 🔴 Red-proof: drop <c>!CrewExposed</c> from the idle close ⇒ the hatch shuts 3.5 s after he put his head out ⇒ red.
        /// </summary>
        [Fact]
        public void CE4670_ACrewmansHeadOut_HoldsHisHatchOpen_UntilHeGoesIn()
        {
            var v = Vehicle(Ifv, SeatRole.Driver, SeatRole.Commander, SeatRole.Gunner, SeatRole.Passenger);
            var cdr = _w.CreateEntity(); var pax = _w.CreateEntity(); var stranger = _w.CreateEntity();
            Seat(v, 1, cdr); Seat(v, 3, pax);
            Step();                                                        // first sight
            Ask(v, cdr, true); Step();                                     // t = 1.0
            Assert.Equal(OpeningState.Opening, Slot(v, 2).State);         // id 3, the commander's hatch
            Assert.True(Slot(v, 2).CrewExposed);
            Assert.Equal(OpeningState.Closed, Slot(v, 0).State);           // the ramp stays up
            Assert.Equal(OpeningState.Closed, Slot(v, 1).State);           // the driver's hatch too
            for (int i = 0; i < 20; i++) Step();                           // t = 11: far past 1.5 + 2
            Assert.Equal(OpeningState.Opening, Slot(v, 2).State);
            Assert.Equal(1f, _w.GetComponentRO<VehicleOpenings>(v).ProgressAt(2, 1.5f, _t));

            Ask(v, pax, true); Step();                                     // a passenger: the ramp is no hatch
            Ask(v, stranger, true); Step();                                // not aboard
            Assert.Equal(OpeningState.Closed, Slot(v, 0).State);
            Assert.Equal(1L, _sys.Requests);

            Ask(v, cdr, false); Step();                                    // head in
            Assert.Equal(OpeningState.Closing, Slot(v, 2).State);
            Assert.Equal(_t, Slot(v, 2).Since);
            Assert.False(Slot(v, 2).CrewExposed);
            Assert.Equal(2L, _sys.Requests);

            Ask(v, cdr, true); Step();                                     // out again — and then he dismounts
            Assert.True(Slot(v, 2).CrewExposed);
            Seat(v, 1, Entity.Null); Step();
            Assert.False(Slot(v, 2).CrewExposed);
            for (int i = 0; i < 8; i++) Step();                            // 4 s idle > 1.5 + 2
            Assert.Equal(OpeningState.Closing, Slot(v, 2).State);

            var theirs = Vehicle(Ifv, SeatRole.Commander);
            _w.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var c2 = _w.CreateEntity();
            Seat(theirs, 0, c2);
            Ask(theirs, c2, true); Step();
            Assert.False(_w.HasComponent<VehicleOpenings>(theirs));      // the transport carries it to its arbiter
        }
    }
}
