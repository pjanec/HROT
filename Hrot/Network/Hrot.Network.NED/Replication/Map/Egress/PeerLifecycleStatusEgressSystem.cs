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
        private readonly DdsWriter<EntityLifecycleStatusDescriptor> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;
        private readonly HashSet<long> _reported = new();               // phase-2 Active published
        private readonly HashSet<long> _reportedConstructing = new();   // CE-288 (C2): phase-1 Constructing published

        public string TopicName => "EntityLifecycleStatus";
        public long DescriptorOrdinal => -3; // not a per-entity descriptor; distinct from EntityMaster (-2)
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        private static readonly IReadOnlyList<int> _targetIds = new int[] { GlobalComponentIds.NetworkIdentity };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public PeerLifecycleStatusEgressSystem(DdsParticipant participant, NetworkEntityMap entityMap, long localNodeId)
        {
            _writer = new DdsWriter<EntityLifecycleStatusDescriptor>(participant, "EntityLifecycleStatus");
            _entityMap = entityMap;
            _localNodeId = (int)localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            var repo = view as EntityRepository;
            long ts = repo != null ? repo.GlobalVersion : 0;

            // CE-288 (C2) — PHASE 1: publish Constructing PROMPTLY, once, as soon as the tagged ghost exists
            // (still Constructing). This is the creator's "participating, in progress" signal (§3b.2 / §3c ②):
            // it lets the creator's SHORT phase-1 timeout distinguish a supporting host that is simply slow to
            // reach Active from an older/broken host that will never report at all.
            var constructing = view.Query()
                .With<NetworkIdentity>()
                .With<ReportLifecycleOnActive>()
                .WithLifecycle(EntityLifecycle.Constructing)
                .Build();
            foreach (var entity in constructing)
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (_reportedConstructing.Contains(netId.Value)) continue;
                _writer.Write(new EntityLifecycleStatusDescriptor
                {
                    EntityId   = netId.Value,
                    NodeId     = _localNodeId,
                    StateValue = (int)EntityLifecycle.Constructing,
                    Timestamp  = ts,
                });
                _reportedConstructing.Add(netId.Value);
                SentSampleCount++;
                FdpLog<PeerLifecycleStatusEgressSystem>.Debug(
                    "[Node-{0}] reliable-init: published Constructing (phase-1) for NetID={1}", _localNodeId, netId.Value);
            }

            // PHASE 2: publish Active once the ghost finishes local construction (the release signal).
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<ReportLifecycleOnActive>()
                .WithLifecycle(EntityLifecycle.Active)
                .Build();

            foreach (var entity in query)
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (_reported.Contains(netId.Value))
                    continue;

                _writer.Write(new EntityLifecycleStatusDescriptor
                {
                    EntityId   = netId.Value,
                    NodeId     = _localNodeId,
                    StateValue = (int)EntityLifecycle.Active,
                    Timestamp  = ts,
                });

                _reported.Add(netId.Value);
                SentSampleCount++;
                FdpLog<PeerLifecycleStatusEgressSystem>.Debug(
                    "[Node-{0}] reliable-init: published Active status for NetID={1}", _localNodeId, netId.Value);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            _reported.Remove(networkEntityId);
            _reportedConstructing.Remove(networkEntityId);
        }
    }
}
