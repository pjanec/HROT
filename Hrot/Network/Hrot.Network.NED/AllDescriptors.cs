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
        dtNearMiss              = 85,        // ⭐ CE-3064 — a round passed close to a unit (Muscle → Brain)
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
        // ⭐ Buildings Stage 5b — a terrain door's live state (owner → every node). 📄 docs/DESIGN_Building_Interiors.md §3j
        dtDoorState                   = 120,
        // 121 retired (5d-1's door-only EntityDoorCommand; never reuse)
        // ⭐ R-221 — an actor's interaction with an entity it does not own (any kind) → the target's owner. 📄 docs/DESIGN_Entity_Interactions.md
        dtInteractionRequest          = 122,
        // ⭐ CE-3136 P-7a — a static obstacle's box (creator → every node, before promotion). 📄 docs/DESIGN_Peek_And_Fire.md §9
        dtObstacleShape               = 123,
        // ⭐ WL-1b (CE-1049) — a turret part's pose (the MuscleGround group → every node). 📄 docs/DESIGN_Weapon_Layers.md §5b
        dtTurretPose                  = 124,
        // ⭐ WL-2b (CE-1049, W-11 / R-267) — a turret's order (Brain group → Muscle) and the muscle's report (MuscleGround → Brain).
        dtWeaponTask                  = 125,
        dtWeaponTaskStatus            = 126,
        // ⭐ WL-6 (W-13d, R-272) — the weapon brain's posture of a unit's weapon (Brain group → every node). 📄 docs/DESIGN_Weapon_Layers.md §5h
        dtWeaponPosture               = 127,
        // ⭐ CE-3173 S4e — an operator's motion axes (the Brain group → the Muscle). 📄 docs/DESIGN_Entity_Control.md §8c
        dtManualDrive                 = 128,
        // ⭐ CE-3172 S4e — which station holds a unit (the Brain group → every node; C-3). 📄 docs/DESIGN_Entity_Control.md §8c
        dtPossessedBy                 = 129,
        // ⭐ CE-3174 (C-7) — a unit's ammunition per mount (the Brain group → every node, for the FPS HUD). 📄 docs/DESIGN_Entity_Control.md §8d
        dtWeaponAmmo                  = 130,
        // ⭐ CE-3110 I-2b (R-224) — a vehicle's passenger list, the vehicle's owner (Brain group) → every node, which derives the
        //   passenger's side from it. 📄 docs/DESIGN_Entity_Interactions.md §7.1c
        dtPassengerBuffer             = 131,
        // ⭐ Weapon systems WS-1 (W-C, R-136) — a weapon system's deploy state, its owner (Brain group) → every node.
        //   📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §2, §6
        dtWeaponSystemState           = 132,
        // ⭐ CE-2189 — a missile launched at an aircraft (Muscle → its Brain). 📄 docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §3c
        dtMissileWarning              = 133,
        // ⛔ RESERVED — CE-2188's flare / chaff request (Brain → Muscle), RETIRED by BS-1-DESIGN §5.4c (a release is an ordinary
        //   WeaponFireRequest on the dispenser mount). No translator uses it. ⚠ Do NOT reuse 134.
        dtDispenseCountermeasure      = 134,
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
