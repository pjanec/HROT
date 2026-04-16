using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Time.Messages;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Network.Cyclone.Topics; // For NetworkAppId

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class PacketBridgeSystem : IEcsModuleSystem
    {
        private readonly FdpEventBus _bus;
        private readonly bool _isMaster;
        private readonly int _localNodeId;
        
        // Slave side: The entity used to send ACKs
        private Entity _localAckEntity = Entity.Null;
        private long _lastEmittedOrderFrame = -1;

        // Master side: Track last ack processing
        private Dictionary<int, long> _lastAckForwarded = new Dictionary<int, long>();

        public PacketBridgeSystem(FdpEventBus bus, bool isMaster, int localNodeId)
        {
            FdpLog<PacketBridgeSystem>.Info(
                "[PacketBridgeSystem] Created. IsMaster={0}, LocalNodeId={1}",
                isMaster,
                localNodeId);
            _bus = bus;
            _isMaster = isMaster;
            _localNodeId = localNodeId;
            
            if (_isMaster) 
                _bus.Register<FrameOrderDescriptor>();
            else 
                _bus.Register<FrameAckDescriptor>();
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (_isMaster) ExecuteMaster(view);
            else ExecuteSlave(view);
        }

        private void ExecuteMaster(ISimulationView view)
        {
            var cmds = view.GetCommandBuffer();
            
            // 1. Bridge Order (Event -> Component)
            var timeQuery = view.Query().With<TimeModeComponent>().Build();
            Entity timeEnt = Entity.Null;
            foreach(var e in timeQuery) { timeEnt = e; break; }
            
            if (timeEnt != Entity.Null)
            {
                foreach (var order in _bus.Read<FrameOrderDescriptor>())
                {
                     FdpLog<PacketBridgeSystem>.Info(
                         "[Bridge-Master] Forwarding Order {0}",
                         order.FrameID);
                     ref readonly var prev = ref view.GetComponentRO<TimeModeComponent>(timeEnt);
                     var next = prev;
                     next.FrameNumber = order.FrameID;
                     next.FixedDelta = order.FixedDelta;
                     cmds.SetComponent(timeEnt, next);
                }
            }
            
            // 2. Bridge Acks (Component -> Event)
            var ackQuery = view.Query().With<FrameAckComponent>().Build();
            foreach(var e in ackQuery)
            {
                ref readonly var ack = ref view.GetComponentRO<FrameAckComponent>(e);
                
                // Filter out self-acks or invalid IDs
                if (ack.SenderNodeId == _localNodeId || ack.SenderNodeId == 0) continue;

                if (!_lastAckForwarded.ContainsKey(ack.SenderNodeId) || _lastAckForwarded[ack.SenderNodeId] < ack.CompletedFrameId)
                {
                    FdpLog<PacketBridgeSystem>.Info(
                        "[Bridge-Master] Forwarding Component Ack ({0}, {1}) to EventBus",
                        ack.SenderNodeId,
                        ack.CompletedFrameId);
                    _bus.Publish(new FrameAckDescriptor {
                        FrameID = ack.CompletedFrameId,
                        NodeID = ack.SenderNodeId
                    });
                    _lastAckForwarded[ack.SenderNodeId] = ack.CompletedFrameId;
                }
            }
        }

        private void ExecuteSlave(ISimulationView view)
        {
            // ... (keep order logic)
            var cmds = view.GetCommandBuffer();

            // 1. Bridge Order (Component -> Event)
            var timeQuery = view.Query().With<TimeModeComponent>().Build();
            foreach(var e in timeQuery)
            {
                ref readonly var mode = ref view.GetComponentRO<TimeModeComponent>(e);
                
                if (mode.FrameNumber > _lastEmittedOrderFrame)
                {
                     FdpLog<PacketBridgeSystem>.Info(
                         "[Bridge-Slave] Received Global Order {0}",
                         mode.FrameNumber);
                    _bus.Publish(new FrameOrderDescriptor {
                        FrameID = mode.FrameNumber,
                        FixedDelta = mode.FixedDelta,
                        SequenceID = mode.FrameNumber
                    });
                    _lastEmittedOrderFrame = mode.FrameNumber;
                }
            }
            
            // 2. Bridge Ack (Event -> Component)
            if (_localAckEntity == Entity.Null)
            {
                _localAckEntity = cmds.CreateEntity();
                long netId = 10000 + _localNodeId;
                cmds.AddComponent(_localAckEntity, new NetworkIdentity { Value = netId });
                cmds.AddComponent(_localAckEntity, new FrameAckComponent { SenderNodeId = _localNodeId, CompletedFrameId = -1, EntityId = netId });
                
                // Set Authority to Self so we can replicate updates OUT
                cmds.AddComponent(_localAckEntity, new NetworkAuthority { 
                     LocalNodeId = _localNodeId,
                     PrimaryOwnerId = _localNodeId
                });
            }
            
            foreach (var ack in _bus.Read<FrameAckDescriptor>())
            {
                FdpLog<PacketBridgeSystem>.Info(
                    "[Bridge-Slave] Sending Ack Local({0}) Frame({1}) to Component",
                    ack.NodeID,
                    ack.FrameID);
                cmds.SetComponent(_localAckEntity, new FrameAckComponent {
                    EntityId = 10000 + _localNodeId,
                    SenderNodeId = _localNodeId,
                    CompletedFrameId = ack.FrameID
                });
            }
        }
    }
}
