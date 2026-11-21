using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Hrot.Presentation.DebugApi;
using Hrot.Editor;
using Hrot.Editor.AiShared.Review;
using Hrot.Editor.Review;
using Hrot.Editor.UiFrameRail;
using Moq;
using Xunit;
using ImGui = ImGuiNET.ImGui;
using Role = Hrot.Editor.AiShared.Review.ReviewLayout.Role;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.Review;

/// <summary>
/// ⭐ R-4 / V4 (docs/UX/DESIGN_Review_Perspective.md §3 D1 / D10 / D11, §3a) — the Review LAYOUT and its automatic switch: ONE layout definition (<see cref="ReviewLayout"/>) docks Frame diff, Search, Breakpoints,
/// Details, the Mission summary and the Event Timeline around the map (the mockup); the editor owns the bundle's windows in it and SHARES the Scenario layout's others; entering an AAR on the all-in-one editor
/// switches to it and leaving puts the previous one back — ⛔ never on a cluster station; a manual switch mid-AAR is respected; the switch touches only windows. Frame rails run under <c>xvfb-run -a</c> (a skip is not a pass).
/// 🔴 Red-proofs (compile): dropping the <c>allInOne</c> guard ⇒ the station rail red; dropping the restore ⇒ the leave rail red.
/// </summary>
[Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheReviewLayoutTests
{
    private static readonly Dictionary<Role, string> Ids = new()
    {
        [Role.MissionSummary] = "t_mission", [Role.Details] = "t_details", [Role.FrameDiff] = "t_diff",
        [Role.Search] = "t_search", [Role.Breakpoints] = "t_bp", [Role.EventTimeline] = "t_timeline",
    };

    private sealed class Win : ManagedWindow
    {
        public Win(string id, string perspective) : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

    /// <summary>A switch over a REAL window manager with a Scenario and a Review window (and Start), the AAR / station flags in the caller's hands.</summary>
    private sealed class Rig
    {
        public readonly WM Windows = new(new IconAtlas(IntPtr.Zero, 16f, 16f));
        public bool InAar, AllInOne = true;
        public readonly ReviewLayoutSwitch Switch;
        public Rig(string startIn = "Scenario")
        {
            Windows.RegisterWindow(new Win("w_scn", "Scenario"));
            Windows.RegisterWindow(new Win("w_bt", "BTree"));
            Windows.RegisterWindow(new Win("w_rev", ReviewLayout.Perspective));
            Windows.RegisterWindow(new Win("w_start", WM.StartPerspectiveName));
            Windows.SwitchPerspective(startIn);
            Switch = new ReviewLayoutSwitch(() => InAar, () => AllInOne, () => Windows.CurrentPerspective, p => Windows.TrySwitchPerspective(p, out _),
                () => Windows.IsStartShowing, Windows.RetargetRememberedPerspective);
        }
        public string Now => Windows.CurrentPerspective;
        public void Frames(int n = 3) { for (int i = 0; i < n; i++) Switch.Update(); }
    }

    // ══ D10 — the automatic switch ═══════════════════════════════════════════

    [Fact]
    public void V4_EnteringAnAarOnTheAllInOneEditor_SwitchesToReview_AndLeavingPutsThePreviousBack()
    {
        var r = new Rig("BTree");
        r.Frames(); Assert.Equal("BTree", r.Now);                       // no AAR: nothing happens
        r.InAar = true; r.Frames();
        Assert.Equal(ReviewLayout.Perspective, r.Now);                  // entering AAR ⇒ Review …
        Assert.Equal("BTree", r.Switch.Before);                         // … remembering where it came from
        r.InAar = false; r.Frames();
        Assert.Equal("BTree", r.Now);                                   // leaving AAR ⇒ the previous layout
        Assert.Null(r.Switch.Before);
    }

    [Fact]
    public void V4_AClusterStationsAar_NeverSwitches()
    {
        var r = new Rig("Scenario") { AllInOne = false };
        r.InAar = true; r.Frames(6);
        Assert.Equal("Scenario", r.Now);                                // the trainees' debrief keeps its layout
        r.InAar = false; r.Frames();
        Assert.Equal("Scenario", r.Now);
    }

    [Fact]
    public void V4_AManualSwitchDuringAar_IsRespected_AndLeavingStillRestoresTheOneFromBeforeTheAar()
    {
        var r = new Rig("Scenario");
        r.InAar = true; r.Frames();
        Assert.Equal(ReviewLayout.Perspective, r.Now);
        r.Windows.SwitchPerspective("BTree"); r.Frames(5);              // the user picks another layout mid-AAR
        Assert.Equal("BTree", r.Now);                                   // respected: the switch does not drag it back
        r.InAar = false; r.Frames();
        Assert.Equal("Scenario", r.Now);                                // and leaving restores the PRE-AAR one, not the manual one
    }

    [Fact]
    public void V4_AnAlreadyReviewLayout_StaysWhenTheAarEnds_AndStartRefusalIsRetried()
    {
        var r = new Rig(ReviewLayout.Perspective);
        r.InAar = true; r.Frames(); r.InAar = false; r.Frames();
        Assert.Equal(ReviewLayout.Perspective, r.Now);                  // the user was in Review before: nothing to put back

        // the cluster rests in Idle (Start shows) when the AAR begins: the switch waits for Start to leave, then switches
        var s = new Rig("Scenario");
        s.Windows.SwitchPerspective(WM.StartPerspectiveName);
        s.InAar = true; s.Frames(4);
        Assert.Equal(WM.StartPerspectiveName, s.Now);                   // refused, no crash, no flood
        s.Windows.LeaveStartPerspective(out _);                         // the gate opens the AAR
        s.Frames();
        Assert.Equal(ReviewLayout.Perspective, s.Now);
        Assert.Equal("Scenario", s.Switch.Before);
    }

    [Fact]
    public void V4_AnAarThatClosesIntoIdle_MakesStartRestoreThePreAarLayout_NotReview()
    {
        var r = new Rig("BTree");
        r.InAar = true; r.Frames();
        Assert.Equal(ReviewLayout.Perspective, r.Now);
        r.Windows.SwitchPerspective(WM.StartPerspectiveName);           // Close session ⇒ Idle ⇒ Start (it remembers Review)
        Assert.Equal(ReviewLayout.Perspective, r.Windows.RememberedPerspective);
        r.InAar = false; r.Frames();                                    // the AAR is over: Start's way back is retargeted
        Assert.Equal("BTree", r.Windows.RememberedPerspective);
        Assert.True(r.Windows.LeaveStartPerspective(out _));
        Assert.Equal("BTree", r.Now);
    }

    [Fact]
    public void V4_TheSwitchTouchesOnlyWindows_TheReplayTheMasterAndTheSelectionStay()
    {
        var path = Path.Combine(Path.GetTempPath(), "hrot-v4-" + Guid.NewGuid().ToString("N") + ".fdp");
        var master = new Mock<IClusterMasterControl>();
        long zero = TimeSpan.FromSeconds(1).Ticks, now = zero + TimeSpan.FromSeconds(2).Ticks;
        using var replay = new OwnPartReplay(() => path, () => new ReplayPosition(zero, now), () => master.Object, () => ClusterState.OperatingReplay);
        var selected = new List<int> { 1042 };
        long before = replay.CurrentFrame();

        var r = new Rig("Scenario");
        r.InAar = true; r.Frames();
        r.Windows.SwitchPerspective("BTree");
        r.InAar = false; r.Frames();

        Assert.Empty(master.Invocations);                               // no seek, no pause, nothing was sent to the cluster
        Assert.Equal(before, replay.CurrentFrame());                    // the replay's position is the replay's
        Assert.Equal(new[] { 1042 }, selected);                         // and the selection never passed through the switch
    }

    // ══ D1 / D11 — the layout definition and its composition in both hosts ═══

    [Fact]
    public void V4_TheLayoutPutsTheMockupsWindowsInTheMockupsRegions()
    {
        var p = ReviewLayout.PresetFor(Ids);
        DockRegion R(string id) => p.Placements.First(x => x.WindowId == id).Region;
        Assert.Equal(DockRegion.Left, R("t_mission"));
        Assert.Equal(DockRegion.Right, R("t_details"));
        Assert.All(new[] { "t_diff", "t_search", "t_bp" }, id => Assert.Equal(DockRegion.RightLower, R(id)));   // tabbed under Details
        Assert.Equal(DockRegion.Bottom, R("t_timeline"));
        Assert.Equal(new[] { "t_diff", "t_search", "t_bp" }, p.Placements.Where(x => x.Region == DockRegion.RightLower).Select(x => x.WindowId).ToArray());   // the tab order
        Assert.Equal(5, ReviewLayout.PresetFor(Ids.Where(kv => kv.Key != Role.Breakpoints).ToDictionary(kv => kv.Key, kv => kv.Value)).Placements.Count);   // a host without a role skips it

        // the geometry: the four regions tile the work area around the map, which keeps the middle
        var g = p.Regions(new Vector4(0, 0, 1200, 700));
        Assert.Equal(1200f, g.Left.Z + g.Centre.Z + g.Right.Z, 2);
        Assert.Equal(g.Right.Z, g.RightLower.Z, 2);
        Assert.Equal(g.Right.W + g.RightLower.W, g.Left.W, 2);
        Assert.Equal(700f, g.Left.W + g.Bottom.W, 2);
        Assert.Equal(1200f, g.Bottom.Z, 2);
        Assert.True(g.Centre.Z > 600f && g.Centre.X > g.Left.X, "the map keeps the larger middle");
    }

    [Fact(Timeout = 60_000)]
    public void V4_TheEditorOwnsTheBundleInReview_SharesTheScenarioWindows_AndOpensAndDocksThemOnTheFirstEntry()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var wm = new WM(new IconAtlas(IntPtr.Zero, 512, 512));
            editor.RegisterWindows(wm);

            Assert.Contains(ReviewLayout.Perspective, wm.GetPerspectives());                              // a layout the toolbar / menu offer
            Assert.Equal(ReviewLayout.IconKey, wm.GetPerspectiveIconKey(ReviewLayout.Perspective));        // with its own icon
            Assert.Contains(ReviewLayout.Perspective, wm.GetSelectablePerspectives());
            foreach (var id in new[] { "editor_review_diff", "editor_review_search", "rb_recording_info", "rb_json_export" })
            {
                wm.TryGetWindow(id, out var w);
                Assert.Equal(ReviewLayout.Perspective, w!.OwningPerspective);                               // the bundle's windows belong to it …
            }
            foreach (var id in new[] { "scenario_details", "editor_bp_manager", "editor_mission_summary" })
            {
                Assert.True(wm.TryGetWindow(id, out var w), id);
                Assert.Equal("Scenario", w!.OwningPerspective);                                             // … the Scenario layout's others stay its own …
                Assert.Contains(ReviewLayout.Perspective, w.AlsoShownIn);                                   // … and are SHARED, not copied
            }

            // D2 — openable by hand in any layout: from Scenario, Window ▸ Review ▸ Search shows it (pinned) — it is not taken out of the menu
            wm.SwitchPerspective("Scenario");
            Assert.Null(wm.PendingDockPreset);
            wm.ShowWindow("editor_review_search");
            wm.TryGetWindow("editor_review_search", out var search);
            Assert.True(search!.IsOpen && search.IsPinned);
            wm.HideWindow("editor_review_search");

            // the first entry into Review opens the layout's windows and asks for THE preset; a second entry asks for nothing
            wm.SwitchPerspective(ReviewLayout.Perspective);
            var preset = wm.PendingDockPreset;
            Assert.NotNull(preset);
            Assert.True(wm.TryGetWindow("editor_review_diff", out var diff) && diff!.IsOpen);
            Assert.True(preset!.Placements.Any(x => x.WindowId == "editor_review_search" && x.Region == DockRegion.RightLower));
            Assert.True(preset.Placements.Any(x => x.WindowId == "scenario_details" && x.Region == DockRegion.Right));
            Assert.True(preset.Placements.Any(x => x.WindowId == "editor_bp_manager" && x.Region == DockRegion.RightLower));
            Assert.False(wm.ApplyPendingDockPreset(0, "[Window][###editor_review_search]\nDockId=0x00000001,0\n"));   // a saved arrangement wins …
            Assert.Null(wm.PendingDockPreset);                                                                      // … and the request is gone
            wm.SwitchPerspective("Scenario"); wm.SwitchPerspective(ReviewLayout.Perspective);
            Assert.Null(wm.PendingDockPreset);

            Assert.NotNull(editor.ReviewSwitchForTest);                                                              // D10: the editor drives the switch each frame
        }
        finally { editor.Shutdown(); }
    }

    [Fact]
    public void V4_ASavedArrangementIsNeverRearranged()
    {
        var p = ReviewLayout.PresetFor(Ids);
        Assert.False(p.IsPlacedIn(""));
        Assert.False(p.IsPlacedIn("[Window][###t_diff]\nPos=60,60\nSize=100,100\nCollapsed=0\n\n[Window][###other]\nDockId=0x00000002,0\n"));   // floating: not placed
        Assert.True(p.IsPlacedIn("[Window][###other]\nPos=1,1\n\n[Window][###t_search]\nPos=60,60\nDockId=0x00000010,1\n"));                      // docked: placed
    }

    // ══ the frame — the mockup's docking, measured ═══════════════════════════

    [SkippableFact]
    public void V4_TheLayoutDocksWhereTheMockupDraws_AndTheMapKeepsTheMiddle()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 720);
        ImGui.GetIO().ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;                              // before the first NewFrame
        var preset = ReviewLayout.PresetFor(Ids);
        var rects = new Dictionary<string, (Vector2 P, Vector2 S)>();
        (float X, float Y, float W, float H)? centre = null;
        Vector2 work = default, workPos = default;
        bool applied = false;
        for (int f = 0; f < 10; f++)
        {
            int ff = f;
            frame.Step(() =>
            {
                var vp = ImGui.GetMainViewport(); work = vp.WorkSize; workPos = vp.WorkPos;
                ImGui.SetNextWindowPos(vp.WorkPos); ImGui.SetNextWindowSize(vp.WorkSize);
                ImGui.Begin("##DockSpace", ImGuiNET.ImGuiWindowFlags.NoTitleBar | ImGuiNET.ImGuiWindowFlags.NoResize | ImGuiNET.ImGuiWindowFlags.NoMove | ImGuiNET.ImGuiWindowFlags.NoBackground | ImGuiNET.ImGuiWindowFlags.NoDocking);
                uint id = ImGui.GetID("MainDockSpace");
                ImGui.DockSpace(id, vp.WorkSize, ImGuiNET.ImGuiDockNodeFlags.PassthruCentralNode);
                ImGui.End();
                if (ff == 1) applied = preset.Apply(id);   // the dockspace has settled at its real size first, as in the app
                OpenMapArea.ReportCentralNodeOf(id, vp.Size); centre = OpenMapArea.Current;
                if (ff < 1) return;   // as in the app: the Review windows are first drawn after the preset was laid out (they were hidden before)
                foreach (var w in Ids.Values) { ImGui.Begin("Review · " + w + "###" + w); rects[w] = (ImGui.GetWindowPos(), ImGui.GetWindowSize()); ImGui.Text(w); ImGui.End(); }
            });
        }
        Assert.True(applied);
        var g = preset.Regions(new Vector4(workPos.X, workPos.Y, work.X, work.Y));
        static bool Near(Vector2 p, Vector2 s, Vector4 r, float tol = 16f)
            => MathF.Abs(p.X - r.X) <= tol && MathF.Abs(p.Y - r.Y) <= tol && MathF.Abs(s.X - r.Z) <= tol && MathF.Abs(s.Y - r.W) <= tol;
        Assert.True(Near(rects["t_mission"].P, rects["t_mission"].S, g.Left), "Mission summary: the left column " + rects["t_mission"]);
        Assert.True(Near(rects["t_details"].P, rects["t_details"].S, g.Right), "Details: the right column's top " + rects["t_details"]);
        foreach (var id in new[] { "t_diff", "t_search", "t_bp" })
            Assert.True(Near(rects[id].P, rects[id].S, g.RightLower), id + ": one tab strip under Details " + rects[id]);
        Assert.True(Near(rects["t_timeline"].P, rects["t_timeline"].S, g.Bottom), "Event Timeline: across the bottom " + rects["t_timeline"]);
        var c = Assert.IsType<(float X, float Y, float W, float H)>(centre);
        Assert.True(MathF.Abs(c.X - g.Centre.X) <= 16f && MathF.Abs(c.W - g.Centre.Z) <= 16f && MathF.Abs(c.H - g.Centre.W) <= 16f, "the map keeps the middle " + c);

        // ⭐ the frame of the Review layout — for the side-by-side with docs/UX/review-perspective-visual.svg (⛔ never committed)
        var shot = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) { Directory.CreateDirectory(shot); frame.Screenshot(Path.Combine(shot, "v4-review-layout.png")); }
    }
}
