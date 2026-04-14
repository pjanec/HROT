using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Interfaces;
using INetworkTopology = Fdp.Toolkit.Replication.INetworkTopology;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// Canonical, transport-agnostic <see cref="IEcsModuleSystem"/> that implements the
    /// reliable-initialisation ACK handshake for networked entities.
    ///
    /// <para>For each <see cref="ConstructionOrder"/> event received:</para>
    /// <list type="bullet">
    ///   <item>If the entity has no <see cref="PendingNetworkAck"/> component, the system
    ///     immediately calls <see cref="EntityLifecycleModule.AcknowledgeConstruction"/>.
    ///   </item>
    ///   <item>If <see cref="PendingNetworkAck"/> is present, the system queries
    ///     <see cref="INetworkTopology.GetExpectedPeers"/> and waits until all peer nodes
    ///     report <see cref="EntityLifecycle.Active"/> via
    ///     <see cref="ReceiveLifecycleStatus"/> before acknowledging.</item>
    /// </list>
    ///
    /// <para>Entities stuck in the pending state longer than
    /// <c>reliableInitTimeoutFrames</c> are force-acknowledged to prevent deadlocks.</para>
    ///
    /// <para>This class is the <b>canonical</b> home for gateway logic previously
    /// duplicated across <c>Network.Cyclone/Systems</c> and
    /// <c>ModuleHost/Network</c> (PACK3-N001).  All transport adapters
    /// (Cyclone, future adapters) must reference this class.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class NetworkGatewaySystem : IEcsModuleSystem
    {
        private readonly int _gatewayModuleId;
        private readonly int _localNodeId;
        private readonly INetworkTopology _topology;
        private readonly EntityLifecycleModule _elm;
        private readonly int _reliableInitTimeoutFrames;

        // Track pending network ACKs: Entity → set of node IDs we are still waiting for.
        private readonly Dictionary<Entity, HashSet<int>> _pendingPeerAcks;

        // Track when entities entered the pending state (for timeout).
        private readonly Dictionary<Entity, uint> _pendingStartFrame;

        /// <summary>Reliable init ACK timeout in frames (5 sec @ 60Hz)</summary>
        public const int RELIABLE_INIT_TIMEOUT_FRAMES = 300;

        /// <summary>
        /// Constructs a new <see cref="NetworkGatewaySystem"/>.
        /// </summary>
        /// <param name="gatewayModuleId">
        /// Module ID registered with <paramref name="elm"/> so this system receives
        /// <see cref="ConstructionOrder"/> events.
        /// </param>
        /// <param name="localNodeId">This node's identifier (used for topology lookups).</param>
        /// <param name="topology">Network topology provider for peer discovery.</param>
        /// <param name="elm">Entity lifecycle module that drives construction/destruction events.</param>
        /// <param name="reliableInitTimeoutFrames">
        /// Number of frames before a pending ACK is force-acknowledged.
        /// Negative or zero uses <see cref="NetworkGatewaySystem.RELIABLE_INIT_TIMEOUT_FRAMES"/>.
        /// </param>
        public NetworkGatewaySystem(
            int gatewayModuleId,
            int localNodeId,
            INetworkTopology topology,
            EntityLifecycleModule elm,
            int reliableInitTimeoutFrames = -1)
        {
            _gatewayModuleId  = gatewayModuleId;
            _localNodeId      = localNodeId;
            _topology         = topology ?? throw new ArgumentNullException(nameof(topology));
            _elm              = elm      ?? throw new ArgumentNullException(nameof(elm));
            _reliableInitTimeoutFrames = reliableInitTimeoutFrames > 0
                ? reliableInitTimeoutFrames
                : RELIABLE_INIT_TIMEOUT_FRAMES;

            _pendingPeerAcks  = new Dictionary<Entity, HashSet<int>>();
            _pendingStartFrame = new Dictionary<Entity, uint>();

            // Register with ELM so we receive ConstructionOrder events.
            _elm.RegisterModule(_gatewayModuleId);
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            uint currentFrame = 0;
            if (view is EntityRepository repo)
                currentFrame = repo.GlobalVersion;

            var cmd = view.GetCommandBuffer();

            ProcessConstructionOrders(view, cmd, currentFrame);
            ProcessDestructionOrders(view, cmd);
            CheckPendingAckTimeouts(cmd, currentFrame);
        }

        private void ProcessConstructionOrders(ISimulationView view, IEntityCommandBuffer cmd, uint currentFrame)
        {
            var events = view.ConsumeEvents<ConstructionOrder>();

            foreach (var evt in events)
            {
                // Fast path: no PendingNetworkAck → acknowledge immediately.
                if (!view.HasComponent<PendingNetworkAck>(evt.Entity))
                {
                    if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                        FdpLog<NetworkGatewaySystem>.Debug(
                            "Entity {0} missing PendingNetworkAck. ACKing.",
                            evt.Entity.Index);

                    _elm.AcknowledgeConstruction(evt.Entity, _gatewayModuleId, currentFrame, cmd);
                    continue;
                }

                // Reliable path: wait for all expected peers.
                var pendingInfo  = view.GetComponentRO<PendingNetworkAck>(evt.Entity);
                var expectedPeers = _topology.GetExpectedPeers((long)pendingInfo.ExpectedType);
                var peerSet       = new HashSet<int>(expectedPeers);

                if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                {
                    var peerList = string.Join(",", peerSet);
                    FdpLog<NetworkGatewaySystem>.Debug(
                        "Entity {0}: Reliable mode. Peers: {1}",
                        evt.Entity.Index,
                        peerList);
                }

                if (peerSet.Count == 0)
                {
                    // No peers — acknowledge immediately and clean up.
                    if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                        FdpLog<NetworkGatewaySystem>.Debug(
                            "Entity {0}: No peers. ACKing.",
                            evt.Entity.Index);

                    _elm.AcknowledgeConstruction(evt.Entity, _gatewayModuleId, currentFrame, cmd);
                    cmd.RemoveComponent<PendingNetworkAck>(evt.Entity);
                }
                else
                {
                    if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                        FdpLog<NetworkGatewaySystem>.Debug(
                            "Entity {0}: Waiting for ACKs from {1} peer(s).",
                            evt.Entity.Index,
                            peerSet.Count);

                    _pendingPeerAcks[evt.Entity]   = peerSet;
                    _pendingStartFrame[evt.Entity] = currentFrame;
                }
            }
        }

        /// <summary>
        /// Called by the transport layer (e.g. Cyclone) when a remote node reports
        /// a lifecycle status for an entity.  Removes the reporting node from the
        /// waiting set and, if all peers have responded, acknowledges construction.
        /// </summary>
        public void ReceiveLifecycleStatus(
            Entity entity, int nodeId, EntityLifecycle state,
            IEntityCommandBuffer cmd, uint currentFrame)
        {
            if (!_pendingPeerAcks.TryGetValue(entity, out var pendingPeers))
                return; // Not waiting for this entity.

            if (state != EntityLifecycle.Active)
                return; // Only Active confirmations complete the handshake.

            pendingPeers.Remove(nodeId);

            if (pendingPeers.Count == 0)
            {
                _elm.AcknowledgeConstruction(entity, _gatewayModuleId, currentFrame, cmd);
                cmd.RemoveComponent<PendingNetworkAck>(entity);

                _pendingPeerAcks.Remove(entity);
                _pendingStartFrame.Remove(entity);
            }
        }

        private void CheckPendingAckTimeouts(IEntityCommandBuffer cmd, uint currentFrame)
        {
            var timedOut = new List<Entity>();

            foreach (var kvp in _pendingStartFrame)
            {
                if (currentFrame - kvp.Value > _reliableInitTimeoutFrames)
                {
                    Console.Error.WriteLine(
                        $"[NetworkGatewaySystem] Entity {kvp.Key.Index}: " +
                        $"Timeout waiting for peer ACKs after {_reliableInitTimeoutFrames} frames.");
                    timedOut.Add(kvp.Key);
                }
            }

            foreach (var entity in timedOut)
            {
                _elm.AcknowledgeConstruction(entity, _gatewayModuleId, currentFrame, cmd);
                cmd.RemoveComponent<PendingNetworkAck>(entity);

                _pendingPeerAcks.Remove(entity);
                _pendingStartFrame.Remove(entity);
            }
        }

        private void ProcessDestructionOrders(ISimulationView view, IEntityCommandBuffer cmd)
        {
            var events = view.ConsumeEvents<DestructionOrder>();
            foreach (var evt in events)
            {
                _pendingPeerAcks.Remove(evt.Entity);
                _pendingStartFrame.Remove(evt.Entity);

                cmd.PublishEvent(new DestructionAck
                {
                    Entity   = evt.Entity,
                    ModuleId = _gatewayModuleId,
                    Success  = true
                });
            }
        }
    }
}
