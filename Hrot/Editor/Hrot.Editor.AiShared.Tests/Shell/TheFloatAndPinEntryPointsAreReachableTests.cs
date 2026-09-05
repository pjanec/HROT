using System;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>VC-1</c>'s rails — FLOAT AND PIN ARE REACHABLE.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L4.4</c>: <i>"entry points — toolbar
/// affordance <b>+ the View menu, so a float is reachable with Details closed</b>."</i>
///
/// <para>🔴 <b>The user's finding, verbatim:</b> <i>"cannot open a floating window — neither the
/// contextual float nor the pin."</i> 📐 Three separate causes were measured, and each gets its own
/// rail here, because fixing any two of them still leaves the feature unreachable:</para>
///
/// <list type="number">
/// <item>the Scenario shell never received a <c>WindowManager</c> ⇒ the toolbar buttons never drew;</item>
/// <item>the toolbar's <c>offered.Count &lt; 2</c> early-return swallowed float/pin as well as the switch;</item>
/// <item>the <b>View menu</b> half of <c>L4.4</c> was never built at all *(<c>BP-403</c>)*.</item>
/// </list>
///
/// <para>⚠⚠ <b>What these rails do NOT prove:</b> that a button is VISIBLE on screen — 📌 <c>R-21</c>/
/// <c>R-62</c>, the draw is unrailed by construction. ⭐ They prove the three things a draw depends on:
/// the manager ARRIVED, the gesture WORKS at one offer, and the menu entries EXIST and are enabled by
/// the right condition. ⛔ The on-screen half stays with the user's visual check.</para>
/// </summary>
public sealed class TheFloatAndPinEntryPointsAreReachableTests
{
    private static WindowManager Wm()
        => new(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));

    private static DetailsViewDescriptor View(string id, Func<DetailsContext, bool> applies)
        => new(id, id, 0, applies, () => new Nothing());

    private sealed class Nothing : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    /// <summary>⭐ A shell built the way the SCENARIO composition root builds one — directly, ⛔ NOT
    /// through <c>PerspectiveWorkspaceRegistrar</c>. ⚠ That distinction is the whole of defect ①.</summary>
    private static (DetailsWindow shell, DetailsViewRegistry views) StandaloneShell(int viewCount)
    {
        var views = new DetailsViewRegistry();
        for (int i = 0; i < viewCount; i++) views.Add(View($"v{i}", _ => true));

        var store = new EditorSelectionStore();
        var shell = new DetailsWindow(
            id:                "scenario_details",
            owningPerspective: "Scenario",
            formatter:         new VariableValueFormatter(RawValueDecoder.Instance),
            views:             views,
            context:           new LiveContextSource(() => DetailsContextBuilder.Build(
                                   store, "Scenario", VariableRunState.Planning)));
        return (shell, views);
    }

    // ══ ① the manager ARRIVES, on every registration path ════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>REGISTERING the shell is what hands it the manager — no <c>Attach…</c> call.</b>
    ///
    /// <para>🔴 <b>The measured defect:</b> <c>AttachWindowManager</c> had exactly ONE caller
    /// *(<c>PerspectiveWorkspaceRegistrar</c>)*, and the Scenario host is built at the composition root
    /// instead ⇒ it never got a manager ⇒ ⛔ <c>DrawToolbar</c>'s <c>if (_windowManager != null)</c> was
    /// false for ever and neither button could draw. ⚠ 📌 The <c>2026-08-16</c> silent-default shape —
    /// the caller HELD the manager and did not pass it — and it was MY omission in <c>L6.1c</c>.</para>
    ///
    /// <para>⭐⭐ The fix is asserted THROUGH the gesture, not through the field: a float appearing in the
    /// manager is what "the shell can reach its manager" actually means.</para>
    /// </summary>
    [Fact]
    public void AStandaloneShell_GetsItsManagerFromRegistrationAlone()
    {
        var (shell, _) = StandaloneShell(viewCount: 2);
        var wm = Wm();

        wm.RegisterWindow(shell);   // ⭐ the ONLY wiring — as the Scenario root does it

        // ⭐ The View-menu entry acts on the registered shell; that it opens a float proves the
        //   manager reached it, because OpenFloat registers into the manager it was handed.
        wm.SwitchPerspective("Scenario");
        Invoke(wm, "View/Details/Float current view");

        Assert.Contains(wm.RegisteredWindowIds,
                        id => id == DetailsWindow.FloatIdFor("Scenario", "v0"));
    }

    // ══ ② one offer is enough to float ═══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A SINGLE offered view can still be floated and pinned.</b>
    ///
    /// <para>🔴 <b>The measured defect:</b> <c>DrawToolbar</c> opened with
    /// <c>if (offered.Count &lt; 2) return;</c> — a rule written for the view SWITCH *(one
    /// permanently-pressed button is noise)* — ⛔ and the float/pin affordances sat BELOW it, so they
    /// inherited a guard that has nothing to do with them. ⚠ Floating is most useful at exactly one
    /// offer: the designer wants that view beside something else rather than docked.</para>
    ///
    /// <para>⭐⭐⭐ <b>The assertion is that the TWO DECISIONS ARE INDEPENDENT</b>, because that is exactly
    /// what the defect fused. ⚠⚠ <b>My first version of this rail asserted only
    /// <c>OpenFloat(wm) != null</c> and stayed GREEN through a probe that put the old guard back</b> —
    /// 📌 <c>BP-402</c> ①: a probe that reddens nothing is a finding about the RAIL. ⛔ The gesture was
    /// never guarded; the DRAW was. ⇒ ⭐ the shell now NAMES both decisions
    /// *(<c>ShowsViewSwitch</c> / <c>ShowsFloatAndPin</c>)* and this asserts they disagree at one
    /// offer, which no re-fused implementation can satisfy.</para>
    /// </summary>
    [Fact]
    public void WithExactlyOneOfferedView_TheSwitchIsHiddenButFloatAndPinAreNot()
    {
        var (shell, _) = StandaloneShell(viewCount: 1);
        var wm = Wm();
        wm.RegisterWindow(shell);

        // ⭐ The switch correctly stays hidden — that rule is unchanged and still right.
        Assert.False(shell.ShowsViewSwitch(shell.Frame()));
        // ⭐⭐ …and float/pin are offered ANYWAY. This pair is the whole fix.
        Assert.True(shell.ShowsFloatAndPin);

        Assert.NotNull(shell.OpenFloat(wm));
        Assert.NotNull(shell.Pin(wm));
    }

    /// <summary>
    /// ⛔ <b>No manager ⇒ no float affordance, even with a view showing.</b> ⚠ The other half of
    /// <c>ShowsFloatAndPin</c>: an unregistered shell *(a hand-built one in a rail)* has nowhere to
    /// register the new window, so drawing the button would offer a gesture that cannot complete.
    /// </summary>
    [Fact]
    public void AnUnregisteredShell_OffersNoFloatAffordance()
        => Assert.False(StandaloneShell(viewCount: 2).shell.ShowsFloatAndPin);

    /// <summary>
    /// ⛔ <b>NOTHING showing ⇒ nothing to float.</b> ⚠ The honest negative: with no view claiming the
    /// context the shell draws <c>R-117</c>'s grey line, and a float would have no descriptor to carry.
    /// ⭐ Without this half, a "fix" that floated unconditionally would pass the rail above.
    /// </summary>
    [Fact]
    public void WithNoOfferedView_ThereIsNothingToFloat()
    {
        var (shell, _) = StandaloneShell(viewCount: 0);
        var wm = Wm();
        wm.RegisterWindow(shell);

        Assert.Null(shell.CurrentDescriptor());
        Assert.Null(shell.OpenFloat(wm));
        Assert.Null(shell.Pin(wm));
    }

    // ══ ③ BP-403 — the View menu ═════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-403</c> CLOSED: the View-menu entry points EXIST.</b>
    /// 📄 §6 <c>L4.4</c>'s <i>"+ the View menu, so a float is reachable with Details closed"</i> — 🔴 the
    /// half the tracker recorded as never wired.
    /// </summary>
    [Fact]
    public void TheViewMenu_CarriesAFloatAndAPinEntry()
    {
        var wm = Wm();
        wm.RegisterWindow(StandaloneShell(viewCount: 1).shell);

        var details = wm.GlobalMenu.Root.Children["View"].Children["Details"];

        Assert.Contains("Float current view", details.Children.Keys);
        Assert.Contains("Pin current view",   details.Children.Keys);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THREE shells, TWO menu entries — not six.</b>
    /// ⛔ The failure this forbids: one pair of items PER <c>DetailsWindow</c>, which would put three
    /// near-identical entries in the bar. ⭐ The items resolve the active shell from
    /// <c>CurrentPerspective</c> at click time, so one pair serves every perspective — including one
    /// added later.
    /// </summary>
    [Fact]
    public void ManyShells_LeaveExactlyTwoMenuEntries()
    {
        var wm = Wm();
        foreach (var p in new[] { "Scenario", "BTree", "HSM" })
        {
            var views = new DetailsViewRegistry();
            views.Add(View("v0", _ => true));
            var store = new EditorSelectionStore();
            wm.RegisterWindow(new DetailsWindow(
                id: $"{p}_details", owningPerspective: p,
                formatter: new VariableValueFormatter(RawValueDecoder.Instance),
                views: views,
                context: new LiveContextSource(() => DetailsContextBuilder.Build(
                    store, p, VariableRunState.Planning))));
        }

        Assert.Equal(2, wm.GlobalMenu.Root.Children["View"].Children["Details"].Children.Count);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The menu acts on the CURRENT perspective's shell, not on whichever registered last.</b>
    /// ⛔⛔ This is the rail that makes the single-pair design safe: with one pair of items and three
    /// shells, resolving the target wrongly would silently float the WRONG perspective's view — 📌
    /// <c>R-78</c>'s chameleon failure, and invisible on screen whenever the two coincide.
    /// </summary>
    [Fact]
    public void TheMenuFloats_ThePerspectiveTheDesignerIsIn()
    {
        var wm = Wm();
        foreach (var p in new[] { "Scenario", "BTree" })
        {
            var views = new DetailsViewRegistry();
            views.Add(View($"only.{p}", _ => true));
            var store = new EditorSelectionStore();
            wm.RegisterWindow(new DetailsWindow(
                id: $"{p}_details", owningPerspective: p,
                formatter: new VariableValueFormatter(RawValueDecoder.Instance),
                views: views,
                context: new LiveContextSource(() => DetailsContextBuilder.Build(
                    store, p, VariableRunState.Planning))));
        }

        wm.SwitchPerspective("BTree");
        Invoke(wm, "View/Details/Float current view");

        Assert.Contains   (wm.RegisteredWindowIds, id => id == DetailsWindow.FloatIdFor("BTree",  "only.BTree"));
        Assert.DoesNotContain(wm.RegisteredWindowIds, id => id == DetailsWindow.FloatIdFor("Scenario", "only.Scenario"));
    }

    /// <summary>
    /// ⭐⭐ <b>Greyed WITH A REASON when nothing is showing — ⛔ not hidden.</b>
    /// 📌 The user's <c>2026-08-17</c> ruling: <i>"showing explanatory tooltip would be better than
    /// allowing user to click the button and then saying that it is not possible — same information
    /// value, no false expectations."</i> ⚠ And the enabled label NAMES the view, so the designer knows
    /// what they are about to float.
    /// </summary>
    [Fact]
    public void WithNothingShowing_TheMenuItemIsDisabledAndSaysWhy()
    {
        var wm = Wm();
        wm.RegisterWindow(StandaloneShell(viewCount: 0).shell);
        wm.SwitchPerspective("Scenario");

        var node = wm.GlobalMenu.Root.Children["View"].Children["Details"].Children["Float current view"];

        Assert.False(node.GetEnabled!());
        Assert.Contains("no view is showing", node.ResolveLabel());
    }

    // ══ ══════════════════════════════════════════════════════════════════════

    /// <summary>⭐ Click a global-menu leaf by its path — ⛔ ImGui is never involved, so this is the
    /// gesture the renderer would raise, not a simulation of the renderer.</summary>
    private static void Invoke(WindowManager wm, string path)
    {
        var node = wm.GlobalMenu.Root;
        foreach (var part in path.Split('/')) node = node.Children[part];
        node.OnClick!();
    }
}
