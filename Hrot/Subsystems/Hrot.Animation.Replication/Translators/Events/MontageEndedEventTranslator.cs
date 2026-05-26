using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.Animation.Replication.Translators.Events;

/// <summary>
/// Bidirectional translator for <see cref="MontageEndedEvent"/>.
/// Direction is set by the caller (Muscle = Egress, Brain = Ingress).
/// </summary>
internal sealed class MontageEndedEventTranslator
    : CycloneNativeEventTranslator<MontageEndedEvent, DdsMontageEndedEvent>
{
    private const string TopicNameConst = "hrot/anim/MontageEnded";

    public override TranslatorDirection Direction { get; }

    internal MontageEndedEventTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap, TranslatorDirection direction)
        : base(participant, TopicNameConst, entityMap)
    {
        Direction = direction;
    }

    protected override bool TryEncode(
        in MontageEndedEvent ecs, out DdsMontageEndedEvent dds)
    {
        if (!EntityMap.TryGetNetworkId(ecs.Target, out long netId))
        {
            dds = default;
            return false;
        }
        dds = new DdsMontageEndedEvent
        {
            Target = netId,
            MontageId = ecs.MontageId,
            ActionInstanceId = ecs.ActionInstanceId,
            QueueIndex = ecs.QueueIndex,
            EndReason = (byte)ecs.EndReason,
        };
        return true;
    }

    protected override bool TryDecode(
        in DdsMontageEndedEvent dds, out MontageEndedEvent ecs)
    {
        if (!EntityMap.TryGetEntity(dds.Target, out Entity entity))
        {
            ecs = default;
            return false;
        }
        ecs = new MontageEndedEvent(
            entity,
            dds.MontageId,
            dds.ActionInstanceId,
            dds.QueueIndex,
            (MontageEndReason)dds.EndReason);
        return true;
    }

    internal bool EncodeForTest(in MontageEndedEvent ecs, out DdsMontageEndedEvent dds)
        => TryEncode(ecs, out dds);

    internal bool DecodeForTest(in DdsMontageEndedEvent dds, out MontageEndedEvent ecs)
        => TryDecode(dds, out ecs);
}
