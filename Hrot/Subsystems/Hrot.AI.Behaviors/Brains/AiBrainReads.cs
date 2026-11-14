using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ CE-7035 (X-1; docs/DESIGN_Parallel_Simulation.md §18.2) — the types THIS assembly's brain code reads on another unit that the toolkit's
    /// list (<c>Fdp.Toolkit.Behavior.BrainReads</c>) cannot name: they live above the toolkit (<c>Hrot.Core</c>). Contributed to the brains' phase
    /// under ONE key by the one loader every host and test goes through (<c>CgfBehaviorSetup.LoadFromAiAssembly</c>), so the drills and the CGF
    /// node declare the same set by construction.
    /// </summary>
    public static class AiBrainReads
    {
        /// <summary>The phase key (a re-load replaces the entry).</summary>
        public const string Key = "Hrot.AI.Behaviors";

        /// <summary>Contributes the declarations to the brains' phase (idempotent).</summary>
        public static void Contribute() => Fdp.Toolkit.Behavior.BrainPhase.Contribute(Key, registerTypes: null, prepareFrame: null, declareReads: Declare);

        /// <summary>Declares the set (idempotent).</summary>
        public static void Declare()
        {
            // measured by the drills under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1: a squad play's HSM action reads a member's route
            ParallelReadCheck.DeclareManaged<Hrot.Map.Common.Components.RoutePlan>();
        }
    }
}
