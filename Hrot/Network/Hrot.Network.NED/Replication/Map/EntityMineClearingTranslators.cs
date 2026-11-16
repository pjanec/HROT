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
    /// <summary>⭐ <c>CE-4624</c> MS-T4 (MF-9, MR-11) — publishes a mine clearer's <see cref="MineClearing"/> from the owner of its <c>dtMineClearing</c> (the Brain group) on change.</summary>
    public sealed class EntityMineClearingEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMineClearing;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMineClearing> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMineClearing>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMineClearingEgressTranslator(DdsParticipant participant)
            => _instances = new(new DdsWriter<EntityMineClearing>(participant), static key => new EntityMineClearing { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<MineClearing>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<MineClearing>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<MineClearing>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Same(in prev, in c)) continue;
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        internal static EntityMineClearing ToSample(int netId, in MineClearing c) => new()
        {
            EntityId = netId, Active = c.Active, Tool = (byte)c.Tool, WidthM = c.WidthM, ToX = c.LineTo.X, ToY = c.LineTo.Y, ToZ = c.LineTo.Z,
        };

        internal static bool Same(in EntityMineClearing s, in MineClearing c)
            => s.Active == c.Active && s.Tool == (byte)c.Tool && s.WidthM == c.WidthM && s.ToX == c.LineTo.X && s.ToY == c.LineTo.Y && s.ToZ == c.LineTo.Z;

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-4624</c> MS-T4 (MF-9, MR-11) — applies a mine clearer's <see cref="MineClearing"/> on every node that does not own its <c>dtMineClearing</c> (the Muscle that cuts reads it). An unknown entity waits for nothing: the marker is resent on change.</summary>
    public sealed class EntityMineClearingIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMineClearing;
        private readonly DdsReader<EntityMineClearing>? _reader;
        private readonly NetworkEntityMap _entityMap;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMineClearing>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMineClearingIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityMineClearing>(participant) : null;
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

        internal void Receive(in EntityMineClearing d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityMineClearing d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var entity)) return;
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        internal static MineClearing FromSample(in EntityMineClearing d) => new()
        {
            Active = d.Active, Tool = (MineClearingTool)d.Tool, WidthM = d.WidthM, LineTo = new Vector3(d.ToX, d.ToY, d.ToZ),
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityMineClearing d && !AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) repo.SetComponent(entity, FromSample(in d));
        }
        public void Dispose(int networkEntityId) { }
    }
}
