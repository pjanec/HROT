using Bagira.BDC.SSTM;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Network.Cyclone.Translators;

using DdsFireInteractionEvent = Bagira.BDC.SSTM.FireInteractionEvent;
using EcsFireInteractionEvent = Bagira.Map.Common.Events.FireInteractionEvent;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Ingress translator for transient <see cref="EcsFireInteractionEvent"/> events.
    /// IG is ingress-only and must not publish these events to DDS.
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
            ecs = new EcsFireInteractionEvent
            {
                ShooterX = dds.ShooterX,
                ShooterY = dds.ShooterY,
                TargetX  = dds.TargetX,
                TargetY  = dds.TargetY,
            };
            return true;
        }

        protected override bool TryEncode(in EcsFireInteractionEvent ecs, out DdsFireInteractionEvent dds)
        {
            dds = default;
            return false; // IG is ingress-only for fire interaction events.
        }
    }
}
