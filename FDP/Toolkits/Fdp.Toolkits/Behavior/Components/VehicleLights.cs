using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>⭐ <c>CE-4670</c> (VA-3) — the crew's light discipline: what the light rule may switch on.</summary>
    public enum LightDiscipline : byte
    {
        /// <summary>Road lights when dark, an aircraft's nav and anti-collision lights while its engine runs.</summary>
        Normal = 0,
        /// <summary>Only the convoy (blackout-drive) marker when dark; an aircraft keeps nav and anti-collision.</summary>
        Convoy = 1,
        /// <summary>Nothing lit.</summary>
        Blackout = 2,
        /// <summary>Switched off (parked, no crew decision).</summary>
        Off = 3,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (VA-S3, VA-3; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — a vehicle's or aircraft's lights: <see cref="OnMask"/>
    /// (bit i = light group i of its type, <c>VehicleLightsCatalog</c> order), the crew's <see cref="Discipline"/>, the time the mask last changed, and a searchlight's
    /// aim. Written by the vehicle's BRAIN group owner (<c>LightRuleSystem</c>: discipline × darkness × engine); ⛔ brake lights are never stored (derived from
    /// deceleration by a reader). Replicated (<c>dtVehicleLights</c> 244), recorded; ⭐ SAVED — an authored discipline (a convoy blacked out) is scenario data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.VehicleLights)]
    [DataPolicy(DataPolicy.Default)]
    public struct VehicleLights
    {
        /// <summary>Bit i = light group i is on.</summary>
        public uint OnMask;

        /// <summary>The crew's light discipline (authored, or ⏳ an SOP / order).</summary>
        public LightDiscipline Discipline;

        /// <summary>Sim time (s) the mask last changed.</summary>
        public double Since;

        /// <summary>A searchlight's azimuth / elevation relative to the hull (rad). ⏳ not driven yet.</summary>
        public float SearchAz, SearchEl;

        /// <summary>True when light group <paramref name="i"/> is on.</summary>
        public readonly bool IsOn(int i) => (uint)i < 32 && (OnMask & (1u << i)) != 0;
    }
}
