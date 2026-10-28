using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3266</c> (G3, docs/DESIGN_Civilians_And_Crowd_Control.md §7.18 ST-1) — a local <see cref="StrikeNotification"/> (a blow
    /// struck here) to the wire, topic <c>MeleeStrike</c>, stamped with this node so its own ingress drops it. The
    /// <c>WeaponFireNotificationEgressTranslator</c> shape. A striker this node cannot name on the wire is skipped.
    /// </summary>
    public sealed class MeleeStrikeEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<MeleeStrike> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<MeleeStrike>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtMeleeStrike;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public MeleeStrikeEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, long localNodeId)
            : this(new DdsWriterAdapter<MeleeStrike>(participant), entityMap, localNodeId) { }

        internal MeleeStrikeEgressTranslator(IDdsWriter<MeleeStrike> writer, NetworkEntityMap entityMap, long localNodeId = 0)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.Bus.IsRegistered<StrikeNotification>()) return;
            foreach (ref readonly var evt in view.ReadEvents<StrikeNotification>())
            {
                if (evt.IsRemote || !_entityMap.TryGetNetworkId(evt.Striker, out long strikerId)) continue;
                long targetId = !evt.Target.IsNull && _entityMap.TryGetNetworkId(evt.Target, out long tid) ? tid : 0L;
                _writer.Write(new MeleeStrike { SourceNodeId = _localNodeId, StrikerEntityId = strikerId, TargetEntityId = targetId, Weapon = (byte)evt.Weapon });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-3266</c> — a <c>MeleeStrike</c> from another node becomes a remote <see cref="StrikeNotification"/> here (ui draws
    /// the swing). Its own node's sample is dropped (it is already a local event); an unknown striker is dropped.</summary>
    public sealed class MeleeStrikeIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<MeleeStrike>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<MeleeStrike>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtMeleeStrike;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public MeleeStrikeIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, long localNodeId)
        {
            _reader = participant is not null ? new DdsReader<MeleeStrike>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
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
                if (ToEvent(in d, out var evt)) cmd.PublishEvent(evt);
            }
        }

        /// <summary>The event a sample raises here; false for this node's own sample or an unknown striker (the test seam).</summary>
        internal bool ToEvent(in MeleeStrike d, out StrikeNotification evt)
        {
            evt = default;
            if (d.SourceNodeId != 0 && d.SourceNodeId == _localNodeId) return false;
            if (!_entityMap.TryGetEntity(d.StrikerEntityId, out var striker)) return false;
            var target = d.TargetEntityId != 0 && _entityMap.TryGetEntity(d.TargetEntityId, out var t) ? t : Entity.Null;
            evt = new StrikeNotification { Striker = striker, Target = target, Weapon = (MeleeWeapon)d.Weapon, IsRemote = true };
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
