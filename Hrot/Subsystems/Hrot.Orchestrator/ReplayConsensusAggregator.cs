using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.Orchestrator;

/// <summary>
/// Aggregates per-node <see cref="NodeOpType.PrepareReplay"/> response strings into a
/// single <see cref="ReplayPrepareResult"/> that carries the maximum
/// <c>DurationSeconds</c> and <c>MaxNetworkId</c> across all participating nodes.
///
/// <para>
/// Registered with <see cref="ClusterMaster"/> so the replay duration reported to the
/// orchestrator layer reflects the longest recording in the cluster.
/// </para>
/// </summary>
public sealed class ReplayConsensusAggregator : INodeResponseAggregator
{
    private static readonly JsonSerializerOptions _deserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.PrepareReplay;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        float maxDuration    = 0f;
        long  maxNetworkId   = 0L;

        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.PrepareReplay, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var result = JsonSerializer.Deserialize<ReplayPrepareResult>(json, _deserializeOptions);
                if (result.DurationSeconds > maxDuration) maxDuration  = result.DurationSeconds;
                if (result.MaxNetworkId    > maxNetworkId) maxNetworkId = result.MaxNetworkId;
            }
            catch { /* non-ReplayPrepareResult JSON — skip */ }
        }

        return maxDuration > 0f
            ? new ReplayPrepareResult(MaxNetworkId: maxNetworkId, DurationSeconds: maxDuration)
            : null;
    }
}
