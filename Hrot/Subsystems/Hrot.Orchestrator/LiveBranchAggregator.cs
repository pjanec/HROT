// LiveBranchAggregator: INodeResponseAggregator for NodeOpType.PrepareLive (CE-3312).
// Returns a node's LiveBranchResult — the replay clock at the branch — for LiveBranchProcessManager to restore time and snap to.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ <c>CE-3312</c> — every node answers a live branch with the replay clock at the frame it branched from (<see cref="LiveBranchResult"/>);
/// the master's replay clock drove them all, so they agree up to the slaves' PLL residual — the EARLIEST is taken, as
/// <see cref="CheckpointRestoreAggregator"/> does for a restore. ⛔ Without it a multi-process cluster's branch result reached nobody: the
/// master never restored time after <c>FreezeTime</c> nor snapped the clock (the in-process editor passed the object straight through,
/// which is why only it worked). Registered with <see cref="ClusterMaster"/>; <see cref="LiveBranchProcessManager"/> acts on the result.
/// </summary>
public sealed class LiveBranchAggregator : INodeResponseAggregator
{
    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.PrepareLive;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        LiveBranchResult? earliest = null;
        // ⭐ CE-4097 (AQ108 D5) — the parent and point of the EARLIEST recording node (as ExerciseBranch.Of takes them off the sidecars); the
        //   clock above may come from a node that kept no history, so they are picked on their own.
        Guid parent = Guid.Empty;
        long branchTicks = 0;
        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.PrepareLive, out var json) || string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var result = JsonSerializer.Deserialize<LiveBranchResult>(json, OrchestrationJsonOptions.Default);
                if (result.ParentExerciseId != Guid.Empty
                    && (parent == Guid.Empty || (result.BranchWallTicks > 0 && (branchTicks == 0 || result.BranchWallTicks < branchTicks))))
                {
                    parent = result.ParentExerciseId;
                    branchTicks = result.BranchWallTicks;
                }
                if (result.HistoricalTime.TotalWallTicks == 0) continue;
                // ⭐ CE-3337 — a node that restored the frame from its own recording first (its FrameNumber is the recorded step count);
                //   among those, the earliest clock
                bool better = earliest == null
                    || (result.FromRecording && !earliest.Value.FromRecording)
                    || (result.FromRecording == earliest.Value.FromRecording
                        && result.HistoricalTime.TotalWallTicks < earliest.Value.HistoricalTime.TotalWallTicks);
                if (better) earliest = result;
            }
            catch (JsonException)
            {
                // Malformed JSON -- skip without throwing.
            }
        }
        return earliest is { } e ? e with { ParentExerciseId = parent, BranchWallTicks = branchTicks } : null;
    }
}
