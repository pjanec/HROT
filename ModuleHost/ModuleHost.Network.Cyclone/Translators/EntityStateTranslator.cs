using System;
using CycloneDDS.Runtime;
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
using System.Runtime.InteropServices;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

namespace ModuleHost.Network.Cyclone.Translators
{
    public class EntityStateTranslator : IDescriptorTranslator, INetworkReplayTarget
    {
        private readonly NetworkEntityMap _entityMap;
        private readonly DdsReader<EntityStateTopic> _reader;
        private readonly DdsWriter<EntityStateTopic> _writer;
        
        public string TopicName => "SST_EntityState";
        public long DescriptorOrdinal => -1;

        public EntityStateTranslator(NetworkEntityMap entityMap, DdsParticipant participant)
        {
            _entityMap = entityMap;
            _reader = new DdsReader<EntityStateTopic>(participant);
            _writer = new DdsWriter<EntityStateTopic>(participant);
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            var samples = MemoryMarshal.Cast<byte, EntityStateTopic>(rawData);
            foreach (ref readonly var sample in samples)
            {
                Decode(in sample, cmd);
            }
        }

        private void Decode(in EntityStateTopic topic, IEntityCommandBuffer cmd)
        {
            if (_entityMap.TryGetEntity(topic.EntityId, out var entity))
            {
                    // Update NetworkPosition
                    cmd.SetComponent(entity, new NetworkPosition { Value = new System.Numerics.Vector3((float)topic.PositionX, (float)topic.PositionY, (float)topic.PositionZ) });
                    cmd.SetComponent(entity, new NetworkVelocity { Value = new System.Numerics.Vector3(topic.VelocityX, topic.VelocityY, topic.VelocityZ) });
                    cmd.SetComponent(entity, new NetworkOrientation { Value = new System.Numerics.Quaternion(topic.OrientationX, topic.OrientationY, topic.OrientationZ, topic.OrientationW) });
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var topic = sample.Data;
                Decode(in topic, cmd);
            }
        }

        public void ScanAndPublish(ISimulationView view)
        {
            // Publish local owned entities
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<NetworkOwnership>()
                .With<NetworkPosition>() // Must have position to publish state
                .Build();

            foreach(var entity in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;

                ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var pos = ref view.GetComponentRO<NetworkPosition>(entity);
                
                // Optional components
                var vel = view.HasComponent<NetworkVelocity>(entity) ? view.GetComponentRO<NetworkVelocity>(entity).Value : System.Numerics.Vector3.Zero;
                var rot = view.HasComponent<NetworkOrientation>(entity) ? view.GetComponentRO<NetworkOrientation>(entity).Value : System.Numerics.Quaternion.Identity;

                var topic = new EntityStateTopic
                {
                    EntityId = identity.Value,
                    PositionX = pos.Value.X,
                    PositionY = pos.Value.Y,
                    PositionZ = pos.Value.Z,
                    VelocityX = vel.X,
                    VelocityY = vel.Y,
                    VelocityZ = vel.Z,
                    OrientationX = rot.X,
                    OrientationY = rot.Y,
                    OrientationZ = rot.Z,
                    OrientationW = rot.W,
                    Timestamp = (long)view.Tick
                };

                _writer.Write(topic);
            }
        }

        public void Dispose(long networkEntityId) { }
    }
}
