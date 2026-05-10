using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Events;
using Hrot.Common.Interactions;

namespace Hrot.Common.Systems
{
    // Reads GlobalActionRequestedEvent events from the isolated interaction bus and
    // dispatches them to handlers registered in the GlobalActionRegistry.
    // The bus is injected at construction so this system is not coupled to any
    // particular view implementation.
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class GlobalActionDispatchSystem : IEcsModuleSystem
    {
        private readonly GlobalActionRegistry _registry;
        private readonly FdpEventBus _interactionBus;

        public GlobalActionDispatchSystem(GlobalActionRegistry registry, FdpEventBus interactionBus)
        {
            _registry       = registry       ?? throw new System.ArgumentNullException(nameof(registry));
            _interactionBus = interactionBus ?? throw new System.ArgumentNullException(nameof(interactionBus));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            foreach (ref readonly var evt in _interactionBus.Read<GlobalActionRequestedEvent>())
            {
                if (_registry.TryGetHandler(evt.ActionId, out var handler))
                    handler(view, evt.Target);
            }
        }
    }
}
