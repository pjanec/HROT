using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Replication
{
    /// <summary>
    /// ⭐ Buildings Stage 5d (📄 docs/DESIGN_Building_Interiors.md §3j "5d") — sends a <see cref="DoorCommandEvent"/> raised on THIS node
    /// to the door's owner, when this node is not it. The shape of the damage path (<c>DamageAssessedEgressTranslator</c>): a local
    /// event, skipped when it came off the network (<see cref="DoorCommandEvent.IsRemote"/>), so a command is never sent twice.
    /// A door this node owns is applied here by <see cref="DoorCommandSystem"/> and never leaves.
    /// </summary>
    public sealed class DoorCommandEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDoorCommand";

        private readonly IDdsWriter<EntityDoorCommand> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtDoorCommand;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public DoorCommandEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<EntityDoorCommand>(participant, DdsTopicName), entityMap) { }

        internal DoorCommandEgressTranslator(IDdsWriter<EntityDoorCommand> writer, NetworkEntityMap entityMap)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is EntityRepository r && !r.Bus.IsRegistered<DoorCommandEvent>()) return;
            foreach (ref readonly var evt in view.ReadEvents<DoorCommandEvent>())
            {
                if (evt.IsRemote) continue;
                if (view is EntityRepository repo && DoorCommandSystem.OwnsDoor(repo, evt.Door)) continue;   // applied here
                if (!_entityMap.TryGetNetworkId(evt.Door, out long doorId)) continue;
                long actorId = !evt.Actor.IsNull && _entityMap.TryGetNetworkId(evt.Actor, out long a) ? a : 0;
                _writer.Write(new EntityDoorCommand { DoorEntityId = doorId, Verb = (byte)evt.Verb, ActorEntityId = actorId });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// ⭐ Buildings Stage 5d — republishes a received <see cref="EntityDoorCommand"/> as a local <see cref="DoorCommandEvent"/> with
    /// <see cref="DoorCommandEvent.IsRemote"/> set. Every node takes it; only the door's owner applies it
    /// (<see cref="DoorCommandSystem"/>), every other node ignores it. A door this node does not know is dropped.
    /// </summary>
    public sealed class DoorCommandIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDoorCommand";

        private readonly DdsReader<EntityDoorCommand>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtDoorCommand;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount => 0;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public DoorCommandIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader = participant is not null ? new DdsReader<EntityDoorCommand>(participant, DdsTopicName) : null;
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
                ProcessSample(in data, cmd);
            }
        }

        internal void ProcessSample(in EntityDoorCommand msg, IEntityCommandBuffer cmd)
        {
            if (!_entityMap.TryGetEntity(msg.DoorEntityId, out var door)) return;
            var actor = msg.ActorEntityId != 0 && _entityMap.TryGetEntity(msg.ActorEntityId, out var a) ? a : Entity.Null;
            cmd.PublishEvent(new DoorCommandEvent { Door = door, Verb = (DoorVerb)msg.Verb, Actor = actor, IsRemote = true });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
