using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost
{
    /// <summary>
    /// ECS component registry for cognitive / Brain-tier components.
    ///
    /// <para>Registers: behavior state, locomotion and weapon channels, actor capability,
    /// BTree and HSM brain components, mission plan queue + adapter state, and the
    /// CQRS <see cref="NavigationIntent"/> command component.</para>
    ///
    /// <para>
    /// Components not registered here (e.g. geographic, network-replication) are owned
    /// by <c>HrotSharedComponentRegistry</c> and must not be duplicated.
    /// Call <c>HrotSharedComponentRegistry.RegisterAll</c> first.
    /// </para>
    /// </summary>
    public static class CognitiveComponentRegistry
    {
        /// <summary>
        /// Registers all cognitive simulation components into <paramref name="world"/>.
        /// </summary>
        public static void RegisterAll(EntityRepository world)
        {
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<SimTier>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            // ⭐ MOVED 2026-09-12 to CombatComponentRegistry (CE-259bf slice 2): ActorCapabilityState
            //   is stamped by BehaviorTkbTranslator alongside EntityInfo — which ALREADY lives in the
            //   combat registry — and is read by HealthApplicationSystem / DamageSystem, both of which
            //   SimHost runs via CombatModule. ⛔ PreviousCapabilities stays HERE: its only readers are
            //   CognitiveInterruptSystem (Brain) and the Stride animation reactor (design §3.9a).
            world.RegisterComponent<PreviousCapabilities>();
            // ⛔ O7c-② (2026-09-22): BrainBTreeState is RETIRED — the root tree cursor is an
            //    occurrence slot now (§31). Its id 29 stays RESERVED.
            // ⛔ P4-① (2026-09-22): Blackboard1024 is RETIRED. Its three tenants all left by a named
            //    decision — AiPrimitive working state to the Blueprint tier ladder (SLICE2), squad
            //    state to its own component (O1), and the HeavyDtoType overflow path was never
            //    adopted. 📄 DESIGN_Occurrence_Scoped_Storage.md §30.13.
            // ⛔ O7c-① (2026-09-22): BrainHsm64 is RETIRED — zero production attach sites, so its
            //   tick query could never match. 📄 DESIGN_Occurrence_Scoped_Storage.md §31.5.
            world.RegisterComponent<BrainHsm128>();
            // ⭐ MOVED 2026-09-12 to MissionComponentRegistry (CE-259bf slice 2) — ActiveMissionPlan
            //   already lives there, and MissionPlanQueue is the same tier's queue. SimHost READS it:
            //   EntityMissionIngressTranslator writes it over the wire and MissionPlanTranslator
            //   persists it (design §3.9a).
            // ⭐ MOVED 2026-09-12 to EmbarkationComponentRegistry (CE-259bf slice 3a): embarkation
            //   runtime state spans SimHost (GenesisMaterializationSystem), the Brain (EmbarkExecutor)
            //   and the Editor (EditorCargoSystem) — it belongs to no single role and was parked here.

            // CQRS navigation command — written by the Brain tier (MoveToExecutor)
            // and read by the Muscle tier (NavigationIntentBridgeSystem).
            world.RegisterComponent<NavigationIntent>();

            // BTree/HSM diagnostic tracing — opt-in 1024-byte ring buffers per entity,
            // plus the generic transient DebugState driving them, and the patch event.
            // ⚠ The ring buffers STAY: written only by TraceBufferLifecycleSystem (Brain), and their
            //   SimHost readers are extract-only translators gated on BehaviorState (design §3.9a).
            world.RegisterComponent<BTreeTraceWorkingMemory1024>();
            world.RegisterComponent<HsmTraceWorkingMemory1024>();
            // ⭐ DebugState + PatchDebugStateCommand MOVED 2026-09-12 to
            //   BehaviorDiagnosticsComponentRegistry — SimHost's OWN ToggleAiTrace action writes them.

            // Embarkation commands (edit-1/EDIT1-E001)
            // ⭐ Embark/Disembark commands MOVED with their components (see above).
            world.RegisterEvent<CognitiveInterruptEvent>();
            world.RegisterEvent<ClearBehaviorEvent>();
            world.RegisterEvent<BehaviorFinishedEvent>();
            world.RegisterEvent<AssignBehaviorHashEvent>();
            world.RegisterManagedEvent<AssignTacticalIntentEvent>();
            world.RegisterManagedEvent<AssignBehaviorEvent>();

            // ⭐⭐⭐ MOVED 2026-09-12 to PerceptionRoleComponentRegistry (CE-259bf, design §3.9a/§3.9b).
            //   The EQS trio + the raycast events are the PERCEPTION role's, not the Brain's:
            //   SimHostCapabilities.cs:79 registers EqsModule and EqsSolverSystem is SimHost's own.
            //   ⛔ They lived here under a "Brain-tier" comment, which is why SimHost — a node that
            //   runs NO cognitive system — had to call this registry to get its own role's components.
            //   ⚠ Both hosts now call PerceptionRoleComponentRegistry, so the registered set per host
            //   is unchanged by that move.
        }
    }
}
