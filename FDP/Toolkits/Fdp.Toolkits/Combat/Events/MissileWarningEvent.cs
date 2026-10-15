using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Events
{
    // ⚠ Its own file: the DDS code generator emits one IDL scope per source file.

    /// <summary>
    /// ⭐⭐ <c>CE-2189</c> (<c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3c, Air Ops §7d AD4) — a guided missile was
    /// launched at the aircraft <see cref="Unit"/>. Published by <c>FireProcessingSystem</c> where the round is born (the SimHost), carried
    /// to the aircraft's Brain (DDS <c>MissileWarning</c>, the near-miss pattern), where <c>MissileWarningSensingSystem</c> turns it into
    /// <c>SensorChange.MissileWarning</c> and the <c>MissileWarning</c> component (bearing, seeker, time to impact).
    /// </summary>
    [EventId(CombatConstants.MissileWarningEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MissileWarningEvent
    {
        /// <summary>The aircraft the missile was launched at.</summary>
        public Entity Unit;

        /// <summary>Where the missile was launched from (metres, Sim Z-up).</summary>
        public float FromX, FromY, FromZ;

        /// <summary>Its seeker (<c>GuidanceKind</c>, as a byte).</summary>
        public byte Seeker;

        /// <summary>The flight time to the aircraft at launch (s): the distance over the missile's speed.</summary>
        public float TimeToImpactS;

        /// <summary>True when this event arrived from another node (ingress).</summary>
        [MarshalAs(UnmanagedType.I1)] public bool IsRemote;
    }
}
