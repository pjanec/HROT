using CycloneDDS.Schema;

namespace ModuleHost.Network.Cyclone.Topics
{
    /// <summary>
    /// DDS topic for entity state updates (position, velocity, etc.).
    /// High-frequency updates using best-effort QoS for performance.
    /// </summary>
    [DdsTopic("SST_EntityState")]
    [DdsQos(
        Reliability = DdsReliability.BestEffort,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1
    )]
    public partial struct EntityStateTopic
    {
        /// <summary>Network-wide unique entity identifier</summary>
        [DdsKey, DdsId(0)] public long EntityId;
        
        /// <summary>X position in world coordinates</summary>
        [DdsId(1)] public double PositionX;
        
        /// <summary>Y position in world coordinates</summary>
        [DdsId(2)] public double PositionY;
        
        /// <summary>Z position in world coordinates</summary>
        [DdsId(3)] public double PositionZ;
        
        /// <summary>X velocity component</summary>
        [DdsId(4)] public float VelocityX;
        
        /// <summary>Y velocity component</summary>
        [DdsId(5)] public float VelocityY;
        
        /// <summary>Z velocity component</summary>
        [DdsId(6)] public float VelocityZ;
        
        /// <summary>Orientation quaternion X</summary>
        [DdsId(7)] public float OrientationX;
        
        /// <summary>Orientation quaternion Y</summary>
        [DdsId(8)] public float OrientationY;
        
        /// <summary>Orientation quaternion Z</summary>
        [DdsId(9)] public float OrientationZ;
        
        /// <summary>Orientation quaternion W</summary>
        [DdsId(10)] public float OrientationW;
        
        /// <summary>Timestamp of this state update</summary>
        [DdsId(11)] public long Timestamp;
    }
}
