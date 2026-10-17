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

        /// <summary>⭐ Airborne insertion B-5 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §4) — the fast ropes a helicopter drops: one
        /// per door, in the carrier's frame (x forward, y left, z up from its reference point). Empty ⇒ no ropes (a roped exit then uses one
        /// at each side door, <c>RopeSlideModel.DefaultAnchor</c>).</summary>
        public List<RopeAnchorDto> RopeAnchors { get; init; } = new();

        /// <summary>⭐ Airborne insertion §4 — the ropes' length (m): a Rope hold higher than this above the surface is refused (B-3).</summary>
        public float RopeLengthM { get; init; }

        /// <summary>⭐ <c>CE-2152</c> — a soldier who can carry or drag one casualty: one open seat, the casualty survives his fall.</summary>
        /// <remarks>⭐ Weapon systems WS-2 (R-2016 W-G, DESIGN_Weapon_Systems §5) — and one CARRY STATION (a <see cref="SeatRole.Station"/>
        /// accepting <see cref="MountKinds.Carried"/>): the portable weapon system he carries, beside the casualty seat.</remarks>
        /// <remarks>⭐ <c>CE-3233</c> (civilians B-11, AW-13; docs/DESIGN_Civilians_And_Crowd_Control.md §7.13 ST-1/ST-2) — and two more
        /// carry stations APPENDED (seat 1 stays the HANDS, which presets name by index): seat 2 the BACK (<see cref="SeatDto.Stowed"/>: carried,
        /// seen, not fired from) and seat 3 the HIDDEN station (stowed, <see cref="SeatDto.Concealed"/> and not exposed: nobody sees it, it
        /// arms nobody, it is not sensed or hit).</remarks>
        public static CarrierDefDto PersonCarrier() => new()
        {
            Seats =
            {
                new SeatDto(),
                new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Carried },                                          // 1 the hands
                new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Carried, Stowed = true },                           // 2 the back
                new SeatDto { Role = SeatRole.Station, Accepts = MountKinds.Carried, Stowed = true, Concealed = true, Exposed = false },   // 3 hidden
            },
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
        /// <summary>⭐ Team carry T-a (R-2017 T3) — top-speed factor while two men carry a stretcher.</summary>
        public float StretcherSpeedFactor { get; init; } = 0.6f;
        /// <summary>⭐ Team carry T-a (R-2017 T3) — the rear handle: how far behind the carrier his assistant walks (m). A stretcher's casualty
        /// lies half way between them.</summary>
        public float AssistantBehindMetres { get; init; } = 2f;
    }

    /// <summary>One seat of a <see cref="CarrierDefDto"/>.</summary>
    public record SeatDto
    {
        /// <summary>Who the seat serves.</summary>
        public SeatRole Role { get; init; }

        /// <summary>Overrides the type's <see cref="CarrierDefDto.Exposure"/> for this seat (a gunner's open hatch); null = the type's.</summary>
        public bool? Exposed { get; init; }

        /// <summary>⭐ CE-1083 (docs/DESIGN_Map_3D_Mode.md §6m C-3) — where the rider SITS (his hips), metres from the vehicle's reference
        /// point in its own frame (x forward, y left, z up; an aircraft's is its CG). Read only by the 3-D map: an exposed rider is drawn
        /// there. All zero = no place (he is drawn at the reference point, as before).</summary>
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }

        /// <summary>⭐ CE-1083 — which way the rider faces, degrees counter-clockwise from the vehicle's forward (90 = out of a left door).</summary>
        public float FacingDegrees { get; init; }

        /// <summary>⭐ CE-1083 — how he sits there.</summary>
        public SeatPose Pose { get; init; }

        /// <summary>True when the seat declares a place.</summary>
        public bool HasPlace => X != 0f || Y != 0f || Z != 0f;

        // ⭐⭐ Weapon systems WS-1 (R-2016, docs/designs/weapons/DESIGN_Weapon_Systems.md §3, §5) — a STATION seat holds a weapon system.

        /// <summary>A <see cref="SeatRole.Station"/>'s mount kinds: a weapon boards it only when the weapon's <c>MountKinds</c> share one.</summary>
        public MountKinds Accepts { get; init; }

        /// <summary>A station's operating seat: the index of the host's seat whose occupant works the weapon (its Gunner); −1 = the host
        /// itself (W-H: the host's gunner seat operates its station weapon).</summary>
        public int OperatedBy { get; init; } = -1;

        /// <summary>A station's weapon fires only while its <see cref="OperatedBy"/> seat is occupied (§6 <c>CanFire</c>).</summary>
        public bool NeedsGunner { get; init; }

        /// <summary>A station's own arc, degrees from the host's forward (counter-clockwise +); null = the weapon's own turret limits only.</summary>
        public float? TraverseMinDeg { get; init; }
        /// <inheritdoc cref="TraverseMinDeg"/>
        public float? TraverseMaxDeg { get; init; }

        /// <summary>A station's own elevation limits, degrees (up +); null = the weapon's own.</summary>
        public float? ElevationMinDeg { get; init; }
        /// <inheritdoc cref="ElevationMinDeg"/>
        public float? ElevationMaxDeg { get; init; }

        /// <summary>⭐ W-I (STEER_…_Crew_Presets) — a TKB type the seat starts with when the host is placed (<c>SeatPresets</c>, once, on the
        /// creating node): a weapon system for a <see cref="SeatRole.Station"/>, a soldier for a crew seat. 0 = empty.</summary>
        public long StartWith { get; init; }

        /// <summary>⭐ <c>CE-3233</c> (B-11, §7.13 ST-2) — a station whose weapon is CARRIED, not held: it cannot fire from here (a slung rifle,
        /// a pistol in a holster). It is still seen unless <see cref="Concealed"/>. Appended.</summary>
        public bool Stowed { get; init; }

        /// <summary>⭐ <c>CE-3233</c> (B-11, §7.13 ST-2/ST-6) — a station nobody sees into (under a coat, a suicide vest): what it holds arms
        /// nobody (R-2025), and a carrier's free-station search never fills it unasked (ST-3). Appended.</summary>
        public bool Concealed { get; init; }
    }

    /// <summary>⭐ CE-1083 — how a rider sits in a seat (the 3-D map's figure).</summary>
    public enum SeatPose : byte
    {
        /// <summary>On a bench, knees bent, feet on the floor.</summary>
        Sit = 0,
        /// <summary>On a sill or a floor edge, legs hanging out over it (a helicopter's open door).</summary>
        SitLegsOut = 1,
        /// <summary>⭐ CE-1088 (Hrot docs/DESIGN_Map_3D_Mode.md §6n N-2) — STANDING at a weapon, hands on its grips (a pickup's pintle gunner);
        /// the seat's place is where his FEET are.</summary>
        Stand = 2,
        /// <summary>⭐ CE-1088 — KNEELING at a weapon, hands on its grips; the seat's place is where he kneels.</summary>
        Kneel = 3,
    }
}
