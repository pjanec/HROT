using System.Collections.Generic;
using CycloneDDS.Schema;

namespace Fdp.Toolkit.Squad.DangerArea.Topics
{
    /// <summary>⭐ <c>CE-3072</c> B3 — one danger area on the wire (a <see cref="DangerAreaDescriptor"/> without its threat).</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-eqs-msgs")]
    public partial struct DangerAreaWire
    {
        public uint FeatureId;
        public byte Kind;
        public float CenterX;
        public float CenterY;
        public float CenterZ;
        public float ExtentX;
        public float ExtentY;
        public float AngleRad;
        public float ZFloor;
        public float ZCeiling;
        public float NearX;
        public float NearY;
        public float NearZ;
        public float FarX;
        public float FarY;
        public float FarZ;
        public float DistanceAlongRoute;
    }

    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 — a danger-area sensor's answer, from the node that solved it to the Brain. Keyed like
    /// <c>EqsResult</c> (the unit, the sensor's part id); its own topic because the ranked entry cannot carry an area.
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// </summary>
    [DdsTopic("DangerAreaResult")]
    [DdsIdlFile("hrot-eqs-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct DangerAreaResultTopic
    {
        /// <summary>Network id of the unit.</summary>
        [DdsKey] public long ParentNetworkId;
        /// <summary>The sensor child's part id.</summary>
        [DdsKey] public int LocalChildIndex;
        /// <summary>The sensor's epoch at solve time.</summary>
        public uint Epoch;
        /// <summary>The solver's tick.</summary>
        public uint RefreshTick;
        /// <summary>The areas, in route order.</summary>
        [DdsManaged] public List<DangerAreaWire> Areas;
    }
}
