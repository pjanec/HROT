using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <c>CE-2151</c> (R-274 / R-2009, docs/DESIGN_Entity_Interactions.md §7.4) — a type that CARRIES: its seats and how exposed its
    /// passengers ride. 🔒 User, 2026-10-12: <i>"carrying capacity and seats (driver, gunner, passenger) belong to the vehicle type"</i>.
    /// </summary>
    /// <remarks>
    /// ⭐ Only a type with this descriptor carries — <c>CarrierTkbTranslator</c> stamps the <c>PassengerBuffer</c> and <c>CarrierSeats</c>
    /// from it; an entity without it is boarded by nobody. ⭐ One descriptor for every carrier, a vehicle or (R-274 C, after R-263's injury
    /// states) a person carrying a wounded comrade. ⚠ At most <see cref="PassengerBuffer.Capacity"/> seats — the wire's maximum.
    /// </remarks>
    [TkbDescriptor("Gen.Carrier")]
    public record CarrierDefDto
    {
        /// <summary>The seats, in slot order. A <see cref="SeatRole.Passenger"/> seat takes anyone; a crew seat only its role.</summary>
        public List<SeatDto> Seats { get; init; } = new();

        /// <summary>How the passengers ride: <c>Enclosed</c> (hidden — the default, and what absence means) or <c>Open</c> (a door-less
        /// helicopter, a pickup bed: seen, targeted and hit). Moved here from <c>VehicleParametersDto</c> (CE-2151, R-2009 B2).</summary>
        public PassengerExposure Exposure { get; init; }

        /// <summary>⭐ <c>CE-2152</c> (R-2011 C9) — whether the passengers die when the carrier is destroyed: true for a vehicle (the default),
        /// false for a person carrying a casualty, whose fall DROPS the casualty alive.</summary>
        public bool DiesWithCarrier { get; init; } = true;

        /// <summary>⭐ <c>CE-2152</c> (R-274 C / R-2011 C1) — set on a PERSON who can carry or drag a casualty: the speed factors and offsets.
        /// Null on a vehicle.</summary>
        public PersonCarryDto? Person { get; init; }

        /// <summary>⭐ <c>CE-2152</c> — a soldier who can carry or drag one casualty: one open seat, the casualty survives his fall.</summary>
        public static CarrierDefDto PersonCarrier() => new()
        {
            Seats = { new SeatDto() },
            Exposure = PassengerExposure.Open,
            DiesWithCarrier = false,
            Person = new PersonCarryDto(),
        };

        /// <summary>A type with <paramref name="passengers"/> plain passenger seats, enclosed.</summary>
        public static CarrierDefDto PassengerSeats(int passengers, PassengerExposure exposure = PassengerExposure.Enclosed)
        {
            var dto = new CarrierDefDto { Exposure = exposure };
            for (int i = 0; i < passengers; i++) dto.Seats.Add(new SeatDto());
            return dto;
        }
    }

    /// <summary>⭐ <c>CE-2152</c> (R-2011 C8 / C10) — how a person carries or drags a casualty.</summary>
    public record PersonCarryDto
    {
        /// <summary>Top-speed factor while carrying on the shoulder.</summary>
        public float CarrySpeedFactor { get; init; } = 0.5f;
        /// <summary>Top-speed factor while dragging.</summary>
        public float DragSpeedFactor { get; init; } = 0.3f;
        /// <summary>How far behind the carrier a carried casualty is held (m): on the shoulder.</summary>
        public float CarryBehindMetres { get; init; } = 0f;
        /// <summary>How far behind the carrier a dragged casualty is held (m).</summary>
        public float DragBehindMetres { get; init; } = 1f;
    }

    /// <summary>One seat of a <see cref="CarrierDefDto"/>.</summary>
    public record SeatDto
    {
        /// <summary>Who the seat serves.</summary>
        public SeatRole Role { get; init; }

        /// <summary>Overrides the type's <see cref="CarrierDefDto.Exposure"/> for this seat (a gunner's open hatch); null = the type's.</summary>
        public bool? Exposed { get; init; }
    }
}
