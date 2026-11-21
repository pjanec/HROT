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
        ReplaySeekResult? first = null;
        foreach (var (nodeId, nodeDict) in nodeResponses)
        {
            if (!nodeDict.TryGetValue(NodeOpType.NodeReplaySeek, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                var result = JsonSerializer.Deserialize<ReplaySeekResult>(
                    json, OrchestrationJsonOptions.Default);
                // ⭐ CE-4079 ④ (DESIGN_Replay_Time_Axis_And_Review §12c W3) — a node whose retraction disposes were not acknowledged in time
                //   still answered; the master says so for EVERY node and goes on (a timeout is reported, never fatal).
                if (result.RetractionAckTimeout is { } ackNote)
                    Fdp.Core.Logging.FdpLog<ReplaySeekAggregator>.Warn("[Orchestrator] node {0}'s seek answered with unacknowledged retraction disposes: {1}", nodeId, ackNote);
                if (first == null && result.RestoredTime.TotalWallTicks != 0)
                    first = result;
            }
            catch
            {
                // Malformed JSON -- skip without throwing.
            }
        }

        return first;
    }
}
