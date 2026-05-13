using System.Collections.Generic;
using GizmoMap.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Hub
{
    // Thread-safe multiplexer implementing IGizmoUiStatePublisher.
    // Maintains a list of endpoint publishers and broadcasts each Publish() call
    // to all registered endpoints under a snapshot-copy pattern to prevent
    // InvalidOperationException during concurrent modification.
    //
    // Design: DESIGN.md §2.2
    public sealed class GizmoUiStateHub : IGizmoUiStatePublisher
    {
        private readonly object _lock = new();
        private readonly List<IGizmoUiStatePublisher> _endpoints = new();

        // Registers an additional endpoint. Thread-safe.
        public void AddEndpoint(IGizmoUiStatePublisher endpoint)
        {
            lock (_lock)
                _endpoints.Add(endpoint);
        }

        // Removes a previously registered endpoint. Thread-safe. No-op if not found.
        public void RemoveEndpoint(IGizmoUiStatePublisher endpoint)
        {
            lock (_lock)
                _endpoints.Remove(endpoint);
        }

        // Broadcasts the state to all registered endpoints.
        // Copies the list under the lock, then iterates the copy outside the lock
        // to prevent deadlock if an endpoint calls back into the hub.
        public void Publish(GizmoUiState state)
        {
            IGizmoUiStatePublisher[] snapshot;
            lock (_lock)
            {
                if (_endpoints.Count == 0) return;
                snapshot = _endpoints.ToArray();
            }
            foreach (var ep in snapshot)
                ep.Publish(state);
        }
    }
}
