using Hrot.Common;
using Fdp.Core;

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
        /// The node ids of every currently-known present node (a point-in-time snapshot).
        /// Used by the reliable-init construction barrier to resolve the peer set a creator
        /// must wait for (CE-283; DESIGN_Cross_Node_Construction_Barrier.md §3a.4).
        /// </summary>
        System.Collections.Generic.IReadOnlyList<int> AllNodeIds();

        /// <summary>
        /// CE-285 (C-cap): does node <paramref name="nodeId"/> advertise the capability <paramref name="token"/>?
        /// Membership test over the gathered token set — absence (unknown node or unknown token) = unsupported
        /// (OpenGL-extension semantics, AQ-70 §Q70-B). The reliable-init wait-set includes only nodes for which
        /// <c>Supports(nodeId, CapabilityTokens.ReliableInit)</c> is true (§3b.1 / §3c ①).
        /// </summary>
        bool Supports(int nodeId, string token);

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

        /// <summary>CE-286 (C-roles): the node's <see cref="NodeRole"/> mask, DERIVED from the <c>fdp.role.*</c>
        /// subset of <see cref="Capabilities"/> via <see cref="NodeRoleTokens"/> (AQ-70 §Q70-C — no longer read
        /// off the heartbeat's <c>RolesMask</c>).</summary>
        public NodeRole Role          { get; set; }

        /// <summary>CE-285 (C-cap): the node's static capability token set (namespaced), gathered from the
        /// durable <c>NodeCapabilities</c> descriptor. Empty until that advertisement is seen.</summary>
        public System.Collections.Generic.IReadOnlySet<string> Capabilities { get; set; }
            = new System.Collections.Generic.HashSet<string>();

        public float  CpuUsagePercent { get; set; }
        public long   RamUsedBytes    { get; set; }
        public double LastSeenUtcSeconds { get; set; }
    }
}
