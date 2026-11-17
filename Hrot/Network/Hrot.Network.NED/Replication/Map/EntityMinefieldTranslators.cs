using System;
using System.Collections.Generic;
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
    /// ⭐ <c>CE-4624</c> MS-T2 (minefields §3a, §6.1) — publishes a <see cref="Minefield"/> from the node that owns the entity's <c>dtMinefield</c> descriptor on change; the
    /// <see cref="EntityGroundDamageEgressTranslator"/> shape exactly (send-on-change keyed by network id, a ghost never published, a steady tick builds nothing).
    /// </summary>
    public sealed class EntityMinefieldEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefield;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMinefield> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefield>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMinefieldEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityMinefield>(participant), static key => new EntityMinefield { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<Minefield>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<Minefield>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<Minefield>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in c)) continue;   // compare BEFORE converting
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityMinefield ToSample(int netId, in Minefield c)
        {
            int n = Math.Min((int)c.LaneCount, Minefield.MaxLanes);
            var lanes = new List<DdsMinefieldLane>(n);
            for (int i = 0; i < n; i++)
            {
                var l = c.Lane(i);
                lanes.Add(new DdsMinefieldLane { Ax = l.A.X, Ay = l.A.Y, Bx = l.B.X, By = l.B.Y, Width = l.Width, ClearedBySidePlus1 = l.ClearedBySidePlus1, KnownToSides = l.KnownToSides });
            }
            return new EntityMinefield { EntityId = netId, MineKind = (byte)c.Type, AntiHandling = c.AntiHandling, LaneCount = (byte)n, SpacingM = c.SpacingM, OrientationDeg = c.OrientationDeg,
                                         Seed = unchecked((int)c.Seed), KnownAtStartSides = c.KnownAtStartSides, Lanes = lanes };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityMinefield prev, in Minefield c)
        {
            int n = Math.Min((int)c.LaneCount, Minefield.MaxLanes);
            if (prev.MineKind != (byte)c.Type || prev.AntiHandling != c.AntiHandling || prev.LaneCount != n || prev.SpacingM != c.SpacingM || prev.OrientationDeg != c.OrientationDeg
                || prev.Seed != unchecked((int)c.Seed) || prev.KnownAtStartSides != c.KnownAtStartSides || (prev.Lanes?.Count ?? 0) != n) return false;
            for (int i = 0; i < n; i++)
            {
                var l = c.Lane(i); var y = prev.Lanes![i];
                if (y.Ax != l.A.X || y.Ay != l.A.Y || y.Bx != l.B.X || y.By != l.B.Y || y.Width != l.Width || y.ClearedBySidePlus1 != l.ClearedBySidePlus1 || y.KnownToSides != l.KnownToSides) return false;
            }
            return true;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (minefields §3a, §6.1) — publishes a <see cref="MinefieldStrikes"/> from the node that owns the entity's <c>dtMinefieldStrikes</c> descriptor on change; the
    /// <see cref="EntityGroundDamageEgressTranslator"/> shape exactly (send-on-change keyed by network id, a ghost never published, a steady tick builds nothing).
    /// </summary>
    public sealed class EntityMinefieldStrikesEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefieldStrikes;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMinefieldStrikes> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefieldStrikes>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMinefieldStrikesEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityMinefieldStrikes>(participant), static key => new EntityMinefieldStrikes { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<MinefieldStrikes>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<MinefieldStrikes>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<MinefieldStrikes>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in c)) continue;   // compare BEFORE converting
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        internal static (long Lo, long Hi) Halves(Guid g)
        {
            Span<byte> b = stackalloc byte[16]; g.TryWriteBytes(b);
            return (BitConverter.ToInt64(b), BitConverter.ToInt64(b.Slice(8)));
        }

        internal static Guid GuidOf(long lo, long hi)
        {
            Span<byte> b = stackalloc byte[16]; BitConverter.TryWriteBytes(b, lo); BitConverter.TryWriteBytes(b.Slice(8), hi);
            return new Guid(b);
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityMinefieldStrikes ToSample(int netId, in MinefieldStrikes c)
        {
            int n = Math.Min((int)c.Count, MinefieldStrikes.Capacity);
            var list = new List<DdsMineGone>(n);
            for (int i = 0; i < n; i++) { var (row, col, how) = c.Get(i); list.Add(new DdsMineGone { Row = row, Col = col, How = (byte)how }); }
            var (lo, hi) = Halves(c.Field);
            return new EntityMinefieldStrikes { EntityId = netId, FieldLo = lo, FieldHi = hi, Chunk = c.Chunk, Mines = list };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityMinefieldStrikes prev, in MinefieldStrikes c)
        {
            int n = Math.Min((int)c.Count, MinefieldStrikes.Capacity);
            if (prev.Chunk != c.Chunk || (prev.Mines?.Count ?? 0) != n) return false;
            var (lo, hi) = Halves(c.Field);
            if (prev.FieldLo != lo || prev.FieldHi != hi) return false;
            for (int i = 0; i < n; i++)
            {
                var (row, col, how) = c.Get(i); var y = prev.Mines![i];
                if (y.Row != row || y.Col != col || y.How != (byte)how) return false;
            }
            return true;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (minefields §3a, §6.1) — publishes a <see cref="MinefieldKnowledge"/> from the node that owns the entity's <c>dtMinefieldKnowledge</c> descriptor on change; the
    /// <see cref="EntityGroundDamageEgressTranslator"/> shape exactly (send-on-change keyed by network id, a ghost never published, a steady tick builds nothing).
    /// </summary>
    public sealed class EntityMinefieldKnowledgeEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefieldKnowledge;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMinefieldKnowledge> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefieldKnowledge>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMinefieldKnowledgeEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityMinefieldKnowledge>(participant), static key => new EntityMinefieldKnowledge { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<MinefieldKnowledge>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<MinefieldKnowledge>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<MinefieldKnowledge>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in c)) continue;   // compare BEFORE converting
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        internal static (long Lo, long Hi) Halves(Guid g)
        {
            Span<byte> b = stackalloc byte[16]; g.TryWriteBytes(b);
            return (BitConverter.ToInt64(b), BitConverter.ToInt64(b.Slice(8)));
        }

        internal static Guid GuidOf(long lo, long hi)
        {
            Span<byte> b = stackalloc byte[16]; BitConverter.TryWriteBytes(b, lo); BitConverter.TryWriteBytes(b.Slice(8), hi);
            return new Guid(b);
        }

        private static unsafe int CellInt(in MinefieldKnowledge c, int i)
        {
            fixed (byte* p = c.Cells) return p[i * 4] | (p[i * 4 + 1] << 8) | (p[i * 4 + 2] << 16) | (p[i * 4 + 3] << 24);
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static unsafe EntityMinefieldKnowledge ToSample(int netId, in MinefieldKnowledge c)
        {
            var h = c.Header;
            var cells = new List<int>(MinefieldKnowledge.CellBytes / 4);
            for (int i = 0; i < MinefieldKnowledge.CellBytes / 4; i++) cells.Add(CellInt(in c, i));
            var (lo, hi) = Halves(h.Subject);
            return new EntityMinefieldKnowledge { EntityId = netId, SubjectKind = (byte)h.SubjectKind, SidePlus1 = h.SidePlus1, Kind = (byte)h.Kind, State = h.State, Source = (byte)h.Source,
                                                  Marked = h.Marked, Chunk = h.Chunk, SubjectLo = lo, SubjectHi = hi, ObjectIndex = h.ObjectIndex, LearnedAt = h.LearnedAt.Seconds, Cells = cells };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityMinefieldKnowledge prev, in MinefieldKnowledge c)
        {
            var h = c.Header;
            var (lo, hi) = Halves(h.Subject);
            if (prev.SubjectKind != (byte)h.SubjectKind || prev.SidePlus1 != h.SidePlus1 || prev.Kind != (byte)h.Kind || prev.State != h.State || prev.Source != (byte)h.Source || prev.Marked != h.Marked
                || prev.Chunk != h.Chunk || prev.SubjectLo != lo || prev.SubjectHi != hi || prev.ObjectIndex != h.ObjectIndex || prev.LearnedAt != h.LearnedAt.Seconds) return false;
            if ((prev.Cells?.Count ?? 0) != MinefieldKnowledge.CellBytes / 4) return false;
            for (int i = 0; i < MinefieldKnowledge.CellBytes / 4; i++) if (prev.Cells![i] != CellInt(in c, i)) return false;
            return true;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }


    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (minefields §3a, §6.1) — publishes a <see cref="MinefieldLanes"/> from the node that owns the entity's <c>dtMinefieldLanes</c> descriptor on change; the
    /// <see cref="EntityGroundDamageEgressTranslator"/> shape exactly (send-on-change keyed by network id, a ghost never published, a steady tick builds nothing).
    /// </summary>
    public sealed class EntityMinefieldLanesEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefieldLanes;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMinefieldLanes> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefieldLanes>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMinefieldLanesEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityMinefieldLanes>(participant), static key => new EntityMinefieldLanes { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<MinefieldLanes>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<MinefieldLanes>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<MinefieldLanes>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in c)) continue;   // compare BEFORE converting
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static unsafe EntityMinefieldLanes ToSample(int netId, in MinefieldLanes c)
        {
            int n = Math.Min((int)c.Count, MinefieldLanes.Capacity);
            var list = new List<DdsClearedLane>(n);
            for (int i = 0; i < n; i++) { var l = c.Get(i); list.Add(new DdsClearedLane { Ax = l.A.X, Ay = l.A.Y, Bx = l.B.X, By = l.B.Y, Width = l.Width, SidePlus1 = l.SidePlus1 }); }
            var (lo, hi) = Halves(c.Field);
            return new EntityMinefieldLanes { EntityId = netId, FieldLo = lo, FieldHi = hi, Chunk = c.Chunk, Lanes = list };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityMinefieldLanes prev, in MinefieldLanes c)
        {
            int n = Math.Min((int)c.Count, MinefieldLanes.Capacity);
            if (prev.Chunk != c.Chunk || (prev.Lanes?.Count ?? 0) != n) return false;
            var (lo, hi) = Halves(c.Field);
            if (prev.FieldLo != lo || prev.FieldHi != hi) return false;
            for (int i = 0; i < n; i++)
            {
                var l = c.Get(i); var y = prev.Lanes![i];
                if (y.Ax != l.A.X || y.Ay != l.A.Y || y.Bx != l.B.X || y.By != l.B.Y || y.Width != l.Width || y.SidePlus1 != l.SidePlus1) return false;
            }
            return true;
        }

        internal static (long Lo, long Hi) Halves(Guid g)
        {
            Span<byte> b = stackalloc byte[16]; g.TryWriteBytes(b);
            return (BitConverter.ToInt64(b), BitConverter.ToInt64(b.Slice(8)));
        }

        internal static Guid GuidOf(long lo, long hi)
        {
            Span<byte> b = stackalloc byte[16]; BitConverter.TryWriteBytes(b, lo); BitConverter.TryWriteBytes(b.Slice(8), hi);
            return new Guid(b);
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 — applies a published <see cref="Minefield"/> on every node that does not own it (the <see cref="EntityGroundDamageIngressTranslator"/> shape: an
    /// unknown id makes a ghost, the recorded owner never takes its own sample back).
    /// </summary>
    public sealed class EntityMinefieldIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefield;

        private readonly DdsReader<EntityMinefield>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefield>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMinefieldIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityMinefield>(participant) : null;
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
        internal void Receive(in EntityMinefield d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityMinefield data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityMinefieldIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static Minefield FromSample(in EntityMinefield d)
        {
            var c = new Minefield { Type = (MineType)d.MineKind, AntiHandling = d.AntiHandling, SpacingM = d.SpacingM, OrientationDeg = d.OrientationDeg,
                                    Seed = unchecked((uint)d.Seed), KnownAtStartSides = (ushort)d.KnownAtStartSides };
            int n = Math.Min(d.Lanes?.Count ?? 0, Minefield.MaxLanes);
            for (int i = 0; i < n; i++)
            {
                var l = d.Lanes![i];
                c.SetLane(i, new MinefieldLane(new System.Numerics.Vector2(l.Ax, l.Ay), new System.Numerics.Vector2(l.Bx, l.By), l.Width, l.ClearedBySidePlus1, (ushort)l.KnownToSides));
            }
            c.LaneCount = (byte)n;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityMinefield d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 — applies a published <see cref="MinefieldStrikes"/> on every node that does not own it (the <see cref="EntityGroundDamageIngressTranslator"/> shape: an
    /// unknown id makes a ghost, the recorded owner never takes its own sample back).
    /// </summary>
    public sealed class EntityMinefieldStrikesIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefieldStrikes;

        private readonly DdsReader<EntityMinefieldStrikes>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefieldStrikes>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMinefieldStrikesIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityMinefieldStrikes>(participant) : null;
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
        internal void Receive(in EntityMinefieldStrikes d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityMinefieldStrikes data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityMinefieldStrikesIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static MinefieldStrikes FromSample(in EntityMinefieldStrikes d)
        {
            var c = new MinefieldStrikes { Field = Egress.EntityMinefieldStrikesEgressTranslator.GuidOf(d.FieldLo, d.FieldHi), Chunk = (ushort)d.Chunk };
            int n = Math.Min(d.Mines?.Count ?? 0, MinefieldStrikes.Capacity);
            for (int i = 0; i < n; i++) { var m = d.Mines![i]; c.Set(i, (short)m.Row, (short)m.Col, (MineGone)m.How); }
            c.Count = (byte)n;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityMinefieldStrikes d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 — applies a published <see cref="MinefieldKnowledge"/> on every node that does not own it (the <see cref="EntityGroundDamageIngressTranslator"/> shape: an
    /// unknown id makes a ghost, the recorded owner never takes its own sample back).
    /// </summary>
    public sealed class EntityMinefieldKnowledgeIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefieldKnowledge;

        private readonly DdsReader<EntityMinefieldKnowledge>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefieldKnowledge>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMinefieldKnowledgeIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityMinefieldKnowledge>(participant) : null;
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
        internal void Receive(in EntityMinefieldKnowledge d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityMinefieldKnowledge data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityMinefieldKnowledgeIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static unsafe MinefieldKnowledge FromSample(in EntityMinefieldKnowledge d)
        {
            var c = new MinefieldKnowledge();
            c.Header = new PlaceKnowledgeHeader { SubjectKind = (SubjectKind)d.SubjectKind, SidePlus1 = d.SidePlus1, Kind = (PlaceKnowledgeKind)d.Kind, State = d.State, Source = (KnowledgeSource)d.Source,
                                                  Marked = d.Marked, Chunk = (ushort)d.Chunk, Subject = Egress.EntityMinefieldKnowledgeEgressTranslator.GuidOf(d.SubjectLo, d.SubjectHi),
                                                  ObjectIndex = d.ObjectIndex, LearnedAt = (SimInstant)d.LearnedAt };
            int n = Math.Min(d.Cells?.Count ?? 0, MinefieldKnowledge.CellBytes / 4);
            for (int i = 0; i < n; i++)
            {
                int v = d.Cells![i];
                c.Cells[i * 4] = (byte)v; c.Cells[i * 4 + 1] = (byte)(v >> 8); c.Cells[i * 4 + 2] = (byte)(v >> 16); c.Cells[i * 4 + 3] = (byte)(v >> 24);
            }
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityMinefieldKnowledge d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }


    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 — applies a published <see cref="MinefieldLanes"/> on every node that does not own it (the <see cref="EntityGroundDamageIngressTranslator"/> shape: an
    /// unknown id makes a ghost, the recorded owner never takes its own sample back).
    /// </summary>
    public sealed class EntityMinefieldLanesIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMinefieldLanes;

        private readonly DdsReader<EntityMinefieldLanes>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMinefieldLanes>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMinefieldLanesIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityMinefieldLanes>(participant) : null;
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
        internal void Receive(in EntityMinefieldLanes d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityMinefieldLanes data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityMinefieldLanesIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static unsafe MinefieldLanes FromSample(in EntityMinefieldLanes d)
        {
            var c = new MinefieldLanes { Field = Egress.EntityMinefieldLanesEgressTranslator.GuidOf(d.FieldLo, d.FieldHi), Chunk = (ushort)d.Chunk };
            int n = Math.Min(d.Lanes?.Count ?? 0, MinefieldLanes.Capacity);
            for (int i = 0; i < n; i++)
            {
                var l = d.Lanes![i];
                c.TryAdd(new LaneItem(c.Field, new System.Numerics.Vector2(l.Ax, l.Ay), new System.Numerics.Vector2(l.Bx, l.By), l.Width, l.SidePlus1));
            }
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityMinefieldLanes d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }

}
