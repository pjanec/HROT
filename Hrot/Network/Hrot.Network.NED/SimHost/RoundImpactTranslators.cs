using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>⭐ <c>CE-1128</c> (docs/DESIGN_Visual_Effects.md §10) — the Muscle's local <see cref="RoundImpactNotification"/>s to the wire
    /// (DDS <c>RoundImpact</c>), for every map host's puffs and marks.</summary>
    public sealed class RoundImpactEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "RoundImpact";
        private readonly IDdsWriter<RoundImpact> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtRoundImpact;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        private readonly long _localNodeId;   // ⭐ CE-1148 S2 — stamped as SourceNodeId so this node's own ingress drops it

        public RoundImpactEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, long localNodeId)
            : this(new DdsWriterAdapter<RoundImpact>(participant, DdsTopicName), entityMap, localNodeId) { }

        internal RoundImpactEgressTranslator(IDdsWriter<RoundImpact> writer, NetworkEntityMap entityMap, long localNodeId = 0)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            foreach (ref readonly var evt in view.ReadEvents<RoundImpactNotification>())
            {
                if (evt.IsRemote) continue;
                _entityMap.TryGetNetworkId(evt.Shooter, out long shooterId);   // a mark needs no shooter: 0 when unknown
                _writer.Write(new RoundImpact
                {
                    ShooterEntityId = shooterId, X = evt.X, Y = evt.Y, Z = evt.Z,
                    NormalX = evt.NormalX, NormalY = evt.NormalY, NormalZ = evt.NormalZ,
                    TravelX = evt.TravelX, TravelY = evt.TravelY, TravelZ = evt.TravelZ,
                    MunitionType = evt.Ammo, Kind = evt.Kind, SourceNodeId = _localNodeId,
                });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-1128</c> — a DDS <c>RoundImpact</c> becomes a remote <see cref="RoundImpactNotification"/>, which
    /// <c>EffectSpawnSystem</c> draws. ⭐ CE-1148 S2 (Node Roles §3.5) — on every node: this node's own samples come back and are dropped
    /// by <c>SourceNodeId</c>, so a marking node draws each of its marks once.</summary>
    public sealed class RoundImpactIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "RoundImpact";
        private readonly DdsReader<RoundImpact>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtRoundImpact;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        private readonly long _localNodeId;

        public RoundImpactIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, long localNodeId)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _reader    = participant is not null ? new DdsReader<RoundImpact>(participant, DdsTopicName) : null;
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
                ProcessSample(in d, cmd);
            }
        }

        internal void ProcessSample(in RoundImpact d, IEntityCommandBuffer cmd)
        {
            if (d.SourceNodeId != 0 && d.SourceNodeId == _localNodeId) return;   // ⭐ CE-1148 S2 — this node's own mark
            _entityMap.TryGetEntity(d.ShooterEntityId, out var shooter);
            cmd.PublishEvent(new RoundImpactNotification
            {
                Shooter = shooter, X = d.X, Y = d.Y, Z = d.Z, NormalX = d.NormalX, NormalY = d.NormalY, NormalZ = d.NormalZ,
                TravelX = d.TravelX, TravelY = d.TravelY, TravelZ = d.TravelZ, Ammo = d.MunitionType, Kind = d.Kind, IsRemote = true,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
