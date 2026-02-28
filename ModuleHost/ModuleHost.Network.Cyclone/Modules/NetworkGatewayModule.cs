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

namespace ModuleHost.Network.Cyclone.Modules
{
    /// <summary>
    /// Network Gateway Module that participates in EntityLifecycleManagement.
    /// For entities with ReliableInit flag, this module withholds its construction
    /// ACK until peer nodes confirm entity activation.
    /// 
    /// NOTE: This is a COPY of ModuleHost.Core.Network.NetworkGatewayModule for the
    /// Cyclone-specific network implementation. The original in Core will be removed
    /// in a later phase of the extraction.
    /// </summary>
    public class NetworkGatewayModule : IModule
    {
        public string Name => "NetworkGateway";
        
        private readonly int _localNodeId;
        private readonly INetworkTopology _topology;
        private readonly EntityLifecycleModule _elm;
        
        // Track pending network ACKs: EntityId -> Set of node IDs we're waiting for
        private readonly Dictionary<Entity, HashSet<int>> _pendingPeerAcks;
        
        // Track when entities entered pending state (for timeout)
        private readonly Dictionary<Entity, uint> _pendingStartFrame;
        
        public int ModuleId { get; }
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        
        public void RegisterSystems(ISystemRegistry registry) { }

        public NetworkGatewayModule(
            int moduleId,
            int localNodeId,
            INetworkTopology topology,
            EntityLifecycleModule elm)
        {
            ModuleId = moduleId;
            _localNodeId = localNodeId;
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _elm = elm ?? throw new ArgumentNullException(nameof(elm));
            _pendingPeerAcks = new Dictionary<Entity, HashSet<int>>();
            _pendingStartFrame = new Dictionary<Entity, uint>();
            
            // Register with ELM so we receive ConstructionOrder events
            _elm.RegisterModule(ModuleId);
        }
        
        public void Tick(ISimulationView view, float deltaTime)
        {
            Tick(view, deltaTime, null);
        }

        public void Tick(ISimulationView view, float deltaTime, uint? frameOverride)
        {
            uint currentFrame = 0;
            
            if (frameOverride.HasValue)
            {
                currentFrame = frameOverride.Value;
            }
            else if (view is EntityRepository repo)
            {
                currentFrame = repo.GlobalVersion;
            }
            
            var cmd = view.GetCommandBuffer();
            
            // Process ConstructionOrder events from ELM
            ProcessConstructionOrders(view, cmd, currentFrame);
            
            // Handle DestructionOrder to clean up pending state
            ProcessDestructionOrders(view, cmd);
            
            // Check for timeouts on pending ACKs
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
                    if (FdpLog<NetworkGatewayModule>.IsDebugEnabled)
                        FdpLog<NetworkGatewayModule>.Debug(
                            "Entity {0} missing PendingNetworkAck. ACKing.",
                            evt.Entity.Index);
                    // Fast mode - ACK immediately
                    _elm.AcknowledgeConstruction(evt.Entity, ModuleId, currentFrame, cmd);
                    continue;
                }
                
                // Reliable mode - determine peers and wait for their ACKs
                // NetworkSpawnerSystem populates PendingNetworkAck with ExpectedType
                var pendingInfo = view.GetComponentRO<PendingNetworkAck>(evt.Entity);
                
                var expectedPeers = _topology.GetExpectedPeers((long)pendingInfo.ExpectedType);
                var peerSet = new HashSet<int>(expectedPeers);
                
                if (FdpLog<NetworkGatewayModule>.IsDebugEnabled)
                {
                    var peerList = string.Join(",", peerSet);
                    FdpLog<NetworkGatewayModule>.Debug(
                        "Entity {0}: Reliable mode. Peers: {1}",
                        evt.Entity.Index,
                        peerList);
                }

                if (peerSet.Count == 0)
                {
                    if (FdpLog<NetworkGatewayModule>.IsDebugEnabled)
                        FdpLog<NetworkGatewayModule>.Debug(
                            "Entity {0}: No peers. ACKing.",
                            evt.Entity.Index);
                    // No peers to wait for - ACK immediately
                    _elm.AcknowledgeConstruction(evt.Entity, ModuleId, currentFrame, cmd);
                    cmd.RemoveComponent<PendingNetworkAck>(evt.Entity);
                }
                else
                {
                    if (FdpLog<NetworkGatewayModule>.IsDebugEnabled)
                        FdpLog<NetworkGatewayModule>.Debug(
                            "Entity {0}: Waiting for ACKs.",
                            evt.Entity.Index);
                    // Wait for peer ACKs
                    _pendingPeerAcks[evt.Entity] = peerSet;
                    _pendingStartFrame[evt.Entity] = currentFrame;
                }
            }
        }
        
        /// <summary>
        /// Called by EntityLifecycleStatusTranslator when a peer status message arrives.
        /// </summary>
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
                _elm.AcknowledgeConstruction(entity, ModuleId, currentFrame, cmd);
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
                
                if (currentFrame - startFrame > NetworkConstants.RELIABLE_INIT_TIMEOUT_FRAMES)
                {
                    Console.Error.WriteLine($"[NetworkGatewayModule] Entity {entity.Index}: Timeout waiting for peer ACKs");
                    timedOut.Add(entity);
                }
            }
            
            foreach (var entity in timedOut)
            {
                // Timeout - ACK anyway to prevent blocking forever
                _elm.AcknowledgeConstruction(entity, ModuleId, currentFrame, cmd);
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
                if (_pendingPeerAcks.ContainsKey(evt.Entity))
                {
                    _pendingPeerAcks.Remove(evt.Entity);
                    _pendingStartFrame.Remove(evt.Entity);
                }

                cmd.PublishEvent(new DestructionAck
                {
                    Entity = evt.Entity,
                    ModuleId = ModuleId,
                    Success = true
                });
            }
        }
    }
}
