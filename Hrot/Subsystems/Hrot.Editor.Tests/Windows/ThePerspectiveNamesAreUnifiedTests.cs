using System;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>A1</c> + <c>A9</c> — THE EDITOR AND CGF SPEAK ONE PERSPECTIVE VOCABULARY.</b>
/// 📄 <c>docs/DESIGN_Perspective_Unification.md</c> §1b *(the target subsystem→perspective table)* ·
/// §3 <c>A1</c> · charter <c>D1</c>/<c>D2</c>.
///
/// <para>⭐⭐ <b>Why this rail and not a source scan.</b> A perspective exists because a window CLAIMS it
/// *(§2)*, so the only truthful question is <i>"what does <c>GetPerspectives()</c> return after the real
/// <c>RegisterWindows</c> ran?"</i> — ⛔ a grep over ctor literals is exactly the reading error §1's own
/// correction records *(<c>"I read constructor literals as a claim about runtime"</c>)*.</para>
///
/// <para>⚠ <b>What these CANNOT cover</b> *(📌 <c>M-29</c>)</b>: the status-bar sections both subsystems
/// register with a <c>perspective:</c> argument sit behind an event-bus guard that needs
/// <c>Initialize</c>, so they are not exercised here. ⭐ The WINDOWS are, and they are what
/// <c>GetPerspectives()</c> is derived from.</para>
/// </summary>
public sealed class ThePerspectiveNamesAreUnifiedTests
{
    private static WM Register(Action<WM> registerWindows)
    {
        var wm = new WM(new IconAtlas(IntPtr.Zero, 16f, 16f));
        registerWindows(wm);
        return wm;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>A1</c>'s gate — <c>--mode editor</c> offers <c>Scenario · BTree · HSM · Blueprint</c>.</b>
    ///
    /// <para>⛔⛔ <b>Three names must be ABSENT, and each was a separate defect:</b>
    /// <list type="bullet">
    ///   <item><c>"Editor"</c> — the old id. ⭐ It was only ever displayed as <c>"Scenario"</c> through a
    ///   label alias, so ids did not match across hosts and conformance would have needed a translation
    ///   layer;</item>
    ///   <item><c>"Global"</c> — §1c's phantom, which drew a toolbar icon the user never asked for;</item>
    ///   <item><c>"Authoring"</c> / <c>"Analysis"</c> — 📐 never registered in production, and this is
    ///   the rail that keeps saying so.</item>
    /// </list></para>
    /// </summary>
    [Fact]
    public void TheEditorOffersScenarioAndTheThreeGraphPerspectives()
    {
        var wm = Register(w => new Hrot.Editor.EditorSubsystem().RegisterWindows(w));

        // ⭐ GetPerspectives() is OrderBy(p => p) — this is the whole set, in the order it returns.
        // ⚠ "Blueprint" sorts BEFORE "BTree": OrderBy uses the current culture's string comparison, not
        //   ordinal, so 'l' < 'T'. 📌 Measured, not assumed — an ordinal expectation reddens here.
        Assert.Equal(new[] { "Blueprint", "BTree", "HSM", "Scenario" }, wm.GetPerspectives());

        Assert.DoesNotContain("Editor",    wm.GetPerspectives());
        Assert.DoesNotContain("Global",    wm.GetPerspectives());
        Assert.DoesNotContain("Authoring", wm.GetPerspectives());
        Assert.DoesNotContain("Analysis",  wm.GetPerspectives());
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>A9</c>'s gate — CGF's four diagnostics windows moved to <c>Scenario</c>, and
    /// ⛔ NO <c>CGF</c> perspective remains.</b>
    ///
    /// <para>🔒 <b>User, <c>2026-08-23</c>, overruling my "add, don't replace" lean:</b> <i>"once cgf gets
    /// all 4 perspectives, we should remove the cgf perspective completely. Maybe we should simply and
    /// immediately rename CGF perspective to the 'Scenario' perspective."</i> ⭐ A lingering <c>CGF</c>
    /// perspective is a name the editor will never have, so conformance would carry a permanent
    /// exception.</para>
    ///
    /// <para>⭐ <c>BTree</c>/<c>HSM</c>/<c>Blueprint</c> are correctly ABSENT here — §1e: an empty
    /// perspective is fine, they need no placeholder and no declaration, and they appear with their first
    /// window *(Part B)*.</para>
    /// </summary>
    [Fact]
    public void CgfOffersScenarioAndNoCgfPerspective()
    {
        var wm = Register(w => new Hrot.CGF.CgfSubsystem().RegisterWindows(w));

        Assert.Equal(new[] { "Scenario" }, wm.GetPerspectives());
        Assert.DoesNotContain("CGF", wm.GetPerspectives());
    }

    /// <summary>
    /// ⭐⭐ <b>The payoff, asserted rather than asserted-about:</b> the two hosts' perspective sets
    /// OVERLAP on <c>Scenario</c>. 📄 §1b — <i>"conformance can only compare like with like"</i>.
    /// ⚠ §1d: sharing a NAME does not mean sharing a window SET, and it must not — the CGF and editor
    /// <c>Scenario</c> workspaces are independent, which is why the check is an intersection and not an
    /// equality.
    /// </summary>
    [Fact]
    public void BothHostsShareTheScenarioName()
    {
        var editor = Register(w => new Hrot.Editor.EditorSubsystem().RegisterWindows(w)).GetPerspectives();
        var cgf    = Register(w => new Hrot.CGF.CgfSubsystem().RegisterWindows(w)).GetPerspectives();

        Assert.Contains("Scenario", editor);
        Assert.Contains("Scenario", cgf);
        Assert.Equal(new[] { "Scenario" }, editor.Intersect(cgf).ToArray());
    }
}
