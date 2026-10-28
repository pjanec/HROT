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
        private readonly DdsWriter<EntityManualDrive>? _writer;
        private readonly Dictionary<long, EntityManualDrive> _last = new();
        private int _worldEpoch;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityManualDrive>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtManualDrive;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityManualDriveEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityManualDrive>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<ManualDrive>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<ManualDrive>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityManualDrive ToSample(long netId, in ManualDrive d) => new()
        {
            EntityId = netId, Forward = d.Forward, Lateral = d.Lateral, Steer = d.Steer, Yaw = d.Yaw, Gait = d.Gait, Seq = d.Seq, Vertical = d.Vertical,
        };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-3172</c> S4e (§8c, C-3) — publishes WHO HOLDS a unit (<see cref="PossessedBy"/>) from the node owning its
    /// <c>dtPossessedBy</c> (the Brain, where <c>PossessionSystem</c> is the one writer), on change; when the component goes, ONE
    /// <c>Held = 0</c> sample says so — every station learns it lost (or never had) the unit, with no revoke message to lose.
    /// </summary>
    public sealed class EntityPossessedByEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityPossessedBy>? _writer;
        private readonly Dictionary<long, EntityPossessedBy> _last = new();
        private readonly HashSet<long> _seen = new();
        private readonly List<long> _gone = new();
        private int _worldEpoch;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityPossessedBy>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtPossessedBy;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityPossessedByEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityPossessedBy>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            foreach (var s in Scan(view)) { _writer?.Write(s); SentSampleCount++; }
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
                    long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                    var held = view.GetComponentRO<PossessedBy>(unit);
                    var sample = new EntityPossessedBy
                    {
                        EntityId = netId, Held = 1, StationNodeId = held.StationNodeId, Mode = (byte)held.Mode, Epoch = held.Epoch,
                    };
                    _seen.Add(netId);
                    if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                    _last[netId] = sample;
                    outgoing.Add(sample);
                }
            }
            _gone.Clear();
            foreach (var kv in _last) if (kv.Value.Held != 0 && !_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (long netId in _gone)
            {
                var release = new EntityPossessedBy { EntityId = netId, Held = 0, Epoch = _last[netId].Epoch };
                _last[netId] = release;
                outgoing.Add(release);
            }
            return outgoing;
        }

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
