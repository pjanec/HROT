namespace Hrot.Editor.AiShared;

/// <summary>
/// Canonical mapping from <see cref="AssetKind"/> to the perspective name registered
/// in the window manager. Use this in BOTH directions:
/// <list type="bullet">
///   <item>Forward: <c>AiDocumentManager.Activate</c> → <c>_perspectiveSwitchCallback</c>.</item>
///   <item>Reverse: <c>WindowManagerPerspectiveSwitcher.OnPerspectiveChanged</c> matching.</item>
/// </list>
/// </summary>
/// <remarks>
/// The mapping exists because <c>AssetKind.Hsm.ToString()</c> = <c>"Hsm"</c> but the
/// HSM perspective is registered and displayed as <c>"HSM"</c>.  This helper makes the
/// canonical names explicit and removes the casing fragility (BATCH-10 Bug #3).
/// </remarks>
public static class AssetKindExtensions
{
    /// <summary>
    /// Returns the canonical perspective name for the given <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">The asset kind.</param>
    /// <returns>
    ///   "BTree" for <see cref="AssetKind.BTree"/>,
    ///   "HSM" for <see cref="AssetKind.Hsm"/>,
    ///   "Blueprint" for <see cref="AssetKind.Blueprint"/>,
    ///   or <c>kind.ToString()</c> for other values (Blackboard, Utility).
    /// </returns>
    public static string ToPerspectiveName(this AssetKind kind) => kind switch
    {
        AssetKind.BTree     => "BTree",
        AssetKind.Hsm       => "HSM",
        AssetKind.Blueprint => "Blueprint",
        _                   => kind.ToString(),
    };
}
