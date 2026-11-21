using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-2468</c> UF-2 (DESIGN_Minefields.md §9b) — publishes an <see cref="UnderFireKnowledge"/> from the node that owns the entity's <c>dtUnderFireKnowledge</c> descriptor on change; the
    /// <see cref="EntityMinefieldKnowledgeEgressTranslator"/> shape exactly (send-on-change keyed by network id, a ghost never published, a steady tick builds nothing).
    /// </summary>
    public sealed class EntityUnderFireKnowledgeEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtUnderFireKnowledge;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityUnderFireKnowledge> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUnderFireKnowledge>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityUnderFireKnowledgeEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityUnderFireKnowledge>(participant), static key => new EntityUnderFireKnowledge { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<UnderFireKnowledge>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<UnderFireKnowledge>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<UnderFireKnowledge>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in c)) continue;   // compare BEFORE converting
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityUnderFireKnowledge ToSample(int netId, in UnderFireKnowledge c)
        {
            var h = c.Header;
            var (lo, hi) = EntityMinefieldKnowledgeEgressTranslator.Halves(h.Subject);
            return new EntityUnderFireKnowledge { EntityId = netId, SubjectKind = (byte)h.SubjectKind, SidePlus1 = h.SidePlus1, Kind = (byte)h.Kind, State = h.State, Source = (byte)h.Source,
                                                  Marked = h.Marked, Chunk = h.Chunk, SubjectLo = lo, SubjectHi = hi, ObjectIndex = h.ObjectIndex, LearnedAt = h.LearnedAt.Seconds,
                                                  Cause = (byte)c.Cause, PointX = c.Point.X, PointY = c.Point.Y, PointZ = c.Point.Z, RangeM = c.RangeM, EnemyStrength = c.EnemyStrength };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityUnderFireKnowledge prev, in UnderFireKnowledge c)
        {
            var h = c.Header;
            var (lo, hi) = EntityMinefieldKnowledgeEgressTranslator.Halves(h.Subject);
            return prev.SubjectKind == (byte)h.SubjectKind && prev.SidePlus1 == h.SidePlus1 && prev.Kind == (byte)h.Kind && prev.State == h.State && prev.Source == (byte)h.Source && prev.Marked == h.Marked
                && prev.Chunk == h.Chunk && prev.SubjectLo == lo && prev.SubjectHi == hi && prev.ObjectIndex == h.ObjectIndex && prev.LearnedAt == h.LearnedAt.Seconds
                && prev.Cause == (byte)c.Cause && prev.PointX == c.Point.X && prev.PointY == c.Point.Y && prev.PointZ == c.Point.Z && prev.RangeM == c.RangeM && prev.EnemyStrength == c.EnemyStrength;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-2468</c> UF-2 — applies a published <see cref="UnderFireKnowledge"/> on every node that does not own it (the <see cref="EntityMinefieldKnowledgeIngressTranslator"/> shape: an
    /// unknown id makes a ghost, the recorded owner never takes its own sample back).
    /// </summary>
    public sealed class EntityUnderFireKnowledgeIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtUnderFireKnowledge;

        private readonly DdsReader<EntityUnderFireKnowledge>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUnderFireKnowledge>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityUnderFireKnowledgeIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityUnderFireKnowledge>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        }

        public int DiscardIngress() => _reader.DiscardAll();

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

        /// <summary>Takes one sample (the test seam).</summary>
        internal void Receive(in EntityUnderFireKnowledge d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityUnderFireKnowledge data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityUnderFireKnowledgeIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static UnderFireKnowledge FromSample(in EntityUnderFireKnowledge d)
        {
            var c = new UnderFireKnowledge();
            c.Header = new PlaceKnowledgeHeader { SubjectKind = (SubjectKind)d.SubjectKind, SidePlus1 = d.SidePlus1, Kind = (PlaceKnowledgeKind)d.Kind, State = d.State, Source = (KnowledgeSource)d.Source,
                                                  Marked = d.Marked, Chunk = (ushort)d.Chunk, Subject = Egress.EntityMinefieldKnowledgeEgressTranslator.GuidOf(d.SubjectLo, d.SubjectHi),
                                                  ObjectIndex = d.ObjectIndex, LearnedAt = (SimInstant)d.LearnedAt };
            c.Cause = (UnderFireCause)d.Cause;
            c.Point = new Vector3(d.PointX, d.PointY, d.PointZ);
            c.RangeM = d.RangeM;
            c.EnemyStrength = d.EnemyStrength;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityUnderFireKnowledge d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
