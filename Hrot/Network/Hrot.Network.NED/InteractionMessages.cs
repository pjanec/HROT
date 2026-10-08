using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐ R-221 (📄 docs/DESIGN_Entity_Interactions.md) — the kinds of interaction an actor can request of an entity it does not own.
    /// The discriminator of <see cref="InteractionPayload"/>. ⛔ Values are wire format: append only, never renumber or reuse.
    /// <para>Embark/Disembark/EjectPassengers are RESERVED here so that their slot cannot collide with another kind; their union cases
    /// arrive with the embarkation task (§7).</para>
    /// </summary>
    public enum EInteractionKind : int
    {
        None            = 0,
        Door            = 1,
        Embark          = 2,   // reserved — §7
        Disembark       = 3,   // reserved — §7
        EjectPassengers = 4,   // reserved — §7
    }

    /// <summary>⭐ The <see cref="EInteractionKind.Door"/> case: what the actor does to the door.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct DoorInteractionPayload
    {
        /// <summary><c>DoorVerb</c>: 0 Open · 1 Close · 2 Lock · 3 Unlock · 4 Breach.</summary>
        public byte Verb;
    }

    /// <summary>
    /// ⭐ R-221 — the kind-specific part of an interaction: one case per <see cref="EInteractionKind"/>. A new kind is a new case
    /// (the data model grows; the topic and the translators do not). ⛔ No entity handles in a case: target and actor ride the header
    /// as network ids, and any further entity is an explicit network-id field its codec maps.
    /// </summary>
    [DdsUnion]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct InteractionPayload
    {
        [DdsDiscriminator]
        public EInteractionKind Kind;

        [DdsCase(EInteractionKind.Door)]
        public DoorInteractionPayload Door;
    }

    /// <summary>
    /// ⭐ R-221 — "actor wants to do this to target": sent by a node that does not own the target, applied by the one that does. The
    /// result reaches every node as the target's own replicated state — there is no reply message.
    /// </summary>
    // Reliable + KeepAll: requests are events — a KeepLast reader drops one of two taken together (CE-3095).
    [DdsTopic("EntityInteractionRequest")]
    [DdsIdlFile("hrot-sim-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct EntityInteractionRequest
    {
        /// <summary>Network entity id of what is acted on.</summary>
        public long TargetId;

        /// <summary>Network entity id of who acts, or 0 (an operator / unknown).</summary>
        public long ActorId;

        public InteractionPayload Payload;
    }
}
