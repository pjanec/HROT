using System.Numerics;

namespace Hrot.Editor.AiShared.Comparison.Rendering;

/// <summary>
/// Maps comparison severity strings to display colors and kind strings to glyph labels.
/// See design section 6.4.
/// </summary>
public static class ComparisonStyleMap
{
    // Neutral fallback color for unknown severities.
    private static readonly Vector4 FallbackColor = new(0.5f, 0.5f, 0.5f, 0.6f);

    /// <summary>
    /// Returns the RGBA color for a given severity string (case-insensitive).
    /// Unknown severities return a neutral gray.
    /// </summary>
    public static Vector4 ColorForSeverity(string severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "cosmetic"     => new Vector4(0.5f, 0.5f, 0.5f, 0.6f),
            "tuning"       => new Vector4(0.3f, 0.5f, 1.0f, 1.0f),
            "feature"      => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
            "removal"      => new Vector4(0.9f, 0.2f, 0.2f, 1.0f),
            "behavior"     => new Vector4(1.0f, 0.55f, 0.1f, 1.0f),
            "intent_shift" => new Vector4(1.0f, 0.55f, 0.1f, 1.0f),
            _              => FallbackColor,
        };
    }

    /// <summary>
    /// Returns the glyph string for a given kind string (case-insensitive).
    /// Unknown kinds return "?".
    /// </summary>
    public static string GlyphForKind(string kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "node_added"          => "+",
            "node_removed"        => "-",
            "node_modified"       => "~",
            "variable_added"      => "+v",
            "variable_removed"    => "-v",
            "variable_renamed"    => ">>>",
            "variable_retyped"    => "[]",
            "connection_changed"  => "~>",
            "comment_changed"     => "\"",
            "intent_shift"        => "!!",
            _                     => "?",
        };
    }
}
