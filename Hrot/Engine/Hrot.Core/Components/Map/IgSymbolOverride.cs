using Hrot.Map.Definitions;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// ECS class component caching the per-entity visual override published by the ExCon
/// via the DDS <c>MapEntitySymbol</c> topic.
///
/// Applied as the Layer-2 input to <c>StyleResolutionSystem</c>, sitting between the
/// TKB default (Layer 1) and operator user config (Layer 3).
///
/// Populated by a translator when a <c>MapEntitySymbol</c> DDS sample is received.
/// Using a class component (Tier 2) avoids the unmanaged-struct constraint
/// that prevents storing string-bearing DDS structs directly in Tier-1 tables
/// (see IG-DEBT-008).
/// </summary>
[ComponentId(HrotComponentIds.IgSymbolOverride)]
[DataPolicy(DataPolicy.NoScenario)]
// ⭐ CE-4046 — an immutable record: a change is a new value (`with`) + Set, so the recorder sees it (docs/DESIGN_Replay_Census_H0.md §2)
public record IgSymbolOverride
{
    // ── Known StyleSetId tokens ───────────────────────────────────────────────
    // StyleResolutionSystem.ResolveAffiliation compares StyleSetId against these
    // constants (case-insensitive) to derive ForceId without allocating.

    /// <summary>StyleSetId value that forces <see cref="ForceId.Hostile"/> affiliation.</summary>
    public const string StyleSetHostile = "hostile";

    /// <summary>StyleSetId value that forces <see cref="ForceId.Friend"/> affiliation.</summary>
    public const string StyleSetFriend = "friendly";

    /// <summary>StyleSetId value that forces <see cref="ForceId.Neutral"/> affiliation.</summary>
    public const string StyleSetNeutral = "neutral";

    /// <summary>StyleSetId value that forces <see cref="ForceId.Neutral"/> affiliation.</summary>
    public const string StyleSetUnknown = "unknown";

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>
    /// Named style preset sourced from <c>MapEntitySymbol.StyleSetId</c>.
    /// Known values: <see cref="StyleSetHostile"/>, <see cref="StyleSetFriend"/>,
    /// <see cref="StyleSetNeutral"/>, <see cref="StyleSetUnknown"/>.
    /// <c>null</c> or unrecognised values leave the TKB default intact.
    /// </summary>
    public string? StyleSetId { get; init; }

    /// <summary>
    /// Optional texture/symbol-code override.
    /// Overrides <c>VisualData.SymbolCode</c> when non-null and non-empty.
    /// </summary>
    public string? TextureOverride { get; init; }

    /// <summary>
    /// Optional label-text override.
    /// Overrides the TKB default label when non-null and non-empty.
    /// </summary>
    public string? LabelOverride { get; init; }

    /// <summary>
    /// When <c>true</c>, the history-trail renderer should draw this entity's movement trail.
    /// Sourced from ExCon map configuration.
    /// </summary>
    public bool ShowHistory { get; init; }
}
