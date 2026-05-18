using System;
using CycloneDDS.Schema;

namespace Fdp.Toolkit.DER.Examples
{
    [DdsTopic("LocalEntityMaster")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct LocalEntityMaster
    {
        [DdsKey]
        public int EntityId;

        public long TkbType;
        public ulong DisType;
        public ulong Flags;
        public float MockHealth;   // Made up field
    }

    [DdsTopic("LocalGeoSpatial")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct LocalGeoSpatial
    {
        [DdsKey]
        public int EntityId;

        public double InternalLatitude; // Made up field
        public double InternalLongitude; // Made up field
        public float MockSpeed; // Made up field
    }

    [DdsTopic("LocalMapEntitySymbol")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct LocalMapEntitySymbol
    {
        [DdsKey]
        public int EntityId;

        [DdsKey]
        public int MapGroupId;

        public string MockSymbolCode; // Made up field
        public int MockColorIndex; // Made up field
    }
}
