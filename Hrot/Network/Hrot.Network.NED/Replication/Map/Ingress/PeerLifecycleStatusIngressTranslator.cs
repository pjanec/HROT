using System.Collections.Generic;
using Fdp.Network.Cyclone.Topics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Creator-side consumer of the reliable-init barrier: reads
    /// <see cref="EntityLifecycleStatusDescriptor"/> samples published by peers and forwards each
    /// peer's <c>Active</c> confirmation to <see cref="NetworkGatewaySystem.ReceiveLifecycleStatus"/>,
    /// which drops that peer from the entity's wait-set and releases the barrier when it empties
    /// (docs/DESIGN_Cross_Node_Construction_Barrier.md §1.1/§3a.4).
    ///
    /// <para>Late-joiner path (§2a): because the topic is durable (TransientLocal), a node that
    /// subscribes after an entity was created still receives the retained per-node status
    /// instances. Correlation is by network entity id; a status for an entity this node is not
    /// waiting on is a harmless no-op inside the gateway.</para>
    /// </summary>
    public sealed class PeerLifecycleStatusIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<EntityLifecycleStatusDescriptor> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly NetworkGatewaySystem _gateway;
        private readonly int _localNodeId;

        public string TopicName => "EntityLifecycleStatus";
        public long DescriptorOrdinal => -3;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        private static readonly IReadOnlyList<int> _targetIds = new int[0];
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public PeerLifecycleStatusIngressTranslator(
            DdsParticipant participant, NetworkEntityMap entityMap, NetworkGatewaySystem gateway, long localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityLifecycleStatusDescriptor>(participant) : null!;
            _entityMap = entityMap;
            _gateway = gateway;
            _localNodeId = (int)localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null || _gateway is null) return;

            uint frame = view is EntityRepository repo ? repo.GlobalVersion : 0u;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState != DdsInstanceState.Alive || !sample.IsValid)
                    continue;

                ReceivedSampleCount++;
                var status = sample.Data;

                // Correlate the network entity id to a local entity we may be waiting on.
                if (!_entityMap.TryGetEntity(status.EntityId, out var entity))
                    continue; // not a local entity (or not yet created) — nothing to release

                _gateway.ReceiveLifecycleStatus(
                    entity, status.NodeId, (EntityLifecycle)status.StateValue, cmd, frame);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
