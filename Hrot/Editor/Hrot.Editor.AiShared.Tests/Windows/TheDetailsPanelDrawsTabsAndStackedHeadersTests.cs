using System;
using System.IO;
using System.Linq;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐ <c>BP-515</c> (docs/blueprints/DESIGN_Details_Panel_View_Switching.md §8) — the REAL draw, inside live ImGui frames: the tab bar
/// shows the chosen view and a request switches the tab; stacked, every offered view gets a header, the chosen one open on first sight,
/// and a request opens another without closing the first (two instances alive — §2's 0..1 became 0..N). ✅ Red-proof: the stacked draw
/// ignoring the reveal ⇒ the requested header stays shut and one instance is alive.
/// </summary>
public sealed class TheDetailsPanelDrawsTabsAndStackedHeadersTests
{
    private sealed class Counting : IDetailsViewInstance
    {
        public static int Drawn;
        public void Draw(DetailsContext context, string idScope) { Drawn++; ImGui.TextUnformatted($"view body {idScope}"); }
        public void Dispose() { }
    }

    private static DetailsWindow MakeShell()
    {
        var views = new DetailsViewRegistry();
        views.Add(new DetailsViewDescriptor("variables", "Variables", 10, _ => true, () => new Counting()));
        views.Add(new DetailsViewDescriptor("components", "Components", 30, _ => true, () => new Counting()));
        var store = new EditorSelectionStore();
        return new DetailsWindow(
            id:                "details_bp515_frame",
            owningPerspective: "Scenario",
            formatter:         new VariableValueFormatter(RawValueDecoder.Instance),
            views:             views,
            context:           new LiveContextSource(() => DetailsContextBuilder.Build(store, "Scenario", VariableRunState.Planning)));
    }

    [SkippableFact]
    public void BP515_Tabs_ThenStacked_ARequestRevealsItsView()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-details");
        Directory.CreateDirectory(shots);
        var shell = MakeShell();
        new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f)).RegisterWindow(shell);   // float + pin draw

        using var frame = UiFrameHarness.Begin(900, 500);
        void Draw()
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 400));
            ImGui.Begin("Details##rail");
            shell.DrawClientAreaForRail();
            ImGui.End();
        }

        // tabs: the rank default (Components) is the shown tab
        frame.StepN(4, Draw);
        Assert.Equal(new[] { "components" }, shell.InstantiatedViewIds.ToArray());
        frame.Screenshot(Path.Combine(shots, "details-tabs.png"));

        // a request for Variables selects its tab
        shell.RequestView("variables");
        frame.StepN(4, Draw);
        Assert.Equal("variables", shell.Frame().Choice.View?.Id);
        Assert.Equal(new[] { "variables" }, shell.InstantiatedViewIds.ToArray());
        Assert.Null(shell.PendingViewRequest);

        // stacked: the chosen view's header opens on first sight; a request opens another, the first stays open
        shell.Layout = DetailsLayout.Stacked;
        frame.StepN(4, Draw);
        Assert.Equal(new[] { "variables" }, shell.InstantiatedViewIds.ToArray());
        shell.RequestView("components");
        frame.StepN(4, Draw);
        Assert.Equal(new[] { "components", "variables" }, shell.InstantiatedViewIds.OrderBy(x => x).ToArray());
        Assert.Equal("components", shell.Frame().Choice.View?.Id);
        frame.Screenshot(Path.Combine(shots, "details-stacked.png"));

        // back to tabs: one instance again (switching disposes, as before)
        shell.Layout = DetailsLayout.Tabs;
        frame.StepN(3, Draw);
        Assert.Single(shell.InstantiatedViewIds);
        Assert.True(Counting.Drawn > 0);
    }
}
