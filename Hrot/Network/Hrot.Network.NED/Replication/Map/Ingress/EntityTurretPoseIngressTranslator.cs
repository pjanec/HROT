using System;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ WL-1b (CE-1049) — applies a published turret pose to this node's turret part. 📄 docs/DESIGN_Weapon_Layers.md §5b, W-9.
    /// The part was built here by the spawn (<c>TurretTkbTranslator</c>, WL-1a), so the sample is only looked up, never creates
    /// anything: the hull comes from <see cref="NetworkEntityMap"/>, the part from <see cref="TurretParts.Find"/>. A sample whose hull
    /// or part is not built yet is dropped — the next change or heartbeat lands once it is. The node holding the RECORDED ownership of
    /// <c>(dtTurretPose, part)</c> never takes its own sample back (S8 / F-5).
    /// </summary>
    public sealed class EntityTurretPoseIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityTurretPose";
        private const long OrdinalValue = (long)EDescriptorType.dtTurretPose;

        private readonly DdsReader<EntityTurretPose>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples dropped because the hull or its turret part is not on this node (yet).</summary>
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityTurretPoseIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityTurretPose>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

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

        private void Apply(in EntityTurretPose data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            var part = PartOf(data, view);
            if (part.IsNull) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo && OwnsThePose(repo, part, data.PartId)) return;
            cmd.SetComponent(part, new TurretPose { Azimuth = data.Azimuth, Elevation = data.Elevation });
        }

        private Entity PartOf(in EntityTurretPose data, ISimulationView view)
            => TurretPartLookup.Find(_entityMap, view, data.ParentNetworkId, data.PartId);

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityTurretPose pose) return;
            var part = PartOf(pose, repo);
            if (part.IsNull || OwnsThePose(repo, part, pose.PartId)) return;
            repo.SetComponent(part, new TurretPose { Azimuth = pose.Azimuth, Elevation = pose.Elevation });
        }

        public void Dispose(long networkEntityId) { }

        private static bool OwnsThePose(EntityRepository repo, Entity part, int partId)
            => TurretPartLookup.IsRecordedOwner(repo, part, OrdinalValue, partId);
    }
}
