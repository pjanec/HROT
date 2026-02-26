namespace Bagira.IG.Components;

/// <summary>
/// Named constants for the <see cref="CullingState"/> component and
/// <see cref="Bagira.IG.Systems.MapCullingSystem"/> zoom-to-LOD thresholds.
///
/// Centralised here so that a single edit propagates to all component definitions,
/// system logic, and test assertions (§CODE-STANDARDS §1 — no magic numbers).
/// </summary>
public static class CullingStateConstants
{
    // ── LOD levels ────────────────────────────────────────────────────────────

    /// <summary>
    /// Full-detail rendering: icon, label, damage bar, and sensor overlays.
    /// Assigned when zoom ≥ <see cref="LodSimplifiedZoomThreshold"/>.
    /// </summary>
    public const byte LodFull = 0;

    /// <summary>
    /// Simplified rendering: icon and label only.
    /// Assigned when <see cref="LodIconOnlyZoomThreshold"/> ≤ zoom &lt; <see cref="LodSimplifiedZoomThreshold"/>.
    /// </summary>
    public const byte LodSimplified = 1;

    /// <summary>
    /// Icon-only rendering at <see cref="Bagira.IG.Adapters.SstVisualizerAdapterConstants.LodIconOnlyScale"/>.
    /// Assigned when zoom &lt; <see cref="LodIconOnlyZoomThreshold"/>.
    /// </summary>
    public const byte LodIconOnly = 2;

    // ── Zoom thresholds ───────────────────────────────────────────────────────

    /// <summary>
    /// Camera zoom (px/m) below which <see cref="LodIconOnly"/> is assigned.
    /// 0.1 px/m = 10 m/px — very zoomed out.
    /// </summary>
    public const float LodIconOnlyZoomThreshold = 0.1f;

    /// <summary>
    /// Camera zoom (px/m) below which <see cref="LodSimplified"/> is assigned
    /// (but at or above <see cref="LodIconOnlyZoomThreshold"/>).
    /// 0.5 px/m = 2 m/px — moderately zoomed out.
    /// </summary>
    public const float LodSimplifiedZoomThreshold = 0.5f;
}
