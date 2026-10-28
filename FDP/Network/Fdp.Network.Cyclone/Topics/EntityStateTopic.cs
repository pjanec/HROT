using CycloneDDS.Schema;

namespace Fdp.Network.Cyclone.Topics
{
    /// <summary>
    /// DDS topic for entity state updates (position, velocity, etc.).
    /// High-frequency updates using best-effort QoS for performance.
    /// </summary>
    [DdsTopic]
    [DdsQos(
        Reliability = DdsReliability.BestEffort,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1,
        AutodisposeUnregisteredInstances = false
    )]
    public partial struct EntityStateTopic
    {
        /// <summary>Network-wide unique entity identifier</summary>
        [DdsKey] public long EntityId;
        
        /// <summary>X position in world coordinates</summary>
        public double PositionX;
        
        /// <summary>Y position in world coordinates</summary>
        public double PositionY;
        
        /// <summary>Z position in world coordinates</summary>
        public double PositionZ;
        
        /// <summary>X velocity component</summary>
        public float VelocityX;
        
        /// <summary>Y velocity component</summary>
        public float VelocityY;
        
        /// <summary>Z velocity component</summary>
        public float VelocityZ;
        
        /// <summary>Orientation quaternion X</summary>
        public float OrientationX;
        
        /// <summary>Orientation quaternion Y</summary>
        public float OrientationY;
        
        /// <summary>Orientation quaternion Z</summary>
        public float OrientationZ;
        
        /// <summary>Orientation quaternion W</summary>
        public float OrientationW;
        
        /// <summary>Timestamp of this state update</summary>
        public long Timestamp;
    }
}
