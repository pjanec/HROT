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

    /// <summary>
    /// ⭐⭐⭐ <b>The DOCUMENT-DRIVEN perspectives — the ones a restart must NEVER restore.</b>
    /// 📄 <c>docs/UX/UX_Feature_Perspective_Restore.md</c> §2, whose ruling is verbatim:
    /// <i>"They are a function of the open document … and no document survives a restart."</i>
    ///
    /// <para>⛔⛔ <b>Restoring one lands the user in an EMPTY graph workspace.</b> 📌 That design calls
    /// falling back to the scenario perspective <i>"the desired behaviour, not the bug"</i>.</para>
    ///
    /// <para>⭐⭐ <b>Derived from <see cref="ToPerspectiveName"/>, never re-spelled</b> — ⛔ a second
    /// literal list is a second thing to keep in step. ⚠ <b>Deliberately NOT "every
    /// <see cref="AssetKind"/>"</b>: 📐 <c>Blackboard</c> and <c>Utility</c> register no perspective at
    /// all, and <c>Scenario</c> is the DURABLE one this set exists to protect.</para>
    /// </summary>
    public static IReadOnlyList<string> DocumentDrivenPerspectiveNames { get; } = new[]
    {
        AssetKind.BTree.ToPerspectiveName(),
        AssetKind.Hsm.ToPerspectiveName(),
        AssetKind.Blueprint.ToPerspectiveName(),
    };
}
