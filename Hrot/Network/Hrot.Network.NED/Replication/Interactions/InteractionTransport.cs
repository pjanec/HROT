using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Replication.Interactions
{
    /// <summary>
    /// ⭐ R-221 (📄 docs/DESIGN_Entity_Interactions.md §2) — what the ONE interaction transport needs to know about one kind: its typed
    /// FDP event ↔ its <see cref="InteractionPayload"/> case, who the target and actor are, whether the event came off the network, and
    /// whether THIS node owns the target (then the kind's own handler applies it here and nothing is sent).
    /// </summary>
    public interface IInteractionCodec
    {
        EInteractionKind Kind { get; }

        /// <summary>Every local, not-yet-sent event of this kind whose target this node does not own → <paramref name="send"/>.</summary>
        void ScanOutgoing(EntityRepository repo, OutgoingSink send);

        /// <summary>A received request of this kind → this node's typed event, marked remote.</summary>
        void PublishIncoming(Entity target, Entity actor, in InteractionPayload payload, IEntityCommandBuffer cmd);
    }

    /// <summary>The egress callback: one request to send.</summary>
    public delegate void OutgoingSink(Entity target, Entity actor, in InteractionPayload payload);

    /// <summary>
    /// ⭐ One kind's codec, written as data: four accessors and the two conversions. The transport does the rest.
    /// </summary>
    public sealed class InteractionCodec<TEvent> : IInteractionCodec where TEvent : unmanaged
    {
        public delegate Entity EntityOf(in TEvent e);
        public delegate bool FlagOf(in TEvent e);
        public delegate InteractionPayload PayloadOf(in TEvent e);
        public delegate TEvent EventOf(Entity target, Entity actor, in InteractionPayload payload);

        private readonly EntityOf _target, _actor;
        private readonly FlagOf _isRemote;
        private readonly Func<EntityRepository, Entity, bool> _ownsTarget;
        private readonly PayloadOf _toPayload;
        private readonly EventOf _fromPayload;

        public EInteractionKind Kind { get; }

        public InteractionCodec(EInteractionKind kind, EntityOf target, EntityOf actor, FlagOf isRemote,
            Func<EntityRepository, Entity, bool> ownsTarget, PayloadOf toPayload, EventOf fromPayload)
        {
            Kind = kind;
            _target = target; _actor = actor; _isRemote = isRemote;
            _ownsTarget = ownsTarget; _toPayload = toPayload; _fromPayload = fromPayload;
        }

        public void ScanOutgoing(EntityRepository repo, OutgoingSink send)
        {
            if (!repo.Bus.IsRegistered<TEvent>()) return;
            var events = repo.Bus.Read<TEvent>();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var e = ref events[i];
                if (_isRemote(in e)) continue;                     // came off the network: never sent again
                var target = _target(in e);
                if (_ownsTarget(repo, target)) continue;           // applied here by the kind's handler
                var payload = _toPayload(in e);
                payload.Kind = Kind;
                send(target, _actor(in e), in payload);
            }
        }

        public void PublishIncoming(Entity target, Entity actor, in InteractionPayload payload, IEntityCommandBuffer cmd)
            => cmd.PublishEvent(_fromPayload(target, actor, in payload));
    }

    /// <summary>
    /// ⭐ R-221 — THE list of interaction kinds. A new kind is one entry here (+ its event, payload case and handler); the translators
    /// never change. ⚠ Each kind's FDP event must be registered on every host (<c>HrotSharedComponentRegistry</c>).
    /// </summary>
    public static class InteractionCodecs
    {
        /// <summary>Doors (📄 docs/DESIGN_Building_Interiors.md §3j "5d"): <see cref="DoorCommandEvent"/> ↔ <see cref="DoorInteractionPayload"/>.</summary>
        public static readonly IInteractionCodec Door = new InteractionCodec<DoorCommandEvent>(
            EInteractionKind.Door,
            target:      static (in DoorCommandEvent e) => e.Door,
            actor:       static (in DoorCommandEvent e) => e.Actor,
            isRemote:    static (in DoorCommandEvent e) => e.IsRemote,
            ownsTarget:  static (repo, door) => DoorCommandSystem.OwnsDoor(repo, door),
            toPayload:   static (in DoorCommandEvent e) => new InteractionPayload { Door = new DoorInteractionPayload { Verb = (byte)e.Verb } },
            fromPayload: static (Entity target, Entity actor, in InteractionPayload p)
                => new DoorCommandEvent { Door = target, Actor = actor, Verb = (DoorVerb)p.Door.Verb, IsRemote = true });

        public static IReadOnlyList<IInteractionCodec> All { get; } = new[] { Door };
    }

    /// <summary>
    /// ⭐ R-221 — the ONE egress for every interaction kind: each codec's local, not-owned events become
    /// <see cref="EntityInteractionRequest"/> samples (target and actor mapped to network ids here, once).
    /// </summary>
    public sealed class InteractionEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInteractionRequest";

        private readonly IDdsWriter<EntityInteractionRequest> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IReadOnlyList<IInteractionCodec> _codecs;
        private readonly OutgoingSink _sink;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtInteractionRequest;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public InteractionEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, IReadOnlyList<IInteractionCodec>? codecs = null)
            : this(new DdsWriterAdapter<EntityInteractionRequest>(participant, DdsTopicName), entityMap, codecs) { }

        internal InteractionEgressTranslator(IDdsWriter<EntityInteractionRequest> writer, NetworkEntityMap entityMap, IReadOnlyList<IInteractionCodec>? codecs = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _codecs = codecs ?? InteractionCodecs.All;
            _sink = Send;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is not EntityRepository repo) return;
            for (int i = 0; i < _codecs.Count; i++) _codecs[i].ScanOutgoing(repo, _sink);
        }

        private void Send(Entity target, Entity actor, in InteractionPayload payload)
        {
            if (!_entityMap.TryGetNetworkId(target, out long targetId)) return;
            long actorId = !actor.IsNull && _entityMap.TryGetNetworkId(actor, out long a) ? a : 0;
            _writer.Write(new EntityInteractionRequest { TargetId = targetId, ActorId = actorId, Payload = payload });
            SentSampleCount++;
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// ⭐ R-221 — the ONE ingress: a received <see cref="EntityInteractionRequest"/> becomes its kind's typed FDP event, marked remote,
    /// on every node; only the target's owner applies it (the kind's handler). A kind this build does not know, or a target this node
    /// does not know, is dropped.
    /// </summary>
    public sealed class InteractionIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInteractionRequest";

        private readonly DdsReader<EntityInteractionRequest>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<EInteractionKind, IInteractionCodec> _byKind = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtInteractionRequest;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount => 0;
        /// <summary>Requests of a kind this build does not know (diagnostics).</summary>
        public long UnknownKindCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public InteractionIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, IReadOnlyList<IInteractionCodec>? codecs = null)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            foreach (var c in codecs ?? InteractionCodecs.All) _byKind.Add(c.Kind, c);
            _reader = participant is not null ? new DdsReader<EntityInteractionRequest>(participant, DdsTopicName) : null;
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

        internal void ProcessSample(in EntityInteractionRequest msg, IEntityCommandBuffer cmd)
        {
            if (!_byKind.TryGetValue(msg.Payload.Kind, out var codec)) { UnknownKindCount++; return; }
            if (!_entityMap.TryGetEntity(msg.TargetId, out var target)) return;
            var actor = msg.ActorId != 0 && _entityMap.TryGetEntity(msg.ActorId, out var a) ? a : Entity.Null;
            codec.PublishIncoming(target, actor, in msg.Payload, cmd);
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
