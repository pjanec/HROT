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
        public long EntityId;

        // TKB type index; 0=invalid
        public long TkbType;

        // SISO DIS entity kind (1=Platform, 2=Munition, etc.)
        public byte Diskind;
    }

    // Merged BDC spatial topic: position, orientation, velocity, angular velocity — the same shape and rules as NED's WorldPos
    // (AQ107 D / F), in BDC's own types. Topic name BDC_WorldPos is distinct from NED's WorldPos.
    [DdsTopic("BDC_WorldPos")]
    [DdsIdlFile("bdc-entity-msgs")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct BdcWorldPos
    {
        [DdsKey]
        public long EntityId;

        // Cluster-synced SIMULATION time (seconds) at which the sender sampled the pose; dead reckoning ages from it.
        public double SimTime;

        // Position [m], local ENU (X east, Y north, Z up).
        public BdcVec3d Position;

        // Quaternion vector part (x, y, z); w = +sqrt(max(0, 1 - |v|^2)), sender canonicalises w >= 0 — an all-zero struct is the
        // identity. Encoded/decoded by Fdp.Toolkit.Replication.Components.QuaternionWireCodec (the one helper NED uses too).
        public BdcOrientationQuat OrientationXyz;

        // Linear velocity [m/s], local ENU.
        public BdcVec3f Velocity;

        // Angular velocity [rad/s] about the local ENU axes.
        public BdcVec3f AngularVelocity;
    }
}
