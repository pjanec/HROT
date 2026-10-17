using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — publishes a gas / smoke cloud's
    /// <see cref="AreaEffectCloud"/> from the node owning its <c>dtAreaEffectCloud</c> (its creator), on change. The <c>dtAreaEffectCloud</c> shape.
    /// </summary>
    public sealed class EntityAreaEffectCloudEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityAreaEffectCloud";
        private readonly DdsWriter<EntityAreaEffectCloud>? _writer;
        private readonly Dictionary<long, EntityAreaEffectCloud> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.AreaEffectCloud };

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtAreaEffectCloud;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAreaEffectCloudEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityAreaEffectCloud>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityAreaEffectCloud { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<AreaEffectCloud>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<AreaEffectCloud>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<AreaEffectCloud>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityAreaEffectCloud ToSample(long netId, in AreaEffectCloud c) => new()
        {
            EntityId = netId, Kind = (byte)c.Kind, Radius = c.Radius, Height = c.Height, SightTransmittance = c.SightTransmittance,
            IrritantSeconds = c.IrritantSeconds, BornAtSimSeconds = c.BornAtSimSeconds, Seconds = c.Seconds,
        };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3219</c> part 3 — applies a published <see cref="AreaEffectCloud"/> on this node (sight queries and maps read it).
    /// An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityAreaEffectCloudIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityAreaEffectCloud";
        private const long OrdinalValue = (long)EDescriptorType.dtAreaEffectCloud;
        private readonly DdsReader<EntityAreaEffectCloud>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityAreaEffectCloudIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityAreaEffectCloud>(participant, DdsTopicName) : null;
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

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityAreaEffectCloud d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new AreaEffectCloud
            {
                Kind = (CloudKind)d.Kind, Radius = d.Radius, Height = d.Height, SightTransmittance = d.SightTransmittance,
                IrritantSeconds = d.IrritantSeconds, BornAtSimSeconds = d.BornAtSimSeconds, Seconds = d.Seconds,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
