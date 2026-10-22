using System;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <c>BP-515</c> (docs/blueprints/DESIGN_Details_Panel_View_Switching.md §8) — the Details shell's LAYOUT and its REQUEST FOR A
/// VIEW. 🔒 User, 2026-10-06: <i>"An event that asks for a view selects its tab, or expands its header AND scrolls it into view"</i>;
/// 2026-10-22: <i>"'Properties…' … should open the 'Details' panel (which already contains the entity component view)"</i>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheDetailsPanelHonoursARequestForAViewTests : IDisposable
{
    private sealed class Nothing : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    private bool _componentsApply;

    public TheDetailsPanelHonoursARequestForAViewTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    /// <summary>Two views: "variables" (rank 10, always) and "components" (rank 30, only once <see cref="_componentsApply"/> — the
    /// selection landing a frame after the request).</summary>
    private DetailsWindow MakeShell(string id)
    {
        var views = new DetailsViewRegistry();
        views.Add(new DetailsViewDescriptor("variables", "Variables", 10, _ => true, () => new Nothing()));
        views.Add(new DetailsViewDescriptor("components", "Components", 30, _ => _componentsApply, () => new Nothing()));
        var store = new EditorSelectionStore();
        return new DetailsWindow(
            id:                id,
            owningPerspective: "Scenario",
            formatter:         new VariableValueFormatter(RawValueDecoder.Instance),
            views:             views,
            context:           new LiveContextSource(() => DetailsContextBuilder.Build(store, "Scenario", VariableRunState.Planning)));
    }

    /// <summary>
    /// ⭐ DS-6 — a request for a view NOT yet offered waits; the frame the view joins the offer, it is PICKED and marked to reveal; the
    /// request is then spent. ✅ Red-proof: <c>FrameForDraw</c> ignoring the pending request ⇒ the chosen view stays the rank default.
    /// </summary>
    [Fact]
    public void BP515_ARequest_WaitsForItsView_ThenPicksAndRevealsIt()
    {
        var shell = MakeShell("details_bp515_1");
        shell.RequestView("components");

        var before = shell.FrameForDraw();
        Assert.Equal("components", shell.PendingViewRequest);
        Assert.Null(shell.RevealViewId);
        Assert.NotEqual("components", before.Choice.View?.Id);

        _componentsApply = true;   // ⭐ the selection lands
        var after = shell.FrameForDraw();
        Assert.Equal("components", after.Choice.View?.Id);
        Assert.Equal("components", shell.RevealViewId);
        Assert.Null(shell.PendingViewRequest);
        Assert.Equal("components", shell.FrameForDraw().Choice.View?.Id);   // the pick holds
    }

    /// <summary>⭐ DS-6 — a request for a view that never comes is dropped after <see cref="DetailsWindow.RequestFrames"/>.</summary>
    [Fact]
    public void BP515_ARequestForAViewThatNeverComes_Expires()
    {
        var shell = MakeShell("details_bp515_2");
        shell.RequestView("never-offered");
        for (int i = 0; i < DetailsWindow.RequestFrames - 1; i++) shell.FrameForDraw();
        Assert.Equal("never-offered", shell.PendingViewRequest);
        shell.FrameForDraw();
        Assert.Null(shell.PendingViewRequest);
        Assert.Null(shell.RevealViewId);
        Assert.Equal("variables", shell.FrameForDraw().Choice.View?.Id);   // spent: the rank default, untouched
    }

    /// <summary>⭐ DS-8 — the dump carries the layout; the choice fields keep their meaning in either layout.</summary>
    [Fact]
    public void BP515_TheDump_CarriesTheLayout()
    {
        const string id = "details_bp515_3";
        PanelSnapshot.CaptureEnabled = true;
        var shell = MakeShell(id);
        Assert.Equal(DetailsLayout.Tabs, shell.Layout);   // the default

        shell.SimulateDrawClientArea();
        Assert.Equal("Tabs", PanelSnapshot.TryGet(id)!.Dump()["layout"]!.GetValue<string>());

        shell.Layout = DetailsLayout.Stacked;
        shell.SimulateDrawClientArea();
        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.Equal("Stacked", dump["layout"]!.GetValue<string>());
        Assert.Equal("variables", dump["chosenViewId"]!.GetValue<string>());
    }

    /// <summary>⭐ DS-5 — a stacked header's pin acts on THAT view, not the chosen one (the rank default, Components, rank 30); a
    /// duplicate focuses (R-100 unchanged).</summary>
    [Fact]
    public void BP515_PinningAGivenView_PinsThatView_NotTheChosenOne()
    {
        _componentsApply = true;
        var shell = MakeShell("details_bp515_4");
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var frame = shell.Frame();
        Assert.Equal("components", frame.Choice.View?.Id);   // offered by Rank, highest first
        var variables = frame.Choice.Offered[1];

        var pin = shell.Pin(windows, variables);
        Assert.NotNull(pin);
        Assert.Equal("Variables (pinned)", pin!.Title);
        Assert.Same(pin, shell.Pin(windows, variables));
        Assert.NotNull(shell.OpenFloat(windows, variables));
    }

    /// <summary>
    /// ⭐ DS-7 (🔒 user, 2026-10-22: <i>"a global ui action … no direct calls"</i>) — the shell ANSWERS a request on the window manager's
    /// per-frame hook, open or closed: nothing asked ⇒ it stays closed; asked ⇒ it shows itself and requests the view. ✅ Red-proof: the
    /// shell not subscribing to <c>FrameStarted</c> ⇒ it stays closed.
    /// </summary>
    [Fact]
    public void BP515_TheShell_AnswersARequest_ShowingItselfOnTheAskedView()
    {
        var shell = MakeShell("details_bp515_5");
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        windows.RegisterWindow(shell);
        shell.IsOpen = false;
        bool asked = false;
        shell.Answer(() => asked, "components");

        windows.RaiseFrameStarted();
        Assert.False(shell.IsOpen);
        Assert.Null(shell.PendingViewRequest);

        asked = true;
        windows.RaiseFrameStarted();
        Assert.True(shell.IsOpen, "it showed itself");
        Assert.Equal("components", shell.PendingViewRequest);
    }
}
