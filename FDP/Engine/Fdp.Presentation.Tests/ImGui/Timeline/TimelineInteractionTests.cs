using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Timeline;

/// <summary>
/// ⭐ <c>CE-3330</c> (docs/DESIGN_Replay_Time_Axis_And_Review.md §10; docs/DESIGN_Director_Timeline_Editor.md §2c / §2d) — the timeline's
/// zoom, pan, follow, ruler, snapping and menu as rails on the PURE classes (<see cref="TimelineView"/>, <see cref="TimelineRuler"/>,
/// <see cref="TimelineLayout"/>, <see cref="TimelinePanel.MenuItems"/>), so each defect of §10a is pinned without ImGui.
/// </summary>
public sealed class TimelineInteractionTests
{
    private const long Tick = TimeSpan.TicksPerSecond / 60;   // one 60 Hz frame
    private const long S = TimeSpan.TicksPerSecond;
    private const string Shot = "combat.shot", Hit = "combat.hit";

    private static TimelineIndex Index(int frames, params TimelineEvent[] events)
    {
        var lanes = events.GroupBy(e => e.Lane).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Frame).ToArray());
        var ticks = Enumerable.Range(0, frames).Select(f => f * Tick).ToArray();
        return new TimelineIndex(lanes, ticks, frames, frames);
    }

    private static TimelineEvent E(int frame, string lane, long subject = 1) => new(frame, lane, subject, 0, 0, 0, 0);

    /// <summary>⭐ T1 — a growing extent (a live source, every frame) never resets the view: a fitted view stays fitted, a zoom at the live
    /// edge TAILS (keeps its span, slides with the end), a zoom elsewhere is KEPT. 🔴 Red-proof: <c>OnExtent</c> refitting every change ⇒
    /// the kept window is lost.</summary>
    [Fact]
    public void CE3330_T1_AGrowingExtent_KeepsTheView_FitTailOrWhereTheUserPutIt()
    {
        var v = new TimelineView();
        v.OnExtent(0, 60 * S);
        Assert.Equal(new TimelineWindow(0, 60 * S), v.Window);   // the first extent fits
        v.OnExtent(0, 61 * S);
        Assert.Equal(new TimelineWindow(0, 61 * S), v.Window);   // fitted stays fitted

        v.Zoom(600f, 600f, 0.5);                                 // zoom in at the right edge: the live end stays in view
        long span = v.Window.Span;
        Assert.Equal(61 * S, v.Window.To);
        v.OnExtent(0, 70 * S);
        Assert.Equal((70 * S, span), (v.Window.To, v.Window.Span));   // tailing: slid with the end, same zoom
        Assert.Equal(TimelineFollow.Tail, v.Follow);

        v.Pan(300f, 600f);                                       // pan into the past: the tail lets go
        var kept = v.Window;
        Assert.Equal(TimelineFollow.None, v.Follow);
        v.OnExtent(0, 80 * S);
        Assert.Equal(kept, v.Window);                            // KEPT — not reset, not slid
    }

    /// <summary>
    /// ⭐ <c>CE-4117</c> — a <see cref="TimelineLaneDescriptor.Hidden"/> lane is neither shown nor counted: <c>TimelineLayout.Drawn</c> (the ONE filter the panel's rows and its view model both go through) drops it, and the view model's
    /// per-lane counts hold only the visible lanes. 🔴 Mutation: <c>Drawn</c> returning every lane (or the view model not calling it) ⇒ the hidden lane is counted ⇒ red.
    /// </summary>
    [Fact]
    public void CE4117_AHiddenLane_IsNotShown_AndNotCounted()
    {
        var lanes = new[] { new TimelineLaneDescriptor(Shot, "Shot"), new TimelineLaneDescriptor("state.health", "Health", Hidden: true), new TimelineLaneDescriptor(Hit, "Hit") };
        Assert.Equal(new[] { Shot, Hit }, TimelineLayout.Drawn(lanes).Select(l => l.Id));
        var index = Index(600, E(10, Shot), E(20, "state.health"), E(30, Hit));
        var source = new FakeSource(index);
        var vm = TimelinePanelViewModel.Build("p", "event-timeline", source, null, new TimelineWindow(0, 600 * Tick), lanes);
        Assert.Equal(new[] { Hit, Shot }, vm.Total.Keys.OrderBy(k => k));
        Assert.Equal(2, vm.Total.Count);
    }

    private sealed class FakeSource : ITimelineSource
    {
        public FakeSource(TimelineIndex index) => Index = index;
        public TimelineIndex Index { get; }
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    /// <summary>A window SHOWN before the first extent arrives (a host's jump to an event — R1-d, the frame rail) is kept, not refitted.</summary>
    [Fact]
    public void CE3330_T1_AWindowShownBeforeTheExtent_IsKept()
    {
        var v = new TimelineView();
        v.Show(new TimelineWindow(268 * S, 332 * S));
        v.OnExtent(0, 600 * S);
        Assert.Equal(new TimelineWindow(268 * S, 332 * S), v.Window);
        v.OnExtent(0, 601 * S);
        Assert.Equal(new TimelineWindow(268 * S, 332 * S), v.Window);
    }

    /// <summary>⭐ T5 / T7 — the zoom is proportional to the wheel (a trackpad's tiny delta is a tiny zoom, a notch ≈ 16 %) and stops at
    /// <see cref="TimelineView.MinSpanFrames"/> frames. 🔴 Red-proof: a fixed 0.8 / 1.25 step ⇒ the tiny delta zooms 20 %.</summary>
    [Fact]
    public void CE3330_T5_T7_TheZoom_IsProportional_AndStopsAtTenFrames()
    {
        Assert.InRange(TimelineView.WheelFactor(1f), 0.83, 0.85);       // one notch in
        Assert.InRange(TimelineView.WheelFactor(-1f), 1.18, 1.21);      // one notch out
        Assert.InRange(TimelineView.WheelFactor(0.05f), 0.99, 0.995);   // a trackpad's sliver

        var v = new TimelineView { MinSpan = TimelineLayout.FrameSpacing(Index(3600)) * TimelineView.MinSpanFrames };
        Assert.Equal(10 * Tick, v.MinSpan);
        v.OnExtent(0, 60 * S);
        for (int i = 0; i < 200; i++) v.Zoom(300f, 600f, TimelineView.WheelFactor(1f));
        Assert.Equal(10 * Tick, v.Window.Span);   // ≈ 0.17 s: every frame a distinct column (⛔ before: 0.1 s at most… and 6 frames)
    }

    /// <summary>⭐ T12 — while playing, a playhead that leaves the window PAGES it (the playhead 10 % in); paused, nothing moves.
    /// 🔴 Red-proof: <c>KeepInView</c> a no-op ⇒ the playhead walks out of view.</summary>
    [Fact]
    public void CE3330_T12_PlayingPagesTheWindow_ToKeepThePlayheadInView()
    {
        var v = new TimelineView();
        v.OnExtent(0, 600 * S);
        v.Show(new TimelineWindow(0, 60 * S));
        v.KeepInView(90 * S, playing: false);
        Assert.Equal(new TimelineWindow(0, 60 * S), v.Window);
        v.KeepInView(90 * S, playing: true);
        Assert.Equal(60 * S, v.Window.Span);
        Assert.Equal(90 * S - 6 * S, v.Window.From);
        v.KeepInView(100 * S, playing: true);   // still inside: no jump
        Assert.Equal(84 * S, v.Window.From);
    }

    /// <summary>§10b — fit, the last N (tailing), the range zoom, the off-screen reveal and the zoom HISTORY (Backspace) back through them;
    /// a dragged bracket is clamped and at least the closest zoom wide.</summary>
    [Fact]
    public void CE3330_FitPresetsRangeAndHistory()
    {
        var v = new TimelineView();
        v.OnExtent(0, 600 * S);
        v.Last(30 * S);
        Assert.Equal(new TimelineWindow(570 * S, 600 * S), v.Window);
        v.Range = (100 * S, 110 * S);
        v.ZoomToRange();
        Assert.Equal(new TimelineWindow(100 * S, 110 * S), v.Window);
        v.Reveal(300 * S);
        Assert.Equal(new TimelineWindow(295 * S, 305 * S), v.Window);
        Assert.True(v.Back());
        Assert.Equal(new TimelineWindow(100 * S, 110 * S), v.Window);
        Assert.True(v.Back());
        Assert.Equal(new TimelineWindow(570 * S, 600 * S), v.Window);
        v.Fit();
        Assert.Equal(new TimelineWindow(0, 600 * S), v.Window);

        v.DragTo(new TimelineWindow(-50 * S, 10 * S));   // the bracket dragged past the start: clamped, the span kept
        Assert.Equal(new TimelineWindow(0, 60 * S), v.Window);
        v.DragTo(new TimelineWindow(5 * S, 5 * S));      // an edge dragged onto the other: at least the closest zoom
        Assert.Equal(v.MinSpan, v.Window.Span);
    }

    /// <summary>⭐ T8 — the ruler keeps the HOURS past an hour (⛔ before: <c>m:ss</c> wrapped at 60 min), measured labels never overlap, minor
    /// ticks sit between the majors, and the closest zoom labels the frame. 🔴 Red-proof: the old <c>m\:ss</c> format ⇒ "0:05" for 1:00:05.</summary>
    [Fact]
    public void CE3330_T8_TheRuler_HoursMeasuredLabelsMinorTicksAndFrames()
    {
        Assert.Equal("1:00:05", TimelineRuler.Label(3605 * S, 5 * S, 2 * 3600 * S));
        Assert.Equal("2:05", TimelineRuler.Label(125 * S, 5 * S, 600 * S));
        Assert.Equal("0:01.5", TimelineRuler.Label(S + S / 2, S / 10, 600 * S));
        Assert.Equal("0:01+30", TimelineRuler.Label(S + S / 2, Tick, 600 * S, frameTicks: Tick));

        static float Wide(string s) => s.Length * 9f;
        var ticks = TimelineRuler.Ticks(new TimelineWindow(0, 3 * 3600 * S), 800f, 0, 3 * 3600 * S, Wide);
        var labelled = ticks.Where(t => t.Label != null).ToList();
        Assert.True(labelled.Count >= 3);
        for (int i = 1; i < labelled.Count; i++)
            Assert.True(labelled[i].X >= labelled[i - 1].X + 2 + Wide(labelled[i - 1].Label!), "two ruler labels overlap");
        Assert.Contains(ticks, t => !t.Major);   // minor ticks
        Assert.All(labelled, t => Assert.Matches(@"^\d+:\d\d:\d\d$", t.Label!));   // h:mm:ss over a 3-hour extent

        var close = TimelineRuler.Ticks(new TimelineWindow(10 * S, 10 * S + 10 * Tick), 800f, 0, 600 * S, Wide, frameTicks: Tick);
        Assert.Contains(close, t => t.Label != null && t.Label.Contains('+'));
    }

    /// <summary>§10b snapping, the menu's previous / next — the event nearest the mouse within a few pixels, a time snapped to a frame, the
    /// adjacent event strictly before / after, the frame spacing.</summary>
    [Fact]
    public void CE3330_Snapping_AndTheAdjacentEvent()
    {
        var index = Index(601, E(100, Shot), E(300, Shot, 7), E(305, Hit, 9), E(500, Shot));
        var all = new TimelineWindow(0, 600 * Tick);
        Assert.Equal(7, TimelineLayout.NearestEvent(index, new[] { Shot, Hit }, all, 600f, 302f)!.Value.Subject);
        Assert.Equal(9, TimelineLayout.NearestEvent(index, new[] { Shot, Hit }, all, 600f, 304f)!.Value.Subject);
        Assert.Null(TimelineLayout.NearestEvent(index, new[] { Shot, Hit }, all, 600f, 200f));
        Assert.Equal(300 * Tick, TimelineLayout.SnapToFrame(index, 300 * Tick + Tick / 3));
        Assert.Equal(301 * Tick, TimelineLayout.SnapToFrame(index, 300 * Tick + 2 * Tick / 3));
        Assert.Equal(500, TimelineLayout.Adjacent(index, new[] { Shot }, 300, forward: true)!.Value.Frame);
        Assert.Equal(100, TimelineLayout.Adjacent(index, new[] { Shot }, 300, forward: false)!.Value.Frame);
        Assert.Equal(305, TimelineLayout.Adjacent(index, new[] { Shot, Hit }, 300, forward: true)!.Value.Frame);
        Assert.Null(TimelineLayout.Adjacent(index, new[] { Shot }, 500, forward: true));
        Assert.Equal(Tick, TimelineLayout.FrameSpacing(index));
    }

    private sealed class Cursor : ITimelineCursor
    {
        public long ExtentFrom => 0;
        public long ExtentTo => 600 * Tick;
        public long Now { get; set; }
        public bool CanSeek { get; init; }
        public bool CanPlay { get; init; }
        public bool IsPlaying { get; set; }
        public List<long> Seeks { get; } = new();
        public void Seek(long ticks) { Seeks.Add(ticks); Now = ticks; }
        public void TogglePlay() => IsPlaying = !IsPlaying;
    }

    /// <summary>
    /// ⭐ §9b / R-3059 — the right-click menu's MODE items: where the cursor seeks (a replay, the Replay Browser) Seek here / Play from here /
    /// Seek to this event / the previous and next event in the lane; a live host (no seek) offers no seek at all — ⏳ its "Review from here"
    /// waits on backend's EnterReview; a range adds zoom / clear; every menu can copy the time. ⭐ A seek happens only when an item RUNS.
    /// </summary>
    [Fact]
    public void CE3330_TheMenu_SeeksOnlyWhereTheCursorCan_AndOnlyWhenChosen()
    {
        var index = Index(601, E(100, Shot), E(300, Shot), E(500, Shot));
        var view = new TimelineView();
        view.OnExtent(0, 600 * Tick);
        var replay = new Cursor { CanSeek = true, CanPlay = true };
        var items = TimelinePanel.MenuItems(index, replay, Shot, 300 * Tick, 300, E(300, Shot), "0:05.0", view);
        var labels = items.Select(i => i.Label).ToList();
        Assert.Contains("Seek here (0:05.0)", labels);
        Assert.Contains("Play from here", labels);
        Assert.Contains(labels, l => l.StartsWith("Seek to this event"));
        Assert.Contains("Previous event in this lane", labels);
        Assert.Contains("Next event in this lane", labels);
        Assert.Empty(replay.Seeks);   // building the menu moved nothing

        items.First(i => i.Label == "Next event in this lane").Run();
        Assert.Equal(new[] { 500 * Tick }, replay.Seeks);
        items.First(i => i.Label == "Play from here").Run();
        Assert.Equal(300 * Tick, replay.Now);
        Assert.True(replay.IsPlaying);

        var live = new Cursor { CanSeek = false };
        var liveItems = TimelinePanel.MenuItems(index, live, Shot, 300 * Tick, 300, E(300, Shot), "0:05.0", view).Select(i => i.Label).ToList();
        Assert.DoesNotContain(liveItems, l => l.StartsWith("Seek") || l.StartsWith("Play") || l.Contains("event in this lane"));
        Assert.Contains("Copy the time (0:05.0)", liveItems);

        view.Range = (100 * Tick, 200 * Tick);
        Assert.Contains("Zoom to the range", TimelinePanel.MenuItems(index, live, null, 0, 0, null, "0:00.0", view).Select(i => i.Label));
    }

    private sealed class BreakpointLanes : ITimelineLaneProvider
    {
        public List<TimelineMenuContext> Asked { get; } = new();
        public IReadOnlyList<TimelineLaneDescriptor> Lanes { get; } = new[] { new TimelineLaneDescriptor("trace.bt", "BTree trace") };
        public bool Applies(TimelineLaneContext context) => true;
        public IEnumerable<TimelineMenuItem> MenuItems(TimelineMenuContext context)
        {
            Asked.Add(context);
            yield return new TimelineMenuItem("Set breakpoint here", () => { });
        }
    }

    /// <summary>⭐ R-4087 addendum 4 — the row under the mouse contributes its OWN items through its provider (here a trace lane's
    /// "Set breakpoint here"); a lane of another provider, or no lane, gets none. ⛔ The panel names no lane kind.
    /// 🔴 Red-proof: <c>TimelineLaneCatalog.MenuItems</c> returning nothing ⇒ red.</summary>
    [Fact]
    public void CE3330_TheRowUnderTheMouse_AddsItsProvidersItems()
    {
        var bt = new BreakpointLanes();
        var catalog = new TimelineLaneCatalog().Register(bt);
        var items = catalog.MenuItems(new TimelineMenuContext("trace.bt", 42, 7, null));
        Assert.Equal(new[] { "Set breakpoint here" }, items.Select(i => i.Label));
        Assert.Equal(7, bt.Asked.Single().Frame);
        Assert.Empty(catalog.MenuItems(new TimelineMenuContext("combat.shot", 42, 7, null)));
        Assert.Empty(catalog.MenuItems(new TimelineMenuContext(null, 42, 7, null)));
    }

    /// <summary>A ten-frame recording, one per second, whose clock stood still for frames 3…6 (an operator pause, an edit at frame 5).</summary>
    private static (TimelineIndex Index, Fdp.Core.FlightRecorder.ReplayTimeMap Map) Paused()
    {
        long[] wall = Enumerable.Range(0, 10).Select(i => i * S).ToArray();
        double[] sim = { 0, 1, 2, 3, 3, 3, 3, 4, 5, 6 };
        var flags = new Fdp.Core.FlightRecorder.FrameFlags[10];
        for (int i = 4; i <= 6; i++) flags[i] = Fdp.Core.FlightRecorder.FrameFlags.Halted;
        flags[5] |= Fdp.Core.FlightRecorder.FrameFlags.Changed;
        var lanes = new Dictionary<string, TimelineEvent[]> { [Shot] = new[] { E(2, Shot), E(4, Shot), E(5, Shot), E(8, Shot) } };
        return (new TimelineIndex(lanes, wall, 10, 10), Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, sim, flags));
    }

    /// <summary>
    /// ⭐ T13 (R10, §10b) — on the SIM axis a pause collapses to one instant: the frames inside it share its time (their events cluster
    /// there), it is ONE pause tick, with edits (●); a seek to that instant lands at the pause's END (what changed is shown); on the WALL axis
    /// the same pause is a span. 🔴 Red-proof: <c>ToAxis</c> the identity on Sim ⇒ the pause keeps its 3 s.
    /// </summary>
    [Fact]
    public void CE3330_T13_OnTheSimAxis_APauseCollapses_ASeekLandsAtItsEnd()
    {
        var (index, map) = Paused();
        var sim = TimelineAxis.Sim(map);
        var projected = sim.Project(index);
        Assert.Equal(new long[] { 0, 1, 2, 3, 3, 3, 3, 4, 5, 6 }.Select(x => x * S), projected.FrameTicks);
        Assert.Same(projected, sim.Project(index));   // cached for the same snapshot
        Assert.Equal(index.Lane(Shot).Count, projected.Lane(Shot).Count);

        var pause = Assert.Single(sim.Pauses());
        Assert.Equal((3 * S, 3 * S, true), (pause.From, pause.To, pause.Changed));
        Assert.Equal(6 * S, sim.ToWall(3 * S));        // the paused instant → its LAST frame (what changed during it is shown)
        Assert.Equal(7 * S, sim.ToWall(4 * S));

        var wallPause = Assert.Single(TimelineAxis.Wall.Pauses(map));
        Assert.Equal((3 * S, 7 * S), (wallPause.From, wallPause.To));

        // the two shots inside the pause sit on one instant on Sim ⇒ one cluster of two (addendum 10)
        var marks = TimelineLayout.Marks(projected, new[] { Shot }, new TimelineWindow(0, 6 * S), 600f);
        Assert.Contains(marks, m => m.Count == 2 && m.FromFrame == 4 && m.X0 == 300f);   // the shots of frames 4 and 5 merge at 3 s

        Assert.False(TimelineAxis.Sim(Fdp.Core.FlightRecorder.ReplayTimeMap.Empty).IsSim);   // v6: no sim axis ⇒ wall
    }

    /// <summary>⭐ T13 — the host's cursor seen on the Sim axis: extent and "now" converted, a seek converted back to wall ticks (the host's own
    /// transport, TL-2); and an axis switch keeps the same FRAMES in view (§10b) — the window is mapped through wall time.</summary>
    [Fact]
    public void CE3330_T13_TheCursorOnTheSimAxis_AndASwitchKeepsTheFramesInView()
    {
        var (_, map) = Paused();
        var sim = TimelineAxis.Sim(map);
        var wallCursor = new Cursor { CanSeek = true, Now = 8 * S };
        var c = new AxisCursor(wallCursor, sim);
        Assert.Equal((0L, 6 * S, 5 * S), (c.ExtentFrom, ExtentToOf(c), c.Now));
        c.Seek(3 * S);
        Assert.Equal(6 * S, wallCursor.Seeks.Single());

        var v = new TimelineView();
        v.OnExtent(0, 9 * S);
        v.Show(new TimelineWindow(7 * S, 9 * S));                     // wall 7…9 s = after the pause
        v.Remap(t => sim.ToAxis(TimelineAxis.Wall.ToWall(t)));          // switch to Sim
        v.OnExtent(0, 6 * S);
        Assert.Equal(new TimelineWindow(4 * S, 6 * S), v.Window);      // the same frames: sim 4…6
    }

    private static long ExtentToOf(AxisCursor c) => c.ExtentTo;

    /// <summary>A cursor with a session layer: a mode, a live point, checkpoints, and a review to enter (all in WALL ticks).</summary>
    private sealed class SessionCursor : ITimelineCursor
    {
        public long ExtentFrom => 0;
        public long ExtentTo => 600 * Tick;
        public long Now { get; set; }
        public bool CanSeek { get; init; }
        public TimelineMode Mode { get; init; }
        public bool CanReview { get; init; }
        public long? LivePoint { get; init; }
        public IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark> Checkpoints { get; init; } = Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();
        public List<long> Seeks { get; } = new();
        public List<long> Reviews { get; } = new();
        public void Seek(long ticks) { Seeks.Add(ticks); Now = ticks; }
        public void EnterReview(long wallTicks) => Reviews.Add(wallTicks);
        // ⭐ CE-3330 E5 — the master's loop (WALL ticks)
        public bool CanLoop { get; init; }
        public (long From, long To)? Loop { get; set; }
        public List<(long From, long To)> SetLoops { get; } = new();
        public int Clears { get; private set; }
        public void SetLoop(long fromWall, long toWall) { SetLoops.Add((fromWall, toWall)); Loop = (fromWall, toWall); }
        public void ClearLoop() { Clears++; Loop = null; }
        // ⭐ CE-4097 part two (D21) — the host's run-mode items at the clicked moment
        public List<(long Wall, string Time)> ModeAsked { get; } = new();
        public List<long> ModeRuns { get; } = new();
        public IEnumerable<TimelineMenuItem> ModeMenuItems(long wallTicks, string time)
        {
            ModeAsked.Add((wallTicks, time));
            yield return new TimelineMenuItem($"AAR from {time}", () => ModeRuns.Add(wallTicks));
            yield return new TimelineMenuItem("Take checkpoint here — greyed", () => ModeRuns.Add(-1), Enabled: false);
        }
    }

    /// <summary>
    /// ⭐ CE-4097 part two (AQ108 D21) — the right-click menu carries the HOST's run-mode items for the clicked moment: the panel asks the cursor with the SNAPPED click and its time label, lists what comes
    /// back (greyed stays greyed), and running one calls back with that moment; a cursor with none (the default) adds nothing.
    /// 🔴 Red-proof: <c>MenuItems</c> dropping its <c>cursor.ModeMenuItems(at, time)</c> line ⇒ the labels are missing.
    /// </summary>
    [Fact]
    public void CE4097_D21_TheMenuCarriesTheCursorsRunModeItems_ForTheClickedMoment()
    {
        var index = Index(601, E(100, Shot), E(300, Shot));
        var view = new TimelineView();
        view.OnExtent(0, 600 * Tick);
        var cursor = new SessionCursor { Mode = TimelineMode.Live };

        var items = TimelinePanel.MenuItems(index, cursor, Shot, 300 * Tick, 300, null, "0:05.0", view);

        Assert.Equal((300 * Tick, "0:05.0"), Assert.Single(cursor.ModeAsked));
        Assert.Contains(items, i => i.Label == "AAR from 0:05.0" && i.Enabled);
        Assert.Contains(items, i => i.Label.StartsWith("Take checkpoint here") && !i.Enabled);
        items.First(i => i.Label == "AAR from 0:05.0").Run();
        Assert.Equal(new[] { 300 * Tick }, cursor.ModeRuns);

        // a cursor that adds none (the interface default) leaves the menu exactly as it was
        var plain = Labels(new PlainCursor());
        Assert.DoesNotContain(plain, l => l.StartsWith("AAR"));
        List<string> Labels(ITimelineCursor c) => TimelinePanel.MenuItems(index, c, Shot, 300 * Tick, 300, null, "0:05.0", view).Select(i => i.Label).ToList();
    }

    private sealed class PlainCursor : ITimelineCursor
    {
        public long ExtentFrom => 0;
        public long ExtentTo => 600 * Tick;
        public long Now => 0;
        public bool CanSeek => false;
        public void Seek(long ticks) { }
    }

    /// <summary>
    /// ⭐ CE-3330 E2 (§2e, DAR §9b) — the menu by MODE: a LIVE host that can review offers "Review from here" (and "from this event") and NO seek
    /// (R-3059: a live host seeks by entering a review); a REVIEW offers the seek items plus "Seek to the live point"; a REPLAY the seek items and no
    /// live point. 🔴 Red-proof: the live branch gated on a runtime-false condition ⇒ no review item; the live-point item shown in a replay ⇒ red.
    /// </summary>
    [Fact]
    public void CE3330_E2_TheMenuByMode_LiveReviews_ReviewSeeksToTheLivePoint_ReplaySeeks()
    {
        var index = Index(601, E(100, Shot), E(300, Shot), E(500, Shot));
        var view = new TimelineView();
        view.OnExtent(0, 600 * Tick);
        List<string> Labels(ITimelineCursor c, TimelineEvent? ev = null) =>
            TimelinePanel.MenuItems(index, c, Shot, 300 * Tick, 300, ev, "0:05.0", view).Select(i => i.Label).ToList();

        var live = new SessionCursor { Mode = TimelineMode.Live, CanReview = true };
        var liveItems = TimelinePanel.MenuItems(index, live, Shot, 300 * Tick, 300, E(300, Shot), "0:05.0", view);
        var liveLabels = liveItems.Select(i => i.Label).ToList();
        Assert.Contains(Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft + " DAR from 0:05.0", liveLabels);
        Assert.Contains(liveLabels, l => l.StartsWith(Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft + " DAR from this event"));
        Assert.DoesNotContain(liveLabels, l => l.StartsWith("Seek") || l.StartsWith("Play") || l.Contains("live point"));
        Assert.Empty(live.Reviews);                                           // building the menu entered nothing
        liveItems.First(i => i.Label == Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft + " DAR from 0:05.0").Run();
        liveItems.First(i => i.Label.StartsWith(Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft + " DAR from this event")).Run();
        Assert.Equal(new[] { 300 * Tick, 300 * Tick }, live.Reviews);          // the snapped click, then the event's frame

        Assert.DoesNotContain(Labels(new SessionCursor { Mode = TimelineMode.Live, CanReview = false }), l => l.Contains("DAR"));   // nothing to review

        var review = new SessionCursor { Mode = TimelineMode.Review, CanSeek = true, LivePoint = 550 * Tick };
        var reviewItems = TimelinePanel.MenuItems(index, review, Shot, 300 * Tick, 300, E(300, Shot), "0:05.0", view);
        Assert.Contains("Seek here (0:05.0)", reviewItems.Select(i => i.Label));
        Assert.DoesNotContain(reviewItems.Select(i => i.Label), l => l.Contains("DAR"));
        reviewItems.First(i => i.Label == "⏏ Seek to the live point").Run();
        Assert.Equal(new[] { 550 * Tick }, review.Seeks);

        Assert.DoesNotContain("⏏ Seek to the live point", Labels(new SessionCursor { Mode = TimelineMode.Review, CanSeek = true }));   // live point not known yet

        var replay = new SessionCursor { Mode = TimelineMode.Replay, CanSeek = true, LivePoint = 550 * Tick };
        var replayLabels = Labels(replay);
        Assert.Contains("Seek here (0:05.0)", replayLabels);
        Assert.DoesNotContain("⏏ Seek to the live point", replayLabels);
    }

    /// <summary>⭐ CE-3330 E2 — the Sim-axis view of a session cursor: the live point and the checkpoints convert to the axis, a review's entry
    /// converts BACK to the host's wall ticks (pause-end rule). 🔴 Red-proof: forwarding <c>LivePoint</c> unconverted ⇒ the live point stays wall.</summary>
    [Fact]
    public void CE3330_E2_AxisCursor_ForwardsTheSessionLayer_ConvertedToTheAxis()
    {
        var (_, map) = Paused();
        var sim = TimelineAxis.Sim(map);
        var inner = new SessionCursor
        {
            Mode = TimelineMode.Review, CanReview = true, CanSeek = true, LivePoint = 8 * S,
            Checkpoints = new[]
            {
                new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "early", WallTicks = 2 * S, TotalTime = 2 },
                new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "late", WallTicks = 8 * S, TotalTime = 5 },
            },
        };
        var c = new AxisCursor(inner, sim);
        Assert.Equal(TimelineMode.Review, c.Mode);
        Assert.True(c.CanReview);
        Assert.Equal(5 * S, c.LivePoint);                                       // wall 8 s is sim 5 s
        Assert.Equal(new[] { ("early", 2 * S), ("late", 5 * S) }, c.Checkpoints.Select(m => (m.Label, m.WallTicks)));
        Assert.Same(c.Checkpoints, c.Checkpoints);                              // projected once per list the host hands out

        c.EnterReview(3 * S);                                                   // the paused instant → its END, in wall ticks
        Assert.Equal(new[] { 6 * S }, inner.Reviews);

        Assert.Null(new AxisCursor(new SessionCursor(), sim).LivePoint);        // no live point stays none
    }

    /// <summary>
    /// ⭐ CE-3330 E5 (§2e, DAR §12n) — the loop items by MODE: "Loop this range" only with a range selected, in a review / replay that CanLoop, and it hands the
    /// range (wall) to <c>SetLoop</c>; "Clear the loop" only while a loop exists; neither in Live or on a cursor that cannot loop. Building the menu loops nothing.
    /// 🔴 Red-proof: the mode gate dropped (offered in Live) ⇒ the Live assertion reddens; the range handed swapped/shifted ⇒ the SetLoop assertion reddens.
    /// </summary>
    [Fact]
    public void CE3330_E5_TheLoopItems_AreOfferedOnlyInAReviewOrReplay_WithARange_OrALoop()
    {
        var index = Index(601, E(100, Shot), E(300, Shot), E(500, Shot));
        var view = new TimelineView();
        view.OnExtent(0, 600 * Tick);
        List<TimelineMenuItem> Items(ITimelineCursor c) => TimelinePanel.MenuItems(index, c, Shot, 300 * Tick, 300, null, "0:05.0", view);
        List<string> Labels(ITimelineCursor c) => Items(c).Select(i => i.Label).ToList();

        var review = new SessionCursor { Mode = TimelineMode.Review, CanSeek = true, CanLoop = true };
        Assert.DoesNotContain("Loop this range", Labels(review));            // no range selected
        Assert.DoesNotContain("Clear the loop", Labels(review));             // no loop set

        view.Range = (100 * Tick, 200 * Tick);
        Assert.Contains("Loop this range", Labels(review));
        Assert.Empty(review.SetLoops);                                        // building the menu loops nothing
        Items(review).First(i => i.Label == "Loop this range").Run();
        Assert.Equal(new[] { (100 * Tick, 200 * Tick) }, review.SetLoops);   // the range, in the cursor's ticks

        Assert.Contains("Clear the loop", Labels(review));                    // the loop now exists
        Items(review).First(i => i.Label == "Clear the loop").Run();
        Assert.Equal(1, review.Clears);
        Assert.DoesNotContain("Clear the loop", Labels(review));

        var replay = new SessionCursor { Mode = TimelineMode.Replay, CanSeek = true, CanLoop = true };
        Assert.Contains("Loop this range", Labels(replay));

        var live = new SessionCursor { Mode = TimelineMode.Live, CanReview = true, CanLoop = true, Loop = (1, 2) };   // even a stray loop / flag
        Assert.DoesNotContain(Labels(live), l => l.Contains("Loop") || l.Contains("loop"));
        var none = new SessionCursor { Mode = TimelineMode.None, CanLoop = true, Loop = (1, 2) };
        Assert.DoesNotContain(Labels(none), l => l.Contains("Loop") || l.Contains("loop"));
        Assert.DoesNotContain(Labels(new SessionCursor { Mode = TimelineMode.Review, CanSeek = true, CanLoop = false }), l => l.Contains("oop"));   // the host cannot loop
        view.Range = null;
    }

    /// <summary>⭐ CE-3330 E5 — the Sim-axis view of a cursor's loop: drawn on the axis, set from the axis (a paused instant → its END, like every conversion
    /// back). 🔴 Red-proof: forwarding <c>Loop</c> unconverted ⇒ the bracket stays wall; <c>SetLoop</c> unconverted ⇒ the host receives sim ticks.</summary>
    [Fact]
    public void CE3330_E5_AxisCursor_ConvertsTheLoop_BothWays()
    {
        var (_, map) = Paused();
        var sim = TimelineAxis.Sim(map);
        var inner = new SessionCursor { Mode = TimelineMode.Review, CanLoop = true, Loop = (2 * S, 8 * S) };
        var c = new AxisCursor(inner, sim);
        Assert.True(c.CanLoop);
        Assert.Equal((2 * S, 5 * S), c.Loop);                                  // wall 2 s is sim 2 s, wall 8 s is sim 5 s

        c.SetLoop(3 * S, 5 * S);                                               // sim 3 s is the pause (→ its END, wall 6 s); sim 5 s is wall 8 s
        Assert.Equal(new[] { (6 * S, 8 * S) }, inner.SetLoops);

        c.ClearLoop();
        Assert.Equal(1, inner.Clears);
        Assert.Null(c.Loop);

        Assert.False(new AxisCursor(new SessionCursor(), sim).CanLoop);        // the defaults pass through
    }

    // ── CE-1364 T2 — the track seam (docs/DESIGN_Director_Timeline_Editor.md §10) ──────────────────────────────

    private sealed class FakeTrack : ITimelineTrack
    {
        public string Id => "camera";
        public string Label => "Camera";
        public List<TimelineTrackItem> List { get; } = new();
        public IReadOnlyList<TimelineTrackItem> Items => List;
        public List<string> Calls { get; } = new();
        public bool TakesKeys { get; init; } = true;
        public void Select(string itemId)
        {
            Calls.Add("select " + itemId);
            for (int i = 0; i < List.Count; i++) List[i] = List[i] with { Selected = List[i].Id == itemId };
        }
        public void Move(string itemId, double toSimSeconds, bool commit) => Calls.Add($"move {itemId} {toSimSeconds:0.###} {(commit ? "commit" : "preview")}");
        public void Move(string itemId, double toSimSeconds, bool commit, TrackItemPart part)
        {
            if (part == TrackItemPart.Body) Move(itemId, toSimSeconds, commit);
            else Calls.Add($"move {itemId} {toSimSeconds:0.###} {(commit ? "commit" : "preview")} {part}");
        }
        public bool Key(char key, double atSimSeconds) { Calls.Add($"key {key} {atSimSeconds:0.###}"); return TakesKeys; }
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => new[] { new TimelineMenuItem("Delete key", () => Calls.Add("delete " + itemId)) };
    }

    private static TimelineTrackItem TrackKey(string id, double at, bool selected = false) => new(id, at, null, id, TimelineTrackItemKind.Key, selected);

    /// <summary>⭐ T2 — an item's SIM seconds ⇄ x on BOTH axes. Sim: seconds × ticks. Wall: through the session's map — a key inside the collapsed pause
    /// (sim 3, wall 3…6 s) draws at the pause's wall position (its end, 6 s) and a drop inside the pause converts back to sim 3; no map ⇒ not drawable.
    /// 🔴 Red-proof: <c>AxisTicks</c> returning <c>sim·TPS</c> on Wall ⇒ the pause key lands at 300, not 600.</summary>
    [Fact]
    public void CE1364_T2_AnItem_IsPlacedAndDropped_OnBothAxes_ThroughTheMap()
    {
        var (_, map) = Paused();
        var sim = TimelineAxis.Sim(map);
        var key3 = TrackKey("k3", 3.0);
        var key4 = TrackKey("k4", 4.0);

        var simWindow = new TimelineWindow(0, 6 * S);
        Assert.Equal(300f, TrackLayout.SpanOf(key3, simWindow, 600f, sim, map)!.Value.X0);
        Assert.Equal(400f, TrackLayout.SpanOf(key4, simWindow, 600f, sim, map)!.Value.X0);
        Assert.Equal(4.0, TrackLayout.SimAtX(400f, simWindow, 600f, sim, map)!.Value, 6);

        var wallWindow = new TimelineWindow(0, 9 * S);
        Assert.Equal(600f, TrackLayout.SpanOf(key3, wallWindow, 900f, TimelineAxis.Wall, map)!.Value.X0);   // the pause's wall position (its end)
        Assert.Equal(700f, TrackLayout.SpanOf(key4, wallWindow, 900f, TimelineAxis.Wall, map)!.Value.X0);
        Assert.Equal(3.0, TrackLayout.SimAtX(500f, wallWindow, 900f, TimelineAxis.Wall, map)!.Value, 6);   // a drop inside the pause = the pause's instant
        Assert.Equal(4.0, TrackLayout.SimAtX(700f, wallWindow, 900f, TimelineAxis.Wall, map)!.Value, 6);

        var seg = new TimelineTrackItem("s", 1.0, 3.0, "slow", TimelineTrackItemKind.Segment, false);
        var span = TrackLayout.SpanOf(seg, wallWindow, 900f, TimelineAxis.Wall, map)!.Value;
        Assert.Equal((100f, 600f), (span.X0, span.X1));

        Assert.False(TrackLayout.Drawable(TimelineAxis.Wall, null));   // no map: not placeable
        Assert.Null(TrackLayout.SpanOf(key3, wallWindow, 900f, TimelineAxis.Wall, null));
        Assert.Null(TrackLayout.SimAtX(10f, wallWindow, 900f, TimelineAxis.Wall, null));

        // hit-test: a key within 5 px, else a segment's body, else empty space
        var items = new[] { key4, seg };
        Assert.Equal(0, TrackLayout.HitTest(items, wallWindow, 900f, TimelineAxis.Wall, map, 703f));
        Assert.Equal(1, TrackLayout.HitTest(items, wallWindow, 900f, TimelineAxis.Wall, map, 300f));
        Assert.Equal(-1, TrackLayout.HitTest(items, wallWindow, 900f, TimelineAxis.Wall, map, 800f));
    }

    /// <summary>⭐ T2 — a drop snaps to the nearest recorded FRAME like every seek (the index is the axis's projection). 🔴 Red-proof: <c>snapTo</c> ignored ⇒ 2.4 s.</summary>
    [Fact]
    public void CE1364_T2_ADrop_SnapsToTheNearestFrame()
    {
        var (index, map) = Paused();
        var sim = TimelineAxis.Sim(map);
        var window = new TimelineWindow(0, 6 * S);
        Assert.Equal(2.4, TrackLayout.SimAtX(240f, window, 600f, sim, map)!.Value, 6);
        Assert.Equal(2.0, TrackLayout.SimAtX(240f, window, 600f, sim, map, sim.Project(index))!.Value, 6);
    }

    /// <summary>⭐ T2 (§2c row 2) — a left CLICK on an item only selects it: no Move at all, and the cursor never seeks. Jitter below the threshold is still a click.
    /// 🔴 Red-proof: <c>Press</c> also previewing a Move ⇒ "move" in the calls.</summary>
    [Fact]
    public void CE1364_T2_AClickOnAnItem_OnlySelects_NoMove_NoSeek()
    {
        var track = new FakeTrack();
        track.List.Add(TrackKey("k1", 2.0));
        var cursor = new Cursor { CanSeek = true };
        var g = new TrackGesture();
        g.Press(track, track.Items[0], x: 200f, itemX: 200f);
        g.Update(201.5f, x => x / 100.0);   // jitter
        Assert.False(g.Release());
        Assert.Equal(new[] { "select k1" }, track.Calls);
        Assert.Empty(cursor.Seeks);
        Assert.True(track.Items[0].Selected);
    }

    /// <summary>⭐ T2 — a drag previews Move(commit:false) each update, then EXACTLY ONE Move(commit:true) on release, at the last preview; the grab offset keeps the item
    /// from jumping under the mouse. 🔴 Red-proof: <c>Release</c> committing twice / not at all.</summary>
    [Fact]
    public void CE1364_T2_ADrag_PreviewsEachFrame_ThenCommitsOnce()
    {
        var track = new FakeTrack();
        track.List.Add(TrackKey("k1", 2.0));
        var g = new TrackGesture();
        g.Press(track, track.Items[0], x: 203f, itemX: 200f);        // grabbed 3 px right of the key's centre
        g.Update(253f, x => x / 100.0);                              // x − grab = 250
        g.Update(303f, x => x / 100.0);
        Assert.True(g.Release());                                    // wherever the mouse is
        Assert.Equal(new[] { "select k1", "move k1 2.5 preview", "move k1 3 preview", "move k1 3 commit" }, track.Calls);
        Assert.False(g.Active);
        Assert.False(g.Release());                                   // a second release commits nothing
        Assert.Equal(4, track.Calls.Count);
    }

    /// <summary>⭐ CE-1364 — a segment's two edges are grabbed by the first / last 5 px of its span (a narrow one keeps a body, a key has none); the gesture hands the grabbed
    /// part to every preview and to the one commit, and the grab offset is the EDGE's x. 🔴 Red-proof: <c>HitPart</c> returning Body ⇒ no edge calls; the gesture dropping
    /// the part ⇒ the calls lose the "EndEdge" suffix.</summary>
    [Fact]
    public void CE1364_AnEdgeOfASegment_IsGrabbed_AndTheGestureCarriesThePart()
    {
        var (_, map) = Paused();
        var window = new TimelineWindow(0, 6 * S);
        var seg = new TimelineTrackItem("s", 1.0, 4.0, "seg", TimelineTrackItemKind.Segment, false);   // x 100…400 of 600
        Assert.Equal(TrackItemPart.StartEdge, TrackLayout.HitPart(seg, window, 600f, TimelineAxis.Sim(map), map, 103f));
        Assert.Equal(TrackItemPart.Body, TrackLayout.HitPart(seg, window, 600f, TimelineAxis.Sim(map), map, 250f));
        Assert.Equal(TrackItemPart.EndEdge, TrackLayout.HitPart(seg, window, 600f, TimelineAxis.Sim(map), map, 397f));
        var narrow = seg with { To = 1.03 };                                                           // 3 px wide: no edges
        Assert.Equal(TrackItemPart.Body, TrackLayout.HitPart(narrow, window, 600f, TimelineAxis.Sim(map), map, 101f));
        Assert.Equal(TrackItemPart.Body, TrackLayout.HitPart(TrackKey("k", 1.0), window, 600f, TimelineAxis.Sim(map), map, 100f));

        var track = new FakeTrack();
        track.List.Add(seg);
        var g = new TrackGesture();
        g.Press(track, seg, x: 398f, itemX: 400f, TrackItemPart.EndEdge);   // grabbed 2 px left of the end edge
        g.Update(498f, x => x / 100.0);                                      // x − grab = 500
        Assert.True(g.Release());
        Assert.Equal(new[] { "select s", "move s 5 preview EndEdge", "move s 5 commit EndEdge" }, track.Calls);
    }

    /// <summary>⭐ T2 — a press on EMPTY track space starts nothing: no select, no move, no commit. 🔴 Red-proof: <c>Press</c> starting a gesture without an item.</summary>
    [Fact]
    public void CE1364_T2_ADragOnEmptyTrackSpace_CallsNothing()
    {
        var track = new FakeTrack();
        track.List.Add(TrackKey("k1", 2.0));
        var g = new TrackGesture();
        g.Press(track, null, 50f, 50f);
        g.Update(300f, x => x / 100.0);
        Assert.False(g.Release());
        Assert.Empty(track.Calls);
    }

    /// <summary>⭐ T2 — K / J / L are offered to the track holding the SELECTED item (with the playhead's sim seconds), and to no other; nothing selected ⇒ nobody is
    /// asked. 🔴 Red-proof: <c>OfferKey</c> asking every track.</summary>
    [Fact]
    public void CE1364_T2_K_IsOfferedToTheSelectedItemsTrack_Only()
    {
        var a = new FakeTrack(); a.List.Add(TrackKey("a1", 1.0));
        var b = new FakeTrack(); b.List.Add(TrackKey("b1", 2.0, selected: true));
        Assert.True(TrackLayout.OfferKey(new ITimelineTrack[] { a, b }, 'K', 3.5));
        Assert.Empty(a.Calls);
        Assert.Equal(new[] { "key K 3.5" }, b.Calls);

        b.List[0] = b.List[0] with { Selected = false };   // nothing selected any more
        b.Calls.Clear();
        Assert.True(TrackLayout.OfferKey(new ITimelineTrack[] { a, b }, 'K', 1.0));   // T3: nothing selected ⇒ the first track that takes it
        Assert.Equal(new[] { "key K 1" }, a.Calls); Assert.Empty(b.Calls);
    }
}
