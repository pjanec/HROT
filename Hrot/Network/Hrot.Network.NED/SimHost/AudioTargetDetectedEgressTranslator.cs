using System;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Perception egress translator: consumes <see cref="TargetHeardEvent"/> ECS events
    /// and publishes an <see cref="AudioTargetDetected"/> DDS message for each one.
    /// </summary>
    public sealed class AudioTargetDetectedEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AudioTargetDetected";

        private readonly IDdsWriter<AudioTargetDetected> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => 84;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        /// <summary>Production constructor â€” creates a live DDS writer.</summary>
        public AudioTargetDetectedEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<AudioTargetDetected>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Testable constructor â€” accepts an injected writer stub.</summary>
        internal AudioTargetDetectedEgressTranslator(IDdsWriter<AudioTargetDetected> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="TargetHeardEvent"/> events from the view and publishes an
        /// <see cref="AudioTargetDetected"/> DDS message for each one.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ReadEvents<TargetHeardEvent>();
            foreach (ref readonly var evt in events)
            {
                if (!_entityMap.TryGetNetworkId(evt.Listener, out long listenerId)) continue;
                _writer.Write(new AudioTargetDetected
                {
                    ListenerEntityId  = listenerId,
                    SourceEntityIndex = evt.SourceEntityIndex,
                    OriginX           = evt.Origin.X,
                    OriginY           = evt.Origin.Y,
                    OriginZ           = evt.Origin.Z,
                });
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
