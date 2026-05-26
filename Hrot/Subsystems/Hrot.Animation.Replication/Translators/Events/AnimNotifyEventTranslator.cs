using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.Animation.Replication.Translators.Events;

/// <summary>
/// Bidirectional translator for <see cref="AnimNotifyEvent"/>.
/// Direction is set by the caller (Muscle = Egress, Brain = Ingress).
/// </summary>
internal sealed class AnimNotifyEventTranslator
    : CycloneNativeEventTranslator<AnimNotifyEvent, DdsAnimNotifyEvent>
{
    private const string TopicNameConst = "hrot/anim/AnimNotify";

    public override TranslatorDirection Direction { get; }

    internal AnimNotifyEventTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap, TranslatorDirection direction)
        : base(participant, TopicNameConst, entityMap)
    {
        Direction = direction;
    }

    protected override bool TryEncode(
        in AnimNotifyEvent ecs, out DdsAnimNotifyEvent dds)
    {
        if (!EntityMap.TryGetNetworkId(ecs.Target, out long netId))
        {
            dds = default;
            return false;
        }
        dds = new DdsAnimNotifyEvent
        {
            Target = netId,
            MontageId = ecs.MontageId,
            MarkerHash = ecs.MarkerHash,
            PayloadFloat = ecs.PayloadFloat,
        };
        return true;
    }

    protected override bool TryDecode(
        in DdsAnimNotifyEvent dds, out AnimNotifyEvent ecs)
    {
        if (!EntityMap.TryGetEntity(dds.Target, out Entity entity))
        {
            ecs = default;
            return false;
        }
        ecs = new AnimNotifyEvent(entity, dds.MontageId, dds.MarkerHash, dds.PayloadFloat);
        return true;
    }

    internal bool EncodeForTest(in AnimNotifyEvent ecs, out DdsAnimNotifyEvent dds)
        => TryEncode(ecs, out dds);

    internal bool DecodeForTest(in DdsAnimNotifyEvent dds, out AnimNotifyEvent ecs)
        => TryDecode(dds, out ecs);
}
