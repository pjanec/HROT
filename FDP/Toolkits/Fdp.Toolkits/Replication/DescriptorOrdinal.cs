namespace Fdp.Toolkit.Replication;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-017</c> — the FDP-side descriptor-ordinal vocabulary.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §16. 🔒 <b>User ruling, <c>2026-08-26</c>:</b>
/// *"if same enums needs to exist twice in different namespaces (network one, fdp one), so be it, with same
/// numeric value, translated in network translator, accepted cost for network agnosticism."*</para>
///
/// <para>⭐⭐ <b>What an ordinal actually IS — and it is NOT a wire field.</b> 📐 Measured <c>2026-08-26</c>:
/// it is a <b>bit index</b>. <c>SmartEgressUtil.MarkDirty</c> records it in
/// <c>EgressPublicationState.DirtyDescriptors</c> so an egress translator's
/// <c>ShouldPublish(view, entity, ordinal)</c> can answer *"has this descriptor changed?"*. ⛔ Nothing
/// serialises it — the attribute update carries <c>AttributeId</c> *(<c>Heading = 13</c>)*, never an
/// ordinal. ⚠ An earlier design note claimed the opposite *("a descriptor ordinal IS wire numbering")* and
/// used it to argue the apply path could not leave the DDS assembly; that claim is RETRACTED and this file
/// is the consequence.</para>
///
/// <para>⭐⭐⭐ <b>DUPLICATION IS THE POINT, not debt.</b> 📌 Exactly the precedent
/// <see cref="Patching.AttributeValueKind"/> set against the wire's <c>AttributeValueType</c> under
/// <c>R-134</c>: the FDP-internal path names FDP-internal types, the network layer names wire types, and one
/// declared boundary translates. ⇒ ⛔ do NOT "clean this up" by referencing
/// <c>Hrot.NED.Descriptors.EDescriptorType</c> from FDP code — that is the coupling this file removes.</para>
///
/// <para>⚠⚠ <b>The values MUST match the network enum member-for-member</b>, because both index the same
/// <c>DirtyDescriptors</c> set at runtime — a translator on the network side and an installer on the FDP side
/// must mean the same bit. ⭐ Railed element-wise by
/// <c>TheDescriptorOrdinalVocabulariesAgreeTests</c>: a renumber on either side, or a member added to only
/// one, is a RED — ⛔ never a silent divergence.</para>
/// </summary>
public enum DescriptorOrdinal : long
{
    EntityMaster     = 0,
    EntityInfo       = 1,
    WorldPos         = 2,
    MapVisualOverlay = 3,
    MapRoute         = 4,
    EntityDamage     = 30,
    MapEntitySymbol  = 40,

    // Cognitive (Brain-owned).
    EntityMission         = 51,
    NavigationIntent      = 52,
    NavigationStatus      = 53,
    DeferredTakeOwnership = 54,
    OwnershipUpdate       = 55,

    // Sensor / raycast / path.
    SensorConfig           = 60,
    RaycastRequestBatch    = 61,
    SensorTrackState       = 62,
    RaycastResponseBatch   = 63,
    PathRequestBatch       = 64,
    PathResponseBatch      = 65,
    GroundClampingOverride = 66,

    // Weapon / fire interaction.
    WeaponFireRequest   = 80,
    WeaponFire          = 81,
    MunitionDetonation  = 82,
    EntityHitDamage     = 83,
    AudioTargetDetected = 84,

    // Mission control.
    MissionControlRequest = 90,
    MissionControlAck     = 91,

    // Tactical intent (Brain-to-Brain).
    TacticalIntentRequest = 92,

    // EQS area-query pipeline.
    AreaQueryRequestBatch  = 93,
    AreaQueryResponseBatch = 94,
    EqsSensorConfig        = 95,
    EqsResult              = 96,

    // Animation control — block 100–119 reserved; append within the block.
    AnimationChannelIntent     = 100,
    AnimationChannelStatus     = 101,
    LookAtChannelIntent        = 102,
    LookAtChannelStatus        = 103,
    StanceIntent               = 104,
    StanceStatus               = 105,
    AnimationMontageQueue      = 106,
    AnimationMontageQueueState = 107,
}
