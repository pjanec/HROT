namespace Hrot.UI.Common.Panels;

/// <summary>
/// Shared constants for UI panels in <see cref="Hrot.UI.Common"/>.
/// </summary>
public static class PanelConstants
{
    // ── ConfigPanel ───────────────────────────────────────────────────────────

    /// <summary>Minimum value for the icon-scale slider.</summary>
    public const float IconScaleMin = 0.5f;

    /// <summary>Maximum value for the icon-scale slider.</summary>
    public const float IconScaleMax = 2.0f;

    /// <summary>Default icon scale shown when the Config panel is first opened.</summary>
    public const float IconScaleDefault = 1.0f;

    // ── SpawnerPanel ──────────────────────────────────────────────────────────

    /// <summary>ImGui input-text buffer size (in characters) for filter / search text fields.</summary>
    public const int FilterTextMaxLength = 256;

    // ── MissionPanel ──────────────────────────────────────────────────────────

    /// <summary>Maximum number of characters allowed in the behavior-params JSON editor.</summary>
    public const int MissionBehaviorParamsMaxLength = 2048;

    /// <summary>
    /// Default travel speed (m/s) injected into a <c>MoveToLocation</c> params JSON
    /// when the operator uses the "Pick Location" map-pick workflow.
    /// </summary>
    public const float MoveToLocationDefaultSpeed = 15f;

    /// <summary>
    /// Default arrival radius (metres) injected into a <c>MoveToLocation</c> params JSON
    /// when the operator uses the "Pick Location" map-pick workflow.
    /// </summary>
    public const float MoveToLocationDefaultArrivalRadius = 50f;

    /// <summary>Number of text lines shown in the behavior-params editor.</summary>
    public const int MissionBehaviorParamsEditorLines = 4;

    /// <summary>Fallback error message stored in the conflict alert when the ACK provides no message.</summary>
    public const string VersionConflictErrorMessage = "ERR_VERSION_CONFLICT";

    /// <summary>Filter preset name for the <c>road_graphs</c> map layer.</summary>
    public const string FilterPresetRoadGraphs = "road_graphs";
}
