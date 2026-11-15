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
    /// ⭐ <c>CE-4603</c> (P-4; docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7, V-7) — publishes a cell's felled-trunk <see cref="TreeDamage"/> from the node that owns the
    /// entity's <c>dtTreeDamage</c> descriptor — the node that felled the trunks — on change. The <see cref="EntityBuildingDamageEgressTranslator"/> shape (send-on-change
    /// keyed by network id, a ghost never published), without its key: a trunk is keyed by its position, the same on every node.
    /// </summary>
    public sealed class EntityTreeDamageEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtTreeDamage;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityTreeDamage> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityTreeDamage>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityTreeDamageEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityTreeDamage>(participant), static key => new EntityTreeDamage { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<TreeDamage>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query().With<TreeDamage>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var damage = ref view.GetComponentRO<TreeDamage>(entity);
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in damage)) continue;   // compare BEFORE converting: a steady tick builds nothing
                _instances.Write(netId, ToSample(netId, in damage));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityTreeDamage ToSample(int netId, in TreeDamage c)
        {
            int n = Math.Min((int)c.Count, TreeDamage.Capacity);
            var list = new List<int>(n * 2);
            for (int i = 0; i < n; i++) { var k = c.Get(i); list.Add(k.X); list.Add(k.Y); }
            return new EntityTreeDamage { EntityId = netId, CellX = c.CellX, CellY = c.CellY, Trunks = list };
        }

        internal static bool Matches(in EntityTreeDamage prev, in TreeDamage c)
        {
            if (prev.CellX != c.CellX || prev.CellY != c.CellY) return false;
            int n = Math.Min((int)c.Count, TreeDamage.Capacity);
            if ((prev.Trunks?.Count ?? 0) != n * 2) return false;
            for (int i = 0; i < n; i++) { var k = c.Get(i); if (prev.Trunks![2 * i] != k.X || prev.Trunks[2 * i + 1] != k.Y) return false; }
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
    /// ⭐ <c>CE-4603</c> (P-4) — applies a cell's felled-trunk published <see cref="TreeDamage"/> on every node that does not own it; every reader then builds its
    /// <see cref="FelledTrees"/> from the view it runs on (<c>DoorStates.Of</c> → <see cref="DoorStates.Trees"/>). The <see cref="EntityBuildingDamageIngressTranslator"/>
    /// shape — an unknown id makes a ghost, the recorded owner never takes its own sample back — with nothing to resolve or defer (a trunk needs no terrain index).
    /// </summary>
    public sealed class EntityTreeDamageIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtTreeDamage;

        private readonly DdsReader<EntityTreeDamage>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityTreeDamage>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityTreeDamageIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityTreeDamage>(participant) : null;
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
        internal void Receive(in EntityTreeDamage d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityTreeDamage data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityTreeDamageIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            cmd.SetComponent(entity, FromSample(in data));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static TreeDamage FromSample(in EntityTreeDamage d)
        {
            var c = new TreeDamage { CellX = d.CellX, CellY = d.CellY };
            int n = Math.Min((d.Trunks?.Count ?? 0) / 2, TreeDamage.Capacity);
            for (int i = 0; i < n; i++) c.Set(i, (d.Trunks![2 * i], d.Trunks[2 * i + 1]));
            c.Count = (byte)n;
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityTreeDamage d || Owns(repo, entity)) return;
            repo.SetComponent(entity, FromSample(in d));
        }

        public void Dispose(int networkEntityId) { }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
