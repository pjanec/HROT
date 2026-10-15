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
        /// <summary>The most seats a carrier declares — the list's and its wire topic's width. ⭐ F4 (Air Ops §7a, 🔒 user `2026-10-14`:
        /// "extend the wire capacity … No problem extending" — the list is sent only on change): 12, so the doors-off UH-60 keeps its 8
        /// riders and gains two door stations and two door gunners. Was 8.</summary>
        public const int Capacity = 12;

        /// <summary>⭐ F4 — the seats of a buffer whose type declares none (a test or hand-built world): the list's width before F4, so
        /// widening the list moved no undeclared vehicle's capacity.</summary>
        public const int UndeclaredSeats = 8;

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
        public uint Modes;   // ⭐ F4 — 2 bits × 12 seats (was a ushort for 8)

        /// <summary>The mode of seat <paramref name="seat"/>.</summary>
        public readonly CarryMode ModeOf(int seat) => (CarryMode)((Modes >> (2 * seat)) & 0b11);

        /// <summary>Sets the mode of seat <paramref name="seat"/>.</summary>
        public void SetMode(int seat, CarryMode mode)
            => Modes = (Modes & ~(0b11u << (2 * seat))) | (((uint)mode & 0b11u) << (2 * seat));

        /// <summary>⭐ Team carry T-a (R-2017 T1; docs/DESIGN_Entity_Interactions.md §7.5) — a PERSON carrier's second man: the one at the
        /// rear handle of a stretcher or of a two-man weapon. Null for every other load and for every vehicle. It lives in the LIST, so every
        /// node derives the assistant's <see cref="IsAssistingTag"/> from it and it crosses the wire with the list (appended). ⚠ Not saved
        /// with a scenario, as the carry mode is not.</summary>
        [ScenarioIgnore]
        public Entity Assistant;
    }

    /// <summary>⭐ <c>CE-2152</c> (R-274 C / R-2011 C2) — how a passenger rides: in a seat, or as a casualty a person CARRIES or DRAGS. One
    /// mechanism (embark); the mode picks the casualty's offset and the carrier's speed.</summary>
    public enum CarryMode : byte
    {
        Seated = 0,
        Carried = 1,
        Dragged = 2,
        /// <summary>⭐ Team carry T-a (R-2017 T2) — on a stretcher between two men: the carrier at the front, his named assistant
        /// (<see cref="PassengerBuffer.Assistant"/>) at the rear handle. The 2-bit seat field's free value — no wire shape changes.</summary>
        Stretcher = 3,
    }

    /// <summary>
    /// Inline array of <see cref="PassengerBuffer.Capacity"/> <see cref="Entity"/> slots for <see cref="PassengerBuffer"/>.
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

    /// <summary>Inline array of <see cref="PassengerBuffer.Capacity"/> <see cref="SeatRole"/> values for <see cref="CarrierSeats"/> (one per <see cref="PassengerBuffer"/> slot).</summary>
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
        public ushort ExposedMask;   // ⭐ F4 — 12 seats (was a byte)

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
        /// <summary>⭐ Team carry T-a (R-2017 T3) — top-speed factor while a stretcher is carried.</summary>
        public float StretcherSpeedFactor;
        /// <summary>⭐ Team carry T-a (R-2017 T3) — how far behind the carrier his assistant walks (m): the rear handle.</summary>
        public float AssistantBehindMetres;
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
        public ushort StationMask;   // ⭐ F4 — 12 seats (was a byte)

        /// <summary>The station data per seat (meaningful where <see cref="StationMask"/> has the bit).</summary>
        [ScenarioIgnore]
        public StationSeatSlots Seats;

        /// <summary>Is seat <paramref name="seat"/> a station?</summary>
        public readonly bool IsStation(int seat) => seat >= 0 && seat < PassengerBuffer.Capacity && (StationMask & (1 << seat)) != 0;
    }

    /// <summary>
    /// ⭐⭐ Weapon systems WS-4 (W-I, R-138; DESIGN_Weapon_Systems §4d, §6) — this host's seat presets (<c>SeatDto.StartWith</c>: the M2 in its
    /// station, the gunner in its seat) have been filled, once, on the node that created it. ⭐ SAVED with the scenario, so a load never
    /// fills them again — a seat the user emptied stays empty and no second gunner appears; a host loaded WITHOUT it (a scenario saved
    /// before the presets existed) is filled on load. Never replicated: only the creating node reads it.
    /// </summary>
    /// <summary>
    /// ⭐⭐ Team carry T-a (R-2017 T1; docs/DESIGN_Entity_Interactions.md §7.5) — this man is the SECOND man of a team carry: the carrier
    /// <see cref="Carrier"/> names him as its <see cref="PassengerBuffer.Assistant"/>. Derived on every node from the carrier's list (as
    /// <see cref="IsEmbarkedTag"/> is from a vehicle's); while it stands his fire and his own movement are off (he is placed at the rear
    /// handle). Never saved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.IsAssistingTag)]
    [Fdp.Core.DataPolicy(Fdp.Core.DataPolicy.NoScenario)]
    public struct IsAssistingTag
    {
        /// <summary>The man carrying the load at the front.</summary>
        public Entity Carrier;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SeatPresetsFilled)]
    public struct SeatPresetsFilled
    {
        /// <summary>1 once filled.</summary>
        public byte Filled;
    }

    /// <summary>
    /// ⭐ WS-4 (W-I) — a preset passenger's place, written at its creation by <c>CreateEntityRequestSystem</c> and consumed by
    /// <c>SeatPresetSystem</c> the frame its host is there: board <see cref="Seat"/> of the host whose network id is
    /// <see cref="HostNetworkId"/>, then this is removed. Transient — the creating node only, never saved, never replicated.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PresetSeatIntent)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct PresetSeatIntent
    {
        public long HostNetworkId;
        public byte Seat;
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

    /// <summary>Inline array of <see cref="PassengerBuffer.Capacity"/> <see cref="StationSeat"/> values (one per <see cref="PassengerBuffer"/> slot).</summary>
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
