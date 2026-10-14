using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐ R-221 (📄 docs/DESIGN_Entity_Interactions.md) — the kinds of interaction an actor can request of an entity it does not own.
    /// The discriminator of <see cref="InteractionPayload"/>. ⛔ Values are wire format: append only, never renumber or reuse.
    /// <para>⭐ CE-3110 I-2a — Embark/Disembark/EjectPassengers (reserved by I-1) now have their union cases (§7).</para>
    /// </summary>
    public enum EInteractionKind : int
    {
        None            = 0,
        Door            = 1,
        Embark          = 2,   // ⭐ CE-3110 I-2a — target = the vehicle, actor = the passenger
        Disembark       = 3,   // ⭐ CE-3110 I-2a — target = the vehicle, actor = the passenger
        EjectPassengers = 4,   // ⭐ CE-3110 I-2a — target = the vehicle
        // ⭐ CE-3172 S4e (docs/DESIGN_Entity_Control.md §8c) — possession: a station's request, release and control frames, applied by
        //   the unit's brain owner.
        Possess         = 5,
        ReleasePossession = 6,
        ControlFrame    = 7,
    }

    /// <summary>⭐ The <see cref="EInteractionKind.Door"/> case: what the actor does to the door.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct DoorInteractionPayload
    {
        /// <summary><c>DoorVerb</c>: 0 Open · 1 Close · 2 Lock · 3 Unlock · 4 Breach.</summary>
        public byte Verb;
    }

    /// <summary>⭐ CE-3172 — the <see cref="EInteractionKind.Possess"/> case: which station asks, and how (<c>PossessionMode</c>).</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct PossessInteractionPayload
    {
        public long StationNodeId;
        public byte Mode;
    }

    /// <summary>⭐ CE-3172 — the <see cref="EInteractionKind.ReleasePossession"/> case: which station lets go.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct ReleasePossessionInteractionPayload
    {
        public long StationNodeId;
    }

    /// <summary>⭐ CE-3172 / CE-3173 — the <see cref="EInteractionKind.ControlFrame"/> case: one frame of the station's controls (M-6:
    /// the brain keeps the newest <see cref="Seq"/>).</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct ControlFrameInteractionPayload
    {
        public long StationNodeId;
        public uint Seq;
        public float Forward;
        public float Lateral;
        public float Steer;
        public float Yaw;
        public byte Gait;
        public byte Trigger;
        public byte Mount;
        public float AimX;
        public float AimY;
        public float AimZ;
        public float Vertical;   // ⭐ CE-2168 (MD-D) — the station's climb axis, carried to the brain with the frame (appended)
        /// <summary>⭐ CE-1074 — the operator's stance request (StanceId + 1; 0 = none).</summary>
        public byte Stance;
    }

    /// <summary>⭐ CE-3110 I-2a — the <see cref="EInteractionKind.Embark"/> case. Nothing beyond the header (target = the vehicle, actor =
    /// the passenger); one reserved byte so the case is never an empty struct.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct EmbarkInteractionPayload
    {
        public byte Reserved;

        /// <summary>⭐ <c>CE-2151</c> B-b (R-2009 B4) — the seat role asked for (<c>SeatRole</c>; 0 = Passenger, a plain boarding).</summary>
        public byte Role;

        /// <summary>⭐ <c>CE-2152</c> C-a (R-2011 C5) — carried or dragged onto a person carrier (<c>CarryMode</c>; 0 = seated).</summary>
        public byte Mode;
    }

    /// <summary>⭐ CE-3110 I-2a — the <see cref="EInteractionKind.Disembark"/> case (target = the vehicle, actor = the passenger).</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct DisembarkInteractionPayload
    {
        public byte Reserved;
    }

    /// <summary>⭐ CE-3110 I-2a — the <see cref="EInteractionKind.EjectPassengers"/> case (target = the vehicle).</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct EjectPassengersInteractionPayload
    {
        public byte Reserved;
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

        [DdsCase(EInteractionKind.Embark)]
        public EmbarkInteractionPayload Embark;

        [DdsCase(EInteractionKind.Disembark)]
        public DisembarkInteractionPayload Disembark;

        [DdsCase(EInteractionKind.EjectPassengers)]
        public EjectPassengersInteractionPayload EjectPassengers;

        [DdsCase(EInteractionKind.Possess)]
        public PossessInteractionPayload Possess;

        [DdsCase(EInteractionKind.ReleasePossession)]
        public ReleasePossessionInteractionPayload ReleasePossession;

        [DdsCase(EInteractionKind.ControlFrame)]
        public ControlFrameInteractionPayload ControlFrame;
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
