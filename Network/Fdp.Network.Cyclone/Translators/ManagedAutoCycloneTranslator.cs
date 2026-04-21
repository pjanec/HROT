using System;
using System.IO;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Components; 
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Abstractions;

namespace Fdp.Network.Cyclone.Translators
{
    public class ManagedAutoCycloneTranslator<T> : IDescriptorTranslator, INetworkReplayTarget
        where T : class, new() 
    {
        private readonly DdsReader<T> _reader;
        private readonly DdsWriter<T> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public ManagedAutoCycloneTranslator(DdsParticipant p, string topic, int ordinal, NetworkEntityMap map, GhostCreationSystem ghostCreationSystem)
        {
            if (!ManagedAccessor<T>.IsValid) 
                throw new InvalidOperationException($"Managed type {typeof(T).Name} missing EntityId");

            _reader = new DdsReader<T>(p);
            _writer = new DdsWriter<T>(p);
            _entityMap = map;
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            TopicName = topic;
            DescriptorOrdinal = ordinal;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;

                // 1. Reference Copy (Cheap)
                // CycloneDDS deserialized this into a new object on the heap
                T data = sample.Data; 

                // 2. Fast Accessor
                long netId = ManagedAccessor<T>.GetId(data);

                if (!_entityMap.TryGetEntity(netId, out Entity entity))
                {
                    // Entity not yet known — create a ghost so the promotion pipeline
                    // can drive it once all mandatory descriptors have arrived.
                    var repo = view as EntityRepository;
                    if (repo == null) continue;
                    entity = _ghostCreationSystem.CreateGhost(repo, netId, view.Tick);
                }

                cmd.SetManagedComponent(entity, data);
            }
        }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query().WithManaged<T>().With<NetworkIdentity>().Build();

            foreach (var entity in query)
            {
                // 1. Get Reference
                T component = view.GetManagedComponentRO<T>(entity);
                
                // 2. Check Authority (Assuming standard check)
                if (!view.HasAuthority(entity)) continue;

                // 3. Get Network ID
                long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;

                // 4. Patch ID
                // WARNING: This modifies the live object in the ECS! 
                // For Managed components, this is usually acceptable as EntityId 
                // should match NetworkId anyway.
                ManagedAccessor<T>.SetId(component, netId);

                // 5. Write (Serialization happens here)
                _writer.Write(component);
                SentSampleCount++;
            }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // Deserialize directly to resolve ID
            using var ms = new MemoryStream(rawData.ToArray());
            using var reader = new BinaryReader(ms);
            
            var data = FdpAutoSerializer.Deserialize<T>(reader);
            
            long netId = ManagedAccessor<T>.GetId(data);
            if (_entityMap.TryGetEntity(netId, out Entity entity))
            {
                cmd.SetManagedComponent(entity, data);
            }
        }
        
        public long DescriptorOrdinal { get; }
        public string TopicName { get; }
        public long ReceivedSampleCount { get; protected set; }
        public long SentSampleCount { get; protected set; }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long id) 
        { 
            try
            {
                var instance = new T();
                ManagedAccessor<T>.SetId(instance, id);
                _writer.DisposeInstance(instance);
            }
            catch(Exception)
            {
                // Ignore failure to dispose managed instance
            }
        }
    }
}
