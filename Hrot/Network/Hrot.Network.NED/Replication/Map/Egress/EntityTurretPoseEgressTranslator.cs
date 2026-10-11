using System;
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
    /// ⭐ WL-1b (CE-1049) — publishes a turret part's <see cref="TurretPose"/> from the node that owns <c>dtTurretPose</c> for it (the
    /// Muscle, through the MuscleGround group), keyed (hull network id, part id). 📄 docs/DESIGN_Weapon_Layers.md §5b, W-9.
    /// <para>Sent when either angle moved more than <see cref="AngleThreshold"/> from the last sample sent, and on a salted heartbeat
    /// (a lost BestEffort sample and a late joiner are repaired by it). The rates are written 0 and the stamp is the sim time —
    /// W-10 fills the rates with no wire change.</para>
    /// </summary>
    public sealed class EntityTurretPoseEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityTurretPose";
        private const long   OrdinalValue = (long)EDescriptorType.dtTurretPose;

        /// <summary>The smallest change that is sent: 0.2° (radians).</summary>
        public const float AngleThreshold = 0.2f * MathF.PI / 180f;

        /// <summary>Heartbeat period in ticks (~2 s at 60 Hz), staggered per part.</summary>
        public const uint HeartbeatInterval = 120;

        private readonly DdsWriter<EntityTurretPose>? _writer;
        private readonly Dictionary<(long NetId, int PartId), TurretPose> _lastPublished = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityTurretPoseEgressTranslator(DdsParticipant? participant)
        {
            _writer = participant is not null ? new DdsWriter<EntityTurretPose>(participant, DdsTopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _lastPublished.Clear();

            DateTime stamp = default;
            bool stamped = false;
            foreach (var part in view.Query().With<PartMetadata>().With<TurretPose>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(part);
                var hull = meta.ParentEntity;
                if (!view.IsAlive(hull) || !view.HasComponent<NetworkIdentity>(hull)) continue;
                if (!view.HasAuthority(part, OwnershipExtensions.PackKey(DescriptorOrdinal, meta.InstanceId))) continue;

                var pose = view.GetComponentRO<TurretPose>(part);
                var key = (view.GetComponentRO<NetworkIdentity>(hull).Value, meta.InstanceId);
                bool heartbeat = ((view.Tick + (uint)(part.Index % HeartbeatInterval)) % HeartbeatInterval) == 0;
                if (!heartbeat && _lastPublished.TryGetValue(key, out var prev)
                    && MathF.Abs(pose.Azimuth - prev.Azimuth) <= AngleThreshold
                    && MathF.Abs(pose.Elevation - prev.Elevation) <= AngleThreshold) continue;
                _lastPublished[key] = pose;

                if (!stamped)
                {
                    stamp = SimStampCodec.Encode(Fdp.Toolkit.Time.SimClock.Of(view).TotalTime);   // once per scan (DESIGN_Dead_Reckoning R4)
                    stamped = true;
                }
                _writer?.Write(new EntityTurretPose
                {
                    ParentNetworkId = key.Item1,
                    PartId          = meta.InstanceId,
                    Azimuth         = pose.Azimuth,
                    Elevation       = pose.Elevation,
                    Time            = stamp,
                });
                SentSampleCount++;
            }
        }

        public void Dispose(long networkEntityId)
        {
            List<(long, int)>? gone = null;
            foreach (var key in _lastPublished.Keys)
                if (key.NetId == networkEntityId) (gone ??= new()).Add(key);
            if (gone != null) foreach (var key in gone) _lastPublished.Remove(key);
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
