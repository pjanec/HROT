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
}
