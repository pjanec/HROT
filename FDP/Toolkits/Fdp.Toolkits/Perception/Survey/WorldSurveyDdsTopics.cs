using CycloneDDS.Schema;

namespace Fdp.Toolkit.Perception.Survey.Topics
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S4 — a WorldSurvey sensor's answer, from the node that solved it to the brain. Keyed like <c>EqsResult</c> and
    /// <c>DangerAreaResult</c> (the unit, the sensor's part id); the answer rides as JSON (WS-11: rooms, portals and rays are lists of
    /// lists, and the payload class is the one the brain reads). 📄 docs/DESIGN_World_Survey_Sensor.md §3.
    /// </summary>
    [DdsTopic]
    [DdsIdlFile("hrot-eqs-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct WorldSurveyResultTopic
    {
        /// <summary>Network id of the unit.</summary>
        [DdsKey] public long ParentNetworkId;
        /// <summary>The sensor child's part id.</summary>
        [DdsKey] public int LocalChildIndex;
        /// <summary>The sensor's epoch at solve time.</summary>
        public uint Epoch;
        /// <summary>The solver's tick.</summary>
        public uint RefreshTick;
        /// <summary>The content hash of <see cref="PayloadJson"/> (as the solver computed it).</summary>
        public long ContentHash;
        /// <summary>The answer — <c>WorldSurveyPayload</c> as JSON.</summary>
        [DdsManaged] public string PayloadJson;
    }
}
