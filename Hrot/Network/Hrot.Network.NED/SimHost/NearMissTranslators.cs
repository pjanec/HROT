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
        private readonly IDdsWriter<NearMiss> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<NearMiss>();
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNearMiss;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public NearMissEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<NearMiss>(participant), entityMap) { }

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
                _writer.Write(ToSample(unitId, in evt));
                SentSampleCount++;
            }
        }

        /// <summary>⭐ <c>CE-3262</c> — the wire form, the round's origin included (the test seam).</summary>
        internal static NearMiss ToSample(long unitId, in NearMissEvent evt) => new()
        {
            UnitEntityId = unitId, X = evt.X, Y = evt.Y, Z = evt.Z,
            OriginX = evt.OriginX, OriginY = evt.OriginY, OriginZ = evt.OriginZ, HasOrigin = evt.HasOrigin ? (byte)1 : (byte)0,
        };

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-3064</c> — the Brain's half: a DDS <c>NearMiss</c> becomes a remote <see cref="NearMissEvent"/> on the unit,
    /// which <c>NearMissSensingSystem</c> turns into <c>SensorChange.NearMiss</c>.</summary>
    public sealed class NearMissIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<NearMiss>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<NearMiss>();
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNearMiss;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public NearMissIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null ? new DdsReader<NearMiss>(participant) : null;
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

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
                cmd.PublishEvent(ToEvent(in d, unit));
            }
        }

        /// <summary>⭐ <c>CE-3262</c> — the event a sample raises on the unit's brain, marked remote, with the round's origin (the test seam).</summary>
        internal static NearMissEvent ToEvent(in NearMiss d, Entity unit) => new()
        {
            Unit = unit, X = d.X, Y = d.Y, Z = d.Z, IsRemote = true,
            OriginX = d.OriginX, OriginY = d.OriginY, OriginZ = d.OriginZ, HasOrigin = d.HasOrigin != 0,
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
