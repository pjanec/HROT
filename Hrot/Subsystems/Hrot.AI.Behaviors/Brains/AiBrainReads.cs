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
        /// <remarks>⭐ CE-3448 — also the rules' tree (<see cref="Objectives.RuleTree.PrepareFrame"/>), an index the rule brains read: refreshed serially
        /// before the phase. It used to be contributed by <c>CgfLogicPack</c> only, so a host that loads the AI assembly without that pack (the rule rails)
        /// ran the phase with the tree never prepared; here every host gets it by construction.</remarks>
        public static void Contribute() => Fdp.Toolkit.Behavior.BrainPhase.Contribute(Key, registerTypes: null,
            prepareFrame: Objectives.RuleTree.PrepareFrame, declareReads: Declare);

        /// <summary>Declares the set (idempotent).</summary>
        public static void Declare()
        {
            // measured by the drills under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1: a squad play's HSM action reads a member's route
            ParallelReadCheck.DeclareManaged<Hrot.Map.Common.Components.RoutePlan>();
            // ⭐ CE-3448 — the rules (Objectives/*): measured by the rule rails under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=2 (the survey). Every rule
            //   writes its OWN status in the phase, so a parent, a trigger's condition, a Best / SelectionMean or a verb's target check reads another
            //   rule's from the tick-start view — the one-tick lag §18.2c names
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.ScenarioRules.ObjectiveLinks>();        // SelectionMean.IsStandard — a child's parent / subject
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.ScenarioRules.RuleControl>();           // RuleCommands.Control — the target's last request seq
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.ScenarioRules.ExerciseResult>();        // ObjectiveHost.Enter — the exercise phase on the World Environment
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Scenario.EntityGuid>();                 // MemberEvaluation — a member's guid
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Scenario.MacroExpansionStatus>();       // InsertMacro — the expansion's state
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Scenario.RefKey>();                     // MemberEvaluation.Names — a member's key
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.SkillAmplifier>();        // SetSkillAmplifier — the verdict's read of the amplifier
            ParallelReadCheck.DeclareManaged<Fdp.Toolkit.Behavior.Components.ActiveMissionPlan>(); // MissionCommand — the unit's plan
        }
    }
}
