using System;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Bridges the <c>TimePulse</c> DDS topic to the local <see cref="FdpEventBus"/>.
    ///
    /// The <c>SlaveTimeController</c> consumes <see cref="TimePulseDescriptor"/> events from
    /// the bus each tick to drive its PLL.  Without this translator the bus never receives
    /// pulses and IG time remains frozen at wall-clock rate with no network sync.
    ///
    /// On ingress each sample is published directly to the event bus via
    /// <see cref="FdpEventBus.Publish{T}"/>.
    ///
    /// IG does not publish time pulses — <see cref="ScanAndPublish"/> is a no-op (the Master
    /// TimeController publishes its own pulses locally; other nodes only consume).
    /// </summary>
    public class TimePulseTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "TimePulse";
        private const long   OrdinalValue = 100; // matches EventId(100) on TimePulseDescriptor

        private readonly DdsReader<TimePulseDescriptor> _reader;
        private readonly FdpEventBus                    _eventBus;

        public string TopicName        => DdsTopicName;
        public long   DescriptorOrdinal => OrdinalValue;

        public TimePulseTranslator(DdsParticipant? participant, FdpEventBus eventBus)
        {
            // participant may be null in unit-test mode — PollIngress becomes a no-op
            _reader   = participant is not null ? new DdsReader<TimePulseDescriptor>(participant) : null!;
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

            // Pre-register so Consume<T> works without a first-Publish warm-up.
            _eventBus.Register<TimePulseDescriptor>();
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return; // test mode — no DDS participant supplied
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                _eventBus.Publish(sample.Data);
            }
        }

        // ── Egress (IG is slave — does not publish time pulses) ───────────────

        public void ScanAndPublish(ISimulationView view) { }

        // ── Replay / ghost promotion (unused for pure-event translator) ────────

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}
