namespace Hrot.IG.Adapters;

/// <summary>
/// Named constants for <see cref="NedVisualizerAdapter"/> rendering geometry,
/// asset paths, and scaling factors (§CODE-STANDARDS §1 — no magic numbers).
/// </summary>
public static class NedVisualizerAdapterConstants
{
    // ── Asset loading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Base directory (relative to working directory) searched for symbol texture
    /// files with the pattern <c>{textureName}.png</c>.
    /// </summary>
    public const string AssetBasePath = "assets/symbols/";

    // ── Fallback rendering ────────────────────────────────────────────────────

    /// <summary>
    /// Radius of the fallback colored circle drawn when no texture file is
    /// available, in pixels.
    /// </summary>
    public const int FallbackCircleRadiusPx = 10;

    // ── Label ─────────────────────────────────────────────────────────────────

    /// <summary>Pixel offset below the icon centre where the entity label is drawn.</summary>
    public const int LabelOffsetPx = 20;

    /// <summary>Font size for entity label text.</summary>
    public const int LabelFontSize = 10;

    // ── Selection highlight ───────────────────────────────────────────────────

    /// <summary>
    /// Radius of the selection ring drawn around a selected entity, in pixels.
    /// </summary>
    public const int SelectionRadiusPx = 20;

    // ── Damage bar ────────────────────────────────────────────────────────────

    /// <summary>Full width of the damage bar in pixels.</summary>
    public const int DamageBarWidth = 30;

    /// <summary>Height of the damage bar in pixels.</summary>
    public const int DamageBarHeight = 4;

    /// <summary>
    /// Vertical offset above the icon centre at which the top edge of the
    /// damage bar is placed, in pixels.
    /// </summary>
    public const int DamageBarOffsetY = 25;

    /// <summary>
    /// Half-width used to horizontally center the damage bar over the icon.
    /// </summary>
    public const int DamageBarHalfWidth = DamageBarWidth / 2;

    /// <summary>
    /// Damage percentage (0–100) below which the damage bar fill is green
    /// (entity is healthy).
    /// </summary>
    public const float DamageGreenThreshold = 30f;

    /// <summary>
    /// Damage percentage (0–100) below which the damage bar fill is yellow
    /// (entity is damaged but not critical). Values at or above this threshold
    /// render as red.
    /// </summary>
    public const float DamageYellowThreshold = 70f;

    // ── Texture scaling ───────────────────────────────────────────────────────

    /// <summary>
    /// Draw scale applied to symbol textures at LOD 0 (full detail) and
    /// LOD 1 (simplified).
    /// </summary>
    public const float DefaultScale = 1.0f;

    /// <summary>
    /// Draw scale applied to symbol textures at LOD 2 (icon-only, very zoomed out).
    /// </summary>
    public const float LodIconOnlyScale = 0.5f;

    // ── Hit testing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pick hit-test radius in world units, calculated as
    /// <see cref="FallbackCircleRadiusPx"/> pixels converted at the default
    /// initial zoom (<see cref="IgCameraConstants.InitialZoom"/>).
    /// At 0.5 px/m this is 10 px / 0.5 = 20 m.
    /// </summary>
    public const float HitRadiusWorldUnits =
        (float)FallbackCircleRadiusPx / IgCameraConstants.InitialZoom;
}
