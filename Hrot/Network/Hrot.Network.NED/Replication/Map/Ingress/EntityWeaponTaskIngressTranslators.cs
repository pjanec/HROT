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
    /// ⭐ WL-2b (CE-1049, 📄 docs/DESIGN_Weapon_Layers.md §5d) — applies a published <see cref="WeaponTask"/> to this node's turret part
    /// (the Muscle's): the target's network id becomes this node's entity (unknown ⇒ <see cref="Entity.Null"/>, which the muscle
    /// reports as NoTarget rather than aiming at the point). A sample whose hull or part is not built yet is dropped — the order is
    /// Reliable + TransientLocal, so the next change re-sends it; the recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityWeaponTaskIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityWeaponTask";
        private const long OrdinalValue = (long)EDescriptorType.dtWeaponTask;
        private readonly DdsReader<EntityWeaponTask>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWeaponTaskIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityWeaponTask>(participant, DdsTopicName) : null;
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
                var part = TurretPartLookup.Find(_entityMap, view, sample.Data.ParentNetworkId, sample.Data.PartId);
                if (part.IsNull) { DroppedSampleCount++; continue; }
                if (view is EntityRepository repo && TurretPartLookup.IsRecordedOwner(repo, part, OrdinalValue, sample.Data.PartId)) continue;
                cmd.SetComponent(part, ToTask(sample.Data));
            }
        }

        private WeaponTask ToTask(in EntityWeaponTask d)
        {
            var target = Entity.Null;
            if (d.TargetNetworkId != 0 && !_entityMap.TryGetEntity(d.TargetNetworkId, out target)) target = Entity.Null;
            return new WeaponTask
            {
                Mode = (WeaponTaskMode)d.Mode, Target = target, Point = new System.Numerics.Vector3(d.PointX, d.PointY, d.PointZ),
                MountIndex = d.MountIndex, RoundsGranted = d.RoundsGranted, AmmoGuid = d.AmmoGuid,
                CooldownSeconds = d.CooldownSeconds, Epoch = d.Epoch,
                Arc = (Fdp.Toolkit.Combat.ArcPreference)d.Arc,   // ⭐ ui handoff P2
            };
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ WL-2b (CE-1049, W-3) — applies a published <see cref="WeaponTaskStatus"/> to this node's turret part (the Brain's).</summary>
    public sealed class EntityWeaponTaskStatusIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityWeaponTaskStatus";
        private const long OrdinalValue = (long)EDescriptorType.dtWeaponTaskStatus;
        private readonly DdsReader<EntityWeaponTaskStatus>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWeaponTaskStatusIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityWeaponTaskStatus>(participant, DdsTopicName) : null;
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
                var d = sample.Data;
                var part = TurretPartLookup.Find(_entityMap, view, d.ParentNetworkId, d.PartId);
                if (part.IsNull) { DroppedSampleCount++; continue; }
                if (view is EntityRepository repo && TurretPartLookup.IsRecordedOwner(repo, part, OrdinalValue, d.PartId)) continue;
                cmd.SetComponent(part, new WeaponTaskStatus
                {
                    Epoch = d.Epoch, State = (WeaponTaskState)d.State, Reason = (WeaponTaskFailure)d.Reason, RoundsFired = d.RoundsFired,
                });
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ WL-1b/WL-2b — this node's turret part for a wire key (hull network id, part id): the hull from the entity map, the
    /// part from <see cref="TurretParts.Find"/>; <see cref="Entity.Null"/> when either is not built here (yet).</summary>
    internal static class TurretPartLookup
    {
        public static Entity Find(NetworkEntityMap map, ISimulationView view, long hullNetworkId, int partId)
        {
            int index = TurretParts.IndexOf(partId);
            if (index < 0 || !map.TryGetEntity(hullNetworkId, out var hull)) return Entity.Null;
            return TurretParts.Find(view, hull, index);
        }

        public static bool IsRecordedOwner(EntityRepository repo, Entity part, long descriptor, int partId)
            => AuthorityExtensions.IsRecordedOwner(repo, part, OwnershipExtensions.PackKey(descriptor, partId));
    }
}
