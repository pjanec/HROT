using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.Radar;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-4617</c> (B-1, RS-10; docs/designs/air-attack/DESIGN_Radar_SAM.md) — publishes a vehicle's <see cref="RadarEmission"/> from the node
    /// owning its <c>dtRadarEmission</c> (the brain owner), on change. The <c>dtGesture</c> shape; the tracked target goes as its network id.
    /// </summary>
    public sealed class EntityRadarEmissionEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityRadarEmission> _instances;

        private static readonly int[] s_targetIds = { GlobalComponentIds.RadarEmission };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRadarEmission>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtRadarEmission;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityRadarEmissionEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityRadarEmission>(participant) : null, static key => new EntityRadarEmission { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            BeginFrame(view);
            ScanState(view, offFrame: false);
        }

        /// <summary>⭐ CE-3427 N-1 — the world-epoch and destruction bookkeeping, on the live world.</summary>
        public void BeginFrame(ISimulationView view) => _instances.OnFrame(view);

        /// <summary>⭐ CE-3427 N-1 — the state scan: reads only <paramref name="view"/>, so it may run on the off-frame replica.</summary>
        public void ScanState(ISimulationView view, bool offFrame)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RadarEmission>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<RadarEmission>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(view, netId, view.GetComponentRO<RadarEmission>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam): the tracked target as its network id, 0 when none or not networked.</summary>
        internal static EntityRadarEmission ToSample(ISimulationView view, int netId, in RadarEmission e)
        {
            var t = e.TrackedTarget;
            int target = !t.IsNull && view.IsAlive(t) && view.HasComponent<NetworkIdentity>(t) ? view.GetComponentRO<NetworkIdentity>(t).Value : 0;
            return new() { EntityId = netId, Mode = (byte)e.Mode, EmitterClass = (byte)e.EmitterClass, TrackedTarget = target, RangeM = e.RangeM, OnSince = e.OnSince };
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-4617</c> — applies a vehicle's published <see cref="RadarEmission"/> on this node. An unknown unit is dropped; the recorded
    /// owner never takes its own sample back; a tracked target this node does not know yet arrives as none.</summary>
    public sealed class EntityRadarEmissionIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtRadarEmission;
        private readonly DdsReader<EntityRadarEmission>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRadarEmission>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityRadarEmissionIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityRadarEmission>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
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

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityRadarEmission d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            var tracked = d.TrackedTarget != 0 && _entityMap.TryGetEntity(d.TrackedTarget, out var t) ? t : Entity.Null;
            cmd.AddComponent(unit, new RadarEmission
            {
                Mode = (RadarMode)d.Mode, EmitterClass = (RadarEmitterClass)d.EmitterClass, TrackedTarget = tracked, RangeM = d.RangeM, OnSince = (SimInstant)d.OnSince,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
