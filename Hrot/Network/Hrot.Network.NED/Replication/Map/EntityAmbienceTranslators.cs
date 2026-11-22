using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Ambient;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-11034</c> (R-11014; 📄 docs/designs/audio/DESIGN_Ambience.md §2) — publishes an ambience layer's <see cref="Ambience"/> from the node owning its <c>dtAmbience</c> (the
    /// layer's creator — no group), on change; the layer's end (the owner's network destroy) DISPOSES the instance, so a renderer stops the sound. The
    /// <see cref="EntityWeatherEgressTranslator"/> shape: send-on-change keyed by entity, and the egress change gate so a steady frame builds no sample (no string).
    /// </summary>
    public sealed class EntityAmbienceEgressTranslator : IDescriptorTranslator
    {
        // ⭐ CE-2407 (DESIGN_Egress_Change_Detection §5, E1) — skips the entities none of these inputs changed for; NetworkIdentity is implied
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(Ambience));

        [Fdp.Core.NotState("static readonly component-id list, fixed at type load")]
        private static readonly int[] s_targetIds = { GlobalComponentIds.Ambience };
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a destruction, a wipe or a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityAmbience> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAmbience>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtAmbience;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAmbienceEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityAmbience>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityAmbienceEgressTranslator(IDdsWriter<EntityAmbience>? writer)
            => _instances = new(writer is null ? null : s => writer.Write(s), writer is null ? null : s => writer.DisposeInstance(s),
                writer is null ? null : s => writer.UnregisterInstance(s), static key => new EntityAmbience { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<Ambience>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<Ambience>().DefaultRoutePublishable().Build())
            {
                if (!_gate.Visit(e)) continue;   // ⭐ CE-2407 E1 — nothing this translator reads has changed since its last scan
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<Ambience>(e));
                if (_instances.TryGetLast(netId, out var prev) && Same(in prev, in sample)) continue;   // on change only
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam): seconds of sim time and metres, the kind as its dotted name.</summary>
        internal static EntityAmbience ToSample(int netId, in Ambience a) => new()
        {
            EntityId = netId, Kind = a.Kind.ToString(), Intensity = a.Intensity, Radius = a.Radius, Until = a.Until.Seconds,
        };

        private static bool Same(in EntityAmbience a, in EntityAmbience b)
            => a.Kind == b.Kind && a.Intensity == b.Intensity && a.Radius == b.Radius && a.Until == b.Until;

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-11034</c> — applies a published ambience layer on every node that does not own it; a sample for an entity this node does not know yet is HELD (the newest per entity,
    /// the weather's shape: the topic and the entity's creation are not ordered). The recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityAmbienceIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtAmbience;
        private readonly DdsReader<EntityAmbience>? _reader;
        private readonly NetworkEntityMap _entityMap;
        [Fdp.Core.ProtocolCache("sample held until the layer's entity is known here; applied then removed")]
        private readonly Dictionary<int, EntityAmbience> _pending = new();
        private int _worldEpoch;   // ⭐ R-4297 ④ (CE-1980) — a world replacement forgets the pending samples (they named the old world)
        [Fdp.Core.WorldDerived("scratch list cleared at end of every Flush")]
        private readonly List<int> _done = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAmbience>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityAmbienceIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityAmbience>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _pending.Clear();
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _pending[sample.Data.EntityId] = sample.Data;
                }
            }
            Flush(cmd, view);
        }

        /// <summary>Takes one sample as if the reader had (the test seam).</summary>
        internal void Receive(in EntityAmbience d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            for (int i = 0; i < _done.Count; i++) _pending.Remove(_done[i]);
            _done.Clear();
        }

        private bool TryApply(in EntityAmbience d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            cmd.AddComponent(e, FromSample(in d));
            return true;
        }

        /// <summary>⭐ The one sample → component mapping (this ingress, and a forwarded creation request — the <c>dtAreaEffectCloud</c> shape).</summary>
        internal static Ambience FromSample(in EntityAmbience d) => new()
        {
            Kind = new FixedString32(d.Kind ?? string.Empty), Intensity = d.Intensity, Radius = d.Radius, Until = (SimInstant)d.Until,
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) => _pending.Remove(networkEntityId);
    }
}
