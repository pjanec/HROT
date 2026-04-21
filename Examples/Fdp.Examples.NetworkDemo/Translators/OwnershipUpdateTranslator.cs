using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using ToolkitMsgs = Fdp.Toolkit.Replication.Messages;
using TopicMsgs = Fdp.Network.Cyclone.Topics;
using Fdp.Toolkit.Replication.Extensions; 
using Fdp.Core.Logging;
using Fdp.Network.Cyclone.Services;
using Fdp.Network.Cyclone;
using Fdp.Network.Cyclone.Abstractions;
using CycloneDDS.Runtime;

namespace Fdp.Examples.NetworkDemo.Translators
{
    public class OwnershipUpdateTranslator : Fdp.Interfaces.IDescriptorTranslator, INetworkReplayTarget, IDisposable
    {
        private readonly NodeIdMapper _nodeMapper;
        private readonly DdsReader<TopicMsgs.OwnershipUpdate> _reader;
        private readonly DdsWriter<TopicMsgs.OwnershipUpdate> _writer;

        public string TopicName => "OwnershipUpdate";
        public long DescriptorOrdinal => -1; 
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        
        public OwnershipUpdateTranslator(NodeIdMapper nodeMapper, DdsParticipant participant)
        {
            _nodeMapper = nodeMapper;
             _reader = new DdsReader<TopicMsgs.OwnershipUpdate>(participant);
             _writer = new DdsWriter<TopicMsgs.OwnershipUpdate>(participant);
        }
        
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void ScanAndPublish(ISimulationView view)
        {
            var toolkitEvents = view.ReadEvents<ToolkitMsgs.OwnershipUpdate>();
            
            foreach (var evt in toolkitEvents)
            {
                var (typeId, instanceId) = OwnershipExtensions.UnpackKey(evt.PackedKey);
                
                int newOwnerGlobalId = -1;
                try 
                {
                    var extId = _nodeMapper.GetExternalId(evt.NewOwnerNodeId);
                    newOwnerGlobalId = extId.AppInstanceId;
                }
                catch (Exception ex)
                {
                    FdpLog<OwnershipUpdateTranslator>.Error(
                        "Failed to map Internal ID {0} to External ID: {1}",
                        evt.NewOwnerNodeId,
                        ex.Message);
                    continue; 
                }

                var topicMsg = new TopicMsgs.OwnershipUpdate
                {
                    EntityId = evt.NetworkId.Value,
                    DescrTypeId = typeId,
                    InstanceId = instanceId,
                    NewOwner = newOwnerGlobalId
                };
                
                _writer.Write(topicMsg);
                SentSampleCount++;
            }
        }

         public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (view is not EntityRepository repo) return; 

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState == CycloneDDS.Runtime.DdsInstanceState.Alive) // Fully qualified
                {
                    ReceivedSampleCount++;
                    var topicMsg = sample.Data;
                    
                    int internalOwnerId = _nodeMapper.GetOrRegisterInternalId(new TopicMsgs.NetworkAppId { AppDomainId = 0, AppInstanceId = topicMsg.NewOwner });

                    long packedKey = OwnershipExtensions.PackKey(topicMsg.DescrTypeId, topicMsg.InstanceId);
                    
                    var toolkitMsg = new ToolkitMsgs.OwnershipUpdate
                    {
                        NetworkId = new NetworkIdentity { Value = topicMsg.EntityId },
                        PackedKey = packedKey,
                        NewOwnerNodeId = internalOwnerId
                    };
                    
                    repo.Bus.Publish(toolkitMsg);
                }
            }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (view is not EntityRepository repo) return;
            
            var replayMsgs = MemoryMarshal.Cast<byte, TopicMsgs.OwnershipUpdate>(rawData);
            foreach (var topicMsg in replayMsgs)
            {
                // Logic duplicated from PollIngress loop
                int internalOwnerId = _nodeMapper.GetOrRegisterInternalId(new TopicMsgs.NetworkAppId { AppDomainId = 0, AppInstanceId = topicMsg.NewOwner });

                long packedKey = OwnershipExtensions.PackKey(topicMsg.DescrTypeId, topicMsg.InstanceId);
                
                var toolkitMsg = new ToolkitMsgs.OwnershipUpdate
                {
                    NetworkId = new NetworkIdentity { Value = topicMsg.EntityId },
                    PackedKey = packedKey,
                    NewOwnerNodeId = internalOwnerId
                };
                
                repo.Bus.Publish(toolkitMsg);
            }
        }
        
        public void Dispose(long networkEntityId) { }
        
        public void Dispose()
        {
            _reader?.Dispose();
            _writer?.Dispose();
        }
    }
}
