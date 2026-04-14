using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost_Core.Abstractions;

namespace FDP.Toolkit.Time.Translators
{
    /// <summary>
    /// Slave-side lockstep translator.
    /// <para>
    /// <b>Ingress:</b> reads <see cref="FrameOrderDescriptor"/> from DDS and publishes
    /// <see cref="AdvanceFrameIntent"/> onto the bus for the slave controller.
    /// </para>
    /// <para>
    /// <b>Egress:</b> drains <see cref="FrameStepCompletedEvent"/> from the bus and
    /// writes them to the <c>FrameAck</c> DDS topic as <see cref="FrameAckDescriptor"/>.
    /// </para>
    /// <para>
    /// No writer for <c>FrameOrder</c> and no reader for <c>FrameAck</c> are created —
    /// echo loops are structurally impossible on the slave side.
    /// No tracking state is maintained.
    /// </para>
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="participant"/> in test environments;
    /// both egress and ingress become safe no-ops.
    /// </para>
    /// </summary>
    public sealed class SlaveLockstepTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "FrameOrder";
        private const long   OrdinalValue   = 203;

        private readonly DdsReader<FrameOrderDescriptor>? _orderReader;
        private readonly DdsWriter<FrameAckDescriptor>?   _ackWriter;
        private readonly FdpEventBus _eventBus;
        private readonly int _localNodeId;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;

        /// <summary>Creates the translator.</summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> in unit-test environments — both
        /// <see cref="ScanAndPublish"/> and <see cref="PollIngress"/> become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The local <see cref="FdpEventBus"/> shared with <see cref="Controllers.SlaveSyncController"/>.
        /// </param>
        /// <param name="localNodeId">
        /// This node's ID, embedded in the <see cref="FrameAckDescriptor.NodeID"/> field so
        /// the master can attribute incoming ACKs to the correct slave.
        /// </param>
        public SlaveLockstepTranslator(DdsParticipant? participant, FdpEventBus eventBus,
            int localNodeId = 0)
        {
            _eventBus    = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;

            if (participant is not null)
            {
                _orderReader = new DdsReader<FrameOrderDescriptor>(participant);
                _ackWriter   = new DdsWriter<FrameAckDescriptor>(participant);
            }
        }

        // ── Egress ───────────────────────────────────────────────────────────

        /// <summary>
        /// Drains <see cref="FrameStepCompletedEvent"/> events from the bus and writes them
        /// to the <c>FrameAck</c> DDS topic.  Called every frame.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var evt in _eventBus.ConsumeManaged<FrameStepCompletedEvent>())
            {
                if (_ackWriter is null) continue;

                _ackWriter.Write(new FrameAckDescriptor
                {
                    FrameID = evt.FrameID,
                    NodeID  = _localNodeId,
                });
            }
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        /// <summary>
        /// Reads <see cref="FrameOrderDescriptor"/> samples from DDS and publishes
        /// <see cref="AdvanceFrameIntent"/> onto the bus for the slave controller.
        /// Called every frame.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_orderReader is null) return;

            using var loan = _orderReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                _eventBus.PublishManaged(new AdvanceFrameIntent
                {
                    FrameID       = sample.Data.FrameID,
                    FixedDelta    = sample.Data.FixedDelta,
                    TargetSimTime = sample.Data.TargetSimTime,
                });
            }
        }

        // ── Ghost promotion / entity lifecycle — not applicable ──────────────

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
