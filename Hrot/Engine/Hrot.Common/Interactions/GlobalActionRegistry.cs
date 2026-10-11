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

        // ⭐ CE-3178 — a handler for THIS node's own clicks only: an action an operator starts here (a follow-up pick, an
        //   order issued from this station). A click relayed from another node (GlobalActionRequestedEvent.Relayed) does not
        //   run it — that node's own registry already did, so running it here too would issue the order twice and open a
        //   pick on a window nobody clicked.
        public void RegisterLocal(int actionId, GlobalActionHandler handler)
        {
            Register(actionId, handler);
            _localOnly.Add(actionId);
        }

        private readonly HashSet<int> _localOnly = new();

        // Returns true and sets <paramref name="handler"/> when a handler exists for
        // <paramref name="actionId"/>.
        public bool TryGetHandler(int actionId, out GlobalActionHandler handler)
            => _handlers.TryGetValue(actionId, out handler!);

        // ⭐ CE-3178 — as above, and false for a RegisterLocal action that was relayed from another node.
        public bool TryGetHandler(int actionId, bool relayed, out GlobalActionHandler handler)
        {
            if (relayed && _localOnly.Contains(actionId)) { handler = null!; return false; }
            return TryGetHandler(actionId, out handler);
        }
    }
}
