using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Fdp.Network.Cyclone.Translators;

using DdsFireInteractionEvent = Hrot.NED.Messages.FireInteractionEvent;
using EcsFireInteractionEvent = Hrot.Map.Common.Events.FireInteractionEvent;

namespace Hrot.Map.Common.Replication
{
    /// <summary>
    /// Bidirectional translator for transient <see cref="EcsFireInteractionEvent"/> events.
    ///
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>IG (ingress)</b> — calls <see cref="PollIngress"/> each tick to receive
    ///     fire-interaction events published by SimHost and post them to the local event bus.
    ///   </description></item>
    ///   <item><description>
    ///     <b>SimHost (egress)</b> — calls <see cref="ScanAndPublish"/> each tick to
    ///     encode locally-raised events and write them to the DDS topic.
    ///   </description></item>
    /// </list>
    /// </summary>
    public class FireInteractionEventTranslator
        : CycloneNativeEventTranslator<EcsFireInteractionEvent, DdsFireInteractionEvent>
    {
        private const string DdsTopicName = "FireInteractionEvent";

        public override TranslatorDirection Direction => TranslatorDirection.Bidirectional;

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
