using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Events
{
    // ⚠ Its own file: the DDS code generator emits one IDL scope per source file.

    /// <summary>
    /// ⭐ <c>CE-3064</c> (R-206) — a bullet passed within <see cref="CombatConstants.NearMissRadius"/> of <see cref="Unit"/> without
    /// being fired by its side: the unit is being SHOT AT. Published by <c>BallisticsSystem</c> where bullets fly (the Muscle),
    /// carried to the Brain (DDS <c>NearMiss</c>), where <c>NearMissSensingSystem</c> turns it into <c>SensorChange.NearMiss</c>.
    /// ⛔ No shooter: the unit knows it is fired upon and roughly where the round passed, not by whom.
    /// </summary>
    [EventId(CombatConstants.NearMissEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct NearMissEvent
    {
        /// <summary>The unit the round passed close to.</summary>
        public Entity Unit;

        /// <summary>The closest point of the round's path to the unit (metres, Sim Z-up).</summary>
        public float X, Y, Z;

        /// <summary>True when this event arrived from another node (ingress), not from local ballistics.</summary>
        [MarshalAs(UnmanagedType.I1)] public bool IsRemote;
    }
}
