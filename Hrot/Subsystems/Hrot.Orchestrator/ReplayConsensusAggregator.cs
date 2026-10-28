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
    private readonly NodeOpType _target;
    private readonly bool _endAtEarliestLast;

    /// <param name="target">The prepare op whose results this aggregates — <c>PrepareReplay</c>, or ⭐ CE-3329 <c>PrepareReview</c>.</param>
    /// <param name="endAtEarliestLast">⭐ CE-3329 F13 — a REVIEW ends at the live point: the EARLIEST of the nodes' last frames (no node
    /// replays past what every node recorded), never a duration estimate.</param>
    public ReplayConsensusAggregator(NodeOpType target = NodeOpType.PrepareReplay, bool endAtEarliestLast = false)
    {
        _target = target;
        _endAtEarliestLast = endAtEarliestLast;
    }

    public NodeOpType TargetOp => _target;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        float maxDuration    = 0f;
        long  maxNetworkId   = 0L;
        long  firstFrame     = 0L;   // ⭐ R-4012 — the earliest first frame across the cluster: the replay's zero
        long  lastFrame      = 0L;   //            the latest last frame: the replay's end
        // ⭐ CE-3329 P1 (R3) — every node's map; the anchor node's (the earliest first frame) leads the intersection
        var maps = new List<(long First, Fdp.Core.FlightRecorder.ReplayTimeMap Map)>();
        // ⭐ CE-3329 §9f — the checkpoint marks: the union by label (a cluster checkpoint is one label on every node), the earliest
        var marks = new Dictionary<string, Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();
        // ⭐ AQ107 C — the origin the run used: the ANCHOR node's (the earliest first frame, like the time map) that carries one
        (long First, Fdp.Core.FlightRecorder.Metadata.GeoOriginRecord Origin, string? Terrain)? geo = null;

        foreach (var (nodeId, nodeDict) in nodeResponses)
        {
            if (!nodeDict.TryGetValue(_target, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var result = JsonSerializer.Deserialize<ReplayPrepareResult>(json, _deserializeOptions);
                // ⭐ CE-4079 ④ (DESIGN_Replay_Time_Axis_And_Review §12c W3) — a node whose retraction disposes were not acknowledged in time
                //   still answered; the master says so and goes on (a timeout is reported, never fatal).
                if (result.RetractionAckTimeout is { } ackNote)
                    Fdp.Core.Logging.FdpLog<ReplayConsensusAggregator>.Warn("[Orchestrator] node {0}'s {1} answered with unacknowledged retraction disposes: {2}", nodeId, _target, ackNote);
                if (result.DurationSeconds > maxDuration) maxDuration  = result.DurationSeconds;
                if (result.MaxNetworkId    > maxNetworkId) maxNetworkId = result.MaxNetworkId;
                if (result.FirstFrameWallTicks > 0 && (firstFrame == 0 || result.FirstFrameWallTicks < firstFrame))
                    firstFrame = result.FirstFrameWallTicks;
                if (_endAtEarliestLast)
                {
                    if (result.LastFrameWallTicks > 0 && (lastFrame == 0 || result.LastFrameWallTicks < lastFrame)) lastFrame = result.LastFrameWallTicks;
                }
                else if (result.LastFrameWallTicks > lastFrame) lastFrame = result.LastFrameWallTicks;
                if (result.TimeMap is { HasSimTime: true } map) maps.Add((result.FirstFrameWallTicks, map));
                if (result.GeoOrigin is { } nodeOrigin && (geo is null || result.FirstFrameWallTicks < geo.Value.First))
                    geo = (result.FirstFrameWallTicks, nodeOrigin, result.TerrainName);
                foreach (var mark in result.Checkpoints ?? System.Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>())
                    if (!marks.TryGetValue(mark.Label, out var seen) || mark.WallTicks < seen.WallTicks) marks[mark.Label] = mark;
            }
            catch { /* non-ReplayPrepareResult JSON — skip */ }
        }

        // ⭐⭐ R-4012 (Q91 §4-P P1) — with spans known, the replay lasts from the earliest first frame to the latest last
        //   frame. ⛔ Was the longest single recording, which is shorter than the exercise when no node recorded all of it.
        if (firstFrame > 0 && lastFrame > firstFrame)
            maxDuration = System.Math.Max(maxDuration, (float)((lastFrame - firstFrame) / (double)System.TimeSpan.TicksPerSecond));

        // ⭐ AQ107 C — a REPLAY's aggregate always names the origin (the master publishes it as the NED wire's frame origin): the anchor
        //   node's, else 0,0,0 when no recording carries one (R-229 "Missing geo = zeros"). A review's aggregate leaves it null — the run's
        //   origin was published at its load and a review changes nothing.
        Fdp.Core.FlightRecorder.Metadata.GeoOriginRecord? aggregatedOrigin = null;
        string? aggregatedTerrain = null;
        if (_target == NodeOpType.PrepareReplay && maxDuration > 0f)
        {
            if (geo is { } anchor) { aggregatedOrigin = anchor.Origin; aggregatedTerrain = anchor.Terrain; }
            else
            {
                aggregatedOrigin = new Fdp.Core.FlightRecorder.Metadata.GeoOriginRecord();   // 0, 0, 0
                Fdp.Core.Logging.FdpLog<ReplayConsensusAggregator>.Warn(
                    "[Orchestrator] AQ107 C: no node's recording carries a geo origin (it pre-dates CE-3126) — the replay's GeoReference is 0,0,0.");
            }
        }

        return maxDuration > 0f
            ? new ReplayPrepareResult(MaxNetworkId: maxNetworkId, DurationSeconds: maxDuration,
                                      FirstFrameWallTicks: firstFrame, LastFrameWallTicks: lastFrame,
                                      TimeMap: maps.Count == 0 ? null
                                          : Fdp.Core.FlightRecorder.ReplayTimeMap.Intersect(maps.OrderBy(m => m.First).Select(m => m.Map)),
                                      Checkpoints: marks.Values.OrderBy(m => m.WallTicks).ToArray(),
                                      GeoOrigin: aggregatedOrigin, TerrainName: aggregatedTerrain)
            : null;
    }
}
