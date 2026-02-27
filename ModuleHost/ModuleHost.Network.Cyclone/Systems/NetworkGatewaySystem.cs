using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Core.Network;
using Fdp.Interfaces;
using INetworkTopology = Fdp.Interfaces.INetworkTopology;

namespace ModuleHost.Network.Cyclone.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class NetworkGatewaySystem : IModuleSystem
    {
        private readonly int _gatewayModuleId;
        private readonly int _localNodeId;
        private readonly INetworkTopology _topology;
        private readonly EntityLifecycleModule _elm;
        private readonly int _reliableInitTimeoutFrames;
        
        // Track pending network ACKs: EntityId -> Set of node IDs we're waiting for
        private readonly Dictionary<Entity, HashSet<int>> _pendingPeerAcks;
        
        // Track when entities entered pending state (for timeout)
        private readonly Dictionary<Entity, uint> _pendingStartFrame;

        public NetworkGatewaySystem(
            int gatewayModuleId,
            int localNodeId,
            INetworkTopology topology,
            EntityLifecycleModule elm,
            int reliableInitTimeoutFrames = -1)
        {
            _gatewayModuleId = gatewayModuleId;
            _localNodeId = localNodeId;
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _elm = elm ?? throw new ArgumentNullException(nameof(elm));
            _reliableInitTimeoutFrames = reliableInitTimeoutFrames > 0 ? reliableInitTimeoutFrames : NetworkConstants.RELIABLE_INIT_TIMEOUT_FRAMES;
            
            _pendingPeerAcks = new Dictionary<Entity, HashSet<int>>();
            _pendingStartFrame = new Dictionary<Entity, uint>();
            
            // Register with ELM so we receive ConstructionOrder events
            _elm.RegisterModule(_gatewayModuleId);
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            uint currentFrame = 0;
            if (view is EntityRepository repo)
            {
                currentFrame = repo.GlobalVersion;
            }
            
            var cmd = view.GetCommandBuffer();
            
            // Ported logic
            ProcessConstructionOrders(view, cmd, currentFrame);
            ProcessDestructionOrders(view);
            CheckPendingAckTimeouts(cmd, currentFrame);
        }
        
        private void ProcessConstructionOrders(ISimulationView view, IEntityCommandBuffer cmd, uint currentFrame)
        {
            var events = view.ConsumeEvents<ConstructionOrder>();
            
            foreach (var evt in events)
            {
                // Only handle entities with PendingNetworkAck component
                if (!view.HasComponent<PendingNetworkAck>(evt.Entity))
                {
                    if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                        FdpLog<NetworkGatewaySystem>.Debug(
                            "Entity {0} missing PendingNetworkAck. ACKing.",
                            evt.Entity.Index);
                    // Fast mode - ACK immediately
                    _elm.AcknowledgeConstruction(evt.Entity, _gatewayModuleId, currentFrame, cmd);
                    continue;
                }
                
                // Reliable mode - determine peers and wait for their ACKs
                var pendingInfo = view.GetComponentRO<PendingNetworkAck>(evt.Entity);
                
                var expectedPeers = _topology.GetExpectedPeers((long)pendingInfo.ExpectedType);
                var peerSet = new HashSet<int>(expectedPeers);
                
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
                    if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                        FdpLog<NetworkGatewaySystem>.Debug(
                            "Entity {0}: No peers. ACKing.",
                            evt.Entity.Index);
                    // No peers to wait for - ACK immediately
                    _elm.AcknowledgeConstruction(evt.Entity, _gatewayModuleId, currentFrame, cmd);
                    cmd.RemoveComponent<PendingNetworkAck>(evt.Entity);
                }
                else
                {
                    if (FdpLog<NetworkGatewaySystem>.IsDebugEnabled)
                        FdpLog<NetworkGatewaySystem>.Debug(
                            "Entity {0}: Waiting for ACKs.",
                            evt.Entity.Index);
                    // Wait for peer ACKs
                    _pendingPeerAcks[evt.Entity] = peerSet;
                    _pendingStartFrame[evt.Entity] = currentFrame;
                }
            }
        }
        
        public void ReceiveLifecycleStatus(Entity entity, int nodeId, EntityLifecycle state, IEntityCommandBuffer cmd, uint currentFrame)
        {
            if (!_pendingPeerAcks.TryGetValue(entity, out var pendingPeers))
                return; // Not waiting for this entity
            
            if (state != EntityLifecycle.Active)
                return; // Only care about Active confirmations
            
            if (pendingPeers.Remove(nodeId))
            {
                // ACK received from node
            }
            
            // Check if all peers have ACKed
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
                var entity = kvp.Key;
                var startFrame = kvp.Value;
                
                if (currentFrame - startFrame > _reliableInitTimeoutFrames)
                {
                    Console.Error.WriteLine($"[NetworkGatewaySystem] Entity {entity.Index}: Timeout waiting for peer ACKs");
                    timedOut.Add(entity);
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
        
        private void ProcessDestructionOrders(ISimulationView view)
        {
            var events = view.ConsumeEvents<DestructionOrder>();
            foreach (var evt in events)
            {
                if (_pendingPeerAcks.ContainsKey(evt.Entity))
                {
                    _pendingPeerAcks.Remove(evt.Entity);
                    _pendingStartFrame.Remove(evt.Entity);
                }
            }
        }
    }
}
