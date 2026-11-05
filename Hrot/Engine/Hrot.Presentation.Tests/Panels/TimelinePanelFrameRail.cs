using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9a) — the Event Timeline in a REAL ImGui frame: the whole run (density
/// strips), a zoomed window (glyphs, clusters with counts), and a half-scanned index (the unscanned part shaded). The index is synthetic —
/// ten minutes shaped like a three-contact firefight — so every lane has something to draw. Screenshots land in
/// <c>HROT_TIMELINE_SHOTS</c> (else the temp folder): evidence for a human, ⛔ never compared as images.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TimelinePanelFrameRail
{
    private const int Hz = 60;
    private const long Tick = TimeSpan.TicksPerSecond / Hz;
    private static readonly long T0 = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc).Ticks;

    private sealed class Life { public static readonly TimelineLaneDescriptor Spawned = ReplicationTimelineLanes.Spawned, Left = ReplicationTimelineLanes.Left; }
    private sealed class Combat { public static readonly TimelineLaneDescriptor Shot = CombatTimelineLanes.Shot, Hit = CombatTimelineLanes.Hit, Blast = CombatTimelineLanes.Blast; }
    private sealed class Behaviour
    {
        public static readonly TimelineLaneDescriptor Changes = BehaviourTimelineLanes.Changes, Orders = BehaviourTimelineLanes.Orders,
            Reports = BehaviourTimelineLanes.Reports, Decisions = BehaviourTimelineLanes.Decisions;
    }

    /// <summary>⭐ R-4079: the lanes the panel draws are the REGISTERED ones — here the shipped catalogue, as the Replay Browser composes it.</summary>
    private static readonly IReadOnlyList<TimelineLaneDescriptor> Lanes = new TimelineLaneCatalog()
        .Register(new ReplicationTimelineLanes()).Register(new CombatTimelineLanes()).Register(new BehaviourTimelineLanes())
        .Lanes(new TimelineLaneContext(Recording: true));

    private sealed class Source : ITimelineSource
    {
        public TimelineIndex Index { get; init; } = TimelineIndex.Empty;
        public float Progress => Index.TotalFrames == 0 ? 0 : (float)Index.ScannedFrames / Index.TotalFrames;
        public bool IsComplete => Index.IsComplete;
        public string? Error => null;
    }

    private sealed class Cursor : ITimelineCursor
    {
        public long ExtentFrom { get; init; }
        public long ExtentTo { get; init; }
        public long Now { get; set; }
        public bool CanSeek => true;
        public void Seek(long ticks) => Now = ticks;
    }

    /// <summary>Ten minutes: twelve units at the start, three contacts (2:00, 4:30, 7:00) with bursts, hits, blasts and deaths, the
    /// commander's orders before each and the reports after, brain changes and decision switches around them.</summary>
    private static TimelineIndex Firefight(int scannedFrames = -1)
    {
        const int frames = 10 * 60 * Hz;
        var rng = new Random(26);
        var events = new List<TimelineEvent>();
        void Add(int f, TimelineLaneDescriptor lane, int who, long other = 0, float v = 0) => events.Add(new TimelineEvent(f, lane.Id, who, other, 0, 0, v));
        int S(double seconds) => (int)(seconds * Hz);

        for (int u = 1; u <= 12; u++) Add(0, Life.Spawned, u);
        foreach (var (start, length, dead) in new[] { (120.0, 40.0, new int[] { 7 }), (270.0, 55.0, new int[] { 3, 9, 11 }), (420.0, 30.0, new int[] { 5 }) })
        {
            Add(S(start - 20), Behaviour.Orders, 1, 2); Add(S(start - 19), Behaviour.Orders, 1, 3);
            Add(S(start - 8), Behaviour.Decisions, 2, 4, 0x2067);
            for (int b = 0; b < (int)(length * 1.5); b++)                 // bursts: a few rounds each, all through the contact
            {
                double t = start + rng.NextDouble() * length;
                int shooter = rng.Next(2, 13);
                for (int r = 0; r < rng.Next(2, 6); r++) Add(S(t + r * 0.12), Combat.Shot, shooter, rng.Next(2, 13), 1);
                if (rng.NextDouble() < 0.18) Add(S(t + 0.4), Combat.Hit, rng.Next(2, 13));
                if (rng.NextDouble() < 0.12) Add(S(t + 0.2), Behaviour.Changes, shooter, 0, 2);
            }
            for (int k = 0; k < 3; k++) Add(S(start + rng.NextDouble() * length), Combat.Blast, rng.Next(2, 13), 0, 0.6f);
            foreach (var d in dead) Add(S(start + length - 4 + rng.NextDouble() * 3), Life.Left, d);
            Add(S(start + length + 5), Behaviour.Reports, 1, 2); Add(S(start + length + 9), Behaviour.Reports, 1, 3);
            Add(S(start + length + 3), Behaviour.Decisions, 2, 6, 0x2067);
        }
        for (int k = 0; k < 40; k++) Add(rng.Next(frames), Behaviour.Changes, rng.Next(1, 13), 0, 1);

        int scanned = scannedFrames < 0 ? frames : scannedFrames;
        var lanes = events.Where(e => e.Frame < scanned).GroupBy(e => e.Lane).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Frame).ToArray());
        var ticks = Enumerable.Range(0, scanned).Select(f => T0 + f * Tick).ToArray();
        return new TimelineIndex(lanes, ticks, scanned, frames);
    }

    private static string ShotPath(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_TIMELINE_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-timeline-rail");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>Renders the panel full-window for a few frames, screenshots it, and counts pixels of <paramref name="lane"/>'s colour.</summary>
    private static int DrawAndShoot(UiFrameSession frame, TimelinePanel panel, ITimelineSource source, ITimelineCursor cursor, string name, TimelineLaneDescriptor lane)
        => DrawAndShoot(frame, panel, source, cursor, name, TimelinePanel.ColorOf(lane));

    private static int DrawAndShoot(UiFrameSession frame, TimelinePanel panel, ITimelineSource source, ITimelineCursor cursor, string name, uint abgr)
    {
        frame.StepN(4, () =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(1280, 300));
            ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
            panel.Draw(source, cursor, Lanes);
            ImGui.End();
        });
        string path = ShotPath(name);
        frame.Screenshot(path);
        var image = Raylib.LoadImage(path);
        try
        {
            uint c = abgr;                                                // ABGR
            int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF), hits = 0;
            for (int y = 0; y < image.Height; y += 2)
                for (int x = 0; x < image.Width; x += 2)
                {
                    var p = Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) < 60) hits++;
                }
            return hits;
        }
        finally { Raylib.UnloadImage(image); }
    }

    [SkippableFact]
    public void CE1302_TheEventTimeline_DrawsTheRun_AZoomedContact_AndAHalfScannedIndex()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var full = new Source { Index = Firefight() };
        var cursor = new Cursor { ExtentFrom = T0, ExtentTo = T0 + (10 * 60 * Hz - 1) * Tick, Now = T0 + 290 * TimeSpan.TicksPerSecond };

        using var frame = UiFrameHarness.Begin(1280, 300);

        var whole = new TimelinePanel();
        int shotPixels = DrawAndShoot(frame, whole, full, cursor, "timeline-1-whole-run.png", Combat.Shot);
        Assert.True(shotPixels > 20, $"the Shot lane drew nothing on the whole run ({shotPixels} px)");

        var zoomed = new TimelinePanel();
        zoomed.Show(new TimelineWindow(T0 + 268 * TimeSpan.TicksPerSecond, T0 + 332 * TimeSpan.TicksPerSecond));   // the second contact
        int destroyedPixels = DrawAndShoot(frame, zoomed, full, cursor, "timeline-2-zoomed-contact.png", Life.Left);
        Assert.True(destroyedPixels > 4, $"the three deaths of the second contact drew nothing ({destroyedPixels} px)");
        Assert.True(zoomed.Window.Span < 70 * TimeSpan.TicksPerSecond, "the zoom did not hold");

        var half = new Source { Index = Firefight(scannedFrames: 5 * 60 * Hz) };
        int halfShots = DrawAndShoot(frame, new TimelinePanel(), half, cursor, "timeline-3-half-scanned.png", Combat.Shot);
        Assert.True(halfShots > 10 && halfShots < shotPixels, $"a half-scanned index draws only its scanned half ({halfShots} vs {shotPixels} px)");
    }

    /// <summary>
    /// ⭐ <c>CE-4117</c> — a lane marked <see cref="TimelineLaneDescriptor.Hidden"/> (the director's five state lanes) is NOT drawn: the same events in the same colour draw pixels as a visible lane and none as a hidden one.
    /// 🔴 Mutation: <c>TimelinePanel.Draw</c> not filtering through <c>TimelineLayout.Drawn</c> ⇒ the hidden lane draws ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE4117_AHiddenLane_IsNotDrawn()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const int frames = 10 * 60 * Hz;
        var events = Enumerable.Range(0, 200).Select(i => new TimelineEvent(i * 170, "state.demo", 1, 0, 0, 0, i)).ToArray();
        var source = new Source { Index = new TimelineIndex(new Dictionary<string, TimelineEvent[]> { ["state.demo"] = events }, Enumerable.Range(0, frames).Select(f => f * Tick).ToArray(), frames, frames) };
        var cursor = new Cursor { ExtentFrom = 0, ExtentTo = (frames - 1) * Tick, Now = 0 };
        var shown = new TimelineLaneDescriptor("state.demo", "Demo", Color: 0xFFFF00FFu);
        using var frame = UiFrameHarness.Begin(1280, 300);

        int Drawn(TimelineLaneDescriptor lane, string shot)
        {
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                new TimelinePanel().Draw(source, cursor, new[] { lane });
                ImGui.End();
            });
            string path = ShotPath(shot);
            frame.Screenshot(path);
            var image = Raylib.LoadImage(path);
            try
            {
                int hits = 0;
                for (int y = 0; y < image.Height; y += 2)
                    for (int x = 0; x < image.Width; x += 2)
                    { var px = Raylib.GetImageColor(image, x, y); if (px.R > 200 && px.G < 80 && px.B > 200) hits++; }
                return hits;
            }
            finally { Raylib.UnloadImage(image); }
        }

        Assert.True(Drawn(shown, "timeline-hidden-0-shown.png") > 20, "control: a visible lane draws its events");
        Assert.Equal(0, Drawn(shown with { Hidden = true }, "timeline-hidden-1-hidden.png"));
    }

    // ── ⭐ CE-4167 part 1 (X2 item styles, X3 tree rows, RV-2 extent from the tracks) ─────────────────────────────────────────────────

    private sealed class SynthTrack : ITimelineTrack
    {
        public SynthTrack(string id, TrackHeader? header, params TimelineTrackItem[] items) { Id = id; Header = header; Items = items; }
        public string Id { get; }
        public string Label => Id;
        public TrackHeader? Header { get; }
        public IReadOnlyList<TimelineTrackItem> Items { get; }
        public void Select(string itemId) { }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<TimelineMenuItem>();
    }

    private static int CountColour(string path, uint abgr, int x0 = 0, int x1 = int.MaxValue, int tol = 24)
    {
        byte r = (byte)(abgr & 0xFF), g = (byte)(abgr >> 8 & 0xFF), b = (byte)(abgr >> 16 & 0xFF);
        var image = Raylib.LoadImage(path);
        try
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
                {
                    var px = Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(px.R - r) <= tol && Math.Abs(px.G - g) <= tol && Math.Abs(px.B - b) <= tol) n++;
                }
            return n;
        }
        finally { Raylib.UnloadImage(image); }
    }

    /// <summary>
    /// ⭐ <c>CE-4167</c> part 1 — a synthetic rules-view-like set of tracks in a REAL frame with NO recording (an empty index, no time map): a two-level tree with statuses,
    /// and one item of every style in its own colour. The tracks draw at all (RV-2), each style leaves its own mark (X2: a hollow dashed outline, hatch lines, a diamond with a tick),
    /// the status text is in its colour (X3), and collapsing the node removes its child row. Screenshots land in <c>HROT_TIMELINE_SHOTS</c> — look at them; ⛔ never committed.
    /// 🔴 Red-proofs: RV-2 — <c>ExtentOfTracks</c> returning null ⇒ "No recording." and nothing drawn · X2 — <c>DrawOf</c> giving <c>Expected</c> a fill ⇒ the interior is no longer empty · X3 — <c>Visible</c> not hiding ⇒ the child status stays.
    /// </summary>
    [SkippableFact]
    public void CE4167_R1_TracksWithStylesAndATree_DrawWithNoRecording()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const uint Magenta = 0xFFFF00FF, Cyan = 0xFFFFFF00, Yellow = 0xFF00FFFF, Green = 0xFF00FF00, Orange = 0xFF0080FF, Purple = 0xFFFF40C0;
        TimelineTrackItem Seg(string id, double a, double b, TimelineTrackItemStyle st, uint col) => new(id, a, b, id, TimelineTrackItemKind.Segment, false, col, st);
        TimelineTrackItem Key(string id, double at, TimelineTrackItemStyle st, uint col) => new(id, at, null, id, TimelineTrackItemKind.Key, false, col, st);
        var tracks = new ITimelineTrack[]
        {
            new SynthTrack("ambush", new TrackHeader(0, "Ambush", "Active 6:10", Orange, "T", Collapsible: true),
                Seg("expected", 20, 70, TimelineTrackItemStyle.Expected, Yellow)),
            new SynthTrack("fire", new TrackHeader(1, "Fire at target", "Acting 2/3", Purple),
                Seg("hatched", 10, 40, TimelineTrackItemStyle.Hatched, Green), Key("pinned", 50, TimelineTrackItemStyle.Pinned, Cyan)),
            new SynthTrack("solid", null, Key("plain", 5, TimelineTrackItemStyle.Solid, Magenta)),
        };
        var source = new Source { Index = TimelineIndex.Empty };
        var cursor = new Cursor { ExtentFrom = 0, ExtentTo = 0, Now = 0 };   // no extent at all: the index is empty
        var panel = new TimelinePanel { Tracks = () => tracks };
        using var frame = UiFrameHarness.Begin(1280, 300);

        string Shoot(TimelinePanel p, ITimelineTrack[] shown, string name)
        {
            p.Tracks = () => shown;
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                p.Draw(source, cursor, Array.Empty<TimelineLaneDescriptor>());
                ImGui.End();
            });
            string path = ShotPath(name);
            frame.Screenshot(path);
            return path;
        }

        string empty = Shoot(new TimelinePanel(), Array.Empty<ITimelineTrack>(), "timeline-r1-0-no-tracks.png");
        Assert.Equal(0, CountColour(empty, Yellow) + CountColour(empty, Cyan) + CountColour(empty, Magenta));   // control: no tracks, no recording ⇒ nothing

        string open = Shoot(panel, tracks, "timeline-r1-1-styles-and-tree.png");
        Assert.True(CountColour(open, Magenta, x0: 240) > 10, "RV-2 / Solid: the plain key draws with no recording");
        Assert.True(CountColour(open, Yellow, x0: 240) > 40, "X2 Expected: the dashed outline is drawn");
        Assert.True(CountColour(open, Green, x0: 240) > 30, "X2 Hatched: the diagonal lines are drawn");
        Assert.True(CountColour(open, Cyan, x0: 240) > 20, "X2 Pinned: the diamond and its tick are drawn");
        Assert.True(CountColour(open, Orange, 0, 240) > 20, "X3: the node's status is drawn in its colour in the label column");
        Assert.True(CountColour(open, Purple, 0, 240) > 20, "X3: the child's status is drawn");
        panel.SetCollapsed("ambush", true);
        string shut = Shoot(panel, tracks, "timeline-r1-2-collapsed.png");
        Assert.Equal(0, CountColour(shut, Purple, 0, 240));   // the child row is gone
        Assert.True(CountColour(shut, Orange, 0, 240) > 20, "the node itself stays, with its status");
        Assert.True(CountColour(shut, Magenta, x0: 240) > 10, "a header-less track below it is not hidden");
    }

    /// <summary>A cursor on the SIM axis over a straight-run map (sim = frame / 60, no pauses), counting its seeks.</summary>
    private sealed class SimCursor : ITimelineCursor
    {
        public long ExtentFrom { get; init; }
        public long ExtentTo { get; init; }
        public long Now { get; set; }
        public bool CanSeek => true;
        public List<long> Seeks { get; } = new();
        public void Seek(long ticks) { Seeks.Add(ticks); Now = ticks; }
        public TimelineAxis Axis { get; init; } = TimelineAxis.Wall;
        public Fdp.Core.FlightRecorder.ReplayTimeMap? TimeMap { get; init; }
    }

    /// <summary>Moves the pointer for the NEXT frame: ImGui's event AND the window's real pointer — rlImGui feeds Raylib's pointer into ImGui every frame and
    /// would otherwise overwrite the injected position with wherever the X pointer sits (measured: the window centre).</summary>
    private static void Pos(ImGuiIOPtr io, float x, float y)
    {
        io.AddMousePosEvent(x, y);
        Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y));
    }

    private sealed class RailTrack : ITimelineTrack
    {
        public string Id => "camera";
        public string Label => "Camera";
        public List<TimelineTrackItem> List { get; } = new();
        public IReadOnlyList<TimelineTrackItem> Items => List;
        public List<string> Calls { get; } = new();
        public List<double> Commits { get; } = new();
        public void Select(string itemId)
        {
            Calls.Add("select " + itemId);
            for (int i = 0; i < List.Count; i++) List[i] = List[i] with { Selected = List[i].Id == itemId };
        }
        public void Move(string itemId, double toSimSeconds, bool commit)
        {
            Calls.Add(commit ? "commit" : "preview");
            int i = List.FindIndex(x => x.Id == itemId);
            List[i] = List[i] with { From = toSimSeconds };   // the track applies the edit; the panel redraws it there
            if (commit) Commits.Add(toSimSeconds);
        }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<TimelineMenuItem>();
    }

    /// <summary>
    /// ⭐ <c>CE-1364</c> T2 (docs/DESIGN_Director_Timeline_Editor.md §10) — a track with two keys in a REAL ImGui frame, driven by injected input
    /// (<c>ImGuiIO.AddMousePosEvent</c> / <c>AddMouseButtonEvent</c> after <c>rlImGui.Begin</c> — precedent: <c>tools/ui-probe/synthetic-input</c>): the keys draw
    /// (key-coloured pixels on screen), a CLICK selects and never seeks, a DRAG previews and then commits ONCE, and the key ends where it was dropped.
    /// 🔴 Red-proof: the row's press gated on a runtime-false condition ⇒ nothing selected, nothing moved.
    /// </summary>
    [SkippableFact]
    public void CE1364_T2_ATrackWithTwoKeys_Draws_AClickSelects_ADragMovesTheKey()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        const int frames = 10 * 60 * Hz;
        var wall = Enumerable.Range(0, frames).Select(f => T0 + f * Tick).ToArray();
        var simT = Enumerable.Range(0, frames).Select(f => f / (double)Hz).ToArray();
        var map = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, simT, new Fdp.Core.FlightRecorder.FrameFlags[frames]);
        var cursor = new SimCursor { ExtentFrom = T0, ExtentTo = wall[^1], Now = T0, Axis = TimelineAxis.Sim(map), TimeMap = map };
        var source = new Source { Index = Firefight() };

        var track = new RailTrack();
        track.List.Add(new TimelineTrackItem("a", 100.0, null, "key a", TimelineTrackItemKind.Key, false));
        track.List.Add(new TimelineTrackItem("b", 300.0, null, "key b", TimelineTrackItemKind.Key, false));
        var panel = new TimelinePanel { Tracks = () => new ITimelineTrack[] { track } };

        using var frame = UiFrameHarness.Begin(1280, 300);
        int step = 0;
        Vector2 target = default;
        double before = 0;
        void Step(Action<ImGuiIOPtr>? inject)
        {
            frame.Step(() =>
            {
                if (inject != null) inject(ImGui.GetIO());
                ImGui.SetNextWindowPos(new Vector2(0, 0));
                ImGui.SetNextWindowSize(new Vector2(1280, 300));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                panel.Draw(source, cursor, Lanes);
                ImGui.End();
            });
            step++;
        }
        for (int i = 0; i < 5; i++) Step(null);   // settle

        Assert.Equal(2, panel.LastTrackHits.Count);
        var a = panel.LastTrackHits.Single(h => h.ItemId == "a");
        var b = panel.LastTrackHits.Single(h => h.ItemId == "b");
        Assert.True(b.X > a.X + 100f, "the two keys (100 s and 300 s) did not draw apart");
        frame.Screenshot(ShotPath("timeline-6-track-keys.png"));

        // ── a click on key a: selects, never seeks ──
        target = new Vector2(a.X, a.Y);
        Step(io => Pos(io, target.X, target.Y));
        Step(io => Pos(io, target.X, target.Y));
        Step(io => { Pos(io, target.X, target.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, target.X, target.Y));
        Step(io => { Pos(io, target.X, target.Y); io.AddMouseButtonEvent(0, false); });
        Step(null); Step(null);
        Assert.Equal(new[] { "select a" }, track.Calls);
        Assert.Empty(cursor.Seeks);
        Assert.Equal(0, track.Commits.Count);
        before = track.List[0].From;

        // ── a drag of key a by +80 px: previews, then ONE commit, and it moved ──
        a = panel.LastTrackHits.Single(h => h.ItemId == "a");
        target = new Vector2(a.X, a.Y);
        Step(io => Pos(io, target.X, target.Y));
        Step(io => { Pos(io, target.X, target.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, target.X, target.Y));
        for (int k = 1; k <= 4; k++) { float dx = 20f * k; Step(io => Pos(io, target.X + dx, target.Y)); }
        Step(io => { Pos(io, target.X + 80f, target.Y); io.AddMouseButtonEvent(0, false); });
        Step(null); Step(null);
        frame.Screenshot(ShotPath("timeline-7-track-dragged.png"));

        Assert.Equal(1, track.Commits.Count);
        Assert.True(track.Calls.Count(c => c == "preview") >= 2, $"expected previews while dragging: {string.Join(",", track.Calls)}");
        Assert.True(track.Commits[0] > before + 20 && track.Commits[0] < before + 60, $"the key should move ~+40 s (80 px of a 600 s / ~1196 px window): {before} → {track.Commits[0]}");
        Assert.Empty(cursor.Seeks);
        Assert.Equal(300.0, track.List[1].From);   // the other key stayed
    }

    /// <summary>
    /// ⭐ <c>CE-1364</c> T3 (TE-14) — in a REAL ImGui frame, Ctrl+Z on the focused panel calls the host's <c>Undo</c>, Ctrl+Shift+Z and Ctrl+Y call <c>Redo</c>;
    /// a bare Z does nothing, and a panel with NO tracks ignores the chord (so Ctrl+Z is never stolen on a host that has no track edit to undo).
    /// 🔴 Red-proof: the chord's <c>io.KeyShift</c> test inverted ⇒ Ctrl+Shift+Z undoes instead of redoing.
    /// </summary>
    [SkippableFact]
    public void CE1364_T3_CtrlZ_OnTheFocusedPanel_CallsTheHostsUndo()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        const int frames = 10 * 60 * Hz;
        var wall = Enumerable.Range(0, frames).Select(f => T0 + f * Tick).ToArray();
        var simT = Enumerable.Range(0, frames).Select(f => f / (double)Hz).ToArray();
        var map = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, simT, new Fdp.Core.FlightRecorder.FrameFlags[frames]);
        var cursor = new SimCursor { ExtentFrom = T0, ExtentTo = wall[^1], Now = T0, Axis = TimelineAxis.Sim(map), TimeMap = map };
        var source = new Source { Index = Firefight() };
        var track = new RailTrack();
        track.List.Add(new TimelineTrackItem("a", 100.0, null, "key a", TimelineTrackItemKind.Key, false));
        int undos = 0, redos = 0;
        bool withTracks = true;
        var panel = new TimelinePanel
        {
            Tracks = () => withTracks ? new ITimelineTrack[] { track } : Array.Empty<ITimelineTrack>(),
            Undo = () => undos++, Redo = () => redos++,
        };

        using var frame = UiFrameHarness.Begin(1280, 300);
        void Step(Action<ImGuiIOPtr>? inject)
        {
            frame.Step(() =>
            {
                if (inject != null) inject(ImGui.GetIO());
                ImGui.SetNextWindowPos(new Vector2(0, 0));
                ImGui.SetNextWindowSize(new Vector2(1280, 300));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                panel.Draw(source, cursor, Lanes);
                ImGui.End();
            });
        }
        void Chord(ImGuiKey key, bool shift)
        {
            Step(io => { io.AddKeyEvent(ImGuiKey.ModCtrl, true); if (shift) io.AddKeyEvent(ImGuiKey.ModShift, true); });
            Step(io => io.AddKeyEvent(key, true));
            Step(io => io.AddKeyEvent(key, false));
            Step(io => { io.AddKeyEvent(ImGuiKey.ModCtrl, false); io.AddKeyEvent(ImGuiKey.ModShift, false); });
            Step(null);
        }
        for (int i = 0; i < 5; i++) Step(null);

        // focus the panel by clicking its key
        var a = panel.LastTrackHits.Single(h => h.ItemId == "a");
        var at = new Vector2(a.X, a.Y);
        Step(io => Pos(io, at.X, at.Y));
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
        Step(null);

        Step(io => io.AddKeyEvent(ImGuiKey.Z, true)); Step(io => io.AddKeyEvent(ImGuiKey.Z, false)); Step(null);
        Assert.Equal((0, 0), (undos, redos));   // a bare Z

        Chord(ImGuiKey.Z, shift: false);
        Assert.Equal((1, 0), (undos, redos));
        Chord(ImGuiKey.Z, shift: true);
        Assert.Equal((1, 1), (undos, redos));
        Chord(ImGuiKey.Y, shift: false);
        Assert.Equal((1, 2), (undos, redos));

        withTracks = false;
        Step(null); Step(null);
        Chord(ImGuiKey.Z, shift: false);
        Assert.Equal((1, 2), (undos, redos));   // no tracks ⇒ the chord is not ours
    }

    private sealed class SessionCursor : ITimelineCursor
    {
        public long ExtentFrom { get; init; }
        public long ExtentTo { get; init; }
        public long Now { get; set; }
        public bool CanSeek => true;
        public void Seek(long ticks) => Now = ticks;
        public TimelineMode Mode => TimelineMode.Review;
        public long? LivePoint { get; init; }
        public IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark> Checkpoints { get; init; } = Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();
        public (long From, long To)? Loop { get; init; }   // ⭐ CE-3330 E5
        public bool CanLoop => true;
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> E2 (docs/DESIGN_Director_Timeline_Editor.md §2e) — a review's session layer in a REAL ImGui frame: a checkpoint flag on the ruler
    /// (a glyph, tooltip label) and the live point as a vertical line labelled "live" draw without error, and the live line's colour is on screen only
    /// when there is a live point. 🔴 Red-proof: the overlay's live-point branch gated on a runtime-false condition ⇒ no live-coloured pixels.
    /// </summary>
    [SkippableFact]
    public void CE3330_E2_TheSessionLayer_DrawsACheckpointFlagAndTheLivePoint()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var full = new Source { Index = Firefight() };
        long end = T0 + (10 * 60 * Hz - 1) * Tick;
        var withLive = new SessionCursor
        {
            ExtentFrom = T0, ExtentTo = end, Now = T0 + 100 * TimeSpan.TicksPerSecond, LivePoint = T0 + 400 * TimeSpan.TicksPerSecond,
            Checkpoints = new[] { new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "contact-1", WallTicks = T0 + 120 * TimeSpan.TicksPerSecond } },
        };
        var without = new SessionCursor { ExtentFrom = T0, ExtentTo = end, Now = T0 + 100 * TimeSpan.TicksPerSecond };

        using var frame = UiFrameHarness.Begin(1280, 300);
        const uint LiveColour = 0xFF40C0FF;   // the live point's line (ABGR), a colour no lane uses
        int live = DrawAndShoot(frame, new TimelinePanel(), full, withLive, "timeline-4-review-session-layer.png", LiveColour);
        int none = DrawAndShoot(frame, new TimelinePanel(), full, without, "timeline-5-review-no-live-point.png", LiveColour);
        Assert.True(live > none + 10, $"the live point's line did not draw ({live} px with it, {none} without)");
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> E5 (§2e, DAR §12n) — the master's loop in a REAL ImGui frame: a bracket on the ruler and a tinted band over the lanes draw without error,
    /// and the loop's green is on screen only when the cursor reports a loop. 🔴 Red-proof: the ruler bracket / lane band gated on a runtime-false condition
    /// ⇒ no loop-coloured pixels.
    /// </summary>
    [SkippableFact]
    public void CE3330_E5_TheMastersLoop_DrawsABracketAndABand()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var full = new Source { Index = Firefight() };
        long end = T0 + (10 * 60 * Hz - 1) * Tick;
        var looped = new SessionCursor
        {
            ExtentFrom = T0, ExtentTo = end, Now = T0 + 100 * TimeSpan.TicksPerSecond,
            Loop = (T0 + 200 * TimeSpan.TicksPerSecond, T0 + 400 * TimeSpan.TicksPerSecond),
        };
        var unlooped = new SessionCursor { ExtentFrom = T0, ExtentTo = end, Now = T0 + 100 * TimeSpan.TicksPerSecond };

        using var frame = UiFrameHarness.Begin(1280, 300);
        const uint LoopColour = 0xFF60D860;   // the ruler bracket (ABGR) — a colour no lane uses
        int with = DrawAndShoot(frame, new TimelinePanel(), full, looped, "timeline-8-loop.png", LoopColour);
        int without = DrawAndShoot(frame, new TimelinePanel(), full, unlooped, "timeline-9-no-loop.png", LoopColour);
        Assert.True(with > without + 10, $"the loop's bracket did not draw ({with} px with it, {without} without)");
    }

    /// <summary>A track that opts in to X1 / X4: it carries a link, shows a connect handle on its item, says which rows it may be dropped on, and records what was dropped where.</summary>
    private sealed class ConnectTrack : ITimelineTrack
    {
        private readonly bool _optIn; private readonly IReadOnlyList<TrackLink> _links; private readonly Func<ITimelineTrack, bool> _accepts;
        public ConnectTrack(string id, bool optIn, TimelineTrackItem item, IReadOnlyList<TrackLink>? links = null, Func<ITimelineTrack, bool>? accepts = null, TrackHeader? header = null)
        { Id = id; _optIn = optIn; Items = new[] { item }; _links = links ?? Array.Empty<TrackLink>(); _accepts = accepts ?? (_ => true); Header = header; }
        public string Id { get; }
        public string Label => Id;
        public TrackHeader? Header { get; }
        public IReadOnlyList<TimelineTrackItem> Items { get; }
        public IReadOnlyList<TrackLink> Links => _links;
        public List<(string From, string To)> Dropped { get; } = new();
        public bool ConnectsFrom(string itemId) => _optIn;
        public bool CanConnect(string fromItemId, ITimelineTrack toTrack) => _optIn && _accepts(toTrack);
        public void Connect(string fromItemId, ITimelineTrack toTrack) => Dropped.Add((fromItemId, toTrack.Id));
        public void Select(string itemId) { }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<TimelineMenuItem>();
    }

    /// <summary>
    /// ⭐ CE-4221 (R4; RV-1 X1 · X4 · X5) — the panel's connectors, connect drag and Coming up in a REAL frame. An arrow joins two rows (pixels of its colour are drawn; none when the target is collapsed away — the hidden-end rule); a drag from the handle of an
    /// opt-in track onto a row that accepts calls <c>Connect</c> ONCE with the item and the row; a drop on a row that does not accept, or a press on a track that never opted in, calls nothing; a plain (director-like) track shows no handle and no arrow; and
    /// Coming up lists the next Expected / Pinned things only when the host turned it on. ✅ Red-proof: <c>TrackConnectors.Resolve</c> returning nothing ⇒ no arrow ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE4221_R4_ConnectorsConnectDragAndComingUp_InARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const uint LinkColour = 0xFFFF00FF;
        TimelineTrackItem Seg(string id, double a, double b, TimelineTrackItemStyle st = TimelineTrackItemStyle.Solid) => new(id, a, b, id, TimelineTrackItemKind.Segment, false, 0xFF30C0FF, st);
        var a = new ConnectTrack("a", true, Seg("seg", 10, 30), new[] { new TrackLink("seg", "b", "seg", LinkColour) });
        var b = new ConnectTrack("b", false, Seg("seg", 40, 60));
        var c = new ConnectTrack("c", true, Seg("seg", 70, 90), accepts: t => t.Id != "d");
        var d = new ConnectTrack("d", false, Seg("seg", 20, 50, TimelineTrackItemStyle.Expected));    // the plain row: an Expected stretch, no opt-in
        var tracks = new ITimelineTrack[] { a, b, c, d };
        var source = new Source { Index = TimelineIndex.Empty };
        var cursor = new Cursor { ExtentFrom = 0, ExtentTo = 0, Now = 0 };
        var panel = new TimelinePanel { Tracks = () => tracks, ComingUpCount = 3, ComingUpNow = () => 0d };
        using var frame = UiFrameHarness.Begin(1280, 420);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 420));
                    ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                    panel.Draw(source, cursor, Array.Empty<TimelineLaneDescriptor>());
                    ImGui.End();
                });
        }
        void Drag(Vector2 from, Vector2 to)
        {
            Step(io => Pos(io, from.X, from.Y), 2);
            Step(io => { Pos(io, from.X, from.Y); io.AddMouseButtonEvent(0, true); });
            Step(io => Pos(io, (from.X + to.X) / 2, (from.Y + to.Y) / 2));
            Step(io => Pos(io, to.X, to.Y), 2);
            Step(io => { Pos(io, to.X, to.Y); io.AddMouseButtonEvent(0, false); });
            Step(null, 3);
        }
        Vector2 Centre(string track) { var h = panel.LastHandles.First(x => x.TrackId == track); return (h.Min + h.Max) / 2f; }
        float RowY(string track) { var r = panel.LastRowBands.First(x => x.TrackId == track); return (r.Y0 + r.Y1) / 2f; }

        Step(null, 5);
        string shot = ShotPath("timeline-r4-1-connectors.png");
        frame.Screenshot(shot);
        Assert.Single(panel.LastConnectors);                                                              // a -> b, the only link
        Assert.Equal(("a", "b"), (panel.LastConnectors[0].FromTrack, panel.LastConnectors[0].ToTrack));
        Assert.True(CountColour(shot, LinkColour, x0: 240) > 10, "X1: the arrow is drawn in its colour");
        Assert.Equal(new[] { "a", "c" }, panel.LastHandles.Select(x => x.TrackId));                       // X4: a handle only on the tracks that opted in — never on b or d
        Assert.Equal(new[] { "d" }, panel.LastComingUp.Select(x => x.TrackId));                           // X5: the Expected stretch of d — the only plan; its END is when it happens
        Assert.Equal(50d, panel.LastComingUp[0].At);

        Drag(Centre("a"), new Vector2(Centre("a").X, RowY("b")));                                         // a -> b: b accepts (the default accepts every row)
        Assert.Equal(new[] { ("seg", "b") }, a.Dropped);
        Drag(Centre("c"), new Vector2(Centre("c").X, RowY("d")));                                         // c -> d: c's rule refuses d
        Assert.Empty(c.Dropped);
        Drag(Centre("a"), new Vector2(Centre("a").X, RowY("a")));                                         // dropped on its own row: nothing
        Assert.Single(a.Dropped);
        var onB = panel.LastTrackHits.First(h => h.TrackId == "b");
        Drag(new Vector2(onB.X + 4, onB.Y), new Vector2(onB.X + 4, RowY("a")));                           // a drag that starts on b (no handle): not a connect
        Assert.Empty(b.Dropped); Assert.Single(a.Dropped);

        // ⭐ the hidden-end rule: collapse the node that holds the target ⇒ the arrow is not drawn
        var node = new ConnectTrack("node", false, Seg("seg", 1, 2), header: new TrackHeader(0, "Node", Collapsible: true));
        var inner = new ConnectTrack("inner", false, Seg("seg", 40, 60), header: new TrackHeader(1, "Inner"));
        var src = new ConnectTrack("src", false, Seg("seg", 10, 30), new[] { new TrackLink("seg", "inner", "seg") }, header: new TrackHeader(0, "Src"));
        var tree = new ITimelineTrack[] { node, inner, src };
        panel.Tracks = () => tree; Step(null, 4);
        Assert.Single(panel.LastConnectors);
        panel.SetCollapsed("node", true); Step(null, 4);
        Assert.Empty(panel.LastConnectors);

        // a host that turned nothing on: no strip, no lines (the director's tracks)
        panel.Tracks = () => new ITimelineTrack[] { d }; panel.ComingUpCount = 0; Step(null, 4);
        Assert.Empty(panel.LastComingUp); Assert.Empty(panel.LastHandles); Assert.Empty(panel.LastConnectors);
    }

    // ── ⭐ CE-4264 / CE-4265 (the November ui batch) ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The CE-1364 rail's set-up: ten minutes on the SIM axis (sim = frame / 60), the firefight index, a seekable cursor at <paramref name="nowSeconds"/>.</summary>
    private static (SimCursor Cursor, Source Source) SimRun(double nowSeconds)
    {
        const int frames = 10 * 60 * Hz;
        var wall = Enumerable.Range(0, frames).Select(f => T0 + f * Tick).ToArray();
        var simT = Enumerable.Range(0, frames).Select(f => f / (double)Hz).ToArray();
        var map = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, simT, new Fdp.Core.FlightRecorder.FrameFlags[frames]);
        return (new SimCursor { ExtentFrom = T0, ExtentTo = wall[^1], Now = T0 + (long)(nowSeconds * Hz) * Tick, Axis = TimelineAxis.Sim(map), TimeMap = map },
                new Source { Index = Firefight() });
    }

    /// <summary>
    /// ⭐ <c>CE-4264</c> — a scrub of the playhead ▼ follows the mouse once it LEAVES the one-row ruler (here: up, over the overview strip, then on to the right), and the release
    /// seeks where the mouse was let go. At a low frame rate one fast move is enough to leave the ruler, and the drag used to freeze there.
    /// 🔴 Red-proof (compiles): <c>DrawRuler</c> returning before <c>DragUpdate</c> when not hovered (the old order) ⇒ the seek lands where the mouse left the ruler.
    /// </summary>
    [SkippableFact]
    public void CE4264_AScrubThatLeavesTheRuler_FollowsTheMouse_AndSeeksAtTheRelease()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (cursor, source) = SimRun(nowSeconds: 120);
        var panel = new TimelinePanel();
        using var frame = UiFrameHarness.Begin(1280, 300);
        void Step(Action<ImGuiIOPtr>? inject)
            => frame.Step(() =>
            {
                inject?.Invoke(ImGui.GetIO());
                ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                panel.Draw(source, cursor, Lanes);
                ImGui.End();
            });
        for (int i = 0; i < 5; i++) Step(null);

        var head = Assert.IsType<Vector2>(panel.LastPlayheadAt);
        var ruler = panel.LastRulerRect;
        float above = ruler.Min.Y - 20f;                                         // over the overview strip: off the ruler, and no row there to carry the drag
        Assert.True(above > 0f, $"the ruler sits too high for the rail ({ruler.Min.Y})");
        Step(io => Pos(io, head.X, head.Y));
        Step(io => { Pos(io, head.X, head.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, head.X + 10f, head.Y));                               // the scrub starts on the ruler ...
        Step(io => Pos(io, head.X + 40f, above));                                // ... a fast move leaves it ...
        for (int k = 1; k <= 3; k++) { float dx = 40f + 80f * k; Step(io => Pos(io, head.X + dx, above)); }   // ... and keeps going off it
        float releaseX = head.X + 280f;
        Step(io => { Pos(io, releaseX, above); io.AddMouseButtonEvent(0, false); });
        Step(null); Step(null);

        Assert.Single(cursor.Seeks);                                             // one seek, at the release (§10c)
        var after = Assert.IsType<Vector2>(panel.LastPlayheadAt);
        Assert.True(Math.Abs(after.X - releaseX) <= 3f, $"the playhead should follow the mouse off the ruler to the release x {releaseX}, it is at {after.X} (the drag stopped where it left the ruler)");
    }

    /// <summary>
    /// ⭐ <c>CE-4265</c> — a span that starts LEFT of the view (the timeline panned left, as in the rules view) is clipped at the lane's left edge: nothing of it lands in the
    /// label column, and the part inside the view still draws. 🔴 Red-proof (compiles): <c>DrawTrack</c> without its <c>PushClipRect</c> ⇒ the span's colour fills the label column.
    /// </summary>
    [SkippableFact]
    public void CE4265_ASpanStartingLeftOfTheView_DoesNotDrawOverTheLabels()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const uint Magenta = 0xFFFF00FF;
        var (cursor, source) = SimRun(nowSeconds: 500);
        var track = new SynthTrack("rule", null,
            new TimelineTrackItem("band", 0.0, 300.0, "a long state", TimelineTrackItemKind.Segment, false, Magenta, TimelineTrackItemStyle.Solid));
        var panel = new TimelinePanel { Tracks = () => new ITimelineTrack[] { track } };
        using var frame = UiFrameHarness.Begin(1280, 300);
        void Draw(int n)
            => frame.StepN(n, () =>
            {
                ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                panel.Draw(source, cursor, Lanes);
                ImGui.End();
            });
        Draw(3);
        long from = panel.View.ExtentFrom, span = panel.View.ExtentTo - from;   // the view's own axis (sim ticks here), ten minutes
        panel.Show(new TimelineWindow(from + span / 4, from + span * 3 / 4));   // 2:30 … 7:30: the span (0 … 5:00) begins 150 s left of the view
        Draw(4);
        string path = ShotPath("timeline-ce4265-span-clipped.png");
        frame.Screenshot(path);

        float laneLeft = panel.LastRulerRect.Min.X;
        Assert.True(CountColour(path, Magenta, (int)laneLeft + 2, (int)laneLeft + 400) > 200, "control: the part of the span inside the view draws");
        Assert.Equal(0, CountColour(path, Magenta, 0, (int)laneLeft - 1));       // nothing of it in the label column
    }
}
