namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ECS component IDs for Utility AI diagnostic components.
    /// IDs 149-150 are in the ModuleHost network block (140-159); these two slots
    /// were unallocated and have been reserved for Utility AI diagnostics.
    /// </summary>
    public static class UtilityApplicationComponentIds
    {
        /// <summary>
        /// <c>UtilityDebugFlags</c> — transient per-entity flags controlling Utility AI
        /// diagnostics (trace buffer enable). NoSave: discarded between sessions.
        /// </summary>
        public const int UtilityDebugFlags = 149;

        /// <summary>
        /// <c>UtilityTraceWorkingMemory1024</c> — 1024-byte unmanaged ring buffer of
        /// Utility AI scoring trace records. Opt-in via <see cref="UtilityDebugFlags"/>.
        /// NoSave: not persisted to scenario JSON.
        /// </summary>
        public const int UtilityTraceWorkingMemory = 150;

        /// <summary>
        /// <c>UtilityResultBuffer</c> — ranked output of a <c>UtilityScorer.Evaluate</c> call
        /// stored as an ECS component on the agent entity.
        /// NoSave: recomputed each frame.
        /// </summary>
        public const int UtilityResultBuffer = 151;
    }
}
