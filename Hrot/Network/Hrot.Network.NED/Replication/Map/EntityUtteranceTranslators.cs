using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3264</c> (G1, SP-3; docs/DESIGN_Civilians_And_Crowd_Control.md §7.18) — publishes what a man is SAYING (<see cref="Utterance"/>:
    /// the kind, whom, until when) from the node owning its <c>dtUtterance</c> (the brain owner), on change, with the target as a NETWORK
    /// id (0 = everyone around; a local entity would mean nothing elsewhere — the <c>LookState</c> G7 lesson).
    /// </summary>
    public sealed class EntityUtteranceEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityUtterance>? _writer;
        private readonly Dictionary<long, EntityUtterance> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.Utterance };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUtterance>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtUtterance;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        private readonly NetworkEntityMap _entityMap;

        public EntityUtteranceEgressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _writer = participant is not null ? new DdsWriter<EntityUtterance>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityUtterance { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<Utterance>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<Utterance>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                ref readonly var said = ref view.GetComponentRO<Utterance>(unit);
                long targetId = !said.Target.IsNull && _entityMap.TryGetNetworkId(said.Target, out long tid) ? tid : 0L;
                var sample = ToSample(netId, said, targetId);
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityUtterance ToSample(long netId, in Utterance c, long targetId)
            => new() { EntityId = netId, Kind = (byte)c.Kind, TargetId = targetId, Until = c.Until };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3264</c> — applies a man's published <see cref="Utterance"/> on this node, the target back to this node's entity (ui
    /// draws it; the target's brain reads a command addressed to him). An unknown unit is dropped; the recorded owner never takes its own
    /// sample back.</summary>
    public sealed class EntityUtteranceIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtUtterance;
        private readonly DdsReader<EntityUtterance>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUtterance>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityUtteranceIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityUtterance>(participant) : null;
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
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityUtterance d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new Utterance { Kind = (UtteranceKind)d.Kind, Target = d.TargetId != 0 && _entityMap.TryGetEntity(d.TargetId, out var t) ? t : Entity.Null, Until = d.Until });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
