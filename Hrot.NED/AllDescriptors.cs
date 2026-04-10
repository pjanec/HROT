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
        // Cognitive (Brain-owned) descriptor ordinals.  Values match NavigationIntentEgressTranslator
        // and EntityMissionEgressTranslator ordinals so compile-time schema checks are consistent.
        dtEntityMission    = 51,
        dtNavigationIntent = 52,
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
