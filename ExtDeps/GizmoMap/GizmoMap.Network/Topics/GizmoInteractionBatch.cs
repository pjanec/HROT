using CycloneDDS.Schema;

namespace GizmoMap.Network
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

        // PickToken fields (blittable breakdown of stable network ID + SubElementId)
        public long   PickAnchorId;
        public uint   PickSubElementId;
        public uint   PickStreamId;

        // WorldPos (present for Started/DragUpdate/Commit; zero for Cancel)
        public float WorldX;
        public float WorldY;
        public float WorldZ;

        // GZ047: coordinate space in which WorldPos is expressed (for DragUpdate and Commit).
        // Stored as raw byte to avoid requiring [DdsStruct] on CoordinateSpace (external type).
        // Cast to/from Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace at call sites.
        public byte Space;

        // Carries the integer id of the clicked context menu item when Kind == MenuAction; zero otherwise.
        public int ActionId;
    }
}
