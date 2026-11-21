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

    /// <summary>Filter preset name for the <c>road_graphs</c> map layer.</summary>
    public const string FilterPresetRoadGraphs = "road_graphs";
}
