using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors;
using Fdp.ModuleHost.Abstractions;

using OwnershipUpdateMsg  = Fdp.Toolkit.Replication.Messages.OwnershipUpdate;
using OwnershipUpdateWire = Fdp.Network.Cyclone.Topics.OwnershipUpdate;

namespace Hrot.Map.Common.Replication
{
    /// <summary>
    /// Bidirectional translator for the <c>SST_OwnershipUpdate</c> DDS topic.
    ///
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Muscle (egress)</b> â€” <see cref="ScanAndPublish"/> consumes
    ///     <see cref="OwnershipUpdateMsg"/> events published by <c>DeferredTakeoverSystem</c>
    ///     after it claims split-authority descriptors, and writes them to DDS so the Brain node
    ///     can drop its own authority bits via its local <c>OwnershipIngressSystem</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Brain (ingress)</b> â€” <see cref="PollIngress"/> reads DDS samples and re-publishes
    ///     them onto the local event bus so <c>OwnershipIngressSystem</c> can update
    ///     <see cref="DescriptorOwnership"/> and call <c>SetAuthority(entity, componentId, false)</c>.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// Installed in <see cref="Hrot.Map.Common.Translators.SharedTranslatorPack"/> so that
    /// both Brain and Muscle nodes get the full bidirectional channel without additional
    /// per-role wiring in <c>NedReplicationModule</c>.
    /// </para>
    /// </summary>
    public sealed class OwnershipUpdateTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "SST_OwnershipUpdate";

        private readonly int _localNodeId;
        private readonly DdsReader<OwnershipUpdateWire>? _reader;
        private readonly DdsWriter<OwnershipUpdateWire>? _writer;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtOwnershipUpdate;

        // Event-driven â€” no component ownership mapping needed.
        public IReadOnlyList<int> TargetComponentIds => System.Array.Empty<int>();

        public OwnershipUpdateTranslator(DdsParticipant? participant, int localNodeId)
        {
            _localNodeId = localNodeId;
            if (participant != null)
            {
                _reader = new DdsReader<OwnershipUpdateWire>(participant, DdsTopicName);
                _writer = new DdsWriter<OwnershipUpdateWire>(participant, DdsTopicName);
            }
        }

        /// <summary>
        /// Muscle egress: consume <see cref="OwnershipUpdateMsg"/> bus events and write to DDS.
        /// No-op when no events are pending.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer == null) return;

            var updates = view.ReadEvents<OwnershipUpdateMsg>();
            foreach (var evt in updates)
            {
                // Only forward claims originated by this node to prevent DDSâ†”bus echo loops.
                if (evt.OriginNodeId != 0 && evt.OriginNodeId != _localNodeId)
                    continue;

                var (typeId, instanceId) = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.UnpackKey(evt.PackedKey);

                _writer.Write(new OwnershipUpdateWire
                {
                    EntityId    = evt.NetworkId.Value,
                    DescrTypeId = typeId,
                    InstanceId  = instanceId,
                    NewOwner    = evt.NewOwnerNodeId,
                    OriginNodeId = evt.OriginNodeId != 0 ? evt.OriginNodeId : _localNodeId,
                });

                FdpLog<OwnershipUpdateTranslator>.Debug(
                    "[Node-{0}] OwnershipUpdate egress: EntityId={1} TypeId={2} NewOwner={3}",
                    _localNodeId, evt.NetworkId.Value, typeId, evt.NewOwnerNodeId);
            }
        }

        /// <summary>
        /// Brain ingress: read DDS samples and re-publish onto the local bus
        /// for <c>OwnershipIngressSystem</c> to consume.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;
            if (view is not EntityRepository repo) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                var msg = sample.Data;

                // Drop DDS loopback of our own claim to prevent local echo storms.
                if (msg.OriginNodeId == _localNodeId)
                    continue;

                long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(msg.DescrTypeId, msg.InstanceId);

                repo.Bus.Publish(new OwnershipUpdateMsg
                {
                    NetworkId      = new NetworkIdentity { Value = msg.EntityId },
                    PackedKey      = packedKey,
                    NewOwnerNodeId = msg.NewOwner,
                    OriginNodeId   = msg.OriginNodeId,
                });

                FdpLog<OwnershipUpdateTranslator>.Debug(
                    "[Node-{0}] OwnershipUpdate ingress: EntityId={1} TypeId={2} NewOwner={3}",
                    _localNodeId, msg.EntityId, msg.DescrTypeId, msg.NewOwner);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}
