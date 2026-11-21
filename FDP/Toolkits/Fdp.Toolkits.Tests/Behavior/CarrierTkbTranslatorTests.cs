using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2151</c> B-a (R-274 / R-2009, docs/DESIGN_Entity_Interactions.md §7.4) — carrying is a TYPE fact: only a type declaring
    /// <c>Gen.Carrier</c> gets a passenger list, its capacity is its passenger seats, and its exposure (moved from the vehicle parameters)
    /// stamps the open cabin.
    /// </summary>
    public sealed class CarrierTkbTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public CarrierTkbTranslatorTests()
        {
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<CarrierSeats>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<CarrierStations>();
        }

        public void Dispose() => _world.Dispose();

        private Entity Spawn(CarrierDefDto? carrier)
        {
            var t = new TkbTemplate("T", 990L);
            if (carrier != null) t.AddDescriptor(carrier);
            var e = _world.CreateEntity();
            new CarrierTkbTranslator().Inject(_world, e, t);
            return e;
        }

        /// <summary>⭐ Weapon systems WS-1 (R-2016, DESIGN_Weapon_Systems §3, §5) — a host's STATION seat is stamped with what it accepts, the
        /// seat that operates it, its arc (radians) and the place its weapon stands; it is no passenger seat (a plain boarding never takes
        /// it); a type without a station gets no <see cref="CarrierStations"/>.</summary>
        [Fact]
        public void WS1_AStationSeat_IsStamped_WithWhatItAccepts_WhoOperatesIt_ItsArcAndPlace()
        {
            var pickup = Spawn(new CarrierDefDto
            {
                Exposure = PassengerExposure.Open,
                Seats =
                {
                    new SeatDto { Role = SeatRole.Driver },
                    new SeatDto { Role = SeatRole.Gunner },
                    new SeatDto
                    {
                        Role = SeatRole.Station, Accepts = MountKinds.Pintle, OperatedBy = 1, NeedsGunner = true,
                        TraverseMinDeg = -150f, TraverseMaxDeg = 150f, X = -1.5f, Y = 0f, Z = 1.4f, FacingDegrees = 180f,
                    },
                },
            });
            var st = _world.GetComponentRO<CarrierStations>(pickup);
            Assert.Equal(0b100, st.StationMask);
            Assert.True(st.IsStation(2));
            Assert.False(st.IsStation(1));
            var s = st.Seats[2];
            Assert.Equal(((byte)MountKinds.Pintle, (sbyte)1, (byte)1, (byte)1), (s.Accepts, s.OperatedBy, s.NeedsGunner, s.HasArc));
            Assert.Equal(-150f * MathF.PI / 180f, s.TraverseMin, 4);
            Assert.Equal(MathF.PI / 2f, s.ElevationMax, 4);   // unstated ⇒ the full range
            Assert.Equal((-1.5f, 1.4f, MathF.PI), (s.X, s.Z, s.Facing));
            Assert.Equal(SeatRole.Station, _world.GetComponentRO<CarrierSeats>(pickup).Roles[2]);

            Assert.Equal(0, Embarkation.CapacityOf(_world, pickup));   // no passenger seat
            Assert.Equal(EmbarkResult.Full, Embarkation.TryEmbark(_world, _world.CreateEntity(), pickup));   // the station is not a seat for a man
            Assert.False(_world.HasComponent<CarrierStations>(Spawn(CarrierDefDto.PassengerSeats(4))));
        }

        /// <summary>⭐ R-2009 B3 — a type that does not declare Gen.Carrier (a soldier) carries nobody: no list, no seats, capacity 0.
        /// ✅ Red-proof: stamp the buffer unconditionally ⇒ red.</summary>
        [Fact]
        public void ATypeWithoutGenCarrier_GetsNoPassengerList()
        {
            var soldier = Spawn(null);
            Assert.False(_world.HasComponent<PassengerBuffer>(soldier));
            Assert.False(_world.HasComponent<CarrierSeats>(soldier));
            Assert.Equal(0, Embarkation.CapacityOf(_world, soldier));
        }

        /// <summary>⭐ R-2009 B1 — a 2-seat type takes two boardings and refuses the third (it used to take eight).
        /// ✅ Red-proof: TryEmbark reading PassengerBuffer.Capacity ⇒ red.</summary>
        [Fact]
        public void TheCapacityIsTheTypesPassengerSeats_TheThirdBoardingIsFull()
        {
            var car = Spawn(CarrierDefDto.PassengerSeats(2));
            Assert.Equal(2, Embarkation.CapacityOf(_world, car));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, _world.CreateEntity(), car));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, _world.CreateEntity(), car));
            Assert.Equal(EmbarkResult.Full, Embarkation.TryEmbark(_world, _world.CreateEntity(), car));
        }

        /// <summary>A crew seat is not a passenger seat: a plain boarding cannot take it (seats by role arrive with B-b).</summary>
        [Fact]
        public void ACrewSeat_DoesNotCountAsPassengerCapacity()
        {
            var dto = CarrierDefDto.PassengerSeats(1);
            dto.Seats.Insert(0, new SeatDto { Role = SeatRole.Driver });
            var truck = Spawn(dto);
            var seats = _world.GetComponentRO<CarrierSeats>(truck);
            Assert.Equal(2, seats.SeatCount);
            Assert.Equal(SeatRole.Driver, seats.Roles[0]);
            Assert.Equal(1, Embarkation.CapacityOf(_world, truck));
        }

        /// <summary>⭐ R-2009 B2 / B6 — an OPEN type stamps the open cabin and marks every seat exposed; an enclosed type stamps nothing, and
        /// a seat may override its type (a gunner's open hatch).</summary>
        [Fact]
        public void Exposure_IsTheTypes_ASeatMayOverrideIt()
        {
            var heli = Spawn(CarrierDefDto.PassengerSeats(3, PassengerExposure.Open));
            Assert.True(_world.HasComponent<OpenPassengerCabin>(heli));
            Assert.Equal(0b111, _world.GetComponentRO<CarrierSeats>(heli).ExposedMask);

            var dto = CarrierDefDto.PassengerSeats(2);
            dto.Seats.Add(new SeatDto { Role = SeatRole.Gunner, Exposed = true });
            var apc = Spawn(dto);
            Assert.False(_world.HasComponent<OpenPassengerCabin>(apc));
            Assert.Equal(0b100, _world.GetComponentRO<CarrierSeats>(apc).ExposedMask);
        }

        /// <summary>The seats are a type fact: a saved scenario's copy is overwritten from the TKB (its role array is not serialised).</summary>
        [Fact]
        public void TheSeats_AreAlwaysRewrittenFromTheType()
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new CarrierSeats { SeatCount = 5 });
            var t = new TkbTemplate("T", 990L);
            var dto = CarrierDefDto.PassengerSeats(1);
            dto.Seats.Insert(0, new SeatDto { Role = SeatRole.Driver });
            t.AddDescriptor(dto);
            new CarrierTkbTranslator().Inject(_world, e, t);
            var seats = _world.GetComponentRO<CarrierSeats>(e);
            Assert.Equal(2, seats.SeatCount);
            Assert.Equal(SeatRole.Driver, seats.Roles[0]);
        }

        /// <summary>Eight is the list's and the wire's maximum; a type declaring more fails loudly at spawn.</summary>
        [Fact]
        public void MoreThanEightSeats_IsRefused()   // ⭐ F4 — the list is 12 wide now: 12 seats spawn, 13 are refused
        {
            Spawn(CarrierDefDto.PassengerSeats(PassengerBuffer.Capacity));
            Assert.Throws<InvalidOperationException>(() => Spawn(CarrierDefDto.PassengerSeats(PassengerBuffer.Capacity + 1)));
        }
    }
}
