using Bagira.BDC.SSTM;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Network.Cyclone.Translators;

using DdsFireInteractionEvent = Bagira.BDC.SSTM.FireInteractionEvent;
using EcsFireInteractionEvent = Bagira.Map.Common.Events.FireInteractionEvent;

namespace Bagira.SimHost.Translators
{
    /// <summary>
    /// Egress translator for transient <see cref="EcsFireInteractionEvent"/> events.
    /// SimHost is egress-only and must not consume these events from DDS.
    /// </summary>
    public class FireInteractionEventTranslator
        : CycloneNativeEventTranslator<EcsFireInteractionEvent, DdsFireInteractionEvent>
    {
        private const string DdsTopicName = "FireInteractionEvent";

        public FireInteractionEventTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : base(participant, DdsTopicName, entityMap)
        {
        }

        protected override bool TryDecode(in DdsFireInteractionEvent dds, out EcsFireInteractionEvent ecs)
        {
            ecs = default;
            return false; // SimHost is egress-only for fire interaction events.
        }

        protected override bool TryEncode(in EcsFireInteractionEvent ecs, out DdsFireInteractionEvent dds)
        {
            dds = new DdsFireInteractionEvent
            {
                ShooterX = ecs.ShooterX,
                ShooterY = ecs.ShooterY,
                TargetX  = ecs.TargetX,
                TargetY  = ecs.TargetY,
            };
            return true;
        }
    }
}
