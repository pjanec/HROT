using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat.Events
{
    // ⚠ Its own file: the DDS code generator emits one IDL scope per source file.

    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> (DESIGN_Missiles_And_Ammo §3c, AD3) — the BRAIN asks its aircraft to throw one salvo of flares or chaff
    /// (behaviors' CE-2191 decides when: a missile warning on an IR seeker's bearing). Carried to the SimHost across nodes (DDS
    /// <c>DispenseCountermeasure</c>, <c>CE-2189</c>'s translator pair); <c>CountermeasureSystem</c> spawns the decoys, or ignores it while
    /// the dispenser is empty or the interval has not passed.
    /// </summary>
    [EventId(CombatConstants.DispenseCountermeasureEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DispenseCountermeasureIntent
    {
        public Entity Unit;
        public DecoyKind Kind;
        /// <summary>True when it arrived over the wire (the egress does not echo it back).</summary>
        [MarshalAs(UnmanagedType.I1)] public bool IsRemote;
    }
}
