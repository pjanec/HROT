using System;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a (O3) — applies a static obstacle's published box on every node that does not own it. It is the
    /// <c>[PerInstanceValue]</c> its ghost promotion waits for, so the obstacle is baked with the creator's size, never the TKB
    /// default. 📄 docs/DESIGN_Peek_And_Fire.md §9. The rules of <see cref="EntityDoorStateIngressTranslator"/>: an unknown id
    /// makes a ghost, and the recorded owner of <c>dtObstacleShape</c> never takes its own sample back.
    /// </summary>
    public sealed class EntityObstacleShapeIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityObstacleShape";
        private const long OrdinalValue = (long)EDescriptorType.dtObstacleShape;

        private readonly DdsReader<EntityObstacleShape>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityObstacleShapeIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem, long localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityObstacleShape>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        private void Apply(in EntityObstacleShape data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityObstacleShapeIngressTranslator>.Warn(
                        "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            if (view is EntityRepository ownerCheck && OwnsTheDescriptor(ownerCheck, entity)) return;
            cmd.SetComponent(entity, ToShape(data));
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityObstacleShape shape || OwnsTheDescriptor(repo, entity)) return;
            repo.SetComponent(entity, ToShape(shape));
        }

        public void Dispose(long networkEntityId) { }

        private static ObstacleShape ToShape(in EntityObstacleShape d)
            => new() { Length = d.Length, Width = d.Width, Height = d.Height };

        private static bool OwnsTheDescriptor(EntityRepository repo, Entity entity)
            => Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.IsRecordedOwner(
                repo, entity, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
