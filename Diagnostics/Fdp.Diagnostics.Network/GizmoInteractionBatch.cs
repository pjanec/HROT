using CycloneDDS.Schema;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    /// <summary>
    /// DDS topic that carries a single gizmo interaction event across the network.
    /// One record per event (not batched) because interactions are low-frequency.
    /// </summary>
    [DdsTopic("GizmoInteractionBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable,
            Durability  = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast,
            HistoryDepth = 10)]
    public partial struct GizmoInteractionBatch
    {
        [DdsKey] public byte   SourceNodeId;
        [DdsKey] public uint   SequenceNumber;

        public GizmoInteractionEventKind Kind;

        // PickToken fields (blittable breakdown of Entity + SubElementId)
        public int    PickEntityIndex;
        public ushort PickEntityGeneration;
        public ushort PickSubElementId;

        // WorldPos (present for Started/DragUpdate/Commit; zero for Cancel)
        public float WorldX;
        public float WorldY;
        public float WorldZ;
    }
}
