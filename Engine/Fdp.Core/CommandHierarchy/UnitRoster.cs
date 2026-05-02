using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// ECS component placed on <em>commanding</em> entities.  Carries a fixed-capacity
    /// list of subordinate entity handles and their tactical designations.
    ///
    /// <para>
    /// This component is <b>not saved</b> (<c>DataPolicy.NoSave</c>) because it is entirely
    /// derived from the bottom-up <see cref="UnitSubordinate"/> records and is rebuilt on
    /// every scenario load.
    /// </para>
    ///
    /// <para>
    /// Capacity: <see cref="Capacity"/> (16).  Insertion order is preserved.
    /// Overflow is rejected by <c>UnitHierarchySystem</c> with a diagnostic warning.
    /// </para>
    ///
    /// <para>
    /// Size: 168 bytes -- <c>int Count</c> (4 B) + 4 B alignment pad + <c>long[16]</c> (128 B)
    /// + <c>ushort[16]</c> (32 B).
    /// </para>
    /// </summary>
    [DataPolicy(DataPolicy.NoSave)]
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.UnitRoster)]
    public unsafe struct UnitRoster
    {
        /// <summary>Maximum number of subordinates this roster can hold.</summary>
        public const int Capacity = 16;

        /// <summary>Number of currently registered subordinates (0–<see cref="Capacity"/>).</summary>
        public int Count;

        /// <summary>
        /// Packed entity handles (<c>Entity.PackedValue</c>) for each subordinate.
        /// Parallel with <see cref="TacticalDesignations"/>.
        /// </summary>
        public fixed long SubordinateEntities[Capacity];

        /// <summary>
        /// Tactical designation for each subordinate.
        /// Parallel with <see cref="SubordinateEntities"/>.
        /// </summary>
        public fixed ushort TacticalDesignations[Capacity];
    }
}
