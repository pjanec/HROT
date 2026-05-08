using CycloneDDS.Schema;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.IG.Gizmos
{
    // Announces the IG node's gizmo rendering capabilities to remote subscribers
    // so they can configure their requests appropriately.
    [DdsTopic("IGCapabilitiesAnnounce")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct IGCapabilitiesAnnounce
    {
        [DdsKey] public uint NodeId;
        public PipelineTarget SupportedTargets;
        public ushort SupportedLayerMask;
        public uint SupportedShapeMask;
        [DdsManaged] public string LayerNamesJson;
        // JSON array of gizmo type names registered by this IG instance.
        // "[]" for dumb-terminal IG (no local gizmo plugins after GZ038).
        // MUST NOT be conflated with LayerNamesJson (which describes the layer folder hierarchy).
        [DdsManaged] public string RegisteredGizmosJson;
    }
}
