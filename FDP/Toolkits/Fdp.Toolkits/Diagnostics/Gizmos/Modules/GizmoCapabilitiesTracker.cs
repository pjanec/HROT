using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Modules
{
    // Tracks which remote terminal node IDs have announced via IGCapabilitiesAnnounce
    // and drives the GizmoExecutionController listener count accordingly.
    // Each unique node ID arriving alive increments the count exactly once.
    // Idempotent: a second alive sample from the same node ID is ignored.
    //
    // Design: DESIGN.md §5
    internal sealed class GizmoCapabilitiesTracker
    {
        private readonly GizmoExecutionController _controller;
        private readonly FdpEventBus _bus;
        private readonly HashSet<long> _connectedTerminalIds = new();

        public GizmoCapabilitiesTracker(GizmoExecutionController controller, FdpEventBus bus)
        {
            _controller = controller;
            _bus = bus;
        }

        // Called once per received IGCapabilitiesAnnounce sample.
        // isAlive: true when the sample is a normal data sample; false when the DDS
        // instance state indicates the remote writer has gone non-alive (disconnect/crash).
        public void OnSample(long nodeId, bool isAlive)
        {
            if (isAlive)
            {
                if (_connectedTerminalIds.Add(nodeId))
                {
                    _controller.AddListener();
                    _bus.PublishManaged(new TerminalConnectedEvent { TerminalId = nodeId });
                }
            }
            else
            {
                if (_connectedTerminalIds.Remove(nodeId))
                {
                    _controller.RemoveListener();
                    _bus.PublishManaged(new TerminalDisconnectedEvent { TerminalId = nodeId });
                }
            }
        }

        // Called during module Dispose to balance any still-connected terminal IDs
        // and prevent a leaked listener count.
        public void DrainAll()
        {
            foreach (var _ in _connectedTerminalIds)
                _controller.RemoveListener();
            _connectedTerminalIds.Clear();
        }
    }
}
