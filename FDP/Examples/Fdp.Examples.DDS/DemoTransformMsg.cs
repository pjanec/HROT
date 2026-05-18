using CycloneDDS.Schema;

namespace Fdp.Examples.DDS
{
    /// <summary>
    /// Replicates <c>SimTransform</c> in flat Cartesian space (no geodetic coordinates).
    /// Used by the DistributedTank and UrbanCombat (new) scenarios.
    /// </summary>
    [DdsTopic("FDP.Demo_Transform")]
    public partial struct DemoTransformMsg
    {
        /// <summary>Unique long-lived network identifier for the entity.</summary>
        [DdsKey]
        public long NetworkId;

        /// <summary>Position X (metres, Cartesian).</summary>
        public float PosX;

        /// <summary>Position Y (metres, Cartesian).</summary>
        public float PosY;

        /// <summary>Position Z (metres, Cartesian).</summary>
        public float PosZ;

        /// <summary>Orientation quaternion X component.</summary>
        public float RotX;

        /// <summary>Orientation quaternion Y component.</summary>
        public float RotY;

        /// <summary>Orientation quaternion Z component.</summary>
        public float RotZ;

        /// <summary>Orientation quaternion W component.</summary>
        public float RotW;
    }
}
