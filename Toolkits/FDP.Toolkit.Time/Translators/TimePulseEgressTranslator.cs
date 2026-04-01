using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Time
{
    /// <summary>
    /// Egress translator: consumes <see cref="TimePulseDescriptor"/> events from the local
    /// <see cref="FdpEventBus"/> and publishes them to the <c>"TimePulse"</c> DDS topic.
    /// <para>
    /// Wire this on every node that owns the authoritative simulation clock so that slave
    /// nodes and UI caches (e.g. <c>ClusterUiCache</c>) receive the pulse via DDS.
    /// In particular the <b>Orchestrator</b> must include this translator so that
    /// <c>SteppedMasterController.Step()</c> pulses reach the UI panel.
    /// </para>
    /// </summary>
    internal sealed class TimePulseEgressTranslator : IDescriptorTranslator
    {
        private const string TopicNameConst = "TimePulse";
        private const long   OrdinalValue   = 100;

        private readonly DdsWriter<TimePulseDescriptor> _writer;
        private readonly FdpEventBus _eventBus;

        public TimePulseEgressTranslator(DdsParticipant participant, FdpEventBus eventBus)
        {
            _writer   = new DdsWriter<TimePulseDescriptor>(participant, TopicNameConst);
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public string TopicName        => TopicNameConst;
        public long   DescriptorOrdinal => OrdinalValue;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var pulse in _eventBus.Consume<TimePulseDescriptor>())
                _writer.Write(pulse);
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
