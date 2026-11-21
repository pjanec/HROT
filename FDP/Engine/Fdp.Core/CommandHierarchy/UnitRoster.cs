using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// ECS component placed on <em>commanding</em> entities.  Carries a fixed-capacity
    /// list of subordinate entity handles and their tactical designations.
    ///
    /// <para>
    /// This component is <b>not saved</b> (<c>DataPolicy.NoScenario</c>) because it is entirely
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
    /// Size: 164 bytes -- <c>int Count</c> (4 B) + <c>Entity[16]</c> (16 × 8 B, 4-aligned) + <c>ushort[16]</c> (32 B).
    /// </para>
    ///
    /// <para>
    /// ⭐ <c>CE-467</c> — both buffers are <c>[InlineArray]</c> fields marked <see cref="BlueprintCollectionFieldAttribute"/>, so
    /// <c>CollectionOpsGenerator</c> WRITES their read accessors (<c>UnitRosterSubordinateEntitiesOps</c>,
    /// <c>UnitRosterTacticalDesignationsOps</c>) and a blueprint loops the roster with the standard collection nodes —
    /// no hand-written helper. Read-only from blueprints: only <c>UnitHierarchySystem</c> maintains the roster.
    /// ⛔ HISTORY: they were raw <c>fixed long</c>/<c>fixed ushort</c> buffers (168 B), which the generator refuses
    /// (<c>FCOL002</c>), so a hand-written <c>UnitRosterOps</c> existed. 📄 <c>Architect_Question_78</c> §6.5.
    /// </para>
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.UnitRoster)]
    public struct UnitRoster
    {
        /// <summary>Maximum number of subordinates this roster can hold.</summary>
        public const int Capacity = 16;

        /// <summary>Number of currently registered subordinates (0–<see cref="Capacity"/>).</summary>
        public int Count;

        /// <summary>Inline storage for <see cref="SubordinateEntities"/>.</summary>
        [InlineArray(Capacity)]
        public struct EntityBuffer
        {
            private Entity _e0;
        }

        /// <summary>Inline storage for <see cref="TacticalDesignations"/>.</summary>
        [InlineArray(Capacity)]
        public struct DesignationBuffer
        {
            private ushort _e0;
        }

        /// <summary>
        /// The subordinate entities, in insertion order. Parallel with <see cref="TacticalDesignations"/>.
        /// Slots at or beyond <see cref="Count"/> are <see cref="Entity.Null"/>.
        /// </summary>
        [BlueprintCollectionField(nameof(Count), Access = CollectionAccess.ReadOnly)]
        public EntityBuffer SubordinateEntities;

        /// <summary>
        /// Tactical designation for each subordinate. Parallel with <see cref="SubordinateEntities"/>.
        /// </summary>
        [BlueprintCollectionField(nameof(Count), Access = CollectionAccess.ReadOnly)]
        public DesignationBuffer TacticalDesignations;

        // ── Mutation helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Appends a subordinate to the roster. Returns the 0-based slot index, or -1 if the roster is full.
        /// Does not throw on overflow.
        /// </summary>
        /// <param name="roster">The roster to append to.</param>
        /// <param name="entity">The subordinate.</param>
        /// <param name="designation">Optional tactical designation; defaults to 0.</param>
        /// <returns>The slot index written, or -1 when full.</returns>
        public static int Add(ref UnitRoster roster, Entity entity, ushort designation = 0)
        {
            if (roster.Count >= Capacity) return -1;
            int slot = roster.Count++;
            roster.SubordinateEntities[slot]  = entity;
            roster.TacticalDesignations[slot] = designation;
            return slot;
        }

        /// <summary>
        /// Returns the slot index of a subordinate, or -1 if not present.
        /// </summary>
        /// <param name="roster">The roster to search.</param>
        /// <param name="entity">The subordinate to look up.</param>
        /// <returns>The 0-based slot index, or -1 when not found.</returns>
        public static int IndexOf(ref UnitRoster roster, Entity entity)
        {
            for (int i = 0; i < roster.Count; i++)
                if (roster.SubordinateEntities[i] == entity) return i;
            return -1;
        }
    }
}
