using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using CycloneDDS.Core; // Added for InstanceState
using Fdp.Kernel;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Network.Cyclone.Components;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Network.Cyclone.Services;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Network.Cyclone.Topics;
using FDP.Kernel.Logging;
using ModuleHost.Network.Cyclone.Abstractions;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

namespace ModuleHost.Network.Cyclone.Translators
{
    public class EntityMasterTranslator : IDescriptorTranslator, INetworkReplayTarget
    {
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeIdMapper _nodeMapper;
        private readonly TypeIdMapper _typeMapper;
        
        private readonly DdsReader<EntityMasterTopic> _reader;
        private readonly DdsWriter<EntityMasterTopic> _writer;

        public string TopicName => "SST_EntityMaster";
        public long DescriptorOrdinal => -1;

        public EntityMasterTranslator(NetworkEntityMap entityMap, NodeIdMapper nodeMapper, TypeIdMapper typeMapper, DdsParticipant participant)
        {
            _entityMap = entityMap;
            _nodeMapper = nodeMapper;
            _typeMapper = typeMapper;
            
            _reader = new DdsReader<EntityMasterTopic>(participant);
            _writer = new DdsWriter<EntityMasterTopic>(participant);
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }


        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                var info = sample.Info;
                if (info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                {
                    // For NotAlive samples, Data field depends on implementation but usually contains key fields.
                    // Assuming Data is valid enough to read EntityId.
                    // DdsSample.Data throws if ValidData is false, so we bypass it via FromNative directly.
                    var disposalTopic = DdsTypeSupport.FromNative<EntityMasterTopic>(sample.NativePtr);
                    
                    if (_entityMap.TryGetEntity(disposalTopic.EntityId, out var entityToDestroy))
                    {
                         FdpLog<EntityMasterTranslator>.Info(
                             "Received Death Note for {0} (NotAlive). Mapped to {1}. Destroying...",
                             disposalTopic.EntityId,
                             entityToDestroy);
                        cmd.DestroyEntity(entityToDestroy);
                        _entityMap.Unregister(disposalTopic.EntityId, 0); // Assuming instance 0 for Master
                    }
                    else 
                    {
                        FdpLog<EntityMasterTranslator>.Info(
                            "Processing NotAlive for EntityId {0} (Mapped: False)",
                            disposalTopic.EntityId);
                    }
                    continue;
                }

                var topic = sample.Data;
                Decode(in topic, cmd, view);
            }
        }

        public void ScanAndPublish(ISimulationView view)
        {
            // Iterate all entities that have Identity, SpawnRequest and Ownership
            // We only publish if we are the owner.
            // Use WithLifecycle(All) so that entities in the Constructing state are
            // also announced to remote peers. Remote nodes must receive the EntityMaster
            // before they can create a ghost and send the construction ACK back.
            
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<NetworkSpawnRequest>()
                .With<NetworkOwnership>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach(var entity in query)
            {
                    // Check ownership
                    ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                    if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                        continue;

                    ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(entity);
                    ref readonly var spawn = ref view.GetComponentRO<NetworkSpawnRequest>(entity);
                    
                    var topic = new EntityMasterTopic
                    {
                        EntityId = identity.Value,
                        OwnerId = _nodeMapper.GetExternalId(ownership.LocalNodeId),
                        DisTypeValue = spawn.DisType,
                        TkbTypeValue = spawn.TkbType,
                        Flags = 0 
                    };
                    
                    _writer.Write(topic);
            }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            var samples = MemoryMarshal.Cast<byte, EntityMasterTopic>(rawData);
            foreach (ref readonly var sample in samples)
            {
                Decode(in sample, cmd, view);
            }
        }

        private void Decode(in EntityMasterTopic topic, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (topic.Flags == 0xDEAD)
            {
                if (_entityMap.TryGetEntity(topic.EntityId, out var entityToDestroy))
                {
                    FdpLog<EntityMasterTranslator>.Info(
                        "Received Death Note for {0}. Mapped to {1}. Destroying...",
                        topic.EntityId,
                        entityToDestroy);
                    cmd.DestroyEntity(entityToDestroy);
                    _entityMap.Unregister(topic.EntityId, 0);
                }
                else
                {
                        FdpLog<EntityMasterTranslator>.Warn(
                            "Received Death Note for {0} but it was not found in EntityMap.",
                            topic.EntityId);
                }
                return;
            }

            // Map Owner
            int ownerNodeId = _nodeMapper.GetOrRegisterInternalId(topic.OwnerId);

            // Ignore loopback
            if (ownerNodeId == _nodeMapper.LocalNodeId) return;
            
            // Check if we already have this entity
            if (_entityMap.TryGetEntity(topic.EntityId, out var existingEntity))
            {
                // Update existing
                if (view.HasComponent<NetworkOwnership>(existingEntity))
                {
                    var ownership = view.GetComponentRO<NetworkOwnership>(existingEntity);
                    if (ownership.PrimaryOwnerId != ownerNodeId)
                    {
                        // Create copy with new owner
                        var newOwnership = new NetworkOwnership
                        {
                            PrimaryOwnerId = ownerNodeId,
                            LocalNodeId = ownership.LocalNodeId
                        };
                        cmd.SetComponent(existingEntity, newOwnership);
                    }
                }

                if (view.HasComponent<NetworkAuthority>(existingEntity))
                {
                    var auth = view.GetComponentRO<NetworkAuthority>(existingEntity);
                    if (auth.PrimaryOwnerId != ownerNodeId)
                    {
                            cmd.SetComponent(existingEntity, new NetworkAuthority(ownerNodeId, ownerNodeId));
                    }
                }
                else
                {
                        cmd.AddComponent(existingEntity, new NetworkAuthority(ownerNodeId, ownerNodeId));
                }
            }
            else
            {
                // Create new PROXY entity
                var repo = view as EntityRepository;
                if (repo == null) 
                {
                        FdpLog<EntityMasterTranslator>.Error("Cannot create proxy: View is not EntityRepository");
                        return;
                }

                var newEntity = repo.CreateEntity();
                repo.SetLifecycleState(newEntity, EntityLifecycle.Ghost);
                
                cmd.AddComponent(newEntity, new NetworkIdentity { Value = topic.EntityId });
                
                // Resolve TkbType: prefer the value carried in the topic; fall back to DisTypeValue
                // for legacy senders that pre-date TkbTypeValue.
                long resolvedTkbType = topic.TkbTypeValue != 0 ? topic.TkbTypeValue : (long)topic.DisTypeValue;
                cmd.AddComponent(newEntity, new NetworkSpawnRequest 
                { 
                    DisType = topic.DisTypeValue,
                    TkbType = resolvedTkbType,
                    OwnerId = (ulong)ownerNodeId 
                });

                cmd.AddComponent(newEntity, new NetworkOwnership 
                { 
                    PrimaryOwnerId = ownerNodeId,
                    LocalNodeId = _nodeMapper.LocalNodeId 
                });

                cmd.AddComponent(newEntity, new NetworkAuthority(ownerNodeId, ownerNodeId));

                _entityMap.Register(topic.EntityId, newEntity);
                FdpLog<EntityMasterTranslator>.Info(
                    "Created Proxy Entity {0} for NetID {1}",
                    newEntity,
                    topic.EntityId);
            }
        }
        
        public void Dispose(long networkEntityId) 
        {
            var keySample = new EntityMasterTopic { EntityId = networkEntityId };
            _writer.DisposeInstance(keySample);
        }
    }
}
