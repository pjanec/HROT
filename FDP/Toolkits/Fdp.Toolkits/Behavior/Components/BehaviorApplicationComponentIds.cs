namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// Application-level ECS component IDs for FDP.Toolkit.Behavior managed components.
    /// These IDs fall in the 160-199 application-level descriptor block defined in
    /// GlobalComponentIds.cs but are declared here because the toolkit layer cannot
    /// reference project-specific ID files.
    /// </summary>
    public static class BehaviorApplicationComponentIds
    {
        /// <summary>
        /// <c>ActiveMissionPlan</c> — managed component holding the current active mission plan.
        /// ID 162 reuses the slot formerly occupied by the deleted <c>EntityMissionHolder</c>.
        /// </summary>
        public const int ActiveMissionPlan = 162;

        /// <summary>
        /// <c>BTreeTraceWorkingMemory1024</c> — 1024-byte unmanaged ring buffer of BTree
        /// execution trace records. Opt-in per entity via <see cref="DebugState"/>.
        /// </summary>
        public const int BTreeTraceWorkingMemory = 146;

        /// <summary>
        /// <c>HsmTraceWorkingMemory1024</c> — 1024-byte unmanaged ring buffer of HSM
        /// execution trace records. Opt-in per entity via <see cref="DebugState"/>.
        /// </summary>
        public const int HsmTraceWorkingMemory = 147;

        /// <summary>
        /// <c>DebugState</c> — transient component carrying generic debug bit-flags
        /// (one feature group per subsystem). FDP-level so tick systems can read it
        /// without forcing Fdp.Toolkits to reference Hrot.Common.
        /// </summary>
        public const int DebugState = 148;

        /// <summary>
        /// <c>BehaviorStartRecord</c> — CE-452: the name + parameter text the entity's current root behaviour was started
        /// with (transient). ⚠ 154 is the first of the "154–159 next free" block <c>GlobalComponentIds</c> names — measured
        /// free by a repo-wide search of FDP/Hrot/Stride on 2026-09-30; that comment (Fdp.Core) is not updated from here.
        /// </summary>
        public const int BehaviorStartRecord = 154;
    }
}
