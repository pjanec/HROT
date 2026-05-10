using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Hrot.Common.Events;
using Hrot.Common.Systems;

namespace Hrot.Common.Interactions
{
    // Encapsulates the gizmo interaction pipeline in a dedicated IEcsModule so the
    // kernel calls Tick() after all simulation phases (Input, Simulation, PostSimulation).
    // This ensures gizmo projectors see finalized SimTransform positions before the
    // interaction FSMs run.
    //
    // Execution order inside Tick():
    //   1. gizmoIngress (optional) -- reads DDS GizmoInteractionBatch, writes to _interactionBus
    //   2. _interactionBus.SwapBuffers() -- makes ingress events readable
    //   3. contextIngress (optional) -- translates managed ContextActionTriggered (from
    //      _interactionBus) into GlobalActionRequestedEvent on _interactionBus; the
    //      1-frame delay before step 5 is imperceptible for UI clicks
    //   4. interactionSystems -- dispatch actions, drive gizmo FSMs
    //   5. gizmoEgress (optional) -- reads _interactionBus, writes DDS GizmoInteractionBatch
    //
    // The bus is injected directly into systems that need it (DataDrivenGizmoSystem,
    // GlobalGizmoManager, ContextActionIngressSystem, GlobalActionDispatchSystem)
    // so they read from the isolated bus without any view interception.
    public sealed class GizmoInteractionModule : IEcsModule
    {
        public string Name => "GizmoInteraction";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly FdpEventBus _interactionBus;
        private readonly IEcsModuleSystem? _contextIngress;
        private readonly IEcsModuleSystem[] _interactionSystems;
        private readonly CycloneNetworkIngressSystem? _gizmoIngress;
        private readonly CycloneEgressSystem? _gizmoEgress;

        public GizmoInteractionModule(
            FdpEventBus interactionBus,
            IEcsModuleSystem? contextIngress,
            IEcsModuleSystem[] interactionSystems,
            CycloneNetworkIngressSystem? gizmoIngress = null,
            CycloneEgressSystem? gizmoEgress = null)
        {
            _interactionBus     = interactionBus     ?? throw new System.ArgumentNullException(nameof(interactionBus));
            _interactionSystems = interactionSystems ?? throw new System.ArgumentNullException(nameof(interactionSystems));
            _contextIngress     = contextIngress;
            _gizmoIngress       = gizmoIngress;
            _gizmoEgress        = gizmoEgress;

            // Pre-register unmanaged event types on the isolated bus so Read<T>() never
            // returns an empty span due to missing stream registration.
            _interactionBus.Register<GizmoDragUpdateEvent>();
            _interactionBus.Register<GizmoMouseEvent>();
            _interactionBus.Register<GizmoKeyEvent>();
            _interactionBus.Register<GizmoInteractionStartedEvent>();
            _interactionBus.Register<GizmoInteractionCommitEvent>();
            _interactionBus.Register<GizmoInteractionCancelEvent>();
            _interactionBus.Register<GlobalActionRequestedEvent>();
        }

        // RegisterSystems is intentionally empty: all systems run manually inside Tick()
        // with the raw view; interaction events are isolated via explicit bus injection.
        public void RegisterSystems(ISystemRegistry registry) { }

        public void Tick(ISimulationView view, float deltaTime)
        {
            // 1. Network Ingress (headless only): reads DDS and writes directly to _interactionBus.
            _gizmoIngress?.Execute(view, deltaTime);

            // 2. Advance the isolated bus so ingress events become readable.
            _interactionBus.SwapBuffers();

            // 3. Translate managed context actions into typed bus events.
            //    ContextActionIngressSystem reads ContextActionTriggered from _interactionBus
            //    (injected at construction) and publishes GlobalActionRequestedEvent.
            //    The 1-frame delay to step 4 is imperceptible for UI clicks.
            _contextIngress?.Execute(view, deltaTime);

            // 4. Dispatch actions and drive gizmo FSMs.
            //    Systems read from _interactionBus directly (injected at construction).
            foreach (var sys in _interactionSystems)
                sys.Execute(view, deltaTime);

            // 5. Network Egress (non-headless IG with DDS): reads _interactionBus, sends to DDS.
            _gizmoEgress?.Execute(view, deltaTime);
        }
    }
}
