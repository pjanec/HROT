using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>⭐⭐ <c>C1</c> (Q91 §4-O, <c>R-4009</c>/<c>R-4016</c>) — the master answers node-id requests for its session.</summary>
public sealed partial class ClusterMaster
{
    private readonly NodeIdAllocator _nodeIds = new();

    /// <summary>Every request read this frame gets an answer, on the same (computer, process) key.</summary>
    private void ProcessNodeIdRequests()
    {
        if (_eventBus == null) return;
        foreach (var req in _eventBus.ReadManaged<NodeIdRequestEvent>())
        {
            var o = _nodeIds.Grant(req.ComputerName, req.ProcessId, req.ForcedId, req.PreferredId,
                id => _roster.ActiveNodes.ContainsKey(id));
            if (o.Refused)
                FdpLog<ClusterMaster>.Error("[Orchestrator] C1: node id request from {0} pid {1} REFUSED: {2}.",
                    req.ComputerName, req.ProcessId, o.Reason);
            else
                FdpLog<ClusterMaster>.Info("[Orchestrator] C1: {0} pid {1} is node {2}.", req.ComputerName, req.ProcessId, o.NodeId);

            _eventBus.PublishManaged(new NodeIdGrantEvent
            {
                ComputerName = req.ComputerName,
                ProcessId    = req.ProcessId,
                NodeId       = o.NodeId,
                SessionId    = SessionId,
                Refused      = o.Refused,
                Reason       = o.Reason,
            });
        }
    }
}
