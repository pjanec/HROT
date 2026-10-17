using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ Airborne insertion B-1 (docs/designs/airborne/DESIGN_Airborne_Insertion.md D-1) — publishes a body's <see cref="Descent"/> from the
    /// node owning its <c>dtDescent</c> (the position's owner, Muscle group) when it changes, and ONE sample of Kind 0 when it is removed
    /// (his descent is over), so every node drops it too. The <c>EntityWeaponPosture</c> pattern.
    /// </summary>
    public sealed class EntityDescentEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDescent";
        private readonly DdsWriter<EntityDescent>? _writer;
        private readonly Dictionary<long, EntityDescent> _last = new();
        private readonly HashSet<long> _seen = new();
        private readonly List<long> _gone = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtDescent;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityDescentEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityDescent>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<Descent>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _seen.Clear();
            // ⭐ CE-2210 — EVERY lifecycle, as GeoSpatialEgressTranslator: a body created already coming down (the ejected pilot)
            //   is the creator's to publish only while it is CONSTRUCTING — the creator yields its Muscle group before it turns
            //   Active (Ownership Groups D-7). The default (Active-only) query never saw it then, so nobody ever sent his descent.
            foreach (var e in view.Query().With<Descent>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.All).Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                _seen.Add(netId);
                var sample = ToSample(netId, view.GetComponentRO<Descent>(e), view);
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                Send(netId, sample);
            }
            // a descent that ended here: one "over" sample, then forgotten
            _gone.Clear();
            foreach (var (netId, prev) in _last) if (!_seen.Contains(netId) && prev.Kind != 0) _gone.Add(netId);
            foreach (var netId in _gone) Send(netId, new EntityDescent { EntityId = netId });
        }

        private void Send(long netId, in EntityDescent sample)
        {
            _last[netId] = sample;
            _writer?.Write(sample);
            SentSampleCount++;
        }

        /// <summary>The wire form (the test seam): the anchor as its network id.</summary>
        internal static EntityDescent ToSample(long netId, in Descent d, ISimulationView view)
            => new()
            {
                EntityId = netId, Kind = (byte)d.Kind, Phase = (byte)d.Phase, Canopy = (byte)d.Canopy, AnchorIndex = d.AnchorIndex,
                AnchorNetworkId = !d.Anchor.IsNull && view.IsAlive(d.Anchor) && view.HasComponent<NetworkIdentity>(d.Anchor)
                    ? view.GetComponentRO<NetworkIdentity>(d.Anchor).Value : 0,
                PhaseSince = d.PhaseSince, TargetX = d.Target.X, TargetY = d.Target.Y, TargetZ = d.Target.Z,
            };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ Airborne insertion B-1 — applies a body's published <see cref="Descent"/> on this node (Kind 0 removes it): his class is
    /// Rope / Parachute here too, and the drawing reads the phase. An unknown entity is dropped; the recorded owner never takes its own sample.</summary>
    public sealed class EntityDescentIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDescent";
        private const long OrdinalValue = (long)EDescriptorType.dtDescent;
        private readonly DdsReader<EntityDescent>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityDescentIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityDescent>(participant, DdsTopicName) : null;
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
        internal void Apply(in EntityDescent s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            if (s.Kind == 0)
            {
                if (view.HasComponent<Descent>(e)) cmd.RemoveComponent<Descent>(e);
                return;
            }
            cmd.AddComponent(e, new Descent
            {
                Kind = (DescentKind)s.Kind, Phase = (DescentPhase)s.Phase, Canopy = (CanopyKind)s.Canopy, AnchorIndex = s.AnchorIndex,
                Anchor = s.AnchorNetworkId != 0 && _entityMap.TryGetEntity(s.AnchorNetworkId, out var a) ? a : Entity.Null,
                PhaseSince = s.PhaseSince, Target = new Vector3(s.TargetX, s.TargetY, s.TargetZ),
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
