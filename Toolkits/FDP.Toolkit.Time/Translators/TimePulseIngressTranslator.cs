using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Time
{
    /// <summary>
    /// Ingress translator: polls the <c>"TimePulse"</c> DDS topic and publishes
    /// <see cref="TimePulseDescriptor"/> events into the local <see cref="FdpEventBus"/>
    /// so the <c>SlaveTimeController</c> PLL receives the master's clock signal.
    /// <para>
    /// Wire this on every node that <em>follows</em> the master clock (IG, CGF, …).
    /// Without this translator the bus never receives pulses and the slave's local clock
    /// drifts freely, diverging from the master over time.
    /// </para>
    /// </summary>
    internal sealed class TimePulseIngressTranslator : IDescriptorTranslator
    {
        private const string TopicNameConst = "TimePulse";
        private const long   OrdinalValue   = 100;

        private readonly DdsReader<TimePulseDescriptor>? _reader;
        private readonly FdpEventBus _eventBus;

        public TimePulseIngressTranslator(DdsParticipant? participant, FdpEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _reader   = participant != null
                ? new DdsReader<TimePulseDescriptor>(participant, TopicNameConst)
                : null;
            _eventBus.Register<TimePulseDescriptor>();
        }

        public string TopicName        => TopicNameConst;
        public long   DescriptorOrdinal => OrdinalValue;

        public void ScanAndPublish(ISimulationView view) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;
            using var samples = _reader.Take();
            foreach (var s in samples)
            {
                if (!s.IsValid) continue;
                _eventBus.Publish(s.Data);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
