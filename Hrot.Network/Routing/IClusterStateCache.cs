using Hrot.Common;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// Fast, thread-safe read-model for cluster node health and capabilities.
    ///
    /// <para>
    /// Implementations subscribe to <c>NodeHeartbeatEvent</c> on the local
    /// <c>FdpEventBus</c> and maintain an internal dictionary of the most recently
    /// observed <see cref="NodeCapability"/> per node ID.  The <c>IOwnershipDistributionStrategy</c>
    /// queries this cache to resolve the optimal target node for each descriptor at
    /// entity-creation time without blocking the 60 Hz hot path.
    /// </para>
    /// </summary>
    public interface IClusterStateCache
    {
        /// <summary>
        /// Returns the node ID of the least-loaded active node that carries
        /// <paramref name="requiredRole"/>, or <c>null</c> if no such node is currently known.
        /// </summary>
        /// <param name="requiredRole">The capability role the target node must fulfil.</param>
        int? GetLeastLoadedNode(NodeRole requiredRole);

        /// <summary>
        /// Updates (or inserts) the capability record for a specific node.
        /// Called by the heartbeat bridge from the event-bus subscription.
        /// </summary>
        void UpdateNode(NodeCapability capability);

        /// <summary>
        /// Removes stale nodes whose heartbeat has not been seen for more than
        /// <paramref name="maxSilenceSeconds"/> seconds.
        /// </summary>
        void PruneStale(double nowUtcSeconds, double maxSilenceSeconds = 10.0);
    }

    /// <summary>
    /// Lightweight snapshot of a peer node's capability and load telemetry,
    /// derived from its <c>NodeHeartbeat</c> DDS publication.
    /// </summary>
    public sealed class NodeCapability
    {
        public int    NodeId          { get; set; }
        public NodeRole Role          { get; set; }
        public float  CpuUsagePercent { get; set; }
        public long   RamUsedBytes    { get; set; }
        public double LastSeenUtcSeconds { get; set; }
    }
}
