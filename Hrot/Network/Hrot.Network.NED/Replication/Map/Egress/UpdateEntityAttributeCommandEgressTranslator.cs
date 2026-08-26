using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.SimHost.Installers;

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
                // ⭐⭐⭐ AX-005c / R-134 — THE SOLE DDS BOUNDARY for the binary arm.
                //    The command carries FDP-internal `EntityAttributeChange` records; they become DDS
                //    `AttributeRecord`s HERE and nowhere else, with `AttributeValueKind` converted to
                //    `AttributeValueType` by an explicit mapping (never a cast).
                //    📄 DESIGN_Cgf_AxisB_Rotation_Slice.md §11.1/§11.3 · RULINGS.md R-134.
                //
                // ⭐⭐ This translator was EXTENDED rather than duplicated: 📐 measured `2026-08-25`, it is
                //    already registered in production (SharedTranslatorPack.cs:79) and already carries the
                //    JSON arm to the same DDS topic for the same owner. ⛔ A second translator writing
                //    `UpdateEntityAttributeRequest` would be two implementations of one concept (ruling 9).
                List<AttributeRecord>? records = null;
                if (cmd.AttributeChanges is { Count: > 0 } changes)
                {
                    records = new List<AttributeRecord>(changes.Count);
                    foreach (var change in changes)
                        records.Add(AttributeRecordConversion.ToNetwork(change));
                }

                // ⚠ A command with neither arm is a no-op request: skip it rather than writing an empty
                //   sample the owner would parse and discard. ⛔ Silent network traffic that does nothing
                //   is worse than none.
                if (records == null && string.IsNullOrEmpty(cmd.AttributePatchJson))
                    continue;

                var request = new UpdateEntityAttributeRequest
                {
                    RequestId          = Guid.NewGuid(),
                    EntityId           = (int)cmd.NetworkId,
                    AttributePatchJson = cmd.AttributePatchJson,
                    AttributeRecords   = records,
                    RequireAck         = false,
                };

                _writer.Write(request);
                SentSampleCount++;

                FdpLog<UpdateEntityAttributeCommandEgressTranslator>.Debug(
                    "[UpdateEntityAttributeCommandEgress] NetID={0} patch={1} binaryRecords={2}",
                    cmd.NetworkId, cmd.AttributePatchJson, records?.Count ?? 0);
            }
        }
    }
}
