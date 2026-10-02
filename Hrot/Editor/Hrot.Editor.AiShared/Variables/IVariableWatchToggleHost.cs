namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 98 (<c>98c</c>) — a window that offers "Watch this variable" OUTSIDE the table.</b>
///
/// <para>🔴 <b>The defect (<c>BP-360</c>).</b> 📐 <c>MyBlueprintContextMenu:40</c> enables its watch
/// entry on <c>commands.Get("editor.toggle-variable-watch") is not null</c>, and 📐 <b>nothing in the
/// repo registered that command</b> — the only other mention was a test asserting the constant. ⇒
/// Batch 94's <i>"ONE command, TWO entry points"</i> was <b>half true in production</b>: the Details
/// table's entry was wired by <c>PerspectiveWorkspaceRegistrar.AttachWatchGesture</c>; the outline's
/// was drawn and dead.</para>
///
/// <para>⭐⭐ <b>Same shape as <see cref="ILiveVariableProjectionHost"/>, deliberately.</b> 📌 <c>R-67</c>:
/// the registrar already HOLDS the Watch store, so this arrives in its one
/// <c>RegisterExtraWindow</c> pass and ⛔ <b>the composition root gains nothing to forget</b>. ⚠ The
/// outline is an EXTRA window, registered long after the constructor's single Attach — which is
/// exactly why that Attach could never have reached it.</para>
///
/// <para>⭐ <b>Installed even when the perspective has no Watch</b>, as <c>null</c> — a host can then
/// tell <i>"asked, and there is none"</i> from <i>"never asked"</i>, and it greys its entry rather than
/// offering a click that does nothing. ⛔ That distinction is the whole reason this is an interface and
/// not a constructor argument.</para>
/// </summary>
public interface IVariableWatchToggleHost
{
    /// <summary>
    /// ⭐ Installs the perspective's watch toggle, or <c>null</c> when it has no Watch window.
    ///
    /// <para>⚠ <b>ONE delegate, and the omission is deliberate.</b> The Details table also takes an
    /// <c>IsWatched</c> predicate, to render <i>"Stop watching"</i> instead of <i>"Watch this
    /// variable"</i>. ⛔ The outline's menu cannot use one: <c>MyBlueprintContextMenu</c> draws a fixed
    /// label and 📌 <i>"gains no dependency on the editor's variable assembly"</i> by design. ⇒ taking
    /// a predicate here would be a dependency with no consumer — 📌 exactly the shape this programme
    /// keeps filing. ⭐ The toggle itself still resolves against the STORE, so the two entry points
    /// cannot disagree about what is pinned.</para>
    /// </summary>
    void SetWatchToggle(System.Action<VariableRow>? toggle);
}
