using System;
using System.Collections.Concurrent;
using GizmoMap.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Hub
{
    // In-memory bridge from backend publisher to local terminal consumer.
    // Uses overwrite-by-GizmoInstanceId semantics: last write wins per active schema.
    // Bounded memory: at most one entry per active gizmo instance.
    //
    // Design: DESIGN.md §2.3
    public sealed class LocalGizmoUiStateTransport : IGizmoUiStatePublisher
    {
        private readonly ConcurrentDictionary<uint, GizmoUiState> _pending = new();

        // Producer path: overwrites any existing entry for the same GizmoInstanceId.
        public void Publish(GizmoUiState state)
        {
            _pending[state.GizmoInstanceId] = state;
        }

        // Consumer path: delivers each pending state to the handler, then clears.
        // After this call the dictionary is empty (no double-delivery on the next poll).
        public void PollAndApply(Action<GizmoUiState> handler)
        {
            foreach (var kvp in _pending)
                handler(kvp.Value);
            _pending.Clear();
        }
    }
}
