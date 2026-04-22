using System;
using Fdp.Interfaces;
using Fdp.Core;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Common.Events;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Abstractions;
using NedStatusCode = Hrot.NED.Messages.NedStatusCode;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Egress translator that consumes <see cref="MissionControlAckEvent"/> ECS events
    /// from the local bus and writes a <see cref="MissionControlAck"/> DDS message.
    ///
    /// <para>
    /// This class is the only class that knows about <c>DdsWriter&lt;MissionControlAck&gt;</c>
    /// for mission control. <c>MissionControlExecutionSystem</c> publishes the ACK event
    /// without any DDS dependency; this translator converts it to the wire format.
    /// </para>
    /// </summary>
    public sealed class MissionControlAckEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MissionControlAck";

        private readonly IDdsWriter<MissionControlAck>? _writer;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtMissionControlAck;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor â€” creates a live DDS writer.</summary>
        public MissionControlAckEgressTranslator(DdsParticipant participant)
        {
            _writer = new DdsWriterAdapter<MissionControlAck>(participant, "MissionControlAck");
        }

        /// <summary>Internal test constructor â€” accepts a stub writer.</summary>
        internal MissionControlAckEgressTranslator(IDdsWriter<MissionControlAck> writer)
        {
            _writer = writer;
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            var acks = view.ReadEvents<MissionControlAckEvent>();
            for (int i = 0; i < acks.Length; i++)
            {
                ref readonly var evt = ref acks[i];
                _writer.Write(new MissionControlAck
                {
                    RequestId    = evt.RequestId,
                    ErrorCode    = evt.ErrorCode,
                    ErrorMessage = MapErrorMessage(evt.ErrorCode),
                    NewVersion   = evt.NewVersion,
                });
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }

        // â”€â”€ Error message mapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string? MapErrorMessage(int code)
        {
            return code switch
            {
                (int)NedStatusCode.Success        => null,
                (int)NedStatusCode.EntityNotFound => "ERR_ENTITY_NOT_FOUND",
                (int)NedStatusCode.VersionConflict => "ERR_VERSION_CONFLICT",
                (int)NedStatusCode.NotSupported   => "ERR_NOT_SUPPORTED",
                _                                  => $"ERR_{code}",
            };
        }
    }
}
