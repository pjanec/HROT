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
        /// <summary>Network ID of the parent agent entity (first key field of the compound key).</summary>
        [DdsKey] public long ParentNetworkId;
        /// <summary>PartMetadata.InstanceId of the child sensor; 0 for legacy single-sensor (second key field of the compound key).</summary>
        [DdsKey] public int LocalChildIndex;
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
        /// <summary>Score change threshold for the ScoreDelta publish policy.</summary>
        public float ScoreDeltaThreshold;
        /// <summary>Network ID of the entity in context slot 0 (Self/Observer). 0 = not assigned.</summary>
        public long ContextSlot0NetworkId;
        /// <summary>Network ID of the entity in context slot 1 (Target). 0 = not assigned.</summary>
        public long ContextSlot1NetworkId;
        /// <summary>Network ID of the entity in context slot 2 (Leader). 0 = not assigned.</summary>
        public long ContextSlot2NetworkId;
        /// <summary>
        /// ⭐ <c>CE-486</c> — the sensor has ended (non-key). A child sensor's instance is never disposed while its parent
        /// lives; the Brain writes the sensor's last config with this set, and the Muscle's carrier stops solving.
        /// </summary>
        public bool Suspended;
        /// <summary>
        /// ⭐ <c>CE-3002</c> / <c>R-179</c> — the node that solves this sensor and publishes its result (non-key). The
        /// Brain picks the least-loaded Perception node once per sensor; every node records it as the owner of result
        /// part <see cref="LocalChildIndex"/>, and only that node builds a carrier. <c>0</c> = not named: every
        /// Perception node solves and the ownership record gates the publish (the pre-<c>R-179</c> behaviour).
        /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.8.
        /// </summary>
        public int SolverNodeId;

        /// <summary>
        /// ⭐ CE-3036 / R-186 M′ — the format of <see cref="ConfigJson"/> (<c>SensorConfigCodec.KindSensorEntry</c>); empty
        /// when the sample carries no per-kind config. Non-key; sent only on spawn / change.
        /// </summary>
        [DdsManaged] public string ConfigKind;

        /// <summary>⭐ CE-3036 — the sensor's per-kind config: the TKB's own <c>SensorEntryDto</c> as JSON. Empty = none.</summary>
        [DdsManaged] public string ConfigJson;

        /// <summary>
        /// ⭐ CE-3036 / R-187 N′ — for a TKB sensor (part id ≥ 1000): true = this sample OVERRIDES the TKB default, false =
        /// back to the default. A TKB sensor is otherwise never on the wire (R-185 K).
        /// </summary>
        public bool Override;

        /// <summary>⭐ <c>CE-3063</c> ③ — <see cref="EqsSensor.ContextPoint1"/> (a heard contact's position standing in for slot 1).</summary>
        public float ContextPoint1X;
        /// <summary>See <see cref="ContextPoint1X"/>.</summary>
        public float ContextPoint1Y;
        /// <summary>See <see cref="ContextPoint1X"/>.</summary>
        public float ContextPoint1Z;
        /// <summary>⭐ <c>CE-3063</c> — <see cref="EqsSensor.ContextPointMask"/>.</summary>
        public byte ContextPointMask;
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
        /// <summary>World-space altitude (Sim Z-up). 3D Cognitive Spatial Awareness promotion (P3D-202).</summary>
        public float PositionZ;
        /// <summary>Final computed score.</summary>
        public float Score;
        /// <summary>Result flags (e.g., HasLOSToContext).</summary>
        public ushort Flags;
        /// <summary>
        /// Parallel bitset indicating which bits in <see cref="Flags"/> were actually
        /// computed by the template's tests.
        /// </summary>
        public ushort FlagsMeaningful;
        /// <summary>⭐ <c>CE-3135</c> — <see cref="EqsResult.Stance"/> (<c>StanceId + 1</c>, 0 = none).</summary>
        public byte Stance;
        /// <summary>⭐ <c>CE-3158</c> G1 — <see cref="EqsResult.Kind"/> (<c>CoverKind + 1</c>, 0 = not a cover point).</summary>
        public byte Kind;
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
        /// <summary>Network ID of the parent agent (first key field of the compound key).</summary>
        [DdsKey] public long ParentNetworkId;
        /// <summary>PartMetadata.InstanceId of the child sensor; 0 for legacy single-sensor (second key field of the compound key).</summary>
        [DdsKey] public int LocalChildIndex;
        /// <summary>Sensor epoch at solve time. Brain discards stale deliveries.</summary>
        public uint Epoch;
        /// <summary>Simulation tick at which the solver completed this evaluation.</summary>
        public uint RefreshTick;
        /// <summary>Ranked result entries. Managed field — allocated by the egress translator.</summary>
        [DdsManaged] public List<EqsResultEntry> Results;
    }
}
