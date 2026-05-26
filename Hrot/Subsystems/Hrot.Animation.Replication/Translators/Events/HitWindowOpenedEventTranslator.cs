using System.Runtime.CompilerServices;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.Animation.Replication.Translators.Events;

/// <summary>
/// Bidirectional translator for <see cref="HitWindowOpenedEvent"/>.
/// Direction is set by the caller (Muscle = Egress, Brain = Ingress).
/// </summary>
internal sealed class HitWindowOpenedEventTranslator
    : CycloneNativeEventTranslator<HitWindowOpenedEvent, DdsHitWindowOpenedEvent>
{
    private const string TopicNameConst = "hrot/anim/HitWindowOpened";

    private struct Proxy
    {
        public Entity Target;
        public int MontageId;
        public byte WindowId;
    }

    public override TranslatorDirection Direction { get; }

    internal HitWindowOpenedEventTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap, TranslatorDirection direction)
        : base(participant, TopicNameConst, entityMap)
    {
        Direction = direction;
    }

    protected override bool TryEncode(
        in HitWindowOpenedEvent ecs, out DdsHitWindowOpenedEvent dds)
    {
        if (!EntityMap.TryGetNetworkId(ecs.Target, out long netId))
        {
            dds = default;
            return false;
        }
        dds = new DdsHitWindowOpenedEvent
        {
            Target = netId,
            MontageId = ecs.MontageId,
            WindowId = ecs.WindowId,
        };
        return true;
    }

    protected override bool TryDecode(
        in DdsHitWindowOpenedEvent dds, out HitWindowOpenedEvent ecs)
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
            WindowId = dds.WindowId,
        };
        ecs = Unsafe.As<Proxy, HitWindowOpenedEvent>(ref proxy);
        return true;
    }

    internal bool EncodeForTest(in HitWindowOpenedEvent ecs, out DdsHitWindowOpenedEvent dds)
        => TryEncode(ecs, out dds);

    internal bool DecodeForTest(in DdsHitWindowOpenedEvent dds, out HitWindowOpenedEvent ecs)
        => TryDecode(dds, out ecs);
}
