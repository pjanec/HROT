using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3173</c> S4e (docs/DESIGN_Entity_Control.md §8c, M-1) — publishes a unit's <see cref="ManualDrive"/> from the node
    /// owning its <c>dtManualDrive</c> (the Brain, where <c>OperatorControl</c> writes it), keyed by network id, on change — every frame
    /// while an operator drives it. The cache clears when the world moves (<c>WorldEpoch</c>, CE-3076).
    /// </summary>
    public sealed class EntityManualDriveEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityManualDrive> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityManualDrive>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtManualDrive;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityManualDriveEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityManualDrive>(participant) : null, static key => new EntityManualDrive { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<ManualDrive>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<ManualDrive>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityManualDrive ToSample(int netId, in ManualDrive d) => new()
        {
            EntityId = netId, Forward = d.Forward, Lateral = d.Lateral, Steer = d.Steer, Yaw = d.Yaw, Gait = d.Gait, Seq = d.Seq, Vertical = d.Vertical,
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-3172</c> S4e (§8c, C-3) — publishes WHO HOLDS a unit (<see cref="PossessedBy"/>) from the node owning its
    /// <c>dtPossessedBy</c> (the Brain, where <c>PossessionSystem</c> is the one writer), on change; when the component goes, ONE
    /// <c>Held = 0</c> sample says so — every station learns it lost (or never had) the unit, with no revoke message to lose.
    /// </summary>
    public sealed class EntityPossessedByEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (§8.3) — what this writer published (see above). A release (Held=0) stays remembered until the entity is destroyed.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityPossessedBy> _instances;
        private readonly HashSet<int> _seen = new();
        private readonly List<int> _gone = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityPossessedBy>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtPossessedBy;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityPossessedByEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityPossessedBy>(participant) : null, static key => new EntityPossessedBy { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            foreach (var s in Scan(view)) { _instances.Write(s.EntityId, s); SentSampleCount++; }
        }

        /// <summary>The samples this frame owes (the test seam): changed holds, and one release for each hold that went.</summary>
        internal List<EntityPossessedBy> Scan(ISimulationView view)
        {
            var outgoing = new List<EntityPossessedBy>();
            _seen.Clear();
            if (view is not EntityRepository repo || repo.IsComponentTypeRegistered<PossessedBy>())
            {
                long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
                foreach (var unit in view.Query().With<PossessedBy>().DefaultRoutePublishable().Build())
                {
                    if (!view.HasAuthority(unit, packedKey)) continue;
                    int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                    var held = view.GetComponentRO<PossessedBy>(unit);
                    var sample = new EntityPossessedBy
                    {
                        EntityId = netId, Held = 1, StationNodeId = held.StationNodeId, Mode = (byte)held.Mode, Epoch = held.Epoch,
                    };
                    _seen.Add(netId);
                    if (_instances.Unchanged(netId, sample)) continue;
                    _instances.Remember(netId, sample);   // Scan only decides; ScanAndPublish writes (Write re-remembers the same sample)
                    outgoing.Add(sample);
                }
            }
            _gone.Clear();
            foreach (long key in _instances.Keys)
                if (_instances.TryGetLast(key, out var last) && last.Held != 0 && !_seen.Contains((int)key)) _gone.Add((int)key);
            foreach (int netId in _gone)
            {
                _instances.TryGetLast(netId, out var was);
                var release = new EntityPossessedBy { EntityId = netId, Held = 0, Epoch = was.Epoch };
                _instances.Remember(netId, release);
                outgoing.Add(release);
            }
            return outgoing;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
