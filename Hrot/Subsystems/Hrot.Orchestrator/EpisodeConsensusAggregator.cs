using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Payload produced by <see cref="EpisodeConsensusAggregator"/> and carried by
/// <see cref="ClusterOpCompletedEvent.ResultPayload"/>.
/// Consumed by <see cref="EpisodeProcessManager"/> to update active episode state.
/// </summary>
public sealed class EpisodeConsensusPayload
{
    public Guid EpisodeId { get; init; }
    public bool IsStart   { get; init; }
}

/// <summary>
/// Aggregates ManageEpisode 2PC ACKs and produces an <see cref="EpisodeConsensusPayload"/>
/// result. Registered with <see cref="ClusterMaster"/> for both
/// <see cref="NodeOpType.StartEpisode"/> and <see cref="NodeOpType.StopEpisode"/> operations.
/// </summary>
public sealed class EpisodeConsensusAggregator : INodeResponseAggregator
{
    public NodeOpType TargetOp { get; }

    public EpisodeConsensusAggregator(NodeOpType targetOp)
    {
        TargetOp = targetOp;
    }

    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        foreach (var nodeDict in nodeResponses.Values)
        {
            if (nodeDict.TryGetValue(TargetOp, out var json) && !string.IsNullOrEmpty(json))
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<EpisodeConsensusPayload>(
                        json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }
            }
        }
        return null;
    }
}
