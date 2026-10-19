using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-1223</c> (Entity Control §8k S-2, R-1010) — publishes a man's <see cref="UnderFire"/> stamp from the node owning its
    /// <c>dtUnderFire</c> (the Muscle group — whoever the near-miss and hit producers stamp it on), keyed by network id, on change. Every
    /// other node's <c>HitModel.Sigma</c> then sees the same suppression: a remote first-person station draws the true spread ring.
    /// The cache clears when the world moves (<c>WorldEpoch</c>, CE-4007).
    /// </summary>
    public sealed class EntityUnderFireEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityUnderFire";
        private readonly IDdsWriter<EntityUnderFire>? _writer;
        private readonly Dictionary<long, EntityUnderFire> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.UnderFire };

        /// <summary>A newer stamp is sent only this long (s) after the last one sent: the remote window ends at most this early.</summary>
        public const double RefreshSeconds = 0.5;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtUnderFire;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityUnderFireEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityUnderFire>(participant, DdsTopicName) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityUnderFireEgressTranslator(IDdsWriter<EntityUnderFire>? writer) => _writer = writer;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run (TransientLocal).
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityUnderFire { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<UnderFire>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<UnderFire>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<UnderFire>(e));
                // ⭐ a man under a machine gun is re-stamped by every near miss: a stamp within RefreshSeconds of the last one sent changes
                //   no remote window by more than that (HitModel.UnderFireSeconds is 5 s), so it is not sent — never a sample per round
                if (_last.TryGetValue(netId, out var prev)
                    && sample.LastTime >= prev.LastTime && sample.LastTime - prev.LastTime < RefreshSeconds) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityUnderFire ToSample(long netId, in UnderFire c) => new() { EntityId = netId, LastTime = c.LastTime };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-1223</c> — applies a published <see cref="UnderFire"/> stamp to this node's copy of the man, so the hit model here
    /// doubles σ for the same window the Muscle does. The recorded owner never takes its own sample back; an unknown entity is dropped
    /// (Reliable + TransientLocal re-sends on the next change); the component is added when the man has none yet.
    /// </summary>
    public sealed class EntityUnderFireIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityUnderFire";
        private const long OrdinalValue = (long)EDescriptorType.dtUnderFire;
        private readonly DdsReader<EntityUnderFire>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityUnderFireIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityUnderFire>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

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

        /// <summary>Applies one sample (the test seam: a live participant is not needed to check the mapping).</summary>
        internal void Apply(in EntityUnderFire s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<UnderFire>()) { DroppedSampleCount++; return; }
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            }
            cmd.AddComponent(e, new UnderFire { LastTime = s.LastTime });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
