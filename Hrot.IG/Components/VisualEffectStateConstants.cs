namespace Hrot.IG.Components;

/// <summary>
/// Named constants for <see cref="VisualEffectState"/> component and its renderers
/// (§CODE-STANDARDS §1 — no magic numbers in production code).
/// </summary>
public static class VisualEffectStateConstants
{
    // ── Explosion effect ──────────────────────────────────────────────────────

    /// <summary>Total lifetime of an explosion effect in seconds.</summary>
    public const float ExplosionDurationSeconds = 2.0f;

    /// <summary>Initial circle radius (world units) for an explosion.</summary>
    public const float ExplosionInitialScale = 5.0f;

    /// <summary>Red channel of the explosion colour (orange).</summary>
    public const byte ExplosionColorR = 255;

    /// <summary>Green channel of the explosion colour (orange).</summary>
    public const byte ExplosionColorG = 165;

    /// <summary>Blue channel of the explosion colour (orange).</summary>
    public const byte ExplosionColorB = 0;

    /// <summary>Alpha channel of the explosion colour (fully opaque at spawn).</summary>
    public const byte ExplosionColorA = 255;

    // ── Tracer effect ──────────────────────────────────────────────────────────

    /// <summary>Total lifetime of a tracer line effect in seconds.</summary>
    public const float TracerDurationSeconds = 0.3f;

    /// <summary>Scale factor for the tracer line (width multiplier).</summary>
    public const float TracerScale = 1.0f;

    /// <summary>Red channel of the tracer colour (yellow).</summary>
    public const byte TracerColorR = 255;

    /// <summary>Green channel of the tracer colour (yellow).</summary>
    public const byte TracerColorG = 255;

    /// <summary>Blue channel of the tracer colour (yellow).</summary>
    public const byte TracerColorB = 0;

    /// <summary>Alpha channel of the tracer colour (fully opaque at spawn).</summary>
    public const byte TracerColorA = 255;

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <summary>Line width in pixels used when drawing tracer effects.</summary>
    public const float EffectLineWidthPx = 2.0f;
}
