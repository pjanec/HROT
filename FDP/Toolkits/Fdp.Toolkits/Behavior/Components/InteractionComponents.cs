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
    /// Stamped by <c>VehicleKinematicsTkbTranslator</c> from <c>VehicleParametersDto.PassengerExposure == Open</c>; ⭐ ABSENT means
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

    /// <summary>⭐ <c>CE-3110</c> I-2c — a vehicle type's passenger exposure in the TKB (<c>VehicleParametersDto.PassengerExposure</c>).</summary>
    public enum PassengerExposure : byte
    {
        /// <summary>The default (and what an absent field means): passengers are hidden inside.</summary>
        Enclosed = 0,
        /// <summary>Passengers ride exposed: seen, targeted and hit like anyone.</summary>
        Open = 1,
    }
}
