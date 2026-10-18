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
        // ⭐ CE-3210 — the damage per body part (the Health owner, Brain group → every node). 📄 docs/designs/hit-location/… HL-6.
        //   ⚠ 135 / 136 measured free on backend, ui and behaviors 2026-10-16.
        dtPartDamage                  = 135,
        // ⭐ CE-3210 — where rounds struck a body (the Muscle → every node). HL-8.
        dtHitMarks                    = 136,
        // ⭐ Airborne insertion B-1 — a body coming down a rope or under a canopy (the position's owner, Muscle → every node). D-1.
        //   ⚠ 137 measured free on backend, ui and behaviors 2026-10-16.
        dtDescent                     = 137,
        // ⭐ CE-3216 S1-0 (R-3012, W-8) — the scenario clock (start time) on the World Environment, Brain group (its creator owns it).
        dtScenarioClock               = 138,
        // ⭐ CE-3216 S1 (R-3012) — AUTHORED weather (the global on the World Environment, a region's on a Weather Region), Brain group.
        dtWeather                     = 139,
        // ⭐ CE-1128 — a round marked a surface of the world (Muscle → every map host): the puff and the mark. 📄 docs/DESIGN_Visual_Effects.md §10.
        //   ⚠ 138 / 139 are backend's (dtScenarioClock, dtWeather, measured on origin/backend 2026-10-16) — 140 measured free on all three lanes.
        dtRoundImpact                 = 140,
        // ⭐ CE-1153 — the brain's verdict on the operator's last USE press (Brain group → every node, for the FPS HUD).
        //   📄 docs/DESIGN_Entity_Control.md §8h.6. Reserved for this item at dispatch; measured free on ui, behaviors, backend, coordinator.
        dtOperatorUseStatus           = 141,
        // ⭐ CE-3219 (civilians B-1, CV-6) — a person's stored non-lethal effects, beside Health (Brain group → every node).
        //   📄 docs/DESIGN_Civilians_And_Crowd_Control.md §7.3. Measured free on ui, behaviors, backend, coordinator.
        dtEffectState                 = 142,
        // ⭐ CE-3224 (civilians B-6, CV-13) — what a person is doing with his arms (Brain group → every node).
        //   📄 docs/DESIGN_Civilians_And_Crowd_Control.md §7.7. Measured free on ui, behaviors, backend, coordinator.
        dtGesture                     = 143,
        // ⭐ CE-3219 part 3 (civilians B-1, R-3014) — a gas / smoke cloud's description, its creator → every node (no group).
        //   📄 docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b. Measured free on ui, behaviors, backend, coordinator.
        dtAreaEffectCloud             = 144,
        // ⭐ CE-3238 (civilians B-16, AW-22) — a man's hostile act and its kind (Brain group → every node).
        //   📄 docs/DESIGN_Civilians_And_Crowd_Control.md §7.16. Measured free on ui, behaviors, backend, coordinator.
        dtHostileAct                  = 145,
        // ⭐ CE-3250 (behaviors' BR-3) — a report up the chain of command (an event topic, every node). 📄 DESIGN_Building_Clearing.md §5b.
        //   Measured free on ui, behaviors, backend 2026-10-18.
        dtSquadReport                 = 146,
        // ⭐ CE-3252 (LD-1 / LD-2) — where a person looks: the wanted look and the head turn (Muscle group → every node).
        //   📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a. Measured free on ui, behaviors, backend, coordinator 2026-10-18.
        dtLookState                   = 147,
        // ⭐ CE-3256 (BR-4) — a man in custody: detained, restrained, searched (Brain group → every node). 📄 DESIGN_Entity_Interactions §7.6.
        //   Measured free on ui, behaviors, backend, coordinator 2026-10-19. ⚠ FDP states it too (CustodyRules.CustodyDescriptorOrdinal; a rail pins it).
        dtCustody                     = 148,
        // ⭐ CE-3257 (BR-5) — a man leaning round a corner (Brain group → every node). 📄 hit location §13. Measured free on ui, behaviors,
        //   backend, coordinator 2026-10-19.
        dtLean                        = 149,
        // ⭐ CE-3259 (BR-6/BR-7) — behaviors' CombatProficiency (CE-2273), its brain → every node: the hit model (the muscle) and the
        //   visual sensor read it where they resolve. Measured free on ui, behaviors, backend, coordinator 2026-10-19.
        dtCombatProficiency           = 150,
        // ⭐ CE-3261 — behaviors' EngagementArea (CE-2282), its brain → every node: a commander's NoFire reaches the brain nodes of the men
        //   under him, wherever they run. Measured free on ui, behaviors, backend, coordinator 2026-10-19.
        dtEngagementArea              = 151,
        // ⭐ AQ93 §12 N4 (R-4056) — the TRAFFIC block 160–169, kept clear of the sequential run so lanes cannot collide (TKB 8812
        //   was allocated twice on 2026-10-17). The scenario's traffic settings: the Brain creates them, the Traffic group serves them.
        dtTrafficDemand               = 160,
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

        // ⭐ CE-3228 / CE-3219 part 3 — a gas / smoke cloud asked for on one node and created on another (owner 0 ⇒ the default
        //   processor): the forwarded request must carry the cloud, or the creator builds a bare type-7202 entity nothing ends.
        [DdsCase(EDescriptorType.dtAreaEffectCloud)]
        public EntityAreaEffectCloud AreaEffectCloud;

    }
}
