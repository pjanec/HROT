using CycloneDDS.Schema;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    // Publishes the current UI state of all gizmo settings as a JSON document,
    // enabling remote editors to display and modify gizmo configuration.
    [DdsTopic("GizmoUiState")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GizmoUiState
    {
        [DdsKey] public uint GizmoInstanceId;
        [DdsManaged] public string EditDocumentJson;
    }
}
