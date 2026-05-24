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
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<Blackboard1024>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<MissionPlanQueue>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();

            // CQRS navigation command — written by the Brain tier (MoveToExecutor)
            // and read by the Muscle tier (NavigationIntentBridgeSystem).
            world.RegisterComponent<NavigationIntent>();

            // BTree/HSM diagnostic tracing — opt-in 1024-byte ring buffers per entity,
            // plus the generic transient DebugState driving them, and the patch event.
            world.RegisterComponent<BTreeTraceWorkingMemory1024>();
            world.RegisterComponent<HsmTraceWorkingMemory1024>();
            world.RegisterComponent<DebugState>();
            world.RegisterManagedEvent<PatchDebugStateCommand>();

            // Embarkation commands (edit-1/EDIT1-E001)
            world.RegisterEvent<EmbarkEntityCommand>();
            world.RegisterEvent<DisembarkEntityCommand>();
            world.RegisterEvent<CognitiveInterruptEvent>();
            world.RegisterEvent<ClearBehaviorEvent>();
            world.RegisterEvent<BehaviorFinishedEvent>();
            world.RegisterEvent<AssignBehaviorHashEvent>();
            world.RegisterManagedEvent<AssignTacticalIntentEvent>();
            world.RegisterManagedEvent<AssignBehaviorEvent>();

            // EQS Brain-tier components and update event.
            world.RegisterComponent<EqsSensor>();
            world.RegisterComponent<EqsCognitiveBuffer>();
            world.RegisterManagedEvent<EqsResultUpdateEvent>();

            // EQS Phase 5: per-sensor cross-tick evaluation state.
            world.RegisterComponent<SensorEvalState>();

            // EQS Phase 5: EqsSolverSystem submits RaycastRequestEvents via command buffer
            // playback.  RaycastSolverSystem (Combat/Input) resolves them and publishes
            // RaycastResultEvents.  Both must be registered in every world that hosts these
            // systems so that FdpEventBus.PublishRaw does not throw during harvest/flush.
            world.RegisterEvent<RaycastRequestEvent>();
            world.RegisterEvent<RaycastResultEvent>();
        }
    }
}
