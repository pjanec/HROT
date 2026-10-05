using System;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3062</c> — the Brain's half of hearing: an <c>AudioTargetDetected</c> sample (an anonymous estimate) becomes a
    /// <see cref="SoundContactEvent"/> on the unit that heard it, which the memory merges (<c>CE-3063</c>).
    /// docs/DESIGN_Thermal_And_Acoustic_Sensing.md §3, §5.1. Replaces the IG ingress nothing read.
    /// </summary>
    public sealed class AudioTargetDetectedIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AudioTargetDetected";

        private readonly DdsReader<AudioTargetDetected>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtAudioTargetDetected;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public AudioTargetDetectedIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null ? new DdsReader<AudioTargetDetected>(participant, DdsTopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                if (!_entityMap.TryGetEntity(data.ListenerEntityId, out var listener) || !view.IsAlive(listener)) continue;
                cmd.PublishEvent(new SoundContactEvent
                {
                    Observer = listener, X = data.OriginX, Y = data.OriginY, Z = data.OriginZ, Radius = data.Radius, Kind = data.Kind,
                });
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}
