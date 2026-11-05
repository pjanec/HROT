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
        private readonly NetworkEntityMap _entityMap;
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over. Keyed by the packed (hull network id, part id) — TurretPartKey.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityWeaponTask> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWeaponTask>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponTask;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponTaskEgressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _instances = new(participant is not null ? new DdsWriter<EntityWeaponTask>(participant) : null,
                static key => new EntityWeaponTask { ParentNetworkId = TurretPartKey.Hull(key), PartId = TurretPartKey.Part(key) }, TurretPartKey.Hull);
            _entityMap = entityMap;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            foreach (var part in view.Query().With<PartMetadata>().With<WeaponTask>().Build())
            {
                if (!TurretPartKey.TryResolve(view, part, DescriptorOrdinal, out var hullNet, out int partId)) continue;
                var task = view.GetComponentRO<WeaponTask>(part);
                int targetNet = 0;
                if (!task.Target.IsNull && !_entityMap.TryGetNetworkId(task.Target, out targetNet)) targetNet = 0;
                var sample = new EntityWeaponTask
                {
                    ParentNetworkId = hullNet, PartId = partId, Mode = (byte)task.Mode, TargetNetworkId = targetNet,
                    PointX = task.Point.X, PointY = task.Point.Y, PointZ = task.Point.Z,
                    MountIndex = task.MountIndex, RoundsGranted = task.RoundsGranted, AmmoGuid = task.AmmoGuid,
                    CooldownSeconds = task.CooldownSeconds, Epoch = task.Epoch,
                    Arc = (byte)task.Arc,   // ⭐ ui handoff P2
                    AlongBarrel = task.AlongBarrel,   // ⭐ CE-4143
                };
                long key = TurretPartKey.Pack(hullNet, partId);
                if (_instances.Unchanged(key, sample)) continue;
                _instances.Write(key, sample);
                SentSampleCount++;
            }
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of every part instance of the hull this node published (WITHOUT disposing
        /// them) and forget what was published, so a reclaim republishes. <paramref name="instanceId"/> 0 = all parts.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ WL-2b (CE-1049, W-3) — publishes a turret part's <see cref="WeaponTaskStatus"/> from the node owning
    /// <c>(dtWeaponTaskStatus, part)</c> — the Muscle — keyed like the task, on change.
    /// </summary>
    public sealed class EntityWeaponTaskStatusEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over. Keyed by the packed (hull network id, part id) — TurretPartKey.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityWeaponTaskStatus> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWeaponTaskStatus>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponTaskStatus;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponTaskStatusEgressTranslator(DdsParticipant? participant)
        {
            _instances = new(participant is not null ? new DdsWriter<EntityWeaponTaskStatus>(participant) : null,
                static key => new EntityWeaponTaskStatus { ParentNetworkId = TurretPartKey.Hull(key), PartId = TurretPartKey.Part(key) }, TurretPartKey.Hull);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            foreach (var part in view.Query().With<PartMetadata>().With<WeaponTaskStatus>().Build())
            {
                if (!TurretPartKey.TryResolve(view, part, DescriptorOrdinal, out var hullNet, out int partId)) continue;
                var status = view.GetComponentRO<WeaponTaskStatus>(part);
                var sample = new EntityWeaponTaskStatus
                {
                    ParentNetworkId = hullNet, PartId = partId, Epoch = status.Epoch,
                    State = (byte)status.State, Reason = (byte)status.Reason, RoundsFired = status.RoundsFired,
                };
                long key = TurretPartKey.Pack(hullNet, partId);
                if (_instances.Unchanged(key, sample)) continue;
                _instances.Write(key, sample);
                SentSampleCount++;
            }
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of every part instance of the hull this node published (WITHOUT disposing
        /// them) and forget what was published, so a reclaim republishes. <paramref name="instanceId"/> 0 = all parts.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ WL-1b/WL-2b — the wire key of a turret part (hull network id, part id), gated on this node owning
    /// <c>(descriptor, part)</c>. The EQS sensor key shape (<c>EqsSensorKey.Resolve</c>).</summary>
    internal static class TurretPartKey
    {
        public static bool TryResolve(ISimulationView view, Entity part, long descriptor, out int hullNetworkId, out int partId)
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

        /// <summary>⭐ CE-4007 — the packed key a part's instance is remembered under in a <c>PublishedInstances</c> (hull network id high, part id low).</summary>
        public static long Pack(int hullNetworkId, int partId) => ((long)hullNetworkId << 32) | (uint)partId;
        public static int Hull(long key) => (int)(key >> 32);
        public static int Part(long key) => (int)(uint)key;
    }
}
