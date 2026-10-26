using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Core.Serialization;

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
    private static readonly JsonSerializerOptions _deserializeOptions = FdpJsonOptionsRegistry.DefaultRelaxed;

    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.PrepareReplay;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        float maxDuration    = 0f;
        long  maxNetworkId   = 0L;
        long  firstFrame     = 0L;   // ⭐ R-4012 — the earliest first frame across the cluster: the replay's zero
        long  lastFrame      = 0L;   //            the latest last frame: the replay's end
        // ⭐ CE-3329 P1 (R3) — every node's map; the anchor node's (the earliest first frame) leads the intersection
        var maps = new List<(long First, Fdp.Core.FlightRecorder.ReplayTimeMap Map)>();

        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.PrepareReplay, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var result = JsonSerializer.Deserialize<ReplayPrepareResult>(json, _deserializeOptions);
                if (result.DurationSeconds > maxDuration) maxDuration  = result.DurationSeconds;
                if (result.MaxNetworkId    > maxNetworkId) maxNetworkId = result.MaxNetworkId;
                if (result.FirstFrameWallTicks > 0 && (firstFrame == 0 || result.FirstFrameWallTicks < firstFrame))
                    firstFrame = result.FirstFrameWallTicks;
                if (result.LastFrameWallTicks > lastFrame) lastFrame = result.LastFrameWallTicks;
                if (result.TimeMap is { HasSimTime: true } map) maps.Add((result.FirstFrameWallTicks, map));
            }
            catch { /* non-ReplayPrepareResult JSON — skip */ }
        }

        // ⭐⭐ R-4012 (Q91 §4-P P1) — with spans known, the replay lasts from the earliest first frame to the latest last
        //   frame. ⛔ Was the longest single recording, which is shorter than the exercise when no node recorded all of it.
        if (firstFrame > 0 && lastFrame > firstFrame)
            maxDuration = System.Math.Max(maxDuration, (float)((lastFrame - firstFrame) / (double)System.TimeSpan.TicksPerSecond));

        return maxDuration > 0f
            ? new ReplayPrepareResult(MaxNetworkId: maxNetworkId, DurationSeconds: maxDuration,
                                      FirstFrameWallTicks: firstFrame, LastFrameWallTicks: lastFrame,
                                      TimeMap: maps.Count == 0 ? null
                                          : Fdp.Core.FlightRecorder.ReplayTimeMap.Intersect(maps.OrderBy(m => m.First).Select(m => m.Map)))
            : null;
    }
}
