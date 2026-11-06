using System.Linq;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Fdp.Network.Cyclone.Topics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Peer-side producer of the reliable-init barrier: when a REMOTE ghost tagged
    /// <see cref="ReportLifecycleOnActive"/> (its <see cref="EntityMaster"/> arrived with the
    /// <c>WaitForAcks</c> flag) finishes local construction and reaches
    /// <see cref="EntityLifecycle.Active"/>, this publishes an
    /// <see cref="EntityLifecycleStatusDescriptor"/> so the creating node can release its
    /// barrier (docs/DESIGN_Cross_Node_Construction_Barrier.md §2/§3a.4). Published exactly once
    /// per entity; the durable QoS retains it for late joiners (§2a).
    ///
    /// <para>The descriptor's <c>NodeId</c> is this node's OWNERSHIP node id (§2b) — the same
    /// value it stamps on <c>OwnershipUpdate.NewOwner</c> — so the creator correlates each peer's
    /// readiness against the id space it seeded its wait-set with.</para>
    /// </summary>
    public sealed class PeerLifecycleStatusEgressSystem : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published (one instance per net id: Constructing, then Active overwrites it).
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityLifecycleStatusDescriptor> _instances;
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityLifecycleStatusDescriptor>();
        public long DescriptorOrdinal => -3; // not a per-entity descriptor; distinct from EntityMaster (-2)
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        private static readonly IReadOnlyList<int> _targetIds = new int[] { GlobalComponentIds.NetworkIdentity };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public PeerLifecycleStatusEgressSystem(DdsParticipant participant, NetworkEntityMap entityMap, int localNodeId)
        {
            _entityMap = entityMap;
            _localNodeId = (Fdp.Core.NodeId)((int)localNodeId);
            var node = _localNodeId;
            _instances = new(new DdsWriter<EntityLifecycleStatusDescriptor>(participant), key => new EntityLifecycleStatusDescriptor { EntityId = (int)key, NodeId = node });   // captures the node id once, at construction
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            // ⭐ CE-3076 (CE-2101's rule) — "reported once per net id": a reused id's new ghost would never report
            //   Constructing / Active, and the creator's reliable-init barrier would wait out its timeout.
            // (CE-3076/CE-2101's rule: a reused id's new ghost must report again — OnFrame forgets what the last world published.)
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)

            var repo = view as EntityRepository;
            long ts = repo != null ? repo.GlobalVersion : 0;

            // CE-288 (C2) — PHASE 1: publish Constructing PROMPTLY, once, as soon as the tagged ghost exists
            // (still Constructing). This is the creator's "participating, in progress" signal (§3b.2 / §3c ②):
            // it lets the creator's SHORT phase-1 timeout distinguish a supporting host that is simply slow to
            // reach Active from an older/broken host that will never report at all.
            var constructing = view.Query()
                .DefaultRoutePublishable()
                .With<ReportLifecycleOnActive>()
                .WithLifecycle(EntityLifecycle.Constructing)
                .Build();
            foreach (var entity in constructing)
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (_instances.TryGetLast(netId.Value, out _)) continue;   // Constructing (or already Active) reported for this id
                _instances.Write(netId.Value, new EntityLifecycleStatusDescriptor
                {
                    EntityId   = netId.Value,
                    NodeId     = _localNodeId,
                    StateValue = (int)EntityLifecycle.Constructing,
                    Timestamp  = ts,
                });
                SentSampleCount++;
                FdpLog<PeerLifecycleStatusEgressSystem>.Debug(
                    "[Node-{0}] reliable-init: published Constructing (phase-1) for NetID={1}", _localNodeId, netId.Value);
            }

            // PHASE 2: publish Active once the ghost finishes local construction (the release signal).
            var query = view.Query()
                .DefaultRoutePublishable()
                .With<ReportLifecycleOnActive>()
                .WithLifecycle(EntityLifecycle.Active)
                .Build();

            foreach (var entity in query)
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (_instances.TryGetLast(netId.Value, out var reported) && reported.StateValue == (int)EntityLifecycle.Active)
                    continue;

                _instances.Write(netId.Value, new EntityLifecycleStatusDescriptor
                {
                    EntityId   = netId.Value,
                    NodeId     = _localNodeId,
                    StateValue = (int)EntityLifecycle.Active,
                    Timestamp  = ts,
                });

                SentSampleCount++;
                FdpLog<PeerLifecycleStatusEgressSystem>.Debug(
                    "[Node-{0}] reliable-init: published Active status for NetID={1}", _localNodeId, netId.Value);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire
    }
}
