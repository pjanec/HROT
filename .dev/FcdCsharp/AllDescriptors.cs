using System;
using CycloneDDS.Schema;

namespace Bagira.BDC.SSTD
{
    public enum EDescriptorType
    {
        dtEntityMaster,
        dtEntityInfo,
        dtGeoSpatial,
        dtGeoSpatialDR
        // etc., all known descriptor types here
    }

    // Unified descriptor payload
    [DdsUnion]
    [DdsIdlFile("bdc-sst-all-desc")]
    public partial struct EntityDescriptorUnion
    {
        [DdsDiscriminator]
        public EDescriptorType _d;

        [DdsCase(EDescriptorType.dtEntityMaster)]
        public EntityMaster EntityMaster;

        [DdsCase(EDescriptorType.dtEntityInfo)]
        public EntityInfo EntityInfo;

        [DdsCase(EDescriptorType.dtGeoSpatial)]
        public GeoSpatial GeoSpatial;

        [DdsCase(EDescriptorType.dtGeoSpatialDR)]
        public GeoSpatialDR GeoSpatialDR;

    }
}
