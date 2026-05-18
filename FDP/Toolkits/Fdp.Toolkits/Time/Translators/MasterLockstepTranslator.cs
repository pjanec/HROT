using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Time.Translators
{
    /// <summary>
    /// Master-side lockstep translator.
    /// <para>
    /// <b>Egress:</b> drains <see cref="AdvanceFrameIntent"/> from the bus and publishes
    /// them to the <c>FrameOrder</c> DDS topic as <see cref="FrameOrderDescriptor"/>.
    /// </para>
    /// <para>
    /// <b>Ingress:</b> reads <see cref="FrameAckDescriptor"/> from DDS and publishes
    /// <see cref="FrameStepCompletedEvent"/> onto the bus for the master controller.
    /// </para>
    /// <para>
    /// No reader for <c>FrameOrder</c> and no writer for <c>FrameAck</c> are created —
    /// echo loops are structurally impossible on the master side.
    /// No tracking state is maintained.
    /// </para>
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="participant"/> in test environments;
    /// both egress and ingress become safe no-ops.
    /// </para>
    /// </summary>
    public sealed class MasterLockstepTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "FrameOrder";
        private const long   OrdinalValue   = (long)TimeDescriptorType.MasterFrameOrder;

        private readonly DdsWriter<FrameOrderDescriptor>? _orderWriter;
        private readonly DdsReader<FrameAckDescriptor>?   _ackReader;
        private readonly FdpEventBus _eventBus;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Bidirectional;

        /// <summary>Creates the translator.</summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> in unit-test environments — both
        /// <see cref="ScanAndPublish"/> and <see cref="PollIngress"/> become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The local <see cref="FdpEventBus"/> shared with <see cref="Controllers.MasterSyncController"/>.
        /// </param>
        public MasterLockstepTranslator(DdsParticipant? participant, FdpEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

            if (participant is not null)
            {
                _orderWriter = new DdsWriter<FrameOrderDescriptor>(participant);
                _ackReader   = new DdsReader<FrameAckDescriptor>(participant);
            }
        }

        // ── Egress ───────────────────────────────────────────────────────────

        /// <summary>
        /// Drains <see cref="AdvanceFrameIntent"/> events from the bus and writes them to
        /// the <c>FrameOrder</c> DDS topic.  Called every frame.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var intent in _eventBus.ReadManaged<AdvanceFrameIntent>())
            {
                if (_orderWriter is null) continue;

                _orderWriter.Write(new FrameOrderDescriptor
                {
                    FrameID       = intent.FrameID,
                    FixedDelta    = intent.FixedDelta,
                    TargetSimTime = intent.TargetSimTime,
                    TimeScale     = 0,
                });
                SentSampleCount++;
            }
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        /// <summary>
        /// Reads <see cref="FrameAckDescriptor"/> samples from DDS and publishes
        /// <see cref="FrameStepCompletedEvent"/> onto the bus for the master controller.
        /// Called every frame.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_ackReader is null) return;

            using var loan = _ackReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;

                _eventBus.PublishManaged(new FrameStepCompletedEvent
                {
                    FrameID = sample.Data.FrameID,
                    NodeID  = sample.Data.NodeID,
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
