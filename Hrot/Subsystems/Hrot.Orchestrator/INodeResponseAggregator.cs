using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Strategy for reducing per-node JSON response strings into a single consensus payload.
/// Injected into <see cref="ClusterMaster"/> to keep domain-specific aggregation logic
/// out of the generic 2PC coordinator.
///
/// <para>
/// <see cref="ClusterMaster.RegisterAggregator"/> maps each aggregator to its
/// <see cref="TargetOp"/>.  After all node ACKs for a
/// <see cref="Fdp.Toolkit.Orchestration.ClusterOpType.TransitionState"/> round arrive,
/// <c>ClusterMaster</c> calls <see cref="Aggregate"/> on every registered aggregator
/// and attaches the first non-null result to the outgoing
/// <see cref="Fdp.Toolkit.Orchestration.ClusterOpCompletedEvent.ResultPayload"/>.
/// </para>
/// </summary>
public interface INodeResponseAggregator
{
    /// <summary>
    /// The per-node operation whose result JSON this aggregator processes.
    /// </summary>
    NodeOpType TargetOp { get; }

    /// <summary>
    /// Reduces the per-node response strings into a single consensus result object,
    /// or returns <c>null</c> when no meaningful consensus can be formed.
    /// </summary>
    /// <param name="nodeResponses">
    /// Snapshot of <see cref="Fdp.Toolkit.Orchestration.DistributedTransaction.NodeResponses"/>
    /// for the completed transaction: outer key is node ID, inner key is
    /// <see cref="NodeOpType"/>, value is the serialised result JSON string.
    /// </param>
    object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses);
}
