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
        private const long   OrdinalValue = (long)EDescriptorType.dtTurretPose;

        /// <summary>The smallest change that is sent: 0.2° (radians).</summary>
        public const float AngleThreshold = 0.2f * MathF.PI / 180f;

        /// <summary>Heartbeat period in ticks (~2 s at 60 Hz), staggered per part.</summary>
        public const uint HeartbeatInterval = 120;

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over. Keyed by the packed (hull network id, part id) — TurretPartKey.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityTurretPose> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityTurretPose>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityTurretPoseEgressTranslator(DdsParticipant? participant)
        {
            _instances = new(participant is not null ? new DdsWriter<EntityTurretPose>(participant) : null,
                static key => new EntityTurretPose { ParentNetworkId = TurretPartKey.Hull(key), PartId = TurretPartKey.Part(key) }, TurretPartKey.Hull);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)

            DateTime stamp = default;
            bool stamped = false;
            foreach (var part in view.Query().With<PartMetadata>().With<TurretPose>().Build())
            {
                if (!TurretPartKey.TryResolve(view, part, DescriptorOrdinal, out int hullNet, out int partId)) continue;

                var pose = view.GetComponentRO<TurretPose>(part);
                long key = TurretPartKey.Pack(hullNet, partId);
                bool heartbeat = ((view.Tick + (uint)(part.Index % HeartbeatInterval)) % HeartbeatInterval) == 0;
                if (!heartbeat && _instances.TryGetLast(key, out var prev)
                    && MathF.Abs(pose.Azimuth - prev.Azimuth) <= AngleThreshold
                    && MathF.Abs(pose.Elevation - prev.Elevation) <= AngleThreshold) continue;

                if (!stamped)
                {
                    stamp = SimStampCodec.Encode(Fdp.Toolkit.Time.SimClock.Of(view).TotalTime);   // once per scan (DESIGN_Dead_Reckoning R4)
                    stamped = true;
                }
                _instances.Write(key, new EntityTurretPose
                {
                    ParentNetworkId = hullNet,
                    PartId          = partId,
                    Azimuth         = pose.Azimuth,
                    Elevation       = pose.Elevation,
                    Time            = stamp,
                });
                SentSampleCount++;
            }
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of every part instance of the hull this node published (WITHOUT disposing
        /// them) and forget what was published, so a reclaim republishes. <paramref name="instanceId"/> 0 = all parts.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
