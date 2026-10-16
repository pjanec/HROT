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
    public record struct ReplayPrepareResult(long MaxNetworkId, float DurationSeconds,
                                             long FirstFrameWallTicks = 0, long LastFrameWallTicks = 0);
}
