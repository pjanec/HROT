using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-4657</c> (thermal drivers TD-3, TD-4; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4) — publishes an entity's <see cref="HeatSource"/> from the node owning
    /// its <c>dtHeatSource</c> (the Muscle — the node that moves it), on change. The <c>EntityRadarEmission</c> shape (blittable sample, compared whole).
    /// </summary>
    public sealed class EntityHeatSourceEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtHeatSource;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityHeatSource> _instances;
        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityHeatSource>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityHeatSourceEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityHeatSource>(participant) : null, static key => new EntityHeatSource { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<HeatSource>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<HeatSource>().DefaultRoutePublishable().Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(OrdinalValue, 0);
            foreach (var unit in _query)
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<HeatSource>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityHeatSource ToSample(int netId, in HeatSource h)
            => new() { EntityId = netId, State = (byte)h.State, Since = h.Since.Seconds, MotionSince = h.MotionSince.Seconds };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-4657</c> (TD-6, TD-16; §3c) — publishes the <see cref="FireState"/> spots of a vehicle / a building record / a cell record from the node owning its
    /// <c>dtFireState</c> (the Health owner), on change. The <c>EntityGroundHeatMarks</c> shape: compared BEFORE converting, so a steady tick builds nothing.
    /// </summary>
    public sealed class EntityFireStateEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtFireState;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityFireState> _instances;
        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFireState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityFireStateEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityFireState>(participant) : null, static key => new EntityFireState { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<FireState>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<FireState>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(OrdinalValue, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var fire = ref view.GetComponentRO<FireState>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in fire)) continue;
                _instances.Write(netId, ToSample(netId, in fire));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityFireState ToSample(int netId, in FireState c)
        {
            int n = Math.Min((int)c.Count, FireState.Capacity);
            var list = new List<DdsFireSpot>(n);
            for (int i = 0; i < n; i++)
            {
                var s = c.Get(i);
                list.Add(new DdsFireSpot { X = s.X, Y = s.Y, Z = s.Z, Radius = s.Radius, Since = s.Since.Seconds, Seconds = s.Seconds, Intensity = s.Intensity });
            }
            return new EntityFireState { EntityId = netId, Spots = list };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityFireState prev, in FireState c)
        {
            int n = Math.Min((int)c.Count, FireState.Capacity);
            if ((prev.Spots?.Count ?? 0) != n) return false;
            for (int i = 0; i < n; i++)
            {
                var s = c.Get(i); var y = prev.Spots![i];
                if (y.X != s.X || y.Y != s.Y || y.Z != s.Z || y.Radius != s.Radius || y.Since != s.Since.Seconds || y.Seconds != s.Seconds || y.Intensity != s.Intensity)
                    return false;
            }
            return true;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-4657</c> (TD-3) — applies an entity's published <see cref="HeatSource"/> on every node that does not own it (a late joiner included: TransientLocal).
    /// An unknown id makes a ghost (the descriptor may arrive before the entity's master); the recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityHeatSourceIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtHeatSource;
        private readonly DdsReader<EntityHeatSource>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityHeatSource>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityHeatSourceIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityHeatSource>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = (NodeId)localNodeId;
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
        internal void Receive(in EntityHeatSource d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityHeatSource d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!ThermalIngress.Resolve(d.EntityId, _entityMap, _ghostCreationSystem, view, _localNodeId, out var entity)) return;
            if (view is EntityRepository repo && Owns(repo, entity)) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static HeatSource FromSample(in EntityHeatSource d)
            => HeatSource.Of((HeatSourceState)d.State, (SimInstant)d.Since, (SimInstant)d.MotionSince);

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityHeatSource d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }

    /// <summary>⭐ <c>CE-4657</c> (TD-6) — applies a holder's published <see cref="FireState"/> on every node that does not own it; the heat-marks shape.</summary>
    public sealed class EntityFireStateIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtFireState;
        private readonly DdsReader<EntityFireState>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFireState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityFireStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityFireState>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = (NodeId)localNodeId;
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

        /// <summary>Takes one sample (the test seam).</summary>
        internal void Receive(in EntityFireState d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityFireState d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!ThermalIngress.Resolve(d.EntityId, _entityMap, _ghostCreationSystem, view, _localNodeId, out var entity)) return;
            if (view is EntityRepository repo && Owns(repo, entity)) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static FireState FromSample(in EntityFireState d)
        {
            var c = new FireState();
            int n = Math.Min(d.Spots?.Count ?? 0, FireState.Capacity);
            for (int i = 0; i < n; i++)
            {
                var k = d.Spots![i];
                c.Set(i, new FireSpot(k.X, k.Y, k.Z, k.Radius, (SimInstant)k.Since, k.Seconds, k.Intensity));
            }
            c.Count = (byte)n;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityFireState d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }

    /// <summary>The shared "find the entity, or ghost it" step of the thermal-driver ingress pair (the heat-marks shape).</summary>
    internal static class ThermalIngress
    {
        internal static bool Resolve(int netId, NetworkEntityMap map, GhostCreationSystem ghosts, ISimulationView view, NodeId local, out Entity entity)
        {
            if (map.TryGetEntity(netId, out entity)) return true;
            if (view is not EntityRepository repo)
            {
                FdpLog<EntityHeatSourceIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", local, netId);
                return false;
            }
            entity = ghosts.CreateGhost(repo, netId);
            return true;
        }
    }
}
