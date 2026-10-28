using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ WL-2b (CE-1049, 📄 docs/DESIGN_Weapon_Layers.md §5d, W-2/W-11) — publishes a turret part's <see cref="WeaponTask"/> from the
    /// node owning <c>(dtWeaponTask, part)</c> — the Brain — keyed (hull network id, part id), on change. The target entity crosses as
    /// its network id (0 = aim at the point).
    /// </summary>
    public sealed class EntityWeaponTaskEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityWeaponTask>? _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<(long, int), EntityWeaponTask> _last = new();
        private int _worldEpoch;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWeaponTask>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponTask;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponTaskEgressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _writer = participant is not null ? new DdsWriter<EntityWeaponTask>(participant) : null;
            _entityMap = entityMap;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            foreach (var part in view.Query().With<PartMetadata>().With<WeaponTask>().Build())
            {
                if (!TurretPartKey.TryResolve(view, part, DescriptorOrdinal, out var hullNet, out int partId)) continue;
                var task = view.GetComponentRO<WeaponTask>(part);
                long targetNet = 0;
                if (!task.Target.IsNull && !_entityMap.TryGetNetworkId(task.Target, out targetNet)) targetNet = 0;
                var sample = new EntityWeaponTask
                {
                    ParentNetworkId = hullNet, PartId = partId, Mode = (byte)task.Mode, TargetNetworkId = targetNet,
                    PointX = task.Point.X, PointY = task.Point.Y, PointZ = task.Point.Z,
                    MountIndex = task.MountIndex, RoundsGranted = task.RoundsGranted, AmmoGuid = task.AmmoGuid,
                    CooldownSeconds = task.CooldownSeconds, Epoch = task.Epoch,
                    Arc = (byte)task.Arc,   // ⭐ ui handoff P2
                };
                if (_last.TryGetValue((hullNet, partId), out var prev) && prev.Equals(sample)) continue;
                _last[(hullNet, partId)] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        public void Dispose(long networkEntityId) => TurretPartKey.Forget(_last, networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ WL-2b (CE-1049, W-3) — publishes a turret part's <see cref="WeaponTaskStatus"/> from the node owning
    /// <c>(dtWeaponTaskStatus, part)</c> — the Muscle — keyed like the task, on change.
    /// </summary>
    public sealed class EntityWeaponTaskStatusEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityWeaponTaskStatus>? _writer;
        private readonly Dictionary<(long, int), WeaponTaskStatus> _last = new();
        private int _worldEpoch;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWeaponTaskStatus>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponTaskStatus;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponTaskStatusEgressTranslator(DdsParticipant? participant)
        {
            _writer = participant is not null ? new DdsWriter<EntityWeaponTaskStatus>(participant) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            foreach (var part in view.Query().With<PartMetadata>().With<WeaponTaskStatus>().Build())
            {
                if (!TurretPartKey.TryResolve(view, part, DescriptorOrdinal, out var hullNet, out int partId)) continue;
                var status = view.GetComponentRO<WeaponTaskStatus>(part);
                if (_last.TryGetValue((hullNet, partId), out var prev) && prev.Equals(status)) continue;
                _last[(hullNet, partId)] = status;
                _writer?.Write(new EntityWeaponTaskStatus
                {
                    ParentNetworkId = hullNet, PartId = partId, Epoch = status.Epoch,
                    State = (byte)status.State, Reason = (byte)status.Reason, RoundsFired = status.RoundsFired,
                });
                SentSampleCount++;
            }
        }

        public void Dispose(long networkEntityId) => TurretPartKey.Forget(_last, networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ WL-1b/WL-2b — the wire key of a turret part (hull network id, part id), gated on this node owning
    /// <c>(descriptor, part)</c>. The EQS sensor key shape (<c>EqsSensorKey.Resolve</c>).</summary>
    internal static class TurretPartKey
    {
        public static bool TryResolve(ISimulationView view, Entity part, long descriptor, out long hullNetworkId, out int partId)
        {
            hullNetworkId = 0;
            ref readonly var meta = ref view.GetComponentRO<PartMetadata>(part);
            partId = meta.InstanceId;
            var hull = meta.ParentEntity;
            if (!view.IsAlive(hull) || !view.HasComponent<NetworkIdentity>(hull)) return false;
            if (!view.HasAuthority(part, OwnershipExtensions.PackKey(descriptor, partId))) return false;
            hullNetworkId = view.GetComponentRO<NetworkIdentity>(hull).Value;
            return true;
        }

        public static void Forget<T>(Dictionary<(long, int), T> cache, long hullNetworkId)
        {
            List<(long, int)>? gone = null;
            foreach (var key in cache.Keys) if (key.Item1 == hullNetworkId) (gone ??= new()).Add(key);
            if (gone != null) foreach (var key in gone) cache.Remove(key);
        }
    }
}
