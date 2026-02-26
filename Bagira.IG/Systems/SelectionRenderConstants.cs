namespace Bagira.IG.Systems;

/// <summary>
/// Named constants for <see cref="SelectionRenderSystem"/>
/// (§CODE-STANDARDS §1 — no magic numbers in production code).
/// </summary>
public static class SelectionRenderConstants
{
    /// <summary>
    /// Display name of the selection-ring layer shown in the UI layer panel.
    /// </summary>
    public const string LayerName = "SelectionRings";

    /// <summary>
    /// Layer bit-index value that disables bitmask visibility filtering, keeping
    /// selection rings always drawn.
    /// </summary>
    public const int AlwaysVisibleLayerBitIndex = -1;

    // ── Primary selection fill colour (green, semi-transparent) ──────────────

    /// <summary>Red channel of the primary-selection fill circle.</summary>
    public const byte PrimaryFillR = 0;

    /// <summary>Green channel of the primary-selection fill circle.</summary>
    public const byte PrimaryFillG = 255;

    /// <summary>Blue channel of the primary-selection fill circle.</summary>
    public const byte PrimaryFillB = 0;

    /// <summary>
    /// Alpha channel of the primary-selection fill circle.
    /// Kept intentionally translucent so the entity icon underneath remains visible.
    /// </summary>
    public const byte PrimaryFillAlpha = 50;
}
