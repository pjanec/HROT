using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="UpdateEntityAttributeCommand"/> bus events
    /// into <see cref="UpdateEntityAttributeRequest"/> DDS samples.
    ///
    /// <para>
    /// Published by UI tools such as <c>EntityRotationTool</c> when the operator changes
    /// an attribute on an entity that may be owned by a remote authoritative node.
    /// The translator forwards each command to the DDS network so that the authority
    /// node's <c>UpdateEntityAttributeRequestSystem</c> can apply the patch.
    /// </para>
    ///
    /// <para>
    /// Implements <see cref="INetworkEventTranslator"/> (not <see cref="IDescriptorTranslator"/>)
    /// so the cleanup system does not attempt per-entity teardown on this translator.
    /// </para>
    /// </summary>
    public sealed class UpdateEntityAttributeCommandEgressTranslator : INetworkEventTranslator
    {
        private const string DdsTopicName = "UpdateEntityAttributeRequest";

        private readonly IDdsWriter<UpdateEntityAttributeRequest> _writer;

        /// <inheritdoc/>
        public string TopicName => DdsTopicName;

        /// <inheritdoc/>
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <inheritdoc/>
        public long ReceivedSampleCount { get; private set; }

        /// <inheritdoc/>
        public long SentSampleCount { get; private set; }

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public UpdateEntityAttributeCommandEgressTranslator(DdsParticipant participant)
            : this(new DdsWriterAdapter<UpdateEntityAttributeRequest>(participant, DdsTopicName))
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal UpdateEntityAttributeCommandEgressTranslator(
            IDdsWriter<UpdateEntityAttributeRequest> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <inheritdoc/>
        /// <remarks>Ingress is not applicable; this translator is egress-only.</remarks>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="UpdateEntityAttributeCommand"/> events from the view and writes
        /// each as an <see cref="UpdateEntityAttributeRequest"/> DDS sample.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var cmd in view.ReadManagedEvents<UpdateEntityAttributeCommand>())
            {
                var request = new UpdateEntityAttributeRequest
                {
                    RequestId          = Guid.NewGuid(),
                    EntityId           = (int)cmd.NetworkId,
                    AttributePatchJson = cmd.AttributePatchJson,
                    RequireAck         = false,
                };

                _writer.Write(request);
                SentSampleCount++;

                FdpLog<UpdateEntityAttributeCommandEgressTranslator>.Debug(
                    "[UpdateEntityAttributeCommandEgress] NetID={0} patch={1}",
                    cmd.NetworkId, cmd.AttributePatchJson);
            }
        }
    }
}
