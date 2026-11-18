using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.NED.Descriptors;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-4670</c> (vehicle appearance VA-S2, VA-12; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — publishes a vehicle's
    /// <see cref="VehicleOpenings"/> from the node owning its <c>dtVehicleOpenings</c> (the boarding arbiter — the passenger list's owner), on change. The
    /// <c>EntityFireState</c> shape: compared BEFORE converting, so a steady tick builds nothing. The state crosses as the wire's own byte (R-4295).
    /// </summary>
    public sealed class EntityVehicleOpeningsEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtVehicleOpenings;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityVehicleOpenings> _instances;
        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityVehicleOpenings>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityVehicleOpeningsEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityVehicleOpenings>(participant) : null, static key => new EntityVehicleOpenings { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<VehicleOpenings>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<VehicleOpenings>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(OrdinalValue, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<VehicleOpenings>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in c)) continue;
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityVehicleOpenings ToSample(int netId, in VehicleOpenings c)
        {
            int n = Math.Min((int)c.Count, VehicleOpenings.Capacity);
            var list = new List<DdsOpening>(n);
            for (int i = 0; i < n; i++) list.Add(new DdsOpening { State = (byte)c.Slots[i].State, Since = c.Slots[i].Since });
            return new EntityVehicleOpenings { EntityId = netId, Openings = list };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free.</summary>
        internal static bool Matches(in EntityVehicleOpenings prev, in VehicleOpenings c)
        {
            int n = Math.Min((int)c.Count, VehicleOpenings.Capacity);
            if ((prev.Openings?.Count ?? 0) != n) return false;
            for (int i = 0; i < n; i++)
            {
                var y = prev.Openings![i];
                if (y.State != (byte)c.Slots[i].State || y.Since != c.Slots[i].Since) return false;
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
    /// <summary>⭐ <c>CE-4670</c> (VA-S2) — applies a vehicle's published <see cref="VehicleOpenings"/> on every node that does not own it (a late joiner included:
    /// TransientLocal); an unknown id makes a ghost; the recorded owner never takes its own sample back. The <c>EntityFireState</c> shape.</summary>
    public sealed class EntityVehicleOpeningsIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtVehicleOpenings;
        private readonly DdsReader<EntityVehicleOpenings>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityVehicleOpenings>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityVehicleOpeningsIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityVehicleOpenings>(participant) : null;
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
        internal void Receive(in EntityVehicleOpenings d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityVehicleOpenings d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!ThermalIngress.Resolve(d.EntityId, _entityMap, _ghostCreationSystem, view, _localNodeId, out var entity)) return;   // the shared find-or-ghost step
            if (view is EntityRepository repo && Owns(repo, entity)) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static VehicleOpenings FromSample(in EntityVehicleOpenings d)
        {
            var c = new VehicleOpenings();
            int n = Math.Min(d.Openings?.Count ?? 0, VehicleOpenings.Capacity);
            for (int i = 0; i < n; i++) c.Slots[i] = new OpeningSlot { State = (OpeningState)d.Openings![i].State, Since = d.Openings[i].Since };
            c.Count = (byte)n;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityVehicleOpenings d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
