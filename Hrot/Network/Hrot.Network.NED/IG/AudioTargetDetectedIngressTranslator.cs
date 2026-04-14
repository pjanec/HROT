using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// Perception ingress translator: reads <see cref="AudioTargetDetected"/> DDS messages
    /// and publishes <see cref="TargetHeardEvent"/> onto the local ECS event bus.
    /// </summary>
    public sealed class AudioTargetDetectedIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AudioTargetDetected";

        private readonly DdsReader<AudioTargetDetected>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => 84;

        public AudioTargetDetectedIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<AudioTargetDetected>(participant, DdsTopicName)
                : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                if (!_entityMap.TryGetEntity(data.ListenerEntityId, out var listenerEntity)) continue;
                cmd.PublishEvent(new TargetHeardEvent
                {
                    Listener          = listenerEntity,
                    SourceEntityIndex = data.SourceEntityIndex,
                    Origin            = new Vector3(data.OriginX, data.OriginY, data.OriginZ),
                });
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
