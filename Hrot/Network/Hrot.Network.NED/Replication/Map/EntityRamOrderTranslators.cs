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
using CarKinem.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>⭐ <c>CE-4622</c> K-2 (Checkpoint RM-2) — publishes a hull's <see cref="RamOrder"/> from the owner of its <c>dtRamOrder</c> (the Brain group) on change (the MineClearing pair).</summary>
    public sealed class EntityRamOrderEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtRamOrder;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityRamOrder> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRamOrder>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityRamOrderEgressTranslator(DdsParticipant participant)
            => _instances = new(new DdsWriter<EntityRamOrder>(participant), static key => new EntityRamOrder { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RamOrder>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<RamOrder>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<RamOrder>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Same(in prev, in c)) continue;
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        internal static EntityRamOrder ToSample(int netId, in RamOrder c) => new()
        {
            EntityId = netId, Active = c.Active, Crowd = c.Crowd, TargetNetId = c.Target.NetworkId,
        };

        internal static bool Same(in EntityRamOrder s, in RamOrder c)
            => s.Active == c.Active && s.Crowd == c.Crowd && s.TargetNetId == c.Target.NetworkId;

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-4622</c> K-2 (Checkpoint RM-2) — applies a hull's <see cref="RamOrder"/> on every node that does not own its <c>dtRamOrder</c> (the node that moves it reads it). An unknown entity waits for nothing: the order is resent on change.</summary>
    public sealed class EntityRamOrderIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtRamOrder;
        private readonly DdsReader<EntityRamOrder>? _reader;
        private readonly NetworkEntityMap _entityMap;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRamOrder>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityRamOrderIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityRamOrder>(participant) : null;
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

        internal void Receive(in EntityRamOrder d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityRamOrder d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var entity)) return;
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        internal static RamOrder FromSample(in EntityRamOrder d) => new()
        {
            Active = d.Active, Crowd = d.Crowd, Target = new Fdp.Toolkit.Replication.EntityRef(d.TargetNetId),
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityRamOrder d && !AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) repo.SetComponent(entity, FromSample(in d));
        }
        public void Dispose(int networkEntityId) { }
    }
}
