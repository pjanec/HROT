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
    /// <summary>⭐ <c>CE-4693</c> (bridge laying BL-S3) — publishes a bridge engineer's <see cref="BridgeCarried"/> from the owner of its <c>dtBridgeCarried</c> (the Brain group) on change (the RamOrder / MineClearing pair).</summary>
    public sealed class EntityBridgeCarriedEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtBridgeCarried;
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityBridgeCarried> _instances;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityBridgeCarried>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityBridgeCarriedEgressTranslator(DdsParticipant participant)
            => _instances = new(new DdsWriter<EntityBridgeCarried>(participant), static key => new EntityBridgeCarried { EntityId = (int)key });

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<BridgeCarried>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<BridgeCarried>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var c = ref view.GetComponentRO<BridgeCarried>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Same(in prev, in c)) continue;
                _instances.Write(netId, ToSample(netId, in c));
                SentSampleCount++;
            }
        }

        internal static EntityBridgeCarried ToSample(int netId, in BridgeCarried c) => new()
        {
            EntityId = netId, Count = c.Count, Order = (byte)c.Order, BridgeType = c.BridgeType,
            NearX = c.NearBank.X, NearY = c.NearBank.Y, NearZ = c.NearBank.Z, FarX = c.FarBank.X, FarY = c.FarBank.Y, FarZ = c.FarBank.Z,
            TargetNetId = c.Target.NetworkId, Refusal = (byte)c.Refusal, GapM = c.GapM,
        };

        internal static bool Same(in EntityBridgeCarried s, in BridgeCarried c)
            => s.Count == c.Count && s.Order == (byte)c.Order && s.Refusal == (byte)c.Refusal && s.GapM == c.GapM && s.BridgeType == c.BridgeType && s.TargetNetId == c.Target.NetworkId
               && s.NearX == c.NearBank.X && s.NearY == c.NearBank.Y && s.NearZ == c.NearBank.Z && s.FarX == c.FarBank.X && s.FarY == c.FarBank.Y && s.FarZ == c.FarBank.Z;

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-4693</c> (bridge laying BL-S3) — applies a bridge engineer's <see cref="BridgeCarried"/> on every node that does not own its <c>dtBridgeCarried</c> (the node that moves it reads it). An unknown entity waits for nothing: the order is resent on change.</summary>
    public sealed class EntityBridgeCarriedIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtBridgeCarried;
        private readonly DdsReader<EntityBridgeCarried>? _reader;
        private readonly NetworkEntityMap _entityMap;
        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityBridgeCarried>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityBridgeCarriedIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityBridgeCarried>(participant) : null;
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

        internal void Receive(in EntityBridgeCarried d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityBridgeCarried d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var entity)) return;
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.SetComponent(entity, FromSample(in d));
        }

        internal static BridgeCarried FromSample(in EntityBridgeCarried d) => new()
        {
            Count = d.Count, Order = (BridgeOrder)d.Order, BridgeType = d.BridgeType,
            NearBank = new Fdp.Core.WorldPoint(d.NearX, d.NearY, d.NearZ), FarBank = new Fdp.Core.WorldPoint(d.FarX, d.FarY, d.FarZ),
            Target = new Fdp.Toolkit.Replication.EntityRef(d.TargetNetId), Refusal = (BridgeRefusal)d.Refusal, GapM = d.GapM,
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityBridgeCarried d && !AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0))) repo.SetComponent(entity, FromSample(in d));
        }
        public void Dispose(int networkEntityId) { }
    }
}
