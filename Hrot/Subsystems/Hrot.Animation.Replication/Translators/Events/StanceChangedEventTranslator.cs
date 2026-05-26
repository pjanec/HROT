using CycloneDDS.Runtime;
using Fdp.Core;using Fdp.Interfaces;using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.Animation.Replication.Translators.Events;

/// <summary>
/// Bidirectional translator for <see cref="StanceChangedEvent"/>.
/// Direction is set by the caller (Muscle = Egress, Brain = Ingress).
/// </summary>
internal sealed class StanceChangedEventTranslator
    : CycloneNativeEventTranslator<StanceChangedEvent, DdsStanceChangedEvent>
{
    private const string TopicNameConst = "hrot/anim/StanceChanged";

    public override TranslatorDirection Direction { get; }

    internal StanceChangedEventTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap, TranslatorDirection direction)
        : base(participant, TopicNameConst, entityMap)
    {
        Direction = direction;
    }

    protected override bool TryEncode(
        in StanceChangedEvent ecs, out DdsStanceChangedEvent dds)
    {
        if (!EntityMap.TryGetNetworkId(ecs.Target, out long netId))
        {
            dds = default;
            return false;
        }
        dds = new DdsStanceChangedEvent
        {
            Target = netId,
            PreviousStance = (byte)ecs.PreviousStance,
            NewStance = (byte)ecs.NewStance,
        };
        return true;
    }

    protected override bool TryDecode(
        in DdsStanceChangedEvent dds, out StanceChangedEvent ecs)
    {
        if (!EntityMap.TryGetEntity(dds.Target, out Entity entity))
        {
            ecs = default;
            return false;
        }
        ecs = new StanceChangedEvent(
            entity,
            (StanceId)dds.PreviousStance,
            (StanceId)dds.NewStance);
        return true;
    }

    internal bool EncodeForTest(in StanceChangedEvent ecs, out DdsStanceChangedEvent dds)
        => TryEncode(ecs, out dds);

    internal bool DecodeForTest(in DdsStanceChangedEvent dds, out StanceChangedEvent ecs)
        => TryDecode(dds, out ecs);
}
