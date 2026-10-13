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
