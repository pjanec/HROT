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
    /// ⭐ Buildings Stage 5b — applies a terrain door's published state (and its key) on every node that does not own it.
    /// 📄 docs/DESIGN_Building_Interiors.md §3j. Every reader on this node then builds its door table from the view it runs on (<c>DoorStates.Of</c>, R-219).
    /// <para>Same rules as <see cref="EntityDamageIngressTranslator"/>: an unknown id makes a ghost (TransientLocal can deliver the
    /// state before the master), and the node that holds the RECORDED ownership of <c>dtDoorState</c> never takes its own sample
    /// back (S8 / F-5). ⚠ A plain <see cref="IDescriptorTranslator"/> with its own reader, not a <c>CycloneTranslator</c>: the
    /// sample carries a string (the key), and that base takes unmanaged samples only — the shape
    /// <see cref="MapVisualOverlayIngressTranslator"/> has for the same reason.</para>
    /// </summary>
    public sealed class EntityDoorStateIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDoorState";
        private const long OrdinalValue = (long)EDescriptorType.dtDoorState;

        private readonly DdsReader<EntityDoorState>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityDoorStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem, long localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityDoorState>(participant, DdsTopicName) : null;
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

        private void Apply(in EntityDoorState data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityDoorStateIngressTranslator>.Warn(
                        "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            if (view is EntityRepository ownerCheck && OwnsTheDoorDescriptor(ownerCheck, entity)) return;

            cmd.SetComponent(entity, new DoorState { State = (TerrainDoorState)data.State });
            if (!string.IsNullOrEmpty(data.Key)
                && !(view is EntityRepository r && r.HasComponent<TerrainObjectKey>(entity) && r.GetComponent<TerrainObjectKey>(entity).Key == data.Key))
                cmd.SetManagedComponent(entity, new TerrainObjectKey { Key = data.Key });
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityDoorState door || OwnsTheDoorDescriptor(repo, entity)) return;
            repo.SetComponent(entity, new DoorState { State = (TerrainDoorState)door.State });
            if (!string.IsNullOrEmpty(door.Key)) repo.SetManagedComponent(entity, new TerrainObjectKey { Key = door.Key });
        }

        public void Dispose(long networkEntityId) { }

        private static bool OwnsTheDoorDescriptor(EntityRepository repo, Entity entity)
            => Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.IsRecordedOwner(
                repo, entity, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
