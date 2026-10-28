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
    /// ⭐ <c>CE-3238</c> (civilians B-16, AW-22; docs/DESIGN_Civilians_And_Crowd_Control.md §7.16 HA-1) — publishes a man's
    /// <see cref="HostileAct"/> from the node owning its <c>dtHostileAct</c> (the brain owner), on change. The <c>dtGesture</c> shape.
    /// </summary>
    public sealed class EntityHostileActEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityHostileAct>? _writer;
        private readonly Dictionary<long, EntityHostileAct> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.HostileAct };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityHostileAct>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtHostileAct;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityHostileActEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityHostileAct>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityHostileAct { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<HostileAct>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<HostileAct>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<HostileAct>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityHostileAct ToSample(long netId, in HostileAct a)
            => new() { EntityId = netId, SinceSimTime = a.SinceSimTime, Kind = (byte)a.Kind };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(long networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityHostileAct { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3238</c> — applies a man's published <see cref="HostileAct"/> on this node (other brains, the map and the replay
    /// read it). An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityHostileActIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtHostileAct;
        private readonly DdsReader<EntityHostileAct>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityHostileAct>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityHostileActIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityHostileAct>(participant) : null;
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
        internal void Apply(in EntityHostileAct d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new HostileAct { SinceSimTime = d.SinceSimTime, Kind = (HostileActKind)d.Kind });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
