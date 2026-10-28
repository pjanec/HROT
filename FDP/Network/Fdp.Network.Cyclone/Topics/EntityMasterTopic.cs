using CycloneDDS.Schema;

namespace Fdp.Network.Cyclone.Topics
{
    /// <summary>
    /// DDS topic for entity master descriptor synchronization.
    /// Contains ownership and type information for networked entities.
    /// Uses reliable, transient-local QoS for critical descriptor data.
    /// </summary>
    [DdsTopic]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 100,
        AutodisposeUnregisteredInstances = false
    )]
    public partial struct EntityMasterTopic
    {
        /// <summary>Network-wide unique entity identifier</summary>
        [DdsKey] public long EntityId;
        
        /// <summary>Owner application ID</summary>
        public NetworkAppId OwnerId;
        
        /// <summary>DIS entity type as packed ulong</summary>
        public ulong DisTypeValue;
        
        /// <summary>Entity flags and metadata</summary>
        public int Flags;
        
        /// <summary>
        /// Internal TKB blueprint type ID. Used by the receiving node to look up the template
        /// and apply it to the ghost entity during promotion. Zero means unset (legacy).
        /// </summary>
        public long TkbTypeValue;
    }
}
