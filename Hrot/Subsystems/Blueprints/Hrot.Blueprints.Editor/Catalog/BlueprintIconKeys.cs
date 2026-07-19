using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Editor.Catalog;

/// <summary>
/// Punch-list #9: maps a blueprint header's <c>Dispatch</c> + <c>Primitive.Intent</c> to the
/// Open-Asset picker icon key, so Action / Condition / Function blueprints are visually distinct
/// (they all share <see cref="AssetKind.Blueprint"/>). Header-only — no full-asset load.
/// </summary>
internal static class BlueprintIconKeys
{
    /// <summary>
    /// Resolves the icon key from the (header) dispatch and primitive-intent strings.
    /// <list type="bullet">
    ///   <item><c>Library</c> dispatch → Function.</item>
    ///   <item><c>AiPrimitive</c> + <c>Condition</c> intent → Condition.</item>
    ///   <item><c>AiPrimitive</c> + <c>Action</c> intent → Action.</item>
    ///   <item>anything else (Instance, unknown) → <see langword="null"/> ⇒ caller uses the kind default.</item>
    /// </list>
    /// </summary>
    public static string? ForHeader(string? dispatch, string? intent)
    {
        if (string.Equals(dispatch, "Library", StringComparison.OrdinalIgnoreCase))
            return AssetKindIcons.BlueprintFunctionIconKey;

        if (string.Equals(dispatch, "AiPrimitive", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(intent, "Condition", StringComparison.OrdinalIgnoreCase))
                return AssetKindIcons.BlueprintConditionIconKey;
            if (string.Equals(intent, "Action", StringComparison.OrdinalIgnoreCase))
                return AssetKindIcons.BlueprintActionIconKey;
        }

        return null;
    }
}
