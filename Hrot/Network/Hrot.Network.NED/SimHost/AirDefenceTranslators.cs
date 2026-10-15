using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using DecoyKind = Fdp.Toolkit.Combat.Components.DecoyKind;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>⭐ <c>CE-2189</c> (DESIGN_Missiles_And_Ammo §3c) — the Muscle's local <see cref="MissileWarningEvent"/>s to the wire
    /// (DDS <c>MissileWarning</c>), for the aircraft's Brain. The near-miss pattern (<see cref="NearMissEgressTranslator"/>).</summary>
    public sealed class MissileWarningEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MissileWarning";
        private readonly IDdsWriter<MissileWarning> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtMissileWarning;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public MissileWarningEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<MissileWarning>(participant, DdsTopicName), entityMap) { }

        internal MissileWarningEgressTranslator(IDdsWriter<MissileWarning> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            foreach (ref readonly var evt in view.ReadEvents<MissileWarningEvent>())
            {
                if (evt.IsRemote || !_entityMap.TryGetNetworkId(evt.Unit, out long unitId)) continue;
                _writer.Write(new MissileWarning
                {
                    UnitEntityId = unitId, FromX = evt.FromX, FromY = evt.FromY, FromZ = evt.FromZ,
                    Seeker = evt.Seeker, TimeToImpactS = evt.TimeToImpactS,
                });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-2189</c> — the Brain's half: a DDS <c>MissileWarning</c> becomes a remote <see cref="MissileWarningEvent"/> on the
    /// aircraft, which <c>MissileWarningSensingSystem</c> turns into <c>SensorChange.MissileWarning</c>.</summary>
    public sealed class MissileWarningIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MissileWarning";
        private readonly DdsReader<MissileWarning>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtMissileWarning;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public MissileWarningIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null ? new DdsReader<MissileWarning>(participant, DdsTopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                ProcessSample(sample.Data, cmd, view);
            }
        }

        /// <summary>One sample: the aircraft resolved through this node's entity map, the warning published as a REMOTE event.</summary>
        public void ProcessSample(in MissileWarning d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.UnitEntityId, out var unit) || !view.IsAlive(unit)) return;
            cmd.PublishEvent(new MissileWarningEvent
            {
                Unit = unit, FromX = d.FromX, FromY = d.FromY, FromZ = d.FromZ, Seeker = d.Seeker, TimeToImpactS = d.TimeToImpactS,
                IsRemote = true,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-2188</c> — the Brain's <see cref="DispenseCountermeasureIntent"/>s to the wire (DDS <c>DispenseCountermeasure</c>),
    /// for the SimHost that flies the aircraft.</summary>
    public sealed class DispenseCountermeasureEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "DispenseCountermeasure";
        private readonly IDdsWriter<DispenseCountermeasure> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtDispenseCountermeasure;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public DispenseCountermeasureEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<DispenseCountermeasure>(participant, DdsTopicName), entityMap) { }

        internal DispenseCountermeasureEgressTranslator(IDdsWriter<DispenseCountermeasure> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            foreach (ref readonly var evt in view.ReadEvents<DispenseCountermeasureIntent>())
            {
                if (evt.IsRemote || !_entityMap.TryGetNetworkId(evt.Unit, out long unitId)) continue;
                _writer.Write(new DispenseCountermeasure { UnitEntityId = unitId, Kind = (byte)evt.Kind });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-2188</c> — the SimHost's half: a DDS <c>DispenseCountermeasure</c> becomes a remote
    /// <see cref="DispenseCountermeasureIntent"/>, which <c>CountermeasureSystem</c> throws.</summary>
    public sealed class DispenseCountermeasureIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "DispenseCountermeasure";
        private readonly DdsReader<DispenseCountermeasure>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtDispenseCountermeasure;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public DispenseCountermeasureIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null ? new DdsReader<DispenseCountermeasure>(participant, DdsTopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                ProcessSample(sample.Data, cmd, view);
            }
        }

        /// <summary>One sample: a known kind on an aircraft this node knows ⇒ a REMOTE intent.</summary>
        public void ProcessSample(in DispenseCountermeasure d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (d.Kind is not ((byte)DecoyKind.Flare or (byte)DecoyKind.Chaff)) return;
            if (!_entityMap.TryGetEntity(d.UnitEntityId, out var unit) || !view.IsAlive(unit)) return;
            cmd.PublishEvent(new DispenseCountermeasureIntent { Unit = unit, Kind = (DecoyKind)d.Kind, IsRemote = true });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
