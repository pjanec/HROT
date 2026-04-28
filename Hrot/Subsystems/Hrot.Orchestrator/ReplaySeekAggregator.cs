// ReplaySeekAggregator: INodeResponseAggregator for NodeOpType.NodeReplaySeek.
// Returns the first ReplaySeekResult where RestoredTime.TotalWallTicks != 0.

using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Aggregates per-node <see cref="NodeOpType.NodeReplaySeek"/> response strings and
/// returns the first <see cref="ReplaySeekResult"/> where
/// <see cref="ReplaySeekResult.RestoredTime.TotalWallTicks"/> is non-zero.
/// Registered with <see cref="ClusterMaster"/> so the orchestrator can extract the
/// restored timeline from the first responding node.
/// </summary>
public sealed class ReplaySeekAggregator : INodeResponseAggregator
{
    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.NodeReplaySeek;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.NodeReplaySeek, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                var result = JsonSerializer.Deserialize<ReplaySeekResult>(
                    json, OrchestrationJsonOptions.Default);
                if (result.RestoredTime.TotalWallTicks != 0)
                    return result;
            }
            catch
            {
                // Malformed JSON -- skip without throwing.
            }
        }

        return null;
    }
}
