using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3271</c> (Q94 D, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.2a) — publishes a spawn's
    /// <see cref="SpawnLevelRequest"/> from the node owning its <c>dtSpawnLevel</c> (Muscle group), on change: the creator's request while
    /// it is Constructing (its record still says "mine" until the grantee confirms — every lifecycle, as <c>EntityDescent</c>), then the
    /// position owner's <c>Resolved</c>.
    /// </summary>
    public sealed class EntitySpawnLevelEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntitySpawnLevel>? _writer;
        private readonly Dictionary<long, EntitySpawnLevel> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.SpawnLevelRequest };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntitySpawnLevel>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtSpawnLevel;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntitySpawnLevelEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntitySpawnLevel>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntitySpawnLevel { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<SpawnLevelRequest>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<SpawnLevelRequest>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.All).Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<SpawnLevelRequest>(e));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntitySpawnLevel ToSample(long netId, in SpawnLevelRequest c)
            => new() { EntityId = netId, Mode = (byte)c.Mode, Level = c.Level, Resolved = c.Resolved };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(long networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntitySpawnLevel { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-3271</c> — applies a published <see cref="SpawnLevelRequest"/>, MONOTONICALLY and with no owner check: a resolved sample
    /// always applies; a waiting one only where this node has no request yet. ⛔ The usual owner-skip would DROP the creator's request on a
    /// muscle that claimed the position before it arrived (two topics, no ordering) — the man would keep the sent height. An unknown entity
    /// is dropped.
    /// </summary>
    public sealed class EntitySpawnLevelIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtSpawnLevel;
        private readonly DdsReader<EntitySpawnLevel>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntitySpawnLevel>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntitySpawnLevelIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntitySpawnLevel>(participant) : null;
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
        internal void Apply(in EntitySpawnLevel s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (s.Resolved == 0 && view.HasComponent<SpawnLevelRequest>(e)) return;   // already asked or already done here
            cmd.AddComponent(e, new SpawnLevelRequest { Mode = (SpawnHeightMode)s.Mode, Level = s.Level, Resolved = s.Resolved });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
