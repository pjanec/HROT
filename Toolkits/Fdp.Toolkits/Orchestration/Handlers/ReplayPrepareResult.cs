using System.Globalization;

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
    /// <see cref="ToString"/> returns a JSON string so that the value is stored in a
    /// parseable format when serialized via <c>ResultPayload?.ToString()</c> inside
    /// <c>ClusterUiCache.Process2PcNetworkTraffic</c> and forwarded over DDS.
    /// </para>
    /// </summary>
    public record struct ReplayPrepareResult(long MaxNetworkId, float DurationSeconds)
    {
        /// <summary>
        /// Returns a JSON representation, e.g. <c>{"MaxNetworkId":42,"DurationSeconds":300.5}</c>.
        /// </summary>
        public override string ToString() =>
            "{\"MaxNetworkId\":" + MaxNetworkId +
            ",\"DurationSeconds\":" + DurationSeconds.ToString(CultureInfo.InvariantCulture) + "}";
    }
}
