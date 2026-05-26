using System.Runtime.CompilerServices;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.Animation.Replication.Translators.Events;

/// <summary>
/// Bidirectional translator for <see cref="MontageStartedEvent"/>.
/// Direction is set by the caller (Muscle = Egress, Brain = Ingress).
/// </summary>
internal sealed class MontageStartedEventTranslator
    : CycloneNativeEventTranslator<MontageStartedEvent, DdsMontageStartedEvent>
{
    private const string TopicNameConst = "hrot/anim/MontageStarted";

    // Mutable proxy with identical layout to MontageStartedEvent (no constructors on source type).
    private struct Proxy
    {
        public Entity Target;
        public int MontageId;
        public uint ActionInstanceId;
        public byte QueueIndex;
    }

    public override TranslatorDirection Direction { get; }

    internal MontageStartedEventTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap, TranslatorDirection direction)
        : base(participant, TopicNameConst, entityMap)
    {
        Direction = direction;
    }

    protected override bool TryEncode(
        in MontageStartedEvent ecs, out DdsMontageStartedEvent dds)
    {
        if (!EntityMap.TryGetNetworkId(ecs.Target, out long netId))
        {
            dds = default;
            return false;
        }
        dds = new DdsMontageStartedEvent
        {
            Target = netId,
            MontageId = ecs.MontageId,
            ActionInstanceId = ecs.ActionInstanceId,
            QueueIndex = ecs.QueueIndex,
        };
        return true;
    }

    protected override bool TryDecode(
        in DdsMontageStartedEvent dds, out MontageStartedEvent ecs)
    {
        if (!EntityMap.TryGetEntity(dds.Target, out Entity entity))
        {
            ecs = default;
            return false;
        }
        var proxy = new Proxy
        {
            Target = entity,
            MontageId = dds.MontageId,
            ActionInstanceId = dds.ActionInstanceId,
            QueueIndex = dds.QueueIndex,
        };
        ecs = Unsafe.As<Proxy, MontageStartedEvent>(ref proxy);
        return true;
    }

    // Expose encode/decode for unit tests without a live DDS participant.
    internal bool EncodeForTest(in MontageStartedEvent ecs, out DdsMontageStartedEvent dds)
        => TryEncode(ecs, out dds);

    internal bool DecodeForTest(in DdsMontageStartedEvent dds, out MontageStartedEvent ecs)
        => TryDecode(dds, out ecs);
}
