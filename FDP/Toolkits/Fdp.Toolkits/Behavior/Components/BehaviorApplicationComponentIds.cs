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
    }
}
