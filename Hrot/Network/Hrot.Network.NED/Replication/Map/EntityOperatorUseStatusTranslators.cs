using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-1153</c> (docs/DESIGN_Entity_Control.md §8h.6) — publishes a possessed unit's <see cref="OperatorUseStatus"/> (the brain's verdict
    /// on the last use press) from the node owning its <c>dtOperatorUseStatus</c> (the brain), on change. The <c>dtWeaponAmmo</c> shape.
    /// </summary>
    public sealed class EntityOperatorUseStatusEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityOperatorUseStatus";
        private readonly DdsWriter<EntityOperatorUseStatus>? _writer;
        private readonly Dictionary<long, EntityOperatorUseStatus> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.OperatorUseStatus };

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtOperatorUseStatus;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityOperatorUseStatusEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityOperatorUseStatus>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run (the topic is TransientLocal).
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityOperatorUseStatus { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<OperatorUseStatus>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<OperatorUseStatus>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<OperatorUseStatus>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityOperatorUseStatus ToSample(long netId, in OperatorUseStatus s) => new()
        {
            EntityId = netId, UseSeq = s.UseSeq, ActionId = s.ActionId, Outcome = (byte)s.Outcome, Reason = (byte)s.Reason,
        };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-1153</c> — applies a unit's published <see cref="OperatorUseStatus"/> on this node (the station's HUD reads it). An
    /// unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityOperatorUseStatusIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityOperatorUseStatus";
        private const long OrdinalValue = (long)EDescriptorType.dtOperatorUseStatus;
        private readonly DdsReader<EntityOperatorUseStatus>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityOperatorUseStatusIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityOperatorUseStatus>(participant, DdsTopicName) : null;
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
        internal void Apply(in EntityOperatorUseStatus d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new OperatorUseStatus
            {
                UseSeq = d.UseSeq, ActionId = d.ActionId, Outcome = (UseOutcome)d.Outcome, Reason = (UseReason)d.Reason,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
