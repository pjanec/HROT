using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// ⭐ CE-5018 — the node's own identity as the world's <see cref="INetworkTopology"/> singleton, published by the shared node frame
    /// (<c>HrotNodeBuilder</c>) on EVERY node, so a system that must name THIS node (a breach's damage entity, a crater or minefield record —
    /// <c>BuildingDamageSystem</c>, <c>CellRecordSystem</c>, <c>RecordWriterCore</c>, <c>EditOutcomes</c>) reads the real id instead of 0.
    /// 📐 Before it, no production host published the singleton: every such request went out untargeted, so in a cluster the DEFAULT
    /// PROCESSOR (the brain node) was asked to create the muscle's damage entity, and a possessed bulldozer's bump made no breach.
    /// ⚠ It knows only the local id — the peer set is <c>NetworkAckPeerSet</c>'s, never this.
    /// </summary>
    public sealed class LocalNodeTopology : INetworkTopology
    {
        public LocalNodeTopology(NodeId localNodeId) => LocalNodeId = localNodeId;

        /// <inheritdoc/>
        public NodeId LocalNodeId { get; }

        /// <summary>None: the reliable-init peer set is <c>NetworkAckPeerSet</c>'s (the topology's peer half is retired).</summary>
        public IEnumerable<int> GetExpectedPeers(long tkbType) => Array.Empty<int>();

        /// <summary>This node only — the cluster's membership is the orchestrator's, not this singleton's.</summary>
        public IEnumerable<int> GetAllNodes() => new[] { (int)LocalNodeId };
    }
}
