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
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Animals.AnimalState>();                 // ⭐ CE-4649 part D (AN-21g) — a hunter asks whether a contact is game (Animals.MayEngage); every animal writes its own state in the phase ⇒ a tick-start read
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.UnderFireKnowledge>();         // ⭐ CE-2466 R4 — Firm() asks the side's UnderFire table (records written only by the SERIAL UnderFireWriter, never in this phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Ambient.Ambience>();                    // ⭐ CE-11034 — SetAmbience asks whether the layer sounds (written only serially: the creation path, the action's follow-up, the expiry system)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Scenario.EntityGuid>();                 // MemberEvaluation — a member's guid
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Scenario.MacroExpansionStatus>();       // InsertMacro — the expansion's state
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Scenario.RefKey>();                     // MemberEvaluation.Names — a member's key
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Terrain.Minefield>();               // ⭐ CE-4624 MS-B2 — a recon reads the field's parameters (nobody writes them in the phase)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Navigation.NavigationStatus>();      // ⭐ CE-2439 — a commander reads a member's route state (hill attack); nobody writes it in the phase
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Perception.Components.TargetMemory>();  // ⭐ CE-4621 MC-4 — a dash bike's lookout reads its riders' memory (last tick)
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.SkillAmplifier>();        // SetSkillAmplifier — the verdict's read of the amplifier
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Aggregate.ResolutionPolicy>();                // SetResolutionPolicy — the verb's read of the live policy
            // ⭐ CE-4622 K-4…K-6 — the checkpoint reads the vehicles on its approach and the people it searches (all written outside the phase: the mover,
            //   replication, custody, the follow-ups): a vehicle's speed and replicated motion, its stations (the concealed boot), its hostile act, custody
            ParallelReadCheck.DeclareUnmanaged<global::CarKinem.Core.VehicleState>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.NetworkVelocity>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.NetworkTransform>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.CarrierStations>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Civilians.HostileAct>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Civilians.Custody>();
            ParallelReadCheck.DeclareManaged<Fdp.Toolkit.Behavior.Components.ActiveMissionPlan>(); // MissionCommand — the unit's plan
            // ⭐ CE-3470 — a gunner's DeployWeapon reads the weapon system's deploy state; written only outside the phase (WeaponDeploySystem, and the
            //   gunner's own deploy as a follow-up) ⇒ a declared read, served from the tick-start view
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.WeaponSystemState>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Traffic.TrafficDemand>();   // ⭐ CE-9010 — a convoy lead asks whether SUMO traffic runs (nobody writes it in the phase)
            // ⭐ agg-behaviors ST-0 (design §23.7) — a commander's strength / fight readers (UnitStrength, CombatEffective, the census) read a STOCHASTIC member's
            //   resolution and slots; measured by the acceptance rail under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1. Written only by ST-3's phase B, never in the brain phase
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Aggregate.AggregateUnit>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Aggregate.UnitSlotTable>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Aggregate.UnitHoldings>();               // ⭐ ST-3 — a stochastic member's ammunition share (UnitAmmunition.TryShare)
        }
    }
}
