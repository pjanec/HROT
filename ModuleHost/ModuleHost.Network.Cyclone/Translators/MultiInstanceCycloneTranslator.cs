using System;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Utilities;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Extensions;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Network.Cyclone.Abstractions;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

namespace ModuleHost.Network.Cyclone.Translators
{
    /// <summary>
    /// Translator for multi-instance descriptors (EntityId + InstanceId).
    /// Routes samples to child entities based on InstanceId.
    /// </summary>
    public unsafe class MultiInstanceCycloneTranslator<T> : IDescriptorTranslator, INetworkReplayTarget
        where T : unmanaged
    {
        private readonly DdsReader<T> _reader;
        private readonly DdsWriter<T> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; }
        public long DescriptorOrdinal { get; }

        public MultiInstanceCycloneTranslator(
            DdsParticipant participant, 
            string topicName, 
            long ordinal,
            NetworkEntityMap entityMap)
        {
            if (!MultiInstanceLayout<T>.IsValid)
                throw new InvalidOperationException(
                    $"Type {typeof(T).Name} must have EntityId and InstanceId fields");

            TopicName = topicName;
            DescriptorOrdinal = ordinal;
            _entityMap = entityMap;

            _reader = new DdsReader<T>(participant);
            _writer = new DdsWriter<T>(participant);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();
            
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ProcessSample(sample.Data, cmd, view);
            }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            var samples = MemoryMarshal.Cast<byte, T>(rawData);
            foreach (ref readonly var sample in samples)
            {
                ProcessSample(sample, cmd, view);
            }
        }

        private void ProcessSample(T data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = MultiInstanceLayout<T>.ReadEntityId(&data);
            long instId = MultiInstanceLayout<T>.ReadInstanceId(&data);

            if (!_entityMap.TryGetEntity(netId, out Entity rootEntity))
                return;

            // Resolve target (root if instId==0, child otherwise)
            Entity targetEntity = rootEntity;

            if (instId > 0)
            {
                if (view.HasManagedComponent<ChildMap>(rootEntity))
                {
                    var map = view.GetManagedComponentRO<ChildMap>(rootEntity);
                    if (!map.InstanceToEntity.TryGetValue((int)instId, out targetEntity))
                        return; // Child not spawned yet
                }
                else
                {
                    return; // No child map
                }
            }

            cmd.SetComponent(targetEntity, data);
        }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query().With<T>().Build();
            
            foreach (var entity in query)
            {
                long netIdValue = 0;
                long instIdValue = 0;

                // Case A: Child part
                if (view.HasComponent<PartMetadata>(entity))
                {
                    ref readonly var partMeta = ref view.GetComponentRO<PartMetadata>(entity);
                    
                    if (view.HasComponent<NetworkIdentity>(partMeta.ParentEntity))
                    {
                        netIdValue = view.GetComponentRO<NetworkIdentity>(partMeta.ParentEntity).Value;
                        instIdValue = partMeta.InstanceId;
                    }
                }
                // Case B: Root
                else if (view.HasComponent<NetworkIdentity>(entity))
                {
                    netIdValue = view.GetComponentRO<NetworkIdentity>(entity).Value;
                    instIdValue = 0;
                }

                if (netIdValue == 0) continue;

                // Check authority (supports split authority per instance)
                long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, instIdValue);
                if (!view.HasAuthority(entity, packedKey)) 
                    continue;

                T copy = view.GetComponentRO<T>(entity);
                MultiInstanceLayout<T>.WriteEntityId(&copy, netIdValue);
                MultiInstanceLayout<T>.WriteInstanceId(&copy, instIdValue);

                _writer.Write(copy);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            T keySample = default;
            MultiInstanceLayout<T>.WriteEntityId(&keySample, networkEntityId);
            MultiInstanceLayout<T>.WriteInstanceId(&keySample, 0); 
            _writer.DisposeInstance(keySample);
        }
    }
}

