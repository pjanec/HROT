using System.Threading;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Reference-counted gate around a TogglablePostSimulationGroup that wraps the three
    // core gizmo systems. Enables the group at the first AddListener() call and disables
    // it (with synchronous teardown) when the count returns to zero via RemoveListener().
    //
    // Design: DESIGN.md §2.4 — "Synchronous Direct Teardown".
    // No FdpEventBus is involved. No pending/deferred flags.
    public sealed class GizmoExecutionController
    {
        private readonly TogglablePostSimulationGroup _group;
        private readonly GlobalGizmoManager _globalManager;
        private readonly DataDrivenGizmoSystem _dataDrivenSystem;
        private int _listenerCount;

        public int ListenerCount => _listenerCount;

        public GizmoExecutionController(
            TogglablePostSimulationGroup group,
            GlobalGizmoManager globalManager,
            DataDrivenGizmoSystem dataDrivenSystem)
        {
            _group            = group;
            _globalManager    = globalManager;
            _dataDrivenSystem = dataDrivenSystem;
        }

        // Increments the listener count. Enables the group when the count goes from 0 to 1.
        public void AddListener()
        {
            int count = Interlocked.Increment(ref _listenerCount);
            if (count == 1)
                _group.Enabled = true;
        }

        // Decrements the listener count. When it reaches zero:
        //   1. Calls CancelInteractiveTools() on both managers synchronously.
        //   2. Disables the group immediately.
        public void RemoveListener()
        {
            int count = Interlocked.Decrement(ref _listenerCount);
            if (count == 0)
            {
                _globalManager.CancelInteractiveTools();
                _dataDrivenSystem.CancelInteractiveTools();
                _group.Enabled = false;
            }
        }
    }
}
