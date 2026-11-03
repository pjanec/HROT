using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐⭐ <c>CE-3364</c> D5 (📄 docs/DESIGN_Man_Downed_Event.md §3) — on EVERY node (<c>SharedTranslatorPack</c>): a <see cref="ManDownedEvent"/> raised ON THIS NODE (the Health owner's
    /// <c>HealthApplicationSystem</c>) is written once as an <see cref="EntityManDowned"/>. ⛔ One that ARRIVED (<see cref="ManDownedEvent.IsRemote"/>) is never sent again — the
    /// <c>DamageAssessedEvent.IsRemote</c> precedent (<c>DamageAssessedEgressTranslator</c>).
    /// </summary>
    public sealed class ManDownedEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<EntityManDowned> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityManDowned>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtManDowned;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public ManDownedEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, int localNodeId)
            : this(new DdsWriterAdapter<EntityManDowned>(participant), entityMap, localNodeId) { }

        internal ManDownedEgressTranslator(IDdsWriter<EntityManDowned> writer, NetworkEntityMap entityMap, int localNodeId)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<ManDownedEvent>()) return;
            var events = view.ReadEvents<ManDownedEvent>();
            foreach (ref readonly var evt in events)
            {
                if (evt.IsRemote) continue;                                           // it came over the wire: its own node sent it
                if (!_entityMap.TryGetNetworkId(evt.Victim, out int victimId)) continue;
                _writer.Write(new EntityManDowned
                {
                    VictimEntityId = victimId,
                    SourceEntityId = !evt.Source.IsNull && _entityMap.TryGetNetworkId(evt.Source, out int s) ? s : 0,
                    OriginalSourceEntityId = !evt.OriginalSource.IsNull && _entityMap.TryGetNetworkId(evt.OriginalSource, out int os) ? os : 0,
                    State = (byte)evt.State,
                    BodyPart = evt.HasBodyPart != 0 ? (byte)(evt.BodyPart + 1) : (byte)0,   // id + 1, 0 = none (EntityHitDamage's convention)
                    VictimSide = (byte)evt.VictimSide,
                    SourceNodeId = _localNodeId,
                });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3364</c> D5 — on EVERY node: an <see cref="EntityManDowned"/> from another node is raised here as a <see cref="ManDownedEvent"/> with
    /// <see cref="ManDownedEvent.IsRemote"/> set, the entities resolved. A node drops its OWN sample (<c>SourceNodeId</c>: DDS hands a writer's samples back to its own reader),
    /// so a downing is raised ONCE per node. An unknown victim is dropped (as <c>EntityHitDamageIngressTranslator</c> drops its unknown target: an event, not a state — nothing is
    /// held); an unknown source reads <c>Entity.Null</c>.
    /// </summary>
    public sealed class ManDownedIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<EntityManDowned>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityManDowned>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtManDowned;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        /// <summary>A one-shot world event: discarded while the node is frozen for a join.</summary>
        public TranslatorClass Category => TranslatorClass.WorldEvent;

        public ManDownedIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, int localNodeId)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _reader = participant is not null ? new DdsReader<EntityManDowned>(participant) : null;
        }

        public int DiscardIngress() => _reader.DiscardAll();

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(in data, cmd);
            }
        }

        /// <summary>One received sample; <c>true</c> when it raised the local event. Exposed for tests (no DDS stack needed).</summary>
        internal bool ProcessSample(in EntityManDowned msg, IEntityCommandBuffer cmd)
        {
            if (msg.SourceNodeId != 0 && msg.SourceNodeId == _localNodeId) return false;   // our own sample handed back
            if (!_entityMap.TryGetEntity(msg.VictimEntityId, out var victim)) return false;
            cmd.PublishEvent(new ManDownedEvent
            {
                Victim = victim,
                Source = msg.SourceEntityId != 0 && _entityMap.TryGetEntity(msg.SourceEntityId, out var source) ? source : Entity.Null,
                OriginalSource = msg.OriginalSourceEntityId != 0 && _entityMap.TryGetEntity(msg.OriginalSourceEntityId, out var original) ? original : Entity.Null,
                State = (InjuryState)msg.State,
                HasBodyPart = (byte)(msg.BodyPart != 0 ? 1 : 0),
                BodyPart = msg.BodyPart != 0 ? (byte)(msg.BodyPart - 1) : (byte)0,
                VictimSide = (ForceId)msg.VictimSide,
                IsRemote = true,
            });
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
