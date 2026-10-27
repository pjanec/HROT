namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload returned by <see cref="ReferenceReplayLoadHandler"/> upon a successful
    /// <c>PrepareReplay</c> operation.
    ///
    /// <para>
    /// Carries both the highest network entity ID and the wall-clock duration of the
    /// recording so the cluster master can aggregate them from all participating nodes.
    /// </para>
    ///
    /// <para>
    /// Serialised to/from JSON by <c>System.Text.Json.JsonSerializer</c> via
    /// <c>NodeOpSlaveTranslator.SerializeResultPayload</c> (egress) and
    /// <c>NodeOpMasterTranslator.DeserializeResultPayload</c> /
    /// <c>ClusterUiCache.AggregateReplayDuration</c> (ingress).
    /// Do NOT add a custom <c>ToString()</c> override — use the serializer directly.
    /// </para>
    /// </summary>
    /// <param name="FirstFrameWallTicks">⭐ R-4012 (Q91 §4-P) — this node's first recorded frame; 0 when it holds no recording.
    /// Aggregated: the earliest across the cluster — the replay's zero.</param>
    /// <param name="LastFrameWallTicks">⭐ R-4012 — this node's last recorded frame; 0 when it holds none. Aggregated: the latest.</param>
    /// <param name="TimeMap">⭐ CE-3329 P1 (R3, row 8) — this node's wall ↔ sim map (from its v7 frame index); <see langword="null"/>
    /// without a recording. Aggregated: the cluster's map — the anchor node's sim times, the stretches every node paused in.</param>
    /// <param name="Checkpoints">⭐ CE-3329 (§9f) — this node's recording's checkpoint marks; aggregated: the union by label (the
    /// earliest), what every host's timeline marks.</param>
    /// <param name="GeoOrigin">⭐ AQ107 C — the geodetic origin this node's recording was made under (its metadata); <see langword="null"/>
    /// when the recording pre-dates the field, and on a REVIEW's result (the run's origin is already published). Aggregated by a replay's
    /// aggregator: the anchor node's, else zeros (R-229) — the master publishes it as the NED wire's frame origin.</param>
    /// <param name="TerrainName">⭐ AQ107 C — the terrain resident while this node's recording was made (its metadata), with <paramref name="GeoOrigin"/>.</param>
    public record struct ReplayPrepareResult(long MaxNetworkId, float DurationSeconds,
                                             long FirstFrameWallTicks = 0, long LastFrameWallTicks = 0,
                                             Fdp.Core.FlightRecorder.ReplayTimeMap? TimeMap = null,
                                             Fdp.Core.FlightRecorder.Metadata.CheckpointMark[]? Checkpoints = null,
                                             Fdp.Core.FlightRecorder.Metadata.GeoOriginRecord? GeoOrigin = null,
                                             string? TerrainName = null);
}
