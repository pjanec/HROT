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
        // ⭐ CE-2407 (DESIGN_Egress_Change_Detection §5, E1) — skips the entities none of these inputs changed for; NetworkIdentity is implied
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(WeaponPosture));

        private readonly NetworkEntityMap _entityMap;
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityWeaponPosture> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWeaponPosture>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponPosture;
        // ⭐ CE-3420 (X-5) — declared: the Weapon.* attributes write WeaponPosture, and the descriptor map must know this topic carries it
        private static readonly int[] s_targetIds = { Fdp.Core.GlobalComponentIds.WeaponPosture };
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponPostureEgressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _instances = new(participant is not null ? new DdsWriter<EntityWeaponPosture>(participant) : null, static key => new EntityWeaponPosture { EntityId = (int)key });
            _entityMap = entityMap;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var unit in view.Query().With<WeaponPosture>().DefaultRoutePublishable().Build())
            {
                if (!_gate.Visit(unit)) continue;   // ⭐ CE-2407 E1 — nothing this translator reads has changed since its last scan
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var posture = view.GetComponentRO<WeaponPosture>(unit);
                int targetNet = 0;
                if (!posture.Target.IsNull && !_entityMap.TryGetNetworkId(posture.Target, out targetNet)) targetNet = 0;
                var sample = new EntityWeaponPosture
                {
                    EntityId = netId, Kind = (byte)posture.Kind, TargetNetworkId = targetNet,
                    PointX = posture.Point.X, PointY = posture.Point.Y, PointZ = posture.Point.Z,
                    ActiveMount = posture.ActiveMount,   // ⭐ ui P4
                    HeldItem = (byte)posture.HeldItem,   // ⭐ CE-3234 (B-12, HI-1)
                };
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
