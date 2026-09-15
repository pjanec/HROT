using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// The creator-side waiter of the reliable-initialisation barrier: holds a locally-owned
    /// entity in <c>Constructing</c> until every peer node that must initialise a copy has
    /// reported <see cref="EntityLifecycle.Active"/>.
    ///
    /// <para>A <b>reactive</b> <see cref="DeferredConstructionParticipant"/>: it defers an entity
    /// that carries <see cref="PendingNetworkAck"/>, seeds the wait-set from the
    /// <see cref="NetworkAckPeerSet"/> stamped at spawn, and completes when the transport delivers
    /// each peer's Active status through <see cref="ReceiveLifecycleStatus"/> (or on the base
    /// timeout). An entity with no <see cref="PendingNetworkAck"/> — fast mode
    /// (<see cref="ReliableInitType.None"/>) — is acked immediately.</para>
    ///
    /// <para>The peer set is the roster membership (<c>NodeRoster.NodesWithRole</c> / the NED
    /// cluster cache) resolved at spawn and stamped onto <see cref="NetworkAckPeerSet"/>; the
    /// gateway no longer queries <c>INetworkTopology</c> (retired — no production implementation).
    /// Barrier design <c>docs/DESIGN_Cross_Node_Construction_Barrier.md</c> §1.1/§3a.4.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class NetworkGatewaySystem : DeferredConstructionParticipant
    {
        private readonly int _localNodeId;

        // Entity → the peer node ids we are still waiting for an Active ack from.
        private readonly Dictionary<Entity, HashSet<int>> _pendingPeerAcks = new();

        /// <summary>Reliable init ACK timeout in frames (5 sec @ 60Hz)</summary>
        public const int RELIABLE_INIT_TIMEOUT_FRAMES = DEFAULT_TIMEOUT_FRAMES;

        /// <summary>
        /// Constructs the creator waiter.
        /// </summary>
        /// <param name="gatewayModuleId">Module id registered with <paramref name="elm"/>.</param>
        /// <param name="localNodeId">This node's ownership node id (excluded from any peer set).</param>
        /// <param name="elm">The entity lifecycle module driving construction/destruction.</param>
        /// <param name="reliableInitTimeoutFrames">Frames before a pending ack is force-acked; ≤0 uses the default.</param>
        public NetworkGatewaySystem(
            int gatewayModuleId,
            int localNodeId,
            EntityLifecycleModule elm,
            int reliableInitTimeoutFrames = -1)
            : base(gatewayModuleId, elm, reliableInitTimeoutFrames)
        {
            _localNodeId = localNodeId;
        }

        /// <summary>The gateway is a GLOBAL waiter — it inspects every constructed entity.</summary>
        protected override bool Participates(ISimulationView view, Entity entity, long blueprintId) => true;

        /// <summary>Ack immediately unless the entity is a reliable one with peers still to hear from.</summary>
        protected override bool TryImmediateComplete(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<PendingNetworkAck>(entity))
                return true; // fast mode — no cross-node wait

            // Reliable: defer only if there is at least one peer to wait for.
            return CollectPeers(view, entity).Count == 0;
        }

        /// <summary>Seed the wait-set for a deferred (reliable, non-empty-peer) entity.</summary>
        protected override void OnDeferred(ISimulationView view, Entity entity)
        {
            var peers = CollectPeers(view, entity);
            _pendingPeerAcks[entity] = peers;

            if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                FdpLog<NetworkGatewaySystem>.Debug(
                    "[Node-{0}] Entity {1}: reliable, waiting for {2} peer ack(s).",
                    _localNodeId, entity.Index, peers.Count);
        }

        /// <summary>Clear per-entity state and strip the transient ack tag once acked.</summary>
        protected override void OnCompleted(ISimulationView? view, Entity entity, IEntityCommandBuffer cmd)
        {
            _pendingPeerAcks.Remove(entity);
            cmd.RemoveComponent<PendingNetworkAck>(entity);
        }

        /// <summary>Drop wait-state for an entity destroyed before it completed.</summary>
        protected override void OnDestroyed(Entity entity) => _pendingPeerAcks.Remove(entity);

        /// <summary>
        /// Called by the transport when a remote node reports a lifecycle status. Removes the
        /// reporting node from the wait-set and, when it empties, acknowledges construction.
        /// Only <see cref="EntityLifecycle.Active"/> completes the handshake.
        /// </summary>
        public void ReceiveLifecycleStatus(
            Entity entity, int nodeId, EntityLifecycle state,
            IEntityCommandBuffer cmd, uint currentFrame)
        {
            if (!_pendingPeerAcks.TryGetValue(entity, out var pendingPeers))
                return; // not waiting for this entity

            if (state != EntityLifecycle.Active)
                return; // only Active confirmations complete the handshake

            pendingPeers.Remove(nodeId);

            if (pendingPeers.Count == 0)
                Complete(entity, cmd, currentFrame); // base: ack + OnCompleted cleanup
        }

        // Reads the stamped peer set, excluding the local node (a node never waits for itself).
        private HashSet<int> CollectPeers(ISimulationView view, Entity entity)
        {
            var set = new HashSet<int>();
            if (!view.HasManagedComponent<NetworkAckPeerSet>(entity))
                return set;

            var peers = view.GetManagedComponentRO<NetworkAckPeerSet>(entity).ExpectedAckPeers;
            if (peers != null)
                foreach (var id in peers)
                    if (id != _localNodeId)
                        set.Add(id);
            return set;
        }
    }
}
