using CycloneDDS.Schema;

namespace ModuleHost.Network.Cyclone.Topics
{
    /// <summary>
    /// DDS topic for entity master descriptor synchronization.
    /// Contains ownership and type information for networked entities.
    /// Uses reliable, transient-local QoS for critical descriptor data.
    /// </summary>
    [DdsTopic("SST_EntityMaster")]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 100
    )]
    public partial struct EntityMasterTopic
    {
        /// <summary>Network-wide unique entity identifier</summary>
        [DdsKey, DdsId(0)] public long EntityId;
        
        /// <summary>Owner application ID</summary>
        [DdsId(1)] public NetworkAppId OwnerId;
        
        /// <summary>DIS entity type as packed ulong</summary>
        [DdsId(2)] public ulong DisTypeValue;
        
        /// <summary>Entity flags and metadata</summary>
        [DdsId(3)] public int Flags;
    }
}
