using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3259</c> (BR-6; docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §12c) — publishes a soldier's
    /// <see cref="CombatProficiency"/> (behaviors' CE-2273: authored, unit, type default and their set bits) from the node owning its
    /// <c>dtCombatProficiency</c> (the brain owner), on change, so the muscle's hit model reads the AUTHORED value. The gesture translators' shape.
    /// </summary>
    public sealed class EntityCombatProficiencyEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityCombatProficiency>? _writer;
        private readonly Dictionary<long, EntityCombatProficiency> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.CombatProficiency };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityCombatProficiency>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtCombatProficiency;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityCombatProficiencyEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityCombatProficiency>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityCombatProficiency { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<CombatProficiency>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<CombatProficiency>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<CombatProficiency>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityCombatProficiency ToSample(long netId, in CombatProficiency c)
            => new() { EntityId = netId, Authored = c.Authored, Unit = c.Unit, TypeDefault = c.TypeDefault, Set = c.Set };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(long networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityCombatProficiency { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3259</c> — applies a soldier's published <see cref="CombatProficiency"/> on this node (the hit model's spread, the
    /// sensor's acquisition read it through <c>ProficiencyOf</c>). An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityCombatProficiencyIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtCombatProficiency;
        private readonly DdsReader<EntityCombatProficiency>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityCombatProficiency>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityCombatProficiencyIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityCombatProficiency>(participant) : null;
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
        internal void Apply(in EntityCombatProficiency d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new CombatProficiency { Authored = d.Authored, Unit = d.Unit, TypeDefault = d.TypeDefault, Set = d.Set });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
