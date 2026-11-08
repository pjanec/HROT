using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Editing;
using Hrot.Common.Infrastructure;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Tests.Map3D;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common.Panels;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ CE-4329 (R-4170, 📄 docs/UX/DESIGN_Seats_And_Hats.md §R-4170) — the seat · hat text left the main toolbar for the STATUS BAR, measured on a REAL ImGui frame (Xvfb; a skip is not a pass):
/// the toolbar's run-mode indicator is ONE glyph button and ▶ follows it at one item spacing (nothing between); the status bar's RIGHT end shows the text while its LEFT end stays free for another
/// section; a click on the text opens the menu; a steady frame allocates 0 B. 🔴 Red-proof (compiles): the toolbar drawing the line again between the indicator and ▶ ⇒ the gap rail red; the section
/// registered with sort order 0 / not right-aligned ⇒ the right-end rail red. Renders (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>seat-toolbar.png</c>, <c>seat-statusbar.png</c>, <c>seat-menu.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TheSeatTextIsOnTheStatusBarFrameRail
{
    private const int W = 900, H = 200;

    private static EntityRepository World(Seat seat, EditingMode mode)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>();
        w.RegisterComponent<NetworkIdentity>();
        w.RegisterComponent<EntityInfo>();
        w.SetSingletonManaged(new NetworkEntityMap());
        StationMode.Install(w, () => mode, () => seat);
        MapInteractionPack.Build(new MapInteractionContext { World = w });   // the map's HatArbiter, as every map host builds it
        return w;
    }

    private static void Shoot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    private static void Pos(ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    /// <summary>The toolbar draws the run-mode indicator as one glyph button and ▶ right after it — no text between (the gap is exactly one item spacing).</summary>
    [SkippableFact]
    public void TheToolbar_DrawsNothingBetweenTheIndicatorAndPlay()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        var rig = RunChoiceRig.With(Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit, Hrot.UI.Common.RunMode.RunArmed.Preview);
        var section = new MainToolbarTimeControlSection(rig.F, choice: rig.Choice);
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(W, 40));
            ImGui.Begin("##toolbar", ImGuiWindowFlags.NoDecoration);
            section.Render();
            ImGui.End();
        }
        for (int i = 0; i < 3; i++) frame.Step(Draw);
        Shoot(frame, "seat-toolbar.png");
        float spacing = 0;
        frame.Step(() => { spacing = ImGui.GetStyle().ItemSpacing.X; Draw(); });
        var (ind, play) = (section.IndicatorRect, section.PlayRect);
        Assert.True(ind.Max.X > ind.Min.X && play.Max.X > play.Min.X, "both drawn");
        Assert.InRange(play.Min.X - ind.Max.X, spacing - 0.6f, spacing + 0.6f);   // one spacing: ONE button, then ▶
    }

    /// <summary>The status bar's right end shows "Instructor · Observe"; a section at its LEFT end is untouched; the right text sits clear of it.</summary>
    [SkippableFact]
    public void TheStatusBar_ShowsTheSeatAndHatAtItsRightEnd_AndTheLeftStaysFree()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(Seat.Instructor, EditingMode.Live);
        var bar = new StatusBarManager();
        var seat = StationSeatStatusBarSection.Register(bar, () => world);
        Vector2 leftMin = default, leftMax = default;
        bar.RegisterSection("source_segment", 5, () => { ImGui.Text("Source: live"); leftMin = ImGui.GetItemRectMin(); leftMax = ImGui.GetItemRectMax(); });   // the Review work's segment, at the LEFT end
        for (int i = 0; i < 4; i++) frame.Step(() => bar.Render("Scenario"));
        Shoot(frame, "seat-statusbar.png");

        Assert.Equal("Instructor · Observe", seat.Line());
        var t = seat.TextRect;
        Assert.True(t.Max.X <= W && t.Max.X >= W - 24f, $"the text ends at the bar's right edge ({t.Max.X})");
        Assert.True(t.Min.X > W * 0.6f, "…and starts well right of the middle");
        Assert.True(leftMin.X < 30f, $"the left segment is still first ({leftMin.X})");
        Assert.True(leftMax.X < t.Min.X, "…and clear of the seat text");
    }

    /// <summary>A click on the text asks for the menu and the popup is drawn with the hats and the seats (the hat the seat forbids greyed with its reason).</summary>
    [SkippableFact]
    public void AClickOnTheText_OpensTheMenu()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(Seat.SideController, EditingMode.Live);
        var bar = new StatusBarManager();
        var seat = StationSeatStatusBarSection.Register(bar, () => world);
        for (int i = 0; i < 3; i++) frame.Step(() => bar.Render(""));
        Assert.False(seat.MenuDrawn);
        var at = (seat.TextRect.Min + seat.TextRect.Max) / 2f;
        frame.Step(() => { Pos(ImGui.GetIO(), at.X, at.Y); bar.Render(""); });
        frame.Step(() => { Pos(ImGui.GetIO(), at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); bar.Render(""); });
        frame.Step(() => { Pos(ImGui.GetIO(), at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); bar.Render(""); });
        for (int i = 0; i < 2; i++) frame.Step(() => bar.Render(""));
        Assert.True(seat.MenuDrawn, "the click opened the menu");
        Shoot(frame, "seat-menu.png");
    }

    /// <summary>A steady frame allocates nothing (the line and the rows are rebuilt only when the seat or the hat changes).</summary>
    [SkippableFact]
    public void ASteadyFrame_Allocates0Bytes()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(Seat.Author, EditingMode.Edit);
        var bar = new StatusBarManager();
        var seat = StationSeatStatusBarSection.Register(bar, () => world);
        for (int i = 0; i < 6; i++) frame.Step(() => bar.Render(""));
        long bytes = -1;
        frame.Step(() =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(W, 30));
            ImGui.Begin("##alloc", ImGuiWindowFlags.NoDecoration);
            long before = GC.GetAllocatedBytesForCurrentThread();
            seat.Render();
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        });
        Assert.Equal(0, bytes);
    }

    // ── the model (no frame) ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The menu's rows: the current hat and seat are checked; a hat the seat forbids is greyed with the seat's reason; the rest say why they cannot be picked.</summary>
    [Theory]
    [InlineData(Seat.Author, Hat.Edit, null)]
    [InlineData(Seat.Player, Hat.Edit, "a Player seat cannot edit")]
    [InlineData(Seat.Player, Hat.Command, "a Player seat cannot command")]
    [InlineData(Seat.SideController, Hat.Embodied, "a Side controller seat cannot possess a unit")]
    [InlineData(Seat.Observer, Hat.Command, "an Observer seat cannot command")]
    public void TheMenuRows_GreyWhatTheSeatForbids_WithTheReason(Seat seat, Hat hat, string? reason)
    {
        var rows = StationSeatStatusBarSection.MenuRows(seat, Hat.Observe);
        var row = rows.Single(r => r.Label == StationSeatStatusBarSection.HatLabel(hat));
        if (reason != null) Assert.Equal(reason, row.Why);
        else Assert.NotNull(row.Why);   // allowed but not current: derived, so also greyed — with ITS reason
        Assert.Contains(rows, r => r.Checked && r.Label == "Observe");
        Assert.Contains(rows, r => r.Checked && r.Label == StationSeatStatusBarSection.SeatWord(seat));
    }
}
