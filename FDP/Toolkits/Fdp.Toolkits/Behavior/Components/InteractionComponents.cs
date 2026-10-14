using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// Fixed passenger roster on a vehicle entity.
    /// Managed by <c>EmbarkExecutor</c> (add) and <c>EjectPassengersExecutor</c> (clear).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PassengerBuffer)]
    public struct PassengerBuffer
    {
        public const int Capacity = 8;

        /// <summary>Inline array of <see cref="Entity"/> handles for passengers.</summary>
        /// <remarks>Handled by <c>PassengerBufferTranslator</c>; excluded from FdpAutoSerializer.</remarks>
        [ScenarioIgnore]
        public PassengerSlots Passengers;

        /// <summary>Number of passengers currently aboard (0–<see cref="Capacity"/>).</summary>
        public int Count;

        /// <summary>⭐ <c>CE-2152</c> C-a (R-2011 C5) — how each seat's occupant rides (<see cref="CarryMode"/>), 2 bits per seat (seat i at bits
        /// 2i..2i+1). It lives in the LIST, like the seat, so every node derives the passenger's <see cref="IsEmbarkedTag.Mode"/> from it and
        /// it crosses the wire with the list. ⚠ Not saved with a scenario (genesis boards seated), as the seat is not.</summary>
        [ScenarioIgnore]
        public ushort Modes;

        /// <summary>The mode of seat <paramref name="seat"/>.</summary>
        public readonly CarryMode ModeOf(int seat) => (CarryMode)((Modes >> (2 * seat)) & 0b11);

        /// <summary>Sets the mode of seat <paramref name="seat"/>.</summary>
        public void SetMode(int seat, CarryMode mode)
            => Modes = (ushort)((Modes & ~(0b11 << (2 * seat))) | (((int)mode & 0b11) << (2 * seat)));
    }

    /// <summary>⭐ <c>CE-2152</c> (R-274 C / R-2011 C2) — how a passenger rides: in a seat, or as a casualty a person CARRIES or DRAGS. One
    /// mechanism (embark); the mode picks the casualty's offset and the carrier's speed.</summary>
    public enum CarryMode : byte
    {
        Seated = 0,
        Carried = 1,
        Dragged = 2,
    }

    /// <summary>
    /// Inline array of 8 <see cref="Entity"/> slots for <see cref="PassengerBuffer"/>.
    /// </summary>
    [InlineArray(PassengerBuffer.Capacity)]
    public struct PassengerSlots
    {
#pragma warning disable IDE0044 // backing field required by InlineArray
        private Entity _element;
#pragma warning restore IDE0044
    }

    /// <summary>
    /// Tag component placed on a soldier entity that is currently embarked in a vehicle.
    /// Added by <c>EmbarkExecutor</c>; removed by <c>EjectPassengersExecutor</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.IsEmbarkedTag)]
    public struct IsEmbarkedTag
    {
        /// <summary>The vehicle entity this soldier is currently aboard.</summary>
        public Entity VehicleEntity;

        /// <summary>⭐ <c>CE-3110</c> I-2c (R-271) — 1 when the vehicle's cabin is OPEN (<see cref="OpenPassengerCabin"/>): the passenger
        /// is carried but still seen, targeted and hit; 0 (enclosed) hides it (<c>PhysicalPresence.IsHidden</c>). Set at boarding.</summary>
        public byte Exposed;

        /// <summary>⭐ <c>CE-2151</c> B-b (R-2009 B4) — the seat (the <see cref="PassengerBuffer"/> slot) it occupies. Set at boarding.</summary>
        public byte Seat;

        /// <summary>⭐ <c>CE-2152</c> C-a — how it rides (<see cref="CarryMode"/>), derived from its seat's <see cref="PassengerBuffer.Modes"/>.</summary>
        public CarryMode Mode;
    }

    /// <summary>
    /// ⭐ <c>CE-3110</c> I-2c (R-271) — a vehicle TYPE whose passengers stay exposed (a door-less helicopter, a pickup bed, a motorcycle).
    /// Stamped by <c>CarrierTkbTranslator</c> from <c>CarrierDefDto.Exposure == Open</c> (CE-2151; was <c>VehicleKinematicsTkbTranslator</c>); ⭐ ABSENT means
    /// enclosed (the <c>MobilityKill</c> precedent: a per-type opt-in exists only when it is not the default). Read once, at boarding.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.2a.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.OpenPassengerCabin)]
    public struct OpenPassengerCabin
    {
        /// <summary>Unused; FDP components carry at least one field.</summary>
        public byte Reserved;
    }

    /// <summary>⭐ <c>CE-2151</c> (R-2009) — the role a carrier's seat serves (<c>Gen.Carrier</c>). A <see cref="Passenger"/> seat takes anyone;
    /// a crew seat takes only a boarding that asks for its role (seats by slot: B-b). Crew stay abstract until a type needs them (§7.4 B5).</summary>
    public enum SeatRole : byte
    {
        Passenger = 0,
        Driver = 1,
        Gunner = 2,
        Commander = 3,
        /// <summary>⭐ Weapon systems WS-1 (R-2016) — holds a WEAPON SYSTEM (its <c>Accepts</c> must fit the weapon's mount kinds), never a
        /// person; a plain boarding never takes it.</summary>
        Station = 4,
    }

    /// <summary>Inline array of 8 <see cref="SeatRole"/> values for <see cref="CarrierSeats"/> (one per <see cref="PassengerBuffer"/> slot).</summary>
    [InlineArray(PassengerBuffer.Capacity)]
    public struct SeatRoleSlots
    {
#pragma warning disable IDE0044 // backing field required by InlineArray
        private SeatRole _element;
#pragma warning restore IDE0044
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2151</c> (R-274 / R-2009, docs/DESIGN_Entity_Interactions.md §7.4) — the seats a carrier TYPE declares (TKB <c>Gen.Carrier</c>),
    /// stamped with its <see cref="PassengerBuffer"/> by <c>CarrierTkbTranslator</c> on every node at spawn. ⭐ Only a declaring type carries:
    /// an entity without this component and buffer is not a vehicle anyone can board (soldiers were, before).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CarrierSeats)]
    public struct CarrierSeats
    {
        /// <summary>How many seats the type declares (0–<see cref="PassengerBuffer.Capacity"/>, the wire maximum).</summary>
        public byte SeatCount;

        /// <summary>Each seat's role, in declaration order.</summary>
        [ScenarioIgnore]
        public SeatRoleSlots Roles;

        /// <summary>Seats that ride exposed (bit i = seat i): the type's <c>Exposure</c>, overridden per seat.</summary>
        public byte ExposedMask;

        /// <summary>⭐ <c>CE-2152</c> (R-2011 C9) — 1 when the passengers SURVIVE the carrier's destruction (a person carrying a casualty:
        /// <c>Gen.Carrier.DiesWithCarrier = false</c>); 0, the default, kills them with it (a vehicle).</summary>
        public byte PassengersSurvive;

        /// <summary>How many seats take a plain boarding — the <see cref="SeatRole.Passenger"/> seats.</summary>
        public readonly int PassengerSeats
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SeatCount && i < PassengerBuffer.Capacity; i++)
                    if (Roles[i] == SeatRole.Passenger) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2152</c> C-a (R-274 C / R-2011 C1, docs/DESIGN_Entity_Interactions.md §7.4.8) — a PERSON who can carry or drag a casualty:
    /// stamped by <c>CarrierTkbTranslator</c> from <c>Gen.Carrier.Person</c>. A person carrier takes only an INCAPACITATED passenger
    /// (R-2009 C3), cannot fire while carrying (C7), slows by the mode's factor (C8) and holds the casualty at the mode's offset (C10).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PersonCarrier)]
    public struct PersonCarrier
    {
        /// <summary>Top-speed factor while carrying (on the shoulder).</summary>
        public float CarrySpeedFactor;
        /// <summary>Top-speed factor while dragging.</summary>
        public float DragSpeedFactor;
        /// <summary>How far behind the carrier a carried casualty is held (m, along the carrier's heading).</summary>
        public float CarryBehindMetres;
        /// <summary>How far behind the carrier a dragged casualty is held (m).</summary>
        public float DragBehindMetres;
    }

    /// <summary>
    /// ⭐⭐ Weapon systems WS-1 (R-2016, docs/designs/weapons/DESIGN_Weapon_Systems.md §3, §6) — a host type's STATION seats
    /// (<see cref="SeatRole.Station"/>), stamped by <c>CarrierTkbTranslator</c> from <c>Gen.Carrier</c> on every node beside
    /// <see cref="CarrierSeats"/>: what each station accepts, which seat operates it, its arc, and where the weapon stands. ⭐ A type
    /// fact (never saved, never replicated) — present only on a type that declares a station.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CarrierStations)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CarrierStations
    {
        /// <summary>Bit i = seat i is a station.</summary>
        public byte StationMask;

        /// <summary>The station data per seat (meaningful where <see cref="StationMask"/> has the bit).</summary>
        [ScenarioIgnore]
        public StationSeatSlots Seats;

        /// <summary>Is seat <paramref name="seat"/> a station?</summary>
        public readonly bool IsStation(int seat) => seat >= 0 && seat < PassengerBuffer.Capacity && (StationMask & (1 << seat)) != 0;
    }

    /// <summary>⭐ WS-1 — one station seat: a <see cref="CarrierStations"/> slot (angles in radians, the place in the host's frame).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StationSeat
    {
        /// <summary><c>Fdp.Toolkit.Tkb.Domain.MountKinds</c> it accepts.</summary>
        public byte Accepts;
        /// <summary>The seat whose occupant operates it; −1 = the host itself.</summary>
        public sbyte OperatedBy;
        /// <summary>1 when its weapon fires only while <see cref="OperatedBy"/> is occupied.</summary>
        public byte NeedsGunner;
        /// <summary>1 when it states its own arc (<see cref="TraverseMin"/>…<see cref="ElevationMax"/>).</summary>
        public byte HasArc;
        public float TraverseMin, TraverseMax, ElevationMin, ElevationMax;
        /// <summary>Where the weapon stands, metres in the host's frame (x forward, y left, z up), and which way it faces (radians, CCW).</summary>
        public float X, Y, Z, Facing;
    }

    /// <summary>Inline array of 8 <see cref="StationSeat"/> values (one per <see cref="PassengerBuffer"/> slot).</summary>
    [InlineArray(PassengerBuffer.Capacity)]
    public struct StationSeatSlots
    {
#pragma warning disable IDE0044 // backing field required by InlineArray
        private StationSeat _element;
#pragma warning restore IDE0044
    }

    /// <summary>⭐ <c>CE-3110</c> I-2c — a carrier type's passenger exposure in the TKB (<c>CarrierDefDto.Exposure</c> since CE-2151; was
    /// <c>VehicleParametersDto.PassengerExposure</c>).</summary>
    public enum PassengerExposure : byte
    {
        /// <summary>The default (and what an absent field means): passengers are hidden inside.</summary>
        Enclosed = 0,
        /// <summary>Passengers ride exposed: seen, targeted and hit like anyone.</summary>
        Open = 1,
    }
}
