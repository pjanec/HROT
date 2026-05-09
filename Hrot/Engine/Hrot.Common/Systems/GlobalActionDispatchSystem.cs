using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Events;
using Hrot.Common.Interactions;

namespace Hrot.Common.Systems
{
    // Reads GlobalActionRequestedEvent events produced by ContextActionIngressSystem
    // (or any other producer) and dispatches them to handlers registered in the
    // GlobalActionRegistry.  Runs in the Input phase so handlers execute before
    // Simulation systems that depend on their side-effects.
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class GlobalActionDispatchSystem : IEcsModuleSystem
    {
        private readonly GlobalActionRegistry _registry;

        public GlobalActionDispatchSystem(GlobalActionRegistry registry)
        {
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            foreach (ref readonly var evt in view.ReadEvents<GlobalActionRequestedEvent>())
            {
                if (_registry.TryGetHandler(evt.ActionId, out var handler))
                    handler(view, evt.Target);
            }
        }
    }
}
