using System;
using CycloneDDS.Schema;

namespace Fdp.Network.Cyclone.Topics
{
    /// <summary>
    /// SST ownership transfer message.
    /// Sent when descriptor ownership changes between nodes.
    /// </summary>
    [DdsTopic("SST_OwnershipUpdate")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll, HistoryDepth = 1)]
    public partial struct OwnershipUpdate
    {
        /// <summary>
        /// Network entity ID (not FDP entity).
        /// </summary>
        [DdsId(0)]
        public long EntityId;
        
        /// <summary>
        /// Descriptor type ID.
        /// Examples: 1=EntityState, 2=WeaponState, 0=EntityMaster
        /// </summary>
        [DdsId(1)]
        public long DescrTypeId;
        
        /// <summary>
        /// Descriptor instance ID (for multi-instance descriptors).
        /// Zero if descriptor type has single instance per entity.
        /// </summary>
        [DdsId(2)]
        public long InstanceId;
        
        /// <summary>
        /// New owner node ID.
        /// </summary>
        [DdsId(3)]
        public int NewOwner;
        
        /// <summary>
        /// Timestamp of the update (ms since epoch).
        /// </summary>
        [DdsId(4)]
        public long Timestamp;

        /// <summary>
        /// Node ID that originated the ownership update.
        /// </summary>
        [DdsId(5)]
        public int OriginNodeId;
    }
}
