using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2151</c> B-b (R-2009 B4 / B6, docs/DESIGN_Entity_Interactions.md §7.4) — a slot IS a seat: a boarding asks for a role and
    /// takes the first free seat of it, a crew seat refuses a plain boarding, leaving empties the seat in place, and the passenger rides as
    /// exposed as its SEAT.
    /// </summary>
    public sealed class CarrierSeatsTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public CarrierSeatsTests()
        {
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<CarrierSeats>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterEvent<EmbarkEntityCommand>();
            _world.RegisterEvent<DisembarkEntityCommand>();
            _world.RegisterEvent<EjectPassengersCommand>();
        }

        public void Dispose() => _world.Dispose();

        /// <summary>Driver (closed), gunner (open hatch), two passengers (closed).</summary>
        private Entity Ifv()
        {
            var dto = new CarrierDefDto();
            dto.Seats.Add(new SeatDto { Role = SeatRole.Driver });
            dto.Seats.Add(new SeatDto { Role = SeatRole.Gunner, Exposed = true });
            dto.Seats.Add(new SeatDto());
            dto.Seats.Add(new SeatDto());
            var t = new TkbTemplate("Ifv", 991L);
            t.AddDescriptor(dto);
            var e = _world.CreateEntity();
            new CarrierTkbTranslator().Inject(_world, e, t);
            return e;
        }

        private Entity SeatOf(Entity vehicle, int seat) => _world.GetComponentRO<PassengerBuffer>(vehicle).Passengers[seat];

        /// <summary>⭐ B4 — a plain boarding takes the first PASSENGER seat (2), never a crew seat; a crew request takes its own seat; a
        /// Passenger seat takes anyone, so a crew request whose seat is taken (or absent) rides in back; with the passenger seats full too it
        /// is Full. ✅ Red-proofs: FreeSeat ignoring the role ⇒ the plain boarding lands in seat 0 ⇒ red · no passenger-seat fallback for a
        /// crew request ⇒ the spare driver is Full ⇒ red.</summary>
        [Fact]
        public void ABoarding_TakesTheFirstFreeSeatOfItsRole()
        {
            var ifv = Ifv();
            var rifleman = _world.CreateEntity();
            var driver = _world.CreateEntity();
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, rifleman, ifv));
            Assert.Equal(rifleman, SeatOf(ifv, 2));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, driver, ifv, SeatRole.Driver));
            Assert.Equal(driver, SeatOf(ifv, 0));

            var spareDriver = _world.CreateEntity();
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, spareDriver, ifv, SeatRole.Driver));   // driver seat taken ⇒ in back
            Assert.Equal(spareDriver, SeatOf(ifv, 3));
            Assert.Equal(EmbarkResult.Full, Embarkation.TryEmbark(_world, _world.CreateEntity(), ifv, SeatRole.Commander));   // no seat of its own, back full
            Assert.Equal(EmbarkResult.Full, Embarkation.TryEmbark(_world, _world.CreateEntity(), ifv));   // the free gunner seat refuses a plain boarding
            Assert.True(SeatOf(ifv, 1).IsNull);
            Assert.Equal(3, _world.GetComponentRO<PassengerBuffer>(ifv).Count);
            Assert.Equal((byte)2, _world.GetComponentRO<IsEmbarkedTag>(rifleman).Seat);
        }

        /// <summary>⭐ B4 — leaving empties the seat IN PLACE: the passenger in the next seat keeps it, and the next boarding fills the hole.
        /// ✅ Red-proof: the old swap-remove ⇒ the second passenger moves into seat 2 ⇒ red.</summary>
        [Fact]
        public void Leaving_EmptiesTheSeatInPlace_TheNextBoardingFillsIt()
        {
            var ifv = Ifv();
            var a = _world.CreateEntity();
            var b = _world.CreateEntity();
            Embarkation.TryEmbark(_world, a, ifv);
            Embarkation.TryEmbark(_world, b, ifv);
            Assert.Equal(b, SeatOf(ifv, 3));

            Assert.True(Embarkation.Disembark(_world, a, ifv));
            Assert.True(SeatOf(ifv, 2).IsNull);
            Assert.Equal(b, SeatOf(ifv, 3));
            Assert.Equal(1, _world.GetComponentRO<PassengerBuffer>(ifv).Count);

            var c = _world.CreateEntity();
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, c, ifv));
            Assert.Equal(c, SeatOf(ifv, 2));
        }

        /// <summary>⭐ B6 — exposure is the SEAT's: the gunner's open hatch rides exposed, the closed cabin hidden — on the deciding node and
        /// on every node that derives the tag from the list (EmbarkedStateSystem).</summary>
        [Fact]
        public void APassenger_RidesAsExposedAsItsSeat()
        {
            var ifv = Ifv();
            var gunner = _world.CreateEntity();
            var rifleman = _world.CreateEntity();
            Embarkation.TryEmbark(_world, gunner, ifv, SeatRole.Gunner);
            Embarkation.TryEmbark(_world, rifleman, ifv);
            Assert.Equal((byte)1, _world.GetComponentRO<IsEmbarkedTag>(gunner).Exposed);
            Assert.Equal((byte)0, _world.GetComponentRO<IsEmbarkedTag>(rifleman).Exposed);

            // a replica node holds the list but derives the tags itself
            _world.RemoveComponent<IsEmbarkedTag>(gunner);
            new EmbarkedStateSystem().Execute(_world, 0.016f);
            var tag = _world.GetComponentRO<IsEmbarkedTag>(gunner);
            Assert.Equal((byte)1, tag.Exposed);
            Assert.Equal((byte)1, tag.Seat);
        }

        /// <summary>Ejecting everyone walks every seat, holes included.</summary>
        [Fact]
        public void EjectAll_EmptiesEverySeat_HolesIncluded()
        {
            var ifv = Ifv();
            var driver = _world.CreateEntity();
            var a = _world.CreateEntity();
            Embarkation.TryEmbark(_world, driver, ifv, SeatRole.Driver);
            Embarkation.TryEmbark(_world, a, ifv);
            Assert.Equal(2, Embarkation.EjectAll(_world, ifv));
            Assert.Equal(0, _world.GetComponentRO<PassengerBuffer>(ifv).Count);
            Assert.False(_world.HasComponent<IsEmbarkedTag>(a));
            Assert.False(_world.HasComponent<IsEmbarkedTag>(driver));
        }
    }
}
