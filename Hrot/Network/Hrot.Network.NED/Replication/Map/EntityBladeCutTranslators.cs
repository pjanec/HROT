using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>⭐ <c>CE-4601</c> (B-4) — publishes a bulldozer's <see cref="BladeCut"/> from the owner of its <c>dtBladeCut</c> (the Brain group) on change.</summary>
    public sealed class EntityBladeCutEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtBladeCut;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityBladeCut> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityBladeCut>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityBladeCutEgressTranslator(DdsParticipant participant)
            => _instances = new(new DdsWriter<EntityBladeCut>(participant), static key => new EntityBladeCut { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<BladeCut>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<BladeCut>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<BladeCut>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Same(in prev, in c)) continue;
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        internal static EntityBladeCut ToSample(int netId, in BladeCut c) => new()
        {
            EntityId = netId, Active = c.Active, Target = (byte)c.Target, AtX = c.At.X, AtY = c.At.Y, AtZ = c.At.Z, PushSeconds = c.PushSeconds,
        };

        internal static bool Same(in EntityBladeCut s, in BladeCut c)
            => s.Active == c.Active && s.Target == (byte)c.Target && s.AtX == c.At.X && s.AtY == c.At.Y && s.AtZ == c.At.Z && s.PushSeconds == c.PushSeconds;

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-4601</c> (B-4) — applies a bulldozer's <see cref="BladeCut"/> on every node that does not own its <c>dtBladeCut</c> (the Muscle that cuts reads it). An unknown entity waits for nothing: the marker is resent on change.</summary>
    public sealed class EntityBladeCutIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtBladeCut;
        private readonly DdsReader<EntityBladeCut>? _reader;
        private readonly NetworkEntityMap _entityMap;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityBladeCut>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityBladeCutIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityBladeCut>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
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

        internal void Receive(in EntityBladeCut d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityBladeCut d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var entity)) return;
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        internal static BladeCut FromSample(in EntityBladeCut d) => new()
        {
            Active = d.Active, Target = (BladeCutTarget)d.Target, At = new Vector3(d.AtX, d.AtY, d.AtZ), PushSeconds = d.PushSeconds,
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityBladeCut d && !AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) repo.SetComponent(entity, FromSample(in d));
        }
        public void Dispose(int networkEntityId) { }
    }
}
