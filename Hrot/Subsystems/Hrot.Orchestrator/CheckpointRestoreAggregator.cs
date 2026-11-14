// CheckpointRestoreAggregator: INodeResponseAggregator for NodeOpType.RestoreSnapshot (CE-3299 B4).
// Returns the node answer with the EARLIEST restored time — the clock the cluster snaps back to (D5).

using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ <c>CE-3299</c> B4 (D5) — every node answers a checkpoint restore with the clock ITS keyframe was written at; they differ by the
/// slaves' PLL residual (≤ 94 ms measured, restore design §1g). The cluster snaps to the EARLIEST, so no node's restored world is
/// ahead of the clock. Registered with <see cref="ClusterMaster"/>; <see cref="LiveBranchProcessManager"/> acts on the result.
/// </summary>
public sealed class CheckpointRestoreAggregator : INodeResponseAggregator
{
    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.RestoreSnapshot;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        CheckpointRestoreResult? earliest = null;
        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.RestoreSnapshot, out var json) || string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var result = JsonSerializer.Deserialize<CheckpointRestoreResult>(json, OrchestrationJsonOptions.Default);
                if (result.RestoredTime.TotalWallTicks == 0) continue;
                if (earliest == null || result.RestoredTime.TotalWallTicks < earliest.Value.RestoredTime.TotalWallTicks)
                    earliest = result;
            }
            catch (JsonException)
            {
                // Malformed JSON -- skip without throwing.
            }
        }
        return earliest;
    }
}
