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
    public record struct ReplayPrepareResult(long MaxNetworkId, float DurationSeconds);
}
