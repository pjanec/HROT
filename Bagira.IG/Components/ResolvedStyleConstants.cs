namespace Bagira.IG.Components;

/// <summary>
/// Named constants for the <see cref="ResolvedStyle"/> component and
/// <see cref="Bagira.IG.Systems.StyleResolutionSystem"/>.
///
/// Centralised here so that a single edit propagates to all struct definitions,
/// system logic, and test assertions
/// (§CODE-STANDARDS §1 — no magic numbers in production code).
/// </summary>
public static class ResolvedStyleConstants
{
    // ── Fixed-buffer sizes ────────────────────────────────────────────────────

    /// <summary>Maximum byte length of the texture-name fixed buffer (null-terminated UTF-8).</summary>
    public const int TextureNameMaxBytes = 16;

    /// <summary>Maximum byte length of the label-text fixed buffer (null-terminated UTF-8).</summary>
    public const int LabelTextMaxBytes = 24;

    /// <summary>
    /// Cache-safety byte ceiling for <see cref="ResolvedStyle"/>.
    /// The struct must remain strictly below this threshold.
    /// </summary>
    public const int MaxStyleBytes = 64;

    // ── Affiliation tint colors (per task spec, RGBA channels) ───────────────

    // Friend → Blue
    /// <summary>Red channel for the Friend (blue-force) affiliation tint.</summary>
    public const byte FriendTintR = 0;
    /// <summary>Green channel for the Friend affiliation tint.</summary>
    public const byte FriendTintG = 100;
    /// <summary>Blue channel for the Friend affiliation tint.</summary>
    public const byte FriendTintB = 255;
    /// <summary>Alpha channel for the Friend affiliation tint.</summary>
    public const byte FriendTintA = 255;

    // Hostile → Red
    /// <summary>Red channel for the Hostile affiliation tint.</summary>
    public const byte HostileTintR = 255;
    /// <summary>Green channel for the Hostile affiliation tint.</summary>
    public const byte HostileTintG = 0;
    /// <summary>Blue channel for the Hostile affiliation tint.</summary>
    public const byte HostileTintB = 0;
    /// <summary>Alpha channel for the Hostile affiliation tint.</summary>
    public const byte HostileTintA = 255;

    // Neutral → Green
    /// <summary>Red channel for the Neutral affiliation tint.</summary>
    public const byte NeutralTintR = 0;
    /// <summary>Green channel for the Neutral affiliation tint.</summary>
    public const byte NeutralTintG = 255;
    /// <summary>Blue channel for the Neutral affiliation tint.</summary>
    public const byte NeutralTintB = 0;
    /// <summary>Alpha channel for the Neutral affiliation tint.</summary>
    public const byte NeutralTintA = 255;

    // Unknown → White (default / safe fallback)
    /// <summary>Red channel for the Unknown affiliation tint (white).</summary>
    public const byte UnknownTintR = 255;
    /// <summary>Green channel for the Unknown affiliation tint (white).</summary>
    public const byte UnknownTintG = 255;
    /// <summary>Blue channel for the Unknown affiliation tint (white).</summary>
    public const byte UnknownTintB = 255;
    /// <summary>Alpha channel for the Unknown affiliation tint (white, fully opaque).</summary>
    public const byte UnknownTintA = 255;

    // ── Damage range ──────────────────────────────────────────────────────────

    /// <summary>Minimum damage level: entity is fully healthy.</summary>
    public const float DamageMin = 0f;

    /// <summary>Maximum damage level: entity is fully destroyed.</summary>
    public const float DamageMax = 100f;
}
