using System;
using CycloneDDS.Schema;

namespace Hrot.NED.Descriptors
{
    public enum EDescriptorType
    {
        dtEntityMaster     = 0,
        dtEntityInfo       = 1,
        dtWorldPos         = 2,
        dtMapVisualOverlay = 3,
        dtMapRoute         = 4,
        dtEntityDamage     = 30,
        dtMapEntitySymbol       = 40,
        // Cognitive (Brain-owned) descriptor ordinals.  Values match NavigationIntentEgressTranslator
        // and EntityMissionEgressTranslator ordinals so compile-time schema checks are consistent.
        dtEntityMission    = 51,
        dtNavigationIntent = 52,
        dtNavigationStatus = 53,             // Muscle-owned nav-completion status
        dtDeferredTakeOwnership = 54,        // Out-of-band pre-genesis routing (Brain → Muscle)
        dtOwnershipUpdate       = 55,        // Out-of-band authority handover notification (Muscle → Brain)
        // Sensor / Raycast / Path
        dtSensorConfig          = 60,
        dtRaycastRequestBatch   = 61,
        dtSensorTrackState      = 62,
        dtRaycastResponseBatch  = 63,
        dtPathRequestBatch      = 64,
        dtPathResponseBatch     = 65,
        dtGroundClampingOverride= 66,
        // Weapon / Fire interaction
        dtWeaponFireRequest     = 80,
        dtWeaponFire            = 81,
        dtMunitionDetonation    = 82,
        dtEntityHitDamage       = 83,
        dtAudioTargetDetected   = 84,
        // Mission control
        dtMissionControlRequest = 90,
        dtMissionControlAck     = 91,
        // Tactical intent (Brain-to-Brain)
        dtTacticalIntentRequest = 92,
        // ⛔ 93 / 94 — RETIRED 2026-10-01 with the AreaQuery pipeline (dtAreaQueryRequestBatch /
        //   dtAreaQueryResponseBatch, topics "AreaQueryRequestBatch" / "AreaQueryResponseBatch"). A WIRE-CONTRACT
        //   REMOVAL: a peer still on the old build publishes topics nobody reads. ⛔ Never reuse 93 or 94.
        //   📄 docs/designs/eqs-2/EQS_Design_v1.3_final.md §18.
        // EQS v1.3 sensor config and result topics
        dtEqsSensorConfig        = 95,
        dtEqsResult              = 96,
        // ⭐ CE-484 — behaviour fault notification (any node → every operator UI). 📄 DESIGN_Behaviour_Fault_And_Teardown.md §4c
        dtBehaviorFault          = 97,
        // ⭐ CE-3048 — a unit's AI intent (task · SOP · ROE), published by its Brain owner, started by the node that gains
        //   the Brain. TransientLocal. 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.7
        dtBrainIntent            = 98,
        // ── Animation control (Brain ↔ Muscle) — DD-2 §6.
        //    Block 100–119 reserved for animation; new entries append within block.
        //    Channels (intent + status pairs)
        dtAnimationChannelIntent      = 100, // Brain → Muscle: AnimationChannel intent (DD-2 §2.2)
        dtAnimationChannelStatus      = 101, // Muscle → Brain: AnimationChannel status (DD-2 §2.2)
        dtLookAtChannelIntent         = 102, // Brain → Muscle: LookAtChannel intent  (DD-2 §2.3)
        dtLookAtChannelStatus         = 103, // Muscle → Brain: LookAtChannel status  (DD-2 §2.3)
        //    Stance descriptor pair (CQRS — not a channel)
        dtStanceIntent                = 104, // Brain → Muscle: StanceIntent          (DD-2 §3)
        dtStanceStatus                = 105, // Muscle → Brain: StanceStatus          (DD-2 §3)
        //    Side-buffer pair (queue spec + queue progress)
        dtAnimationMontageQueue       = 106, // Brain → Muscle: AnimationMontageQueue (DD-2 §4)
        dtAnimationMontageQueueState  = 107, // Muscle → Brain: queue-state           (DD-2 §4.3)
        // etc., all known descriptor types here
    }

    // Unified descriptor payload
    [DdsUnion]
    [DdsIdlFile("hrot-all-desc")]
    public partial struct EntityDescriptorUnion
    {
        [DdsDiscriminator]
        public EDescriptorType _d;

        [DdsCase(EDescriptorType.dtEntityMaster)]
        public EntityMaster EntityMaster;

        [DdsCase(EDescriptorType.dtEntityInfo)]
        public Hrot.NED.Descriptors.EntityInfo EntityInfo;

        [DdsCase(EDescriptorType.dtWorldPos)]
        public WorldPos WorldPos;

        [DdsCase(EDescriptorType.dtMapVisualOverlay)]
        public MapVisualOverlay MapVisualOverlay;

        [DdsCase(EDescriptorType.dtMapRoute)]
        public MapRoute MapRoute;

    }
}
