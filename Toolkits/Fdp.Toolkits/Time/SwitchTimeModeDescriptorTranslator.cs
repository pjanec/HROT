using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// Bridges <see cref="SwitchTimeModeEvent"/> between the local <see cref="FdpEventBus"/>
    /// and the CycloneDDS wire, enabling distributed time-mode switching across all cluster nodes.
    ///
    /// <para>
    /// <b>Egress (Master → DDS):</b> <see cref="ScanAndPublish"/> drains any
    /// <see cref="SwitchTimeModeEvent"/> values that <see cref="Controllers.DistributedTimeCoordinator"/>
    /// published to the bus this frame and writes them onto the DDS <c>SwitchTimeModeEvent</c> topic.
    /// </para>
    /// <para>
    /// <b>Ingress (DDS → Slave):</b> <see cref="PollIngress"/> reads all pending DDS samples and
    /// re-publishes them to the local <see cref="FdpEventBus"/> so that
    /// <see cref="Controllers.SlaveTimeModeListener"/> can gate on them next tick.
    /// </para>
    ///
    /// <para>
    /// Wire one instance per node host that participates in distributed time.  Both egress and ingress
    /// are always executed; the coordinator/listener each only act when they find relevant events,
    /// so running both sides on every node is harmless and keeps the wiring symmetric.
    /// </para>
    ///
    /// <para>
    /// Register via <see cref="TimeNetworkModule.CreateDescriptorTranslator"/> and add the result to
    /// the <c>customTranslators</c> list passed to <c>CycloneNetworkModule</c>.
    /// </para>
    /// </summary>
    public sealed class SwitchTimeModeDescriptorTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "SwitchTimeModeEvent";
        private const long   OrdinalValue   = 201;

        private readonly DdsReader<SwitchTimeModeWireDto>? _reader;
        private readonly DdsWriter<SwitchTimeModeWireDto>? _writer;
        private readonly FdpEventBus _eventBus;

        // Tracks the last message received from the network to break the echo loop.
        private SwitchTimeModeWireDto? _lastIngressed;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;

        /// <summary>
        /// Creates the translator.
        /// </summary>
        /// <param name="participant">
        /// The DDS domain participant.  Pass <see langword="null"/> in unit-test environments where
        /// no DDS infrastructure is available — both <see cref="ScanAndPublish"/> and
        /// <see cref="PollIngress"/> become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The local <see cref="FdpEventBus"/> shared with the time controllers on this node.
        /// </param>
        public SwitchTimeModeDescriptorTranslator(DdsParticipant? participant, FdpEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

            if (participant is not null)
            {
                // Use the blittable wire DTO (SwitchTimeModeWireDto) which has [DdsTopic] and
                // full codegen support.  SwitchTimeModeEvent itself cannot carry [DdsTopic]
                // because the Cyclone IDL codegen cannot represent the TimeMode enum in CDR scope.
                _reader = new DdsReader<SwitchTimeModeWireDto>(participant);
                _writer = new DdsWriter<SwitchTimeModeWireDto>(participant);
            }

            // Pre-register so Consume<T> works without a first-Publish warm-up.
            _eventBus.Register<SwitchTimeModeEvent>();
        }

        // ── Egress ───────────────────────────────────────────────────────────

        /// <summary>
        /// Drains any <see cref="SwitchTimeModeEvent"/> values from the <see cref="FdpEventBus"/>
        /// and publishes them to DDS.  Called every frame by <c>CycloneNetworkModule</c>.
        /// On slave-only nodes (no coordinator) the bus is empty and this is a fast no-op.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            foreach (var evt in _eventBus.Read<SwitchTimeModeEvent>())
            {
                // Break the echo loop: skip events that were just ingested from DDS.
                if (_lastIngressed.HasValue &&
                    _lastIngressed.Value.BarrierWallTicks == evt.BarrierWallTicks &&
                    _lastIngressed.Value.TargetModeInt    == (int)evt.TargetMode &&
                    _lastIngressed.Value.FixedDelta       == evt.FixedDelta        &&
                    _lastIngressed.Value.SimTimeSnapshot  == evt.SimTimeSnapshot   &&
                    _lastIngressed.Value.TimeScale        == evt.TimeScale)
                {
                    continue;
                }

                _writer.Write(SwitchTimeModeWireDto.ToWire(evt));
            }
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        /// <summary>
        /// Reads all pending DDS <c>SwitchTimeModeEvent</c> samples and publishes them to
        /// the local <see cref="FdpEventBus"/> for <see cref="Controllers.SlaveTimeModeListener"/>.
        /// Called every frame by <c>CycloneNetworkModule</c>.
        /// On master-only nodes that never receive an inbound sample this is a fast no-op.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                _lastIngressed = sample.Data;
                _eventBus.Publish(sample.Data.ToEvent());
            }
        }

        // ── Ghost promotion / entity lifecycle — not applicable ──────────────

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
