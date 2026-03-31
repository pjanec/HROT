using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress-only translator that publishes <see cref="TimePulseDescriptor"/>
    /// events from the local time controller to DDS.
    /// </summary>
    public sealed class TimePulseEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "TimePulse";
        private const long OrdinalValue = 100;

        private readonly DdsWriter<TimePulseDescriptor> _writer;
        private readonly FdpEventBus _eventBus;

        public TimePulseEgressTranslator(DdsParticipant participant, FdpEventBus eventBus)
        {
            _writer = participant != null
                ? new DdsWriter<TimePulseDescriptor>(participant, DdsTopicName)
                : throw new ArgumentNullException(nameof(participant));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var pulse in _eventBus.Consume<TimePulseDescriptor>())
            {
                _writer.Write(pulse);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}
