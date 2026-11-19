using Fdp.Core;
namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ECS component IDs for Utility AI diagnostic components.
    /// IDs 149-150 are in the ModuleHost network block (140-159); these two slots
    /// were unallocated and have been reserved for Utility AI diagnostics.
    /// </summary>
    // ⛔ ALIASES ONLY (CE-4628 follow-up, user 2026-11-19: "all component ids ... written at one single place"): every id lives in
    //   Fdp.Core GlobalComponentIds (values unchanged); a NEW id is added THERE, never here. Rail: ComponentIdCatalogueTests.
    public static class UtilityApplicationComponentIds
    {
        /// <summary>
        /// <c>UtilityDebugFlags</c> — transient per-entity flags controlling Utility AI
        /// diagnostics (trace buffer enable). NoScenario: discarded between sessions.
        /// </summary>
        public const int UtilityDebugFlags = GlobalComponentIds.UtilityDebugFlags;

        /// <summary>
        /// <c>UtilityTraceWorkingMemory1024</c> — 1024-byte unmanaged ring buffer of
        /// Utility AI scoring trace records. Opt-in via <see cref="UtilityDebugFlags"/>.
        /// NoScenario: not persisted to scenario JSON.
        /// </summary>
        public const int UtilityTraceWorkingMemory = GlobalComponentIds.UtilityTraceWorkingMemory;

        /// <summary>
        /// <c>UtilityResultBuffer</c> — ranked output of a <c>UtilityScorer.Evaluate</c> call
        /// stored as an ECS component on the agent entity.
        /// NoScenario: recomputed each frame.
        /// </summary>
        public const int UtilityResultBuffer = GlobalComponentIds.UtilityResultBuffer;
    }
}
