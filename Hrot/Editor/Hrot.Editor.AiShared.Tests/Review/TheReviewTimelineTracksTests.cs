using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Review;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Review;

/// <summary>⭐ G-7 S5 — a recorded value the two nodes' copies of unit 100 disagree on (the Replica differs lane's rail).</summary>
[ComponentId(484)]
public struct ReplicaProbe { public float X; }

/// <summary>
/// ⭐ R-6 / V7 (docs/UX/DESIGN_Review_Perspective.md §4 V7) — the Event Timeline's two review rows: <b>Search hits</b> (a marker per hit of the Replay search panel, at its frame) and <b>Breakpoint stops</b> (a
/// marker per stop this session). N hits ⇒ N markers; a click on a hit runs the panel's own hit path (the host's seek — the seam called with the frame — and a select), a click on a stop seeks through the transport
/// seam (a waypoint) and selects its unit; no markers ⇒ no row; a steady frame costs 0 B.
/// 🔴 Red-proofs (compile): a stop's click not calling the transport's <c>Seek</c> ⇒ the stop rail red; <c>Tracks()</c> rebuilding every frame ⇒ the 0 B rail red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheReviewTimelineTracksTests : IDisposable
{
    private const int Frames = 600;   // one per second, the zero at 1 s
    private static readonly long Zero = TimeSpan.FromSeconds(1).Ticks;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrot-review-tracks-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingFileReader _reader = new();

    public TheReviewTimelineTracksTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { _reader.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Source : ITimelineSource
    {
        public TimelineIndex Index { get; init; } = TimelineIndex.Empty;
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    private static Source Timeline()
    {
        var ticks = Enumerable.Range(0, Frames).Select(f => Zero + (long)f * TimeSpan.TicksPerSecond).ToArray();
        var sim = Enumerable.Range(0, Frames).Select(f => (double)f).ToArray();
        return new Source { Index = new TimelineIndex(new Dictionary<string, SharedList<TimelineEvent>>(), new SharedList<long>(ticks), Frames, Frames, null, new SharedList<double>(sim)) };
    }

    private sealed class Transport : IReplayTransport
    {
        public readonly List<int> Seeks = new();
        public int Current = 10;
        public bool HasRecording { get; set; } = true;
        public int CurrentFrame => Current;
        public int TotalFrames => Frames;
        public void Seek(int frame) { Seeks.Add(frame); Current = frame; }
        public void Step(int delta) { }
        public bool IsPlaying => false;
        public void Play() { }
        public void Pause() { }
        public float Rate { get; set; } = 1f;
        public bool CanPlay => true;
        public string? PlayDisabledReason => null;
        public PlaybackHistoryTracker Waypoints { get; } = new();
        public double SecondsOf(int frame) => frame;
        public void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds) { wallClockTicks = 0; relativeWallSeconds = 0; simSeconds = double.NaN; }
        public bool HasAxisToggle => false;
        public bool AxisIsSim => false;
        public bool HasSimAxis => false;
        public bool SimAxisActive => false;
        public void ToggleAxis() { }
    }

    /// <summary>A node file of 4 frames with unit 100; returns its path.</summary>
    private string Record()
    {
        string path = Path.Combine(_dir, "node_1.fdp");
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new NetworkIdentity { Value = 100 });
        using var rec = new AsyncRecorder(path, new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)Guid.NewGuid(), NodeId = (Fdp.Core.NodeId)1 });
        long t0 = TimeSpan.FromSeconds(1).Ticks;
        rec.CaptureKeyframe(repo, t0, blocking: true, eventBus: repo.Bus);
        for (uint f = 1; f <= 3; f++) { repo.Tick(); rec.CaptureFrame(repo, f - 1, t0 + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus); }
        return path;
    }

    private sealed class Fixture
    {
        public readonly Transport Transport = new();
        public readonly List<long> Seeks = new();
        public readonly List<int> Selects = new();
        public string[] Files = Array.Empty<string>();
        public FileReview Review = null!;
        public readonly ReviewStopLog Stops = new();
        public ReviewTimelineTracks Tracks = null!;
        public Source Source = Timeline();

        public Fixture(RecordingFileReader reader, string path)
        {
            Files = new[] { path };
            Review = new FileReview(new ReviewFileServices(
                RecordingFiles: () => Files, CurrentFrame: () => Transport.Current, Seek: f => { Seeks.Add(f); Transport.Seek((int)f); },
                Selection: () => null, Select: id => { Selects.Add(id); return true; },
                Serializer: new ScenarioSerializerBuilder("test").Build(), Behaviors: null, Blueprints: null, Reader: reader,
                History: () => Transport.Waypoints));
            Tracks = new ReviewTimelineTracks(() => Source, Review.SearchPanel, Stops, () => Transport, Selects.Add);
        }
    }

    private static SearchResultDto Hit(int frame, Entity e, string context) => new(frame, 0, e, context);

    private Entity ReaderUnit(string path, int frame)
    {
        var ctx = _reader.Of(path)!;
        ctx.SeekToFrame(frame, suppressHistory: true);
        var e = NetworkIdResolver.FindEntityByNetworkId(ctx.SandboxRepo, 100);
        Assert.False(e.IsNull);
        return e;
    }

    [Fact]
    public void V7_NHits_AreNMarkers_AtTheirFrames_AndNoRowWhenThereAreNone()
    {
        string path = Record();
        var f = new Fixture(_reader, path);
        Assert.Empty(f.Tracks.Tracks());                                           // no search yet ⇒ no row

        var unit = ReaderUnit(path, 2);
        f.Review.SearchPanel.SetResultsForTest(new[] { Hit(2, unit, "Health < 10"), Hit(3, unit, "Health < 5"), Hit(40, unit, "Health < 1") });
        var rows = f.Tracks.Tracks();
        Assert.Equal(new[] { ReviewTimelineTracks.HitsId }, rows.Select(r => r.Id));
        Assert.Equal("Search hits", rows[0].Header!.Text);
        Assert.Equal("3", rows[0].Header!.Status);
        var items = rows[0].Items;
        Assert.Equal(new[] { 2d, 3d, 40d }, items.Select(i => i.From));            // sim seconds = frames here
        Assert.All(items, i => Assert.Equal(ReviewTimelineTracks.HitColour, i.Colour));
        Assert.Contains("Health < 10", items[0].Label);                             // the label says what matched

        f.Review.SearchPanel.SetResultsForTest(Array.Empty<SearchResultDto>());    // a new search with nothing found
        Assert.Empty(f.Tracks.Tracks());                                           // ⇒ the row is gone
    }

    [Fact]
    public void V7_AClickOnAHit_SeeksTheHostToItsFrame_AsAWaypoint_AndSelectsItsEntity()
    {
        string path = Record();
        var f = new Fixture(_reader, path);
        var unit = ReaderUnit(path, 2);
        f.Review.SearchPanel.SetResultsForTest(new[] { Hit(2, unit, "a"), Hit(3, unit, "b") });
        var row = f.Tracks.Tracks()[0];

        row.Select(row.Items[1].Id);                                               // a click on the 2nd marker

        Assert.Equal(new long[] { 3 }, f.Seeks);                                   // ⭐ the host's ONE seek, called with the frame
        Assert.Equal(new[] { 100 }, f.Selects);                                    // and its unit selected (the reader entity's network id)
        Assert.True(f.Transport.Waypoints.CanGoBack);                              // a jump is a history waypoint: where it was, then where it went
    }

    [Fact]
    public void V7_TheStopsRow_GrowsOnAHit_AndAClickSeeksThroughTheTransport_AndSelects()
    {
        var f = new Fixture(_reader, Record());
        Assert.Empty(f.Tracks.Tracks());

        f.Stops.Add(Zero + 5 * TimeSpan.TicksPerSecond + 1234, "Alpha · Health < 10", 100);
        var rows = f.Tracks.Tracks();
        Assert.Equal(new[] { ReviewTimelineTracks.StopsId }, rows.Select(r => r.Id));
        Assert.Single(rows[0].Items);

        f.Stops.Add(Zero + 30 * TimeSpan.TicksPerSecond, "Bravo · Fuel = 0", 0);
        rows = f.Tracks.Tracks();                                                  // grew on the second stop
        Assert.Equal(2, rows[0].Items.Count);
        Assert.Equal(new[] { 5, 30 }, f.Tracks.StopFrames);                        // placed by the stop's wall ticks
        Assert.Equal(new[] { 5d, 30d }, rows[0].Items.Select(i => i.From));
        Assert.All(rows[0].Items, i => Assert.Equal(ReviewTimelineTracks.StopColour, i.Colour));

        rows[0].Select(rows[0].Items[0].Id);
        Assert.Equal(new[] { 5 }, f.Transport.Seeks);                              // ⭐ THE seek (the transport seam), called with the frame
        Assert.True(f.Transport.Waypoints.CanGoBack);
        Assert.Equal(new[] { 100 }, f.Selects);

        rows[0].Select(rows[0].Items[1].Id);                                       // a stop with no unit: seeks, selects nothing
        Assert.Equal(new[] { 5, 30 }, f.Transport.Seeks);
        Assert.Equal(new[] { 100 }, f.Selects);
    }

    [Fact]
    public void V7_ASteadyFrame_RebuildsNothing_AndCostsZeroBytes()
    {
        string path = Record();
        var f = new Fixture(_reader, path);
        f.Review.SearchPanel.SetResultsForTest(Enumerable.Range(0, 200).Select(i => Hit(i + 1, ReaderUnit(path, 2), "x" + i)).ToArray());
        f.Stops.Add(Zero + TimeSpan.TicksPerSecond, "s", 100);
        var first = f.Tracks.Tracks()[0].Items[0];
        for (int i = 0; i < 20; i++) f.Tracks.Tracks();                           // warm
        long b = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) f.Tracks.Tracks();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - b;
        Assert.Equal(0, bytes);
        Assert.Same(first, f.Tracks.Tracks()[0].Items[0]);                        // nothing rebuilt
    }

    /// <summary>⭐ V7's frame — both rows on the real Event Timeline panel; both colours are drawn. Renders <c>review-timeline.png</c> under <c>HROT_RAIL_SHOTS</c>.</summary>
    [SkippableFact]
    public void V7_BothRowsOnARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string path = Record();
        var f = new Fixture(_reader, path);
        var unit = ReaderUnit(path, 2);
        f.Review.SearchPanel.SetResultsForTest(new[] { Hit(60, unit, "Health < 10"), Hit(200, unit, "Health < 5"), Hit(430, unit, "Health < 1") });
        f.Stops.Add(Zero + 120L * TimeSpan.TicksPerSecond, "Alpha · Health < 10", 100);
        f.Stops.Add(Zero + 350L * TimeSpan.TicksPerSecond, "Bravo · Fuel = 0", 0);
        var panel = new TimelinePanel { Tracks = () => Array.Empty<ITimelineTrack>() };
        panel.AddTracks(f.Tracks.Tracks);
        var wall = Enumerable.Range(0, Frames).Select(i => Zero + (long)i * TimeSpan.TicksPerSecond).ToArray();
        var simCol = Enumerable.Range(0, Frames).Select(i => (double)i).ToArray();
        var cursor = new Cursor { ExtentFrom = Zero, ExtentTo = Zero + (Frames - 1) * TimeSpan.TicksPerSecond, Now = Zero + 300 * TimeSpan.TicksPerSecond,
            TimeMap = ReplayTimeMap.FromFrames(wall, simCol, new FrameFlags[Frames]) };
        var lanes = new TimelineLaneCatalog().Register(new Fdp.Toolkit.Behavior.Diagnostics.BehaviourTimelineLanes()).Lanes(new TimelineLaneContext(Recording: true));
        using var frame = UiFrameHarness.Begin(1200, 240);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        frame.StepN(5, () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(1200, 240));
            ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
            panel.Draw(f.Source, cursor, lanes);
            ImGui.End();
        });
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-review-rail");
        Directory.CreateDirectory(dir);
        string shot = Path.Combine(dir, "review-timeline.png");
        frame.Screenshot(shot);
        var image = Raylib.LoadImage(shot);
        int blue = 0, red = 0;
        try
        {
            int Count(uint c)
            {
                int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF), n = 0;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++) { var p = Raylib.GetImageColor(image, x, y); if (Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) < 40) n++; }
                return n;
            }
            blue = Count(ReviewTimelineTracks.HitColour); red = Count(ReviewTimelineTracks.StopColour);
        }
        finally { Raylib.UnloadImage(image); }
        Assert.True(blue > 10, $"the Search hits markers are drawn ({blue} px)");
        Assert.True(red > 10, $"the Breakpoint stops markers are drawn ({red} px)");
    }


    // ── S5 · I5 — the Replica differs lane (§6.12 S5, M-5; mockup ②) ───────────────────────────────────────

    /// <summary>Two node files of one exercise: unit 100 owned by node 1 (X = frame) and held by node 2, whose copy lags at frames 2 and 3 (X = frame + 5).</summary>
    private (string P1, string P2) RecordReplicaPair()
    {
        var exercise = Guid.NewGuid();
        string Rec(int node, Func<int, float> x)
        {
            string path = Path.Combine(_dir, $"replica_{node}.fdp");
            var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)exercise, NodeId = (Fdp.Core.NodeId)node };
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>(); repo.RegisterComponent<NetworkAuthority>(); repo.RegisterComponent<ReplicaProbe>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity { Value = 100 });
            repo.AddComponent(e, new NetworkAuthority(1, node));
            repo.AddComponent(e, new ReplicaProbe { X = x(0) });
            using (var rec = new AsyncRecorder(path, meta))
            {
                repo.Tick();
                rec.CaptureKeyframe(repo, Zero, blocking: true, eventBus: repo.Bus);
                uint prev = repo.GlobalVersion;
                for (int f = 1; f <= 5; f++)
                {
                    repo.Tick();
                    repo.GetComponentRW<ReplicaProbe>(e).X = x(f);
                    rec.CaptureFrame(repo, prev, Zero + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
                    prev = repo.GlobalVersion;
                }
            }
            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }
        return (Rec(1, f => f), Rec(2, f => f is 2 or 3 ? f + 5 : f));
    }

    /// <summary>
    /// ⭐ G-7 S5 (I5, M-5) — ONE lane <i>Replica differs · &lt;unit&gt;</i> for the SELECTED unit in the MERGED view: a mark at exactly the frames a replica of it differs from its owner (the same ε as Details ▸ By node),
    /// a click seeks there (a waypoint) — and it is absent with no unit selected, and in a part (no manager). 🔴 Red-proof: the scan marking the frames where nothing differs ⇒ red.
    /// </summary>
    private static ReplicaWatchList Watch()
    {
        var w = new ReplicaWatchList();
        w.Add(100, "ReplicaProbe.X", 0, expert: true);   // an expert field watch (the probe is no vocabulary attribute); tolerance default 0.001
        return w;
    }

    [Fact]
    public void G7S5_TheReplicaWatchLane_DrawsASpanOfTheWatchedItem_ClickSeeksToItsStart_AndExistsOnlyForTheSelectedUnitInMerged()
    {
        var (p1, p2) = RecordReplicaPair();
        using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
        manager.SetBaseWallTicks(Zero);
        FederatedReplayManager? merged = manager; int? selected = 100;
        using var lane = new ReplicaWatchLane(new ReplicaViews(new OwnershipSource(), new ScenarioSerializerBuilder("test").Build()), Watch(), Fdp.Toolkit.ReplayBrowser.Federation.InlineReviewChannel.OverManager(() => merged), () => selected, n => "Bravo 2-3");
        var f = new Fixture(_reader, p1);
        var tracks = new ReviewTimelineTracks(() => f.Source, f.Review.SearchPanel, f.Stops, () => f.Transport, f.Selects.Add, replica: lane);

        for (int i = 0; i < 400 && tracks.ReplicaItems.Count == 0; i++) { tracks.Tracks(); System.Threading.Thread.Sleep(25); }   // the scan runs in the background

        Assert.Equal(new[] { 2 }, tracks.ReplicaFrames);                                           // ⭐ ONE span (the copy lags over frames 2–3, 2 s ≫ the 200 ms threshold), starting at frame 2
        var row = Assert.Single(tracks.Tracks(), r => r.Id == ReviewTimelineTracks.ReplicaId);
        Assert.Equal("Replica watch · Bravo 2-3", row.Label);                                      // one lane, named for the unit
        var item = Assert.Single(row.Items);
        Assert.Equal((2d, 3d), (item.From, item.To));                                              // spans from frame 2 to the last frame that disagrees
        Assert.Contains("believed 7 while the owner node 1 had 2", item.Label);                    // the hover: who believed what
        Assert.Contains("click = seek to its start", item.Label);
        row.Select("r0");                                                                          // a click on the span …
        Assert.Equal(new[] { 2 }, f.Transport.Seeks);                                              // … seeks to its start

        lane.Watch.Remove(100, "ReplicaProbe.X");                                                   // nothing watched ⇒ no lane (the default)
        Assert.DoesNotContain(tracks.Tracks(), r => r.Id == ReviewTimelineTracks.ReplicaId);
        lane.Watch.Add(100, "ReplicaProbe.X", 0, expert: true);
        selected = null;                                                                           // no unit selected ⇒ no lane
        Assert.DoesNotContain(tracks.Tracks(), r => r.Id == ReviewTimelineTracks.ReplicaId);
        selected = 100; merged = null;                                                             // a part (no manager) ⇒ no lane
        Assert.DoesNotContain(tracks.Tracks(), r => r.Id == ReviewTimelineTracks.ReplicaId);
    }

    /// <summary>⭐ §6.13 RW-3/RW-4 — the settings popup's two knobs change what the lane shows: a threshold beyond the 2 s span hides it, a tolerance beyond the 5-unit lag hides it, and the eye (Toggle) is what adds the lane.
    /// 🔴 Red-proof: ignore the tolerance in the scan ⇒ the tolerance half stays a span.</summary>
    [Fact]
    public void G7S5_TheWatchSettings_ThresholdAndToleranceChangeTheResult_TheEyeAddsTheLane()
    {
        var (p1, p2) = RecordReplicaPair();
        using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
        manager.SetBaseWallTicks(Zero);
        var watch = new ReplicaWatchList();
        using var lane = new ReplicaWatchLane(new ReplicaViews(new OwnershipSource(), new ScenarioSerializerBuilder("test").Build()), watch, Fdp.Toolkit.ReplayBrowser.Federation.InlineReviewChannel.OverManager(() => manager), () => 100, n => "Bravo 2-3");
        var f = new Fixture(_reader, p1);
        var tracks = new ReviewTimelineTracks(() => f.Source, f.Review.SearchPanel, f.Stops, () => f.Transport, f.Selects.Add, replica: lane);

        void Settle(Func<bool> done, int max = 400) { for (int i = 0; i < max && !done(); i++) { tracks.Tracks(); System.Threading.Thread.Sleep(25); } tracks.Tracks(); }
        Settle(() => false, 20);   // one pass: nothing is watched
        Assert.Empty(tracks.ReplicaItems);                                       // empty by default ⇒ no lane

        Assert.True(watch.Toggle(100, "ReplicaProbe.X", 0, expert: true));        // the eye on
        Settle(() => tracks.ReplicaItems.Count == 1);
        Assert.Single(tracks.ReplicaItems);

        watch.ThresholdSeconds = 5;                                              // the threshold field: a 2 s disagreement is no longer persistent
        Settle(() => false, 60);
        Assert.Empty(tracks.ReplicaItems);

        watch.ThresholdSeconds = 0.2; watch.SetTolerance(100, "ReplicaProbe.X", 0, 10);   // the tolerance field: the 5-unit lag is within 10
        Settle(() => false, 60);
        Assert.Empty(tracks.ReplicaItems);

        watch.SetTolerance(100, "ReplicaProbe.X", 0, 0.5);
        Settle(() => tracks.ReplicaItems.Count == 1);
        Assert.Single(tracks.ReplicaItems);

        Assert.False(watch.Toggle(100, "ReplicaProbe.X", 0, expert: true));       // the eye off ⇒ the lane goes
        tracks.Tracks();
        Assert.Empty(tracks.ReplicaItems);
    }

    /// <summary>
    /// ⭐ §6.13 (mockups ① and ③) — the lane's GEAR (a trailing button on the track header, generic) opens the settings popup; the popup lists the watched items (eye · icon · label · tolerance · ✕), the threshold "(default)" and the
    /// note; the Details eye is one teal/grey glyph per attribute row. Renders <c>watch-settings.png</c> and <c>watch-details-eye.png</c> under <c>HROT_RAIL_SHOTS</c> (never in git).
    /// 🔴 Red-proof: the header carrying no <c>TrailingClick</c> ⇒ the popup never opens.
    /// </summary>
    [SkippableFact]
    public void RW5_TheGearOpensTheSettingsPopup()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (p1, p2) = RecordReplicaPair();
        using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
        manager.SetBaseWallTicks(Zero);
        var watch = new ReplicaWatchList();
        watch.Add(100, "ReplicaProbe.X", 0, expert: true, label: "Probe");   // gives the lane a span (and so its row)
        FederatedReplayManager? shown = manager;
        using var lane = new ReplicaWatchLane(new ReplicaViews(new OwnershipSource(), new ScenarioSerializerBuilder("test").Build()), watch, Fdp.Toolkit.ReplayBrowser.Federation.InlineReviewChannel.OverManager(() => shown), () => 100, n => "Bravo 2-3");
        var f = new Fixture(_reader, p1);
        var tracks = new ReviewTimelineTracks(() => f.Source, f.Review.SearchPanel, f.Stops, () => f.Transport, f.Selects.Add, replica: lane);
        var kit = new ReplicaWatchKit(lane, () => null);
        for (int i = 0; i < 400 && tracks.ReplicaItems.Count == 0; i++) { tracks.Tracks(); System.Threading.Thread.Sleep(25); }
        tracks.Tracks();
        var row = tracks.Tracks().FirstOrDefault(r => r.Id == ReviewTimelineTracks.ReplicaId);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-review-rail");
        Directory.CreateDirectory(dir);
        Assert.NotNull(row);
        Assert.NotNull(row!.Header!.TrailingClick);                                                  // the gear is declared by the row (generic hook)
        Assert.Equal(Fdp.Presentation.Fonts.IconsFontAwesome6.Gear, row.Header.TrailingGlyph);
        tracks.Settings = kit.Settings;   // only now: the popup draws inside an ImGui frame
        row.Header.TrailingClick!();
        shown = null;   // the popup draws without a scan running; the display-only items below are never scanned
        watch.Add(100, "Health.Current", 0, label: "Strength");
        watch.Add(100, "Ammo.*", 1, label: "Rounds · 7.62");
        watch.Add(100, "Position.X", 0, label: "Position");
        watch.Add(100, "ActorCapabilityState.Capabilities", 0, expert: true);


        using (var frame = UiFrameHarness.Begin(640, 260))
        {
            new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
            bool open = false;
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(640, 260));
                ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
                tracks.Tracks();                                                                       // draws the settings popup (once a frame)
                open = ImGui.IsPopupOpen(ReplicaWatchSettings.PopupId);
                ImGui.End();
            });
            Assert.True(open, "the gear's request opened the settings popup");
            frame.Screenshot(Path.Combine(dir, "watch-settings.png"));
        }
    }

    /// <summary>⭐ §6.13 mockup ① — the Details eye is one glyph per ATTRIBUTE row (teal where watched). Renders <c>watch-details-eye.png</c> under <c>HROT_RAIL_SHOTS</c>.</summary>
    [SkippableFact]
    public void RW1_TheDetailsEye_IsRenderedOnAttributeRows()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (p1, p2) = RecordReplicaPair();
        using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
        manager.SetBaseWallTicks(Zero);
        var watch = new ReplicaWatchList();
        watch.Add(100, "Health.Current", 0, label: "Strength");
        using var lane = new ReplicaWatchLane(new ReplicaViews(new OwnershipSource(), new ScenarioSerializerBuilder("test").Build()), watch, Fdp.Toolkit.ReplayBrowser.Federation.InlineReviewChannel.OverManager(() => manager), () => 100, n => "Bravo 2-3");
        var kit = new ReplicaWatchKit(lane, () => null);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-review-rail");
        Directory.CreateDirectory(dir);
        var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var unit = world.CreateEntity();
        world.AddComponent(unit, new EntityInfo { Name = new FixedString64("Bravo 2-3") });
        world.AddComponent(unit, new Fdp.Toolkit.Combat.Components.Health { Current = 0, Max = 300 });
        var decl = new Fdp.Presentation.Forms.FormDecl
        {
            Id = "rail-watch", Title = "Unit", Requires = new[] { typeof(EntityInfo) },
            Fields = new[]
            {
                new Fdp.Presentation.Forms.FormField { Label = "Name", Kind = Fdp.Presentation.Forms.FieldKind.Text, Attribute = "Name", Section = "State" },
                new Fdp.Presentation.Forms.FormField { Label = "Strength", Attribute = "Health.Current", Min = 0, Max = 300 },
            },
        };
        var view = new Fdp.Presentation.Forms.FormView { Trailer = kit.Eye, CaptureLayout = true };
        using (var frame = UiFrameHarness.Begin(600, 200))
        {
            new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
            frame.StepN(5, () =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(600, 200));
                ImGui.Begin("Details — Bravo 2-3", ImGuiWindowFlags.NoCollapse);
                view.Draw(decl, world, new[] { unit }, editable: false);
                ImGui.End();
            });
            frame.Screenshot(Path.Combine(dir, "watch-details-eye.png"));
        }
        Assert.Equal(new[] { 0, 1 }, view.TrailerRects.Select(t => t.Field).ToArray());   // Name and Strength both attribute rows ⇒ an eye each (Strength is watched: teal)
        world.Dispose();
    }

    /// <summary>⭐ the lane on the REAL Event Timeline panel (mockup ②): its amber marks are drawn. Renders <c>g7s5-replica-lane.png</c> under <c>HROT_RAIL_SHOTS</c>.</summary>
    [SkippableFact]
    public void G7S5_TheReplicaWatchLane_OnARealTimelineFrame_DrawsItsSpan()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (p1, p2) = RecordReplicaPair();
        using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
        manager.SetBaseWallTicks(Zero);
        using var lane = new ReplicaWatchLane(new ReplicaViews(new OwnershipSource(), new ScenarioSerializerBuilder("test").Build()), Watch(), Fdp.Toolkit.ReplayBrowser.Federation.InlineReviewChannel.OverManager(() => manager), () => 100, n => "Bravo 2-3");
        var f = new Fixture(_reader, p1);
        var tracks = new ReviewTimelineTracks(() => f.Source, f.Review.SearchPanel, f.Stops, () => f.Transport, f.Selects.Add, replica: lane);
        for (int i = 0; i < 400 && tracks.ReplicaItems.Count == 0; i++) { tracks.Tracks(); System.Threading.Thread.Sleep(25); }
        Assert.NotEmpty(tracks.ReplicaItems);
        var panel = new TimelinePanel { Tracks = () => Array.Empty<ITimelineTrack>() };
        panel.AddTracks(tracks.Tracks);
        var wall = Enumerable.Range(0, Frames).Select(i => Zero + (long)i * TimeSpan.TicksPerSecond).ToArray();
        var simCol = Enumerable.Range(0, Frames).Select(i => (double)i).ToArray();
        var cursor = new Cursor { ExtentFrom = Zero, ExtentTo = Zero + 5 * TimeSpan.TicksPerSecond, Now = Zero + 2 * TimeSpan.TicksPerSecond,
            TimeMap = ReplayTimeMap.FromFrames(wall, simCol, new FrameFlags[Frames]) };
        var lanes = new TimelineLaneCatalog().Register(new Fdp.Toolkit.Behavior.Diagnostics.BehaviourTimelineLanes()).Lanes(new TimelineLaneContext(Recording: true));
        using var frame = UiFrameHarness.Begin(1200, 200);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        frame.StepN(5, () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(1200, 200));
            ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
            panel.Draw(f.Source, cursor, lanes);
            ImGui.End();
        });
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-review-rail");
        Directory.CreateDirectory(dir);
        string shot = Path.Combine(dir, "g7s5-replica-lane.png");
        frame.Screenshot(shot);
        var image = Raylib.LoadImage(shot);
        int amber = 0;
        try
        {
            int r0 = (int)(ReviewTimelineTracks.ReplicaColour & 0xFF), g0 = (int)((ReviewTimelineTracks.ReplicaColour >> 8) & 0xFF), b0 = (int)((ReviewTimelineTracks.ReplicaColour >> 16) & 0xFF);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) { var p = Raylib.GetImageColor(image, x, y); if (Math.Abs(p.R - r0) + Math.Abs(p.G - g0) + Math.Abs(p.B - b0) < 40) amber++; }
        }
        finally { Raylib.UnloadImage(image); }
        Assert.True(amber > 10, $"the Replica watch span is drawn ({amber} px)");
    }

    private sealed class Cursor : ITimelineCursor
    {
        public long ExtentFrom { get; init; }
        public long ExtentTo { get; init; }
        public long Now { get; set; }
        public bool CanSeek => true;
        public void Seek(long ticks) => Now = ticks;
        public Fdp.Core.FlightRecorder.ReplayTimeMap? TimeMap { get; init; }
    }
}
