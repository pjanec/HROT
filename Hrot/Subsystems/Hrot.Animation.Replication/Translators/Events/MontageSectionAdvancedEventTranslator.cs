using System.Runtime.CompilerServices;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.Animation.Replication.Translators.Events;

/// <summary>
/// Bidirectional translator for <see cref="MontageSectionAdvancedEvent"/>.
/// Direction is set by the caller (Muscle = Egress, Brain = Ingress).
/// </summary>
internal sealed class MontageSectionAdvancedEventTranslator
    : CycloneNativeEventTranslator<MontageSectionAdvancedEvent, DdsMontageSectionAdvancedEvent>
{
    private const string TopicNameConst = "hrot/anim/MontageSectionAdv";

    private struct Proxy
    {
        public Entity Target;
        public int MontageId;
        public byte FromSectionIndex;
        public byte ToSectionIndex;
    }

    public override TranslatorDirection Direction { get; }

    internal MontageSectionAdvancedEventTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap, TranslatorDirection direction)
        : base(participant, TopicNameConst, entityMap)
    {
        Direction = direction;
    }

    protected override bool TryEncode(
        in MontageSectionAdvancedEvent ecs, out DdsMontageSectionAdvancedEvent dds)
    {
        if (!EntityMap.TryGetNetworkId(ecs.Target, out long netId))
        {
            dds = default;
            return false;
        }
        dds = new DdsMontageSectionAdvancedEvent
        {
            Target = netId,
            MontageId = ecs.MontageId,
            FromSectionIndex = ecs.FromSectionIndex,
            ToSectionIndex = ecs.ToSectionIndex,
        };
        return true;
    }

    protected override bool TryDecode(
        in DdsMontageSectionAdvancedEvent dds, out MontageSectionAdvancedEvent ecs)
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
            FromSectionIndex = dds.FromSectionIndex,
            ToSectionIndex = dds.ToSectionIndex,
        };
        ecs = Unsafe.As<Proxy, MontageSectionAdvancedEvent>(ref proxy);
        return true;
    }

    internal bool EncodeForTest(in MontageSectionAdvancedEvent ecs, out DdsMontageSectionAdvancedEvent dds)
        => TryEncode(ecs, out dds);

    internal bool DecodeForTest(in DdsMontageSectionAdvancedEvent dds, out MontageSectionAdvancedEvent ecs)
        => TryDecode(dds, out ecs);
}
