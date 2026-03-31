namespace Hrot.IG.Components;

/// <summary>
/// Named constants for <see cref="HistoryTrail"/> component and its rendering
/// (§CODE-STANDARDS §1 — no magic numbers in production code).
/// </summary>
public static class HistoryTrailConstants
{
    // ── Buffer sizing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum number of XY position samples stored per entity trail.
    /// The circular buffer silently discards the oldest sample when this limit is
    /// exceeded, bounding memory at <c>2 × MaxTrailPoints × 4</c> bytes per entity.
    /// </summary>
    public const int MaxTrailPoints = 64;

    // ── Timing defaults ───────────────────────────────────────────────────────

    /// <summary>
    /// Default interval in seconds between consecutive position samples when
    /// none is supplied at construction.
    /// </summary>
    public const float DefaultSampleIntervalSeconds = 0.5f;

    // ── Trail rendering colours ───────────────────────────────────────────────

    /// <summary>Red channel of the cyan trail line.</summary>
    public const byte TrailColorR = 0;

    /// <summary>Green channel of the cyan trail line.</summary>
    public const byte TrailColorG = 255;

    /// <summary>Blue channel of the cyan trail line.</summary>
    public const byte TrailColorB = 255;

    /// <summary>Alpha channel of the trail line (50 % transparency = 128 / 255).</summary>
    public const byte TrailColorA = 128;

    /// <summary>Line width in pixels used when drawing the trail polyline.</summary>
    public const float TrailLineWidthPx = 2.0f;
}
