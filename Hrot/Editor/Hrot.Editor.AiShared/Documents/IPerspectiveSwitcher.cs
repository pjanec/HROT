namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Abstraction for the editor's active perspective (window group) — reading it, listing what exists, and
/// switching it.
/// <para>
/// In production this wraps <c>WindowManager</c>: <c>GetPerspectives()</c>, <c>CurrentPerspective</c> and
/// <c>SwitchPerspective(name)</c>. In unit tests a simple fake can be injected instead.
/// </para>
///
/// <para>⭐⭐ <b><c>N0</c> (`2026-08-23`) added the two READ members.</b> 📄
/// <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N0</c>. ⛔ <b>A second seam was NOT introduced</b> — this one
/// already existed, already wrapped the window manager, and was already constructed at the only moment the
/// window manager exists *(<c>EditorSubsystem.RegisterWindows</c>)*. 📌 The seam law: a *"we need a shared
/// X"* here meant X existed and was under-adopted.</para>
/// </summary>
public interface IPerspectiveSwitcher
{
    /// <summary>
    /// Switches the editor to the named perspective (e.g. <c>"BTree"</c>, <c>"HSM"</c>, <c>"Blueprint"</c>).
    /// </summary>
    /// <param name="perspectiveName">
    /// The perspective identifier — typically the <see cref="AssetKind"/> name.
    /// </param>
    /// <remarks>
    /// ⚠ <b>An unknown name is REFUSED, not applied</b> — <c>WindowManager.SwitchPerspective</c> logs once
    /// and no-ops *(the perspective batch's <c>A0</c>, <c>BP-488</c>)*. ⇒ ⭐ a caller that needs to know
    /// whether the switch took effect must compare <see cref="CurrentPerspective"/> afterwards; ⛔ this
    /// method cannot report it, and widening it to a <c>bool</c> would duplicate the validation the window
    /// manager already owns.
    /// </remarks>
    void SwitchPerspective(string perspectiveName);

    /// <summary>
    /// ⭐ Every perspective some registered window CLAIMS, in the window manager's own order.
    /// <para>⛔ This is <b>derived</b>, never declared — a perspective exists because a window claims it
    /// *(<c>DESIGN_Perspective_Unification.md</c> §2)*, so there is no registry to consult and an empty
    /// perspective is not representable.</para>
    /// </summary>
    IReadOnlyList<string> GetPerspectives();

    /// <summary>⭐ The active perspective's id — ⛔ the id, never a display label.</summary>
    string CurrentPerspective { get; }
}
