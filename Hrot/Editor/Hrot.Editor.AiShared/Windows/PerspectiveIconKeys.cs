using Fdp.Presentation.WindowManager;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b>THE perspective → atlas-icon-key table. One list, every windowed host.</b>
///
/// <para>🔴🔴 <b>Why this type exists — measured <c>2026-08-27</c>, from the user's <c>--mode all</c>
/// check:</b> *"instead of graphical icons (as rendered in the editor) there are plain imgui buttons in
/// the toolbar"*. 📐 The mechanism is exact and it is in
/// <see cref="PerspectiveToolbarSection.BuildRadioModel"/>: an entry renders a `ToggleIcon` when
/// `GetPerspectiveIconKey(p)` resolves through the <see cref="Fdp.Presentation.Icons.IIconProvider"/>,
/// and <b>falls back to a text-label button when it does not</b>. ⇒ the plain buttons were not a
/// different toolbar implementation at all — ⭐ both hosts already build the SAME
/// <see cref="PerspectiveToolbarSection"/> over the same <c>SilkIconProvider</c> — they were
/// <b>the documented fallback of a section with no keys registered</b>.</para>
///
/// <para>⛔ And the keys were registered in exactly ONE place repo-wide: five inline
/// <c>windowManager.RegisterPerspectiveIconKey(...)</c> calls inside <c>EditorSubsystem</c>'s
/// <c>if (MainToolbar != null)</c> block. ⇒ ⭐⭐ a host-private list of a cross-host fact, which is what
/// ruling 58 (*one registration list*) forbids. This is that list, lifted verbatim.</para>
///
/// <para>⚠ <b>The keys are atlas paths, not perspective names</b> — <c>"perspective/editor"</c> keys the
/// Scenario perspective for historical asset-naming reasons and is deliberately NOT renamed here: that
/// would be an asset rename masquerading as a wiring fix (the same call EditorSubsystem's own A2 note
/// made about the label alias).</para>
/// </summary>
public static class PerspectiveIconKeys
{
    /// <summary>
    /// The table, ordered as the editor declared it. ⭐ Public so a rail can assert the two hosts
    /// register the SAME keys without re-listing them — ⛔ a rail that restates the list cannot catch a
    /// host that skips the call.
    /// </summary>
    public static readonly (string Perspective, string IconKey)[] Table =
    {
        ("BTree",      "asset/btree"),
        ("HSM",        "asset/hsm"),
        ("Blueprint",  "asset/blueprint"),
        ("Blueprints", "asset/blueprint"),
        ("Scenario",   "perspective/editor"),
    };

    /// <summary>
    /// Registers every entry in <see cref="Table"/>. ⭐ Call from each windowed host's
    /// <c>RegisterWindows</c> <b>before</b> constructing <see cref="PerspectiveToolbarSection"/>, so the
    /// radio model resolves its faces on the first frame.
    /// </summary>
    /// <remarks>
    /// ⭐ Registering a key for a perspective this host does not claim is HARMLESS — the section only
    /// asks about perspectives <c>GetPerspectives()</c> returns — which is why one table can serve every
    /// host with no per-host subset and no <c>if (host==…)</c>.
    /// </remarks>
    public static void Register(WindowManager windowManager)
    {
        ArgumentNullException.ThrowIfNull(windowManager);
        foreach (var (perspective, iconKey) in Table)
            windowManager.RegisterPerspectiveIconKey(perspective, iconKey);
    }
}
