using CycloneDDS.Schema;
using Hrot.BDC.Common;

namespace Hrot.BDC.Messages
{
    // Entity lifecycle topic for BDC.
    // When this topic instance is alive the entity exists.
    // When it is disposed the entity is deleted.
    // Topic name BDC_EntityMaster is distinct from NED's EntityMaster.
    [DdsTopic("BDC_EntityMaster")]
    [DdsIdlFile("bdc-entity-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct BdcEntityMaster
    {
        // Entity network ID; 0=invalid
        [DdsKey]
        public int EntityId;

        // TKB type index; 0=invalid
        public long TkbType;

        // SISO DIS entity kind (1=Platform, 2=Munition, etc.)
        public byte Diskind;
    }

    // Merged BDC spatial topic: position, orientation, and velocity.
    // Topic name BDC_WorldPos is distinct from NED's WorldPos.
    [DdsTopic("BDC_WorldPos")]
    [DdsIdlFile("bdc-entity-msgs")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct BdcWorldPos
    {
        [DdsKey]
        public int EntityId;

        public DateTime Time;
        public BdcGeoPoint Pos;
        public BdcEulerOri Ori;
        public BdcAngularVector Vel;
    }
}
