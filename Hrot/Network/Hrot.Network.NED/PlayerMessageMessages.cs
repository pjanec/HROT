using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐ <b>rules S2-f (📄 docs/DESIGN_Scenario_Rules.md §00.9, SR-121) — a message to the players, on the wire.</b> Event-shaped, on the <c>SquadReport</c> precedent:
    /// published once by the node that raised the local <c>PlayerMessageEvent</c> (<c>PlayerMessageEgressTranslator</c>) and raised again as that event on every
    /// other node (<c>PlayerMessageIngressTranslator</c>; <see cref="OriginNodeId"/> skips a node's own sample). Ordinal
    /// <see cref="Hrot.NED.Descriptors.EDescriptorType.dtPlayerMessage"/> = 173. ⛔ Not a state topic: a late joiner does not get old messages (SR-121); the AAR takes
    /// them from the trigger's journal.
    /// </summary>
    [DdsTopic]
    [DdsIdlFile("hrot-scenario-rules")]
    [DdsManaged]
    public partial struct PlayerMessage
    {
        /// <summary>Per sender, increasing.</summary>
        public int MessageId;
        /// <summary>The node that raised it (a node skips its own sample).</summary>
        public int OriginNodeId;
        /// <summary>All / Side / Station / Unit / Instructor.</summary>
        public string ToKind;
        /// <summary>The side (<c>ForceId</c>) / station / unit (network id) <see cref="ToKind"/> names; 0 for All / Instructor.</summary>
        public int ToValue;
        public string Text;
        /// <summary>How long a subtitle shows (s).</summary>
        public float Seconds;
        public byte Priority;
        /// <summary>Sim time (s) it was sent.</summary>
        public double SentAt;
        /// <summary>The network id of the trigger that sent it; 0 = none.</summary>
        public int Source;
    }
}
