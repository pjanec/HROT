namespace Hrot.ScenarioEditor.Adapters;

/// <summary>
/// Named constants for <see cref="NedVisualizerAdapter"/> rendering geometry,
/// asset paths, and scaling factors (Â§CODE-STANDARDS Â§1 â€” no magic numbers).
/// </summary>
public static class NedVisualizerAdapterConstants
{
    // â”€â”€ Asset loading â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Base directory (relative to working directory) searched for symbol texture
    /// files with the pattern <c>{textureName}.png</c>.
    /// </summary>
    public const string AssetBasePath = "assets/symbols/";

    // â”€â”€ Fallback rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Radius of the fallback colored circle drawn when no texture file is
    /// available, in pixels.
    /// </summary>
    public const int FallbackCircleRadiusPx = 10;

    // â”€â”€ Label â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Pixel offset below the icon centre where the entity label is drawn.</summary>
    public const int LabelOffsetPx = 20;

    /// <summary>Font size for entity label text.</summary>
    public const int LabelFontSize = 10;

    // â”€â”€ Selection highlight â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Radius of the selection ring drawn around a selected entity, in pixels.
    /// </summary>
    public const int SelectionRadiusPx = 20;

    // â”€â”€ Damage bar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Full width of the damage bar in pixels.</summary>
    public const int DamageBarWidth = 30;

    /// <summary>Height of the damage bar in pixels.</summary>
    public const int DamageBarHeight = 6;

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
    /// Damage percentage (0â€“100) below which the damage bar fill is green
    /// (entity is healthy).
    /// </summary>
    public const float DamageGreenThreshold = 30f;

    /// <summary>
    /// Damage percentage (0â€“100) below which the damage bar fill is yellow
    /// (entity is damaged but not critical). Values at or above this threshold
    /// render as red.
    /// </summary>
    public const float DamageYellowThreshold = 70f;

    // â”€â”€ Texture scaling â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Draw scale applied to symbol textures at LOD 0 (full detail) and
    /// LOD 1 (simplified).
    /// </summary>
    public const float DefaultScale = 1.0f;

    /// <summary>
    /// Draw scale applied to symbol textures at LOD 2 (icon-only, very zoomed out).
    /// </summary>
    public const float LodIconOnlyScale = 0.5f;

    // â”€â”€ Hit testing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Pick hit-test radius in world units, calculated as
    /// <see cref="FallbackCircleRadiusPx"/> pixels converted at the default
    /// initial zoom (0.5 px/m — 2 m/px).
    /// At 0.5 px/m this is 10 px / 0.5 = 20 m.
    /// </summary>
    public const float HitRadiusWorldUnits =
        (float)FallbackCircleRadiusPx / 0.5f;
}
