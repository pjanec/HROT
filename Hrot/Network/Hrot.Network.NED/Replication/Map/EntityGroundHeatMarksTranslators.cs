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
    /// ⭐ <c>CE-4664</c> (thermal drivers TD-14; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4b) — publishes a ground cell's <see cref="GroundHeatMarks"/> from the node
    /// that owns the entity's <c>dtGroundHeatMarks</c> descriptor — the node that moves the vehicles — on change. The crater pair's shape (<c>EntityGroundDamageTranslators</c>).
    /// </summary>
    public sealed class EntityGroundHeatMarksEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtGroundHeatMarks;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityGroundHeatMarks> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityGroundHeatMarks>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityGroundHeatMarksEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityGroundHeatMarks>(participant), static key => new EntityGroundHeatMarks { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<GroundHeatMarks>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<GroundHeatMarks>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var damage = ref view.GetComponentRO<GroundHeatMarks>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in damage)) continue;   // compare BEFORE converting: a steady tick builds nothing
                _instances.Write(netId, ToSample(netId, in damage));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityGroundHeatMarks ToSample(int netId, in GroundHeatMarks c)
        {
            int n = Math.Min((int)c.Count, GroundHeatMarks.Capacity);
            var list = new List<DdsHeatMark>(n);
            for (int i = 0; i < n; i++)
            {
                var m = c.Get(i);
                list.Add(new DdsHeatMark
                {
                    Kind = (byte)m.Kind, X = m.At.X, Y = m.At.Y, Heading = m.Heading, Length = m.Length, Width = m.Width, HotEnd = (byte)m.HotEnd,
                    StandSince = m.StandSince.Seconds, LeftAt = m.LeftAt.Seconds, EngineOnSeconds = m.EngineOnSeconds, PowerClass = (byte)m.PowerClass,
                });
            }
            return new EntityGroundHeatMarks { EntityId = netId, CellX = c.CellX, CellY = c.CellY, Marks = list };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityGroundHeatMarks prev, in GroundHeatMarks c)
        {
            if (prev.CellX != c.CellX || prev.CellY != c.CellY) return false;
            int n = Math.Min((int)c.Count, GroundHeatMarks.Capacity);
            if ((prev.Marks?.Count ?? 0) != n) return false;
            for (int i = 0; i < n; i++)
            {
                var m = c.Get(i); var y = prev.Marks![i];
                if (y.Kind != (byte)m.Kind || y.X != m.At.X || y.Y != m.At.Y || y.Heading != m.Heading || y.Length != m.Length || y.Width != m.Width
                    || y.HotEnd != (byte)m.HotEnd || y.StandSince != m.StandSince.Seconds || y.LeftAt != m.LeftAt.Seconds || y.EngineOnSeconds != m.EngineOnSeconds
                    || y.PowerClass != (byte)m.PowerClass) return false;
            }
            return true;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // the delete goes on the wire

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-4664</c> (TD-14) — applies a ground cell's published <see cref="GroundHeatMarks"/> on every node that does not own it (a late joiner included: TransientLocal).
    /// The crater pair's shape — an unknown id makes a ghost, the recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityGroundHeatMarksIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtGroundHeatMarks;

        private readonly DdsReader<EntityGroundHeatMarks>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityGroundHeatMarks>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityGroundHeatMarksIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityGroundHeatMarks>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        }

        /// <inheritdoc />
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

        /// <summary>Takes one sample (the test seam), as <see cref="PollIngress"/> does for each one it reads.</summary>
        internal void Receive(in EntityGroundHeatMarks d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityGroundHeatMarks data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityGroundHeatMarksIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static GroundHeatMarks FromSample(in EntityGroundHeatMarks d)
        {
            var c = new GroundHeatMarks { CellX = d.CellX, CellY = d.CellY };
            int n = Math.Min(d.Marks?.Count ?? 0, GroundHeatMarks.Capacity);
            for (int i = 0; i < n; i++)
            {
                var k = d.Marks![i];
                c.Set(i, new HeatMark((HeatMarkKind)k.Kind, new System.Numerics.Vector2(k.X, k.Y), k.Heading, k.Length, k.Width, (HeatMarkEnd)k.HotEnd,
                                      (SimInstant)k.StandSince, (SimInstant)k.LeftAt, k.EngineOnSeconds, (ThermalPowerClass)k.PowerClass));
            }
            c.Count = (byte)n;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityGroundHeatMarks d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
