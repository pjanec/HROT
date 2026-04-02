using System;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Factory methods for <see cref="NodeOpCommand"/> instances.
/// Isolates <c>NodeOpCommand.PayloadJson</c> assignments from <c>ClusterMaster</c> (CMC-S010).
/// </summary>
internal static class ClusterNodeOpBuilder
{
    /// <summary>DDS-path node operation command, fully initialized for a specific target node.</summary>
    internal static NodeOpCommand DdsNodeOp(NodeOpType op, Guid txId, int nodeId, string payload)
        => new() { Operation = op, TransactionId = txId, TargetNodeId = nodeId, PayloadJson = payload };

    /// <summary>NodeOpCommand for invoking a local context handler (no TargetNodeId required).</summary>
    internal static NodeOpCommand LocalContextCmd(NodeOpType op, Guid txId, string payload)
        => new() { Operation = op, TransactionId = txId, PayloadJson = payload };
}
