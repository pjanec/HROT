using System.Collections.Generic;
using CycloneDDS.Schema;

namespace Fdp.Toolkit.Spatial.Eqs.Topics
{
    // ── Brain to Muscle: sensor configuration ────────────────────────────────────

    /// <summary>
    /// Wire-format struct for replicating <see cref="EqsSensor"/> parameters from the Brain
    /// to the Muscle node via CycloneDDS.
    /// Topic name is stable: wire compatibility depends on the exact string "EqsSensorConfig".
    /// </summary>
    [DdsTopic("EqsSensorConfig")]
    [DdsIdlFile("hrot-eqs-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EqsSensorConfigTopic
    {
        /// <summary>Network ID of the entity that owns the sensor (key field).</summary>
        [DdsKey] public long EntityId;
        /// <summary>FNV-1a 32-bit hash of the query template BlueprintId.</summary>
        public uint BlueprintId;
        /// <summary>Sensor version counter. Muscle resets evaluation state on mismatch.</summary>
        public uint Epoch;
        /// <summary>Search radius in world-space units.</summary>
        public float SearchRadius;
        /// <summary>Faction affiliation bitmask.</summary>
        public uint FactionFilter;
        /// <summary>Minimum threat score for the cheap LOS filter.</summary>
        public float ThreatThreshold;
        /// <summary>Publish policy byte (e.g., TopChanged, AlwaysPush).</summary>
        public byte PublishPolicy;
        /// <summary>Solver priority band byte (Critical, Normal, Low).</summary>
        public byte Priority;
    }

    // ── Muscle to Brain: ranked results ──────────────────────────────────────────

    /// <summary>
    /// Single ranked candidate in the DDS wire payload. Mirrors <see cref="EqsResult"/>
    /// but without the internal padding field.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-eqs-msgs")]
    public partial struct EqsResultEntry
    {
        /// <summary>Packed entity value. 0 = positional candidate. -1 = rejected (should not appear on wire).</summary>
        public long EntityId;
        /// <summary>World-space X coordinate.</summary>
        public float PositionX;
        /// <summary>World-space Y coordinate.</summary>
        public float PositionY;
        /// <summary>Final computed score.</summary>
        public float Score;
        /// <summary>Result flags (e.g., HasLOSToContext).</summary>
        public ushort Flags;
    }

    /// <summary>
    /// Wire-format struct for delivering ranked EQS results from the Muscle back to the Brain.
    /// Topic name is stable: wire compatibility depends on the exact string "EqsResult".
    /// </summary>
    [DdsTopic("EqsResult")]
    [DdsIdlFile("hrot-eqs-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EqsResultTopic
    {
        /// <summary>Network ID of the entity that owns the originating sensor (key field).</summary>
        [DdsKey] public long SensorNetworkId;
        /// <summary>Sensor epoch at solve time. Brain discards stale deliveries.</summary>
        public uint Epoch;
        /// <summary>Simulation tick at which the solver completed this evaluation.</summary>
        public uint RefreshTick;
        /// <summary>Ranked result entries. Managed field — allocated by the egress translator.</summary>
        [DdsManaged] public List<EqsResultEntry> Results;
    }
}
