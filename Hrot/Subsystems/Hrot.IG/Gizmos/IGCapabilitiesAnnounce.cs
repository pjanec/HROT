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
        public byte SupportedShapes;
        [DdsManaged] public string LayerNamesJson;
    }
}
