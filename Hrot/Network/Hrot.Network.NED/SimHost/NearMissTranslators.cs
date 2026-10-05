using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>⭐ <c>CE-3064</c> — the Muscle's local <see cref="NearMissEvent"/>s to the wire (DDS <c>NearMiss</c>), for the unit's Brain.</summary>
    public sealed class NearMissEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "NearMiss";
        private readonly IDdsWriter<NearMiss> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNearMiss;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public NearMissEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<NearMiss>(participant, DdsTopicName), entityMap) { }

        internal NearMissEgressTranslator(IDdsWriter<NearMiss> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            foreach (ref readonly var evt in view.ReadEvents<NearMissEvent>())
            {
                if (evt.IsRemote || !_entityMap.TryGetNetworkId(evt.Unit, out long unitId)) continue;
                _writer.Write(new NearMiss { UnitEntityId = unitId, X = evt.X, Y = evt.Y, Z = evt.Z });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-3064</c> — the Brain's half: a DDS <c>NearMiss</c> becomes a remote <see cref="NearMissEvent"/> on the unit,
    /// which <c>NearMissSensingSystem</c> turns into <c>SensorChange.NearMiss</c>.</summary>
    public sealed class NearMissIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "NearMiss";
        private readonly DdsReader<NearMiss>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNearMiss;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public NearMissIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null ? new DdsReader<NearMiss>(participant, DdsTopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var d = sample.Data;
                if (!_entityMap.TryGetEntity(d.UnitEntityId, out var unit) || !view.IsAlive(unit)) continue;
                cmd.PublishEvent(new NearMissEvent { Unit = unit, X = d.X, Y = d.Y, Z = d.Z, IsRemote = true });
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
