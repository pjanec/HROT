using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Interactions
{
    // Callback invoked by GlobalActionDispatchSystem when a GlobalActionRequestedEvent
    // arrives with a matching ActionId.
    public delegate void GlobalActionHandler(ISimulationView view, Entity target);

    // Composition-root owned registry that maps integer action IDs (see GlobalActionIds)
    // to application-level handler callbacks.  Register all handlers before the ECS
    // kernel is initialized; the registry is immutable during runtime.
    public sealed class GlobalActionRegistry
    {
        private readonly Dictionary<int, GlobalActionHandler> _handlers = new();

        // Registers <paramref name="handler"/> for <paramref name="actionId"/>.
        // Throws if the same ID is registered twice (catches accidental duplicates early).
        public void Register(int actionId, GlobalActionHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler, nameof(handler));
            if (!_handlers.TryAdd(actionId, handler))
                throw new InvalidOperationException(
                    $"GlobalActionRegistry: ActionId {actionId} is already registered.");
        }

        // Returns true and sets <paramref name="handler"/> when a handler exists for
        // <paramref name="actionId"/>.
        public bool TryGetHandler(int actionId, out GlobalActionHandler handler)
            => _handlers.TryGetValue(actionId, out handler!);
    }
}
