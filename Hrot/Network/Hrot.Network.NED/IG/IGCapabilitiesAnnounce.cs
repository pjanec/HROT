using CycloneDDS.Schema;

namespace Hrot.IG.Gizmos
{
    // Announces the IG node's gizmo rendering capabilities to remote subscribers
    // so they can configure their requests appropriately.
    [DdsTopic]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    public partial struct IGCapabilitiesAnnounce
    {
        [DdsKey] public uint NodeId;
        public PipelineTargetWire SupportedTargets;
        public ushort SupportedLayerMask;
        public uint SupportedShapeMask;
        [DdsManaged] public string LayerNamesJson;
        // JSON array of gizmo type names registered by this IG instance.
        // "[]" for dumb-terminal IG (no local gizmo plugins after GZ038).
        // MUST NOT be conflated with LayerNamesJson (which describes the layer folder hierarchy).
        [DdsManaged] public string RegisteredGizmosJson;
    }

    /// <summary>
    /// ⭐ CE-4208 (R-134) — the WIRE twin of the engine's <c>Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget</c> (GizmoMap.Contracts):
    /// same member names, same values, same underlying type. Converted at the publisher / receiver, never cast on the engine path.
    /// A rail pins every engine member to an equal-valued member here.
    /// </summary>
    [System.Flags]
    public enum PipelineTargetWire : byte
    {
        None       = 0,
        Map2D      = 1,
        Viewport3D = 2,
        NodeGraph  = 4,
        All        = 7,
        FlatOnly   = 8,
    }
}
