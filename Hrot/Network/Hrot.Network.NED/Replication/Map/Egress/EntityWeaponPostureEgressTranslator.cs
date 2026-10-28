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
    /// ⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13d, R-272) — publishes a unit's <see cref="WeaponPosture"/> from the node owning its
    /// <c>dtWeaponPosture</c> (the Brain), keyed by network id, on change. The target crosses as its network id; the Brain's own
    /// <see cref="WeaponPosture.IdleSeconds"/> clock never does. The cache clears when the world moves (<c>WorldEpoch</c>, CE-3076).
    /// </summary>
    public sealed class EntityWeaponPostureEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityWeaponPosture>? _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, EntityWeaponPosture> _last = new();
        private int _worldEpoch;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWeaponPosture>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponPosture;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponPostureEgressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _writer = participant is not null ? new DdsWriter<EntityWeaponPosture>(participant) : null;
            _entityMap = entityMap;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run (the topic is TransientLocal). Keyed by what THIS writer published — not by entity ownership: a descriptor owner may not be the entity's primary owner.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityWeaponPosture { EntityId = id });
                _last.Clear();
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<WeaponPosture>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var posture = view.GetComponentRO<WeaponPosture>(unit);
                long targetNet = 0;
                if (!posture.Target.IsNull && !_entityMap.TryGetNetworkId(posture.Target, out targetNet)) targetNet = 0;
                var sample = new EntityWeaponPosture
                {
                    EntityId = netId, Kind = (byte)posture.Kind, TargetNetworkId = targetNet,
                    PointX = posture.Point.X, PointY = posture.Point.Y, PointZ = posture.Point.Z,
                    ActiveMount = posture.ActiveMount,   // ⭐ ui P4
                    HeldItem = (byte)posture.HeldItem,   // ⭐ CE-3234 (B-12, HI-1)
                };
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(long networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityWeaponPosture { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
