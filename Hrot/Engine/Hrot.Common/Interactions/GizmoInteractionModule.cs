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
    //   2. _interactionBus.SwapBuffers() -- makes new events readable
    //   3. Systems in order via InteractionScopedView
    //   4. gizmoEgress (optional)  -- reads _interactionBus, writes DDS GizmoInteractionBatch
    //
    // Non-headless wiring: DebugGizmoLayer publishes to _interactionBus; no DDS translators
    //   are passed (gizmoIngress = null, gizmoEgress = null) for SimHost, CGF, and Editor.
    //   IG in non-headless mode passes a gizmoEgress to forward local interactions to SimHost.
    //
    // Headless wiring: gizmoIngress receives remote-viewer interactions from DDS;
    //   gizmoEgress is null (re-broadcasting ingress events back would loop).
    public sealed class GizmoInteractionModule : IEcsModule
    {
        public string Name => "GizmoInteraction";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly FdpEventBus _interactionBus;
        private readonly IEcsModuleSystem[] _systems;
        private readonly CycloneNetworkIngressSystem? _gizmoIngress;
        private readonly CycloneEgressSystem? _gizmoEgress;

        public GizmoInteractionModule(
            FdpEventBus interactionBus,
            IEcsModuleSystem[] systems,
            CycloneNetworkIngressSystem? gizmoIngress = null,
            CycloneEgressSystem? gizmoEgress = null)
        {
            _interactionBus = interactionBus ?? throw new System.ArgumentNullException(nameof(interactionBus));
            _systems        = systems        ?? throw new System.ArgumentNullException(nameof(systems));
            _gizmoIngress   = gizmoIngress;
            _gizmoEgress    = gizmoEgress;

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
        // so they receive an InteractionScopedView rather than the raw EntityRepository.
        public void RegisterSystems(ISystemRegistry registry) { }

        public void Tick(ISimulationView view, float deltaTime)
        {
            // 1. Network Ingress (headless only): reads DDS and writes directly to _interactionBus.
            //    The ingress translator bypasses the global ECB entirely.
            _gizmoIngress?.Execute(view, deltaTime);

            // 2. Advance the isolated bus state so ingress events become readable.
            _interactionBus.SwapBuffers();

            var scopedView = new InteractionScopedView(view, _interactionBus);

            // 3. Run all gizmo interaction systems using the scoped view.
            foreach (var sys in _systems)
                sys.Execute(scopedView, deltaTime);

            // 4. Network Egress (non-headless IG with DDS): reads _interactionBus and sends to DDS.
            _gizmoEgress?.Execute(scopedView, deltaTime);
        }
    }
}
