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
        // EQS area-query pipeline (Brain <-> Muscle)
        dtAreaQueryRequestBatch  = 93,
        dtAreaQueryResponseBatch = 94,
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
