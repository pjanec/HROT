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

        // CE-289/290 (C3/C4): the persistent creator world, cached at defer time so the reactive completion
        // path (ReceiveLifecycleStatus, called by the transport without a view) can reach the poll-store.
        private EntityRepository? _world;

        /// <summary>Sim tick rate used to convert the creator's <c>ReliableInitTimeout</c> (seconds) to frames.</summary>
        private const double FramesPerSecond = 60.0;

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
            _world = view as EntityRepository;   // C4: cache the persistent world for the reactive path.
            var peers = CollectPeers(view, entity);
            _pendingPeerAcks[entity] = peers;

            // C3: honour the creator's per-entity ReliableInitTimeout (stamped on NetworkAckPeerSet as seconds).
            if (view.HasManagedComponent<NetworkAckPeerSet>(entity))
            {
                double secs = view.GetManagedComponentRO<NetworkAckPeerSet>(entity).TimeoutSeconds;
                if (secs > 0)
                    SetPendingTimeout(entity, (int)(secs * FramesPerSecond));
            }

            // C4: publish the in-progress result so the local requestor's poll reads Pending, not "unknown".
            long netId = TryGetNetworkId(view, entity);
            if (netId != 0 && _world != null)
                ConstructionResults.GetOrCreate(_world).SetPending(netId, GetFrame(view));

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
            {
                // C4: record Success for the local requestor's poll-store BEFORE Complete strips state.
                long netId = _world != null ? TryGetNetworkId(_world, entity) : 0;
                Complete(entity, cmd, currentFrame); // base: ack + OnCompleted cleanup
                if (netId != 0 && _world != null)
                    ConstructionResults.GetOrCreate(_world)
                        .Resolve(netId, ConstructionOutcome.Success, ConstructionFailReason.None, currentFrame);
            }
        }

        /// <summary>
        /// CE-289 (C3): the creator's authoritative timeout expired with peers still unheard-from. ABORT — do
        /// NOT force-ack a reliable entity (§3b.3). Record <c>Failed(Timeout)</c> for the requestor, then tear the
        /// entity down locally: <c>BeginDestruction</c> emits the <c>DestructionOrder</c> that
        /// <c>CycloneNetworkCleanupSystem</c> turns into an <c>EntityMaster</c> dispose sample, so every peer's
        /// ghost is removed. The base's <c>DestructionOrder</c> path then runs <c>OnDestroyed</c> to clear our
        /// wait-state.
        /// </summary>
        protected override void OnTimeout(ISimulationView view, Entity entity, IEntityCommandBuffer cmd, uint currentFrame)
        {
            var world = (view as EntityRepository) ?? _world;
            long netId = TryGetNetworkId(view, entity);
            if (netId != 0 && world != null)
                ConstructionResults.GetOrCreate(world)
                    .Resolve(netId, ConstructionOutcome.Failed, ConstructionFailReason.Timeout, currentFrame);

            FdpLog<NetworkGatewaySystem>.Warn(
                "[Node-{0}] Entity {1} (NetId {2}): reliable-init timeout — aborting via EntityMaster dispose.",
                _localNodeId, entity.Index, netId);

            _pendingPeerAcks.Remove(entity);
            _elm.BeginDestruction(entity, currentFrame, "reliable-init-timeout", cmd);
            // ⛔ no AcknowledgeConstruction — the entity is torn down, not force-activated.
        }

        private static uint GetFrame(ISimulationView view) => view is EntityRepository r ? r.GlobalVersion : 0u;

        // The entity's network id, or 0 if it has no NetworkIdentity yet.
        private static long TryGetNetworkId(ISimulationView view, Entity entity)
            => view.HasComponent<NetworkIdentity>(entity)
                ? view.GetComponentRO<NetworkIdentity>(entity).Value
                : 0L;

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
