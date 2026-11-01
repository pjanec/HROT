using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-1303</c> P1 (docs/DESIGN_Director_Timeline_Editor.md §11j) — the film director is ONE assembly (<c>Hrot.Presentation</c>) behind ONE
/// registrar, and it needs no editor: a host with no extension and no tabs gets a working runner.
/// </summary>
[Collection(Map3DFrameRail.Serial)]   // FilmStudio is process-wide (CE-1303 P2b: the pilot view is in it too)
public sealed class DirectorPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ce1303-{Guid.NewGuid():N}");

    public void Dispose()
    {
        FilmStudio.Reset();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>The smallest film host: a world-less one that loads nothing and never steps.</summary>
    private sealed class BareHost : IDirectorHost
    {
        public BareHost(TakeStore takes) => Takes = takes;
        public EntityRepository? World => null;
        public void LoadScenario(string scenario) { }
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; }
        public void ExitPreview() => InPreview = false;
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;
        public void StepBy(float seconds) { }
        public double SimTime => 0d;
        public double WallNow => 0d;
        public void Hold(double seconds) { }
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
    }

    [Fact]
    public void CE1303_P1_TheDirectorAssembly_HasNoEditorDependency()
    {
        // ① the assembly that holds the director references no editor assembly
        var asm = typeof(DirectorPack).Assembly;
        var editorRefs = asm.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("Hrot.Editor", StringComparison.Ordinal)).Select(a => a.Name).ToList();
        Assert.True(editorRefs.Count == 0, "Hrot.Presentation references " + string.Join(", ", editorRefs));

        // ② every type of the director lives in that namespace of that assembly
        foreach (var t in new[] { typeof(DirectorRunner), typeof(IDirectorHost), typeof(IRunExtension), typeof(DirectorTakeSession), typeof(DirectorWindow),
                                  typeof(WindowCinema), typeof(MapLayerOverride), typeof(AudioDirectorSound), typeof(FilmFrameSink), typeof(DirectorPack),
                                  typeof(DirectorPackHost), typeof(IDirectorWindowTab) })
        {
            Assert.Equal(asm, t.Assembly);
            Assert.Equal("Hrot.UI.Common.Map3D.Director", t.Namespace);
        }

        // ③ a host with NO extension and NO tabs: Attach yields a runner that plays a trivial reel to its first phase
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        Directory.CreateDirectory(Path.Combine(_root, "s-a"));
        File.WriteAllText(Path.Combine(_root, "s-a", "scenario.json"), "{\"name\":\"s-a\"}");
        store.Save(new Take { Scenario = "s-a", Name = "main", Title = "A", Shots = new[] { new ShotSpec { At = new AtExpr(0.5), Seconds = 0.5, Camera = new CameraSpec { LookAt = new System.Numerics.Vector3(1, 2, 3) } } } });
        var timeline = new Fdp.Presentation.Panels.Timeline.TimelinePanel();
        var pack = DirectorPack.Attach(new DirectorPackHost(new BareHost(store), () => null, () => null, Headless: true, Pilot: () => null, Timeline: timeline));

        Assert.NotNull(timeline.Tracks);                     // the pack wired the host's timeline to the open take
        Assert.Empty(pack.Session.Tracks);
        Assert.Equal(DirectorPhase.Idle, pack.Runner.Phase);
        var problems = pack.Runner.Play(new PlayRequest(Reel.Of("s-a", "main")));
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Assert.Equal(DirectorPhase.Loading, pack.Runner.Phase);
        pack.Tick();                                         // the pack's one per-frame call drives the runner
        Assert.True(pack.Runner.Busy);
        Assert.Null(pack.Runner.Extension);
        Assert.Empty(pack.Window.Tabs);
    }

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (docs/DESIGN_Director_Timeline_Editor.md §11u D1, D3; success condition 1, the Director window's model) — the pack builds ONE reel
    /// composer and ONE render queue and the Director window draws THOSE (not copies of its own); with a reel being composed the window plays and renders the
    /// reel AS IT STANDS (unsaved: the picked saved reel is ignored); its render of one picked take is that take (rendered beside it), of a reel the composed
    /// reel, at the picked size and rate; and on a headless host the queue refuses, so the window's <i>Queue render</i> says why. 🔴 Mutation:
    /// <c>DirectorPack.Attach</c> handing the window <c>composer: null</c> (the window builds its own) ⇒ the window's composer is not the pack's and plays nothing
    /// composed through the pack ⇒ red.
    /// </summary>
    [Fact]
    public void CE1306_TheDirectorWindow_ComposesThePacksReel_AndRendersItAsItStands()
    {
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        var pack = DirectorPack.Attach(new DirectorPackHost(new BareHost(store), () => null, () => null, Headless: true, Pilot: () => null,
                                                            Timeline: new Fdp.Presentation.Panels.Timeline.TimelinePanel()));
        Assert.Same(pack.Composer, pack.Window.Composer);
        Assert.Same(pack.Renders, pack.Window.Renders);

        var saved = new Reel { Name = "saved", Items = new[] { new ReelItem { Scenario = "x", Take = "main" } } };
        Assert.Same(saved, pack.Window.ReelToPlay(saved));                                   // nothing composed: the picked reel
        Assert.Empty(pack.Composer.New("composed"));
        pack.Composer.Add("s-a", "main");
        pack.Composer.Add("s-b", "wide");
        pack.Composer.Move(1, 0);
        var playing = pack.Window.ReelToPlay(saved)!;
        Assert.Equal(new[] { "s-b/wide", "s-a/main" }, playing.Items.Select(i => i.Scenario + "/" + i.Take));
        Assert.True(pack.Composer.Dirty);                                                    // unsaved, and still what plays

        pack.Window.RenderSize = 0;                                                          // 640x360
        pack.Window.RenderRate = 0;                                                          // 15 fps
        var reelRender = pack.Window.RenderRequestFor(playing);
        Assert.Same(playing, reelRender.Reel);
        Assert.Equal((RenderKind.Movie, 640, 360, 15), (reelRender.Kind, reelRender.Width, reelRender.Height, reelRender.Fps));
        var takeRender = pack.Window.RenderRequestFor(Reel.Of("s-a", "main"));
        Assert.Equal(("s-a", "main", (Reel?)null), (takeRender.Scenario, takeRender.Take, takeRender.Reel));

        Assert.Equal(DirectorRenders.NoRenderer, pack.Window.QueueRender(playing));          // headless: refused, with the reason
        Assert.Empty(pack.Renders.Jobs);
    }

    // ── CE-1303 P2a (§11m) — CR-4 on a host that CANNOT seek (the editor's kind: a live run, R-3034) ─────────────────────────────────────

    /// <summary>A live host: a load restarts the run at sim 0, each step is taken at once, a hold advances its wall clock (no sleeping).</summary>
    private sealed class LiveHost : IDirectorHost
    {
        public LiveHost(TakeStore takes)
        {
            Takes = takes;
            World.RegisterComponent<SimTransform>();
            World.RegisterComponent<EntityInfo>();
            World.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        }
        public EntityRepository World { get; } = new();
        EntityRepository? IDirectorHost.World => World;
        public void LoadScenario(string scenario) => SimTime = 0d;
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; }
        public void ExitPreview() => InPreview = false;
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;
        public void StepBy(float seconds) { SimTime += seconds; Steps++; }
        public double SimTime { get; private set; }
        public double WallNow { get; private set; }
        public void Hold(double seconds) { Holds.Add(SimTime); WallNow += Math.Max(seconds, 0d); }
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
        public int Steps;
        /// <summary>The sim second of every held frame.</summary>
        public readonly List<double> Holds = new();
    }

    private static readonly System.Numerics.Vector3 Key1At = new(10f, 0f, 0f), Key2At = new(0f, 30f, 0f);

    private (DirectorPack Pack, LiveHost Host) LivePack()
    {
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        store.Save(new Take
        {
            Scenario = "s-a", Name = "main",
            Keys = new[]
            {
                new CameraKey { At = new AtExpr(1d), Camera = new CameraSpec { LookAt = Key1At } },
                new CameraKey { At = new AtExpr(3d), Camera = new CameraSpec { LookAt = Key2At } },
            },
            EndAt = new AtExpr(5d),
        });
        var host = new LiveHost(store);
        var pack = DirectorPack.Attach(new DirectorPackHost(host, () => null, () => null, Headless: true, Pilot: () => null,
                                                            Timeline: new Fdp.Presentation.Panels.Timeline.TimelinePanel()));
        return (pack, host);
    }

    /// <summary>Frames until the source reaches <paramref name="sim"/>; the film camera then.</summary>
    private static System.Numerics.Vector3? CameraAt(DirectorPack pack, LiveHost host, double sim)
    {
        for (int i = 0; i < 2000 && pack.Runner.Busy && !(pack.Runner.Phase == DirectorPhase.Playing && host.SimTime >= sim - 1e-9); i++) pack.Tick();
        pack.Tick();   // the frame AT sim: its camera is set before its step
        return FilmStudio.Shot?.Pose?.LookAt;
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D4, success condition 3 on an existing host) — the runner plays the OPEN take: an unsaved edit (the cut from 3 s to
    /// 2 s through <c>TakeEditor</c>) is what the NEXT play shows at 2.5 s, and the file keeps 3 s. 🔴 Mutation: <c>DirectorPack.Attach</c> not
    /// setting <c>Runner.OpenTake</c> ⇒ the second play reads the file: key 1 at 2.5 s.
    /// </summary>
    [Fact]
    public void CE1303_P2a_TheNextPlay_ShowsTheUnsavedEdit_OnALiveHost()
    {
        FilmStudio.Reset();
        var (pack, host) = LivePack();
        Assert.Empty(pack.Session.Open("s-a", "main"));
        var request = new PlayRequest(Reel.Of("s-a", "main"));

        Assert.Empty(pack.Runner.Play(request));
        Assert.Equal(Key1At, CameraAt(pack, host, 2.5));
        pack.Runner.Stop();

        Assert.True(pack.Session.Editor!.Apply(new TakeEdit.MoveKey(1, 2d)));
        Assert.True(pack.Session.Editor.Dirty);
        Assert.Empty(pack.Runner.Play(request));
        Assert.Equal(Key2At, CameraAt(pack, host, 2.5));
        pack.Runner.Stop();
        Assert.Equal(3d, pack.Takes.Load("s-a", "main").Value!.Keys[1].At.Offset);
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D5, D6) — on a host that cannot seek, <c>PlayFrom(t)</c> FAST-FORWARDS: one step per frame (never two — R-3038),
    /// no frame held until the source reaches t, every frame held after; and Loop shot is refused once, in the report, while the run plays on.
    /// 🔴 Mutation: the fast-forward flag ignored (every frame held) ⇒ holds before 3.5 s.
    /// </summary>
    [Fact]
    public void CE1303_P2a_PlayFrom_OnALiveHost_FastForwards_AndCannotLoop()
    {
        FilmStudio.Reset();
        var (pack, host) = LivePack();
        pack.Runner.LoopShot = true;
        Assert.Empty(pack.Runner.PlayFrom(new PlayRequest(Reel.Of("s-a", "main")), 3.5));
        long frames = 0;
        while (pack.Runner.Busy && frames < 2000) { pack.Tick(); frames++; }

        var report = pack.Runner.Reports[0];
        Assert.Equal("ok", report.Status);
        Assert.Equal(1, report.MaxStepsInAFrame);                                   // the stepping rule, fast-forward included
        Assert.DoesNotContain(host.Holds, sim => sim < 3.5 - 1e-6);                  // no hold before the start …
        Assert.Contains(host.Holds, sim => sim >= 3.5 && sim < 4.9);                  // … paced from there
        Assert.Contains(report.Problems, p => p.Contains("Loop shot needs a source that seeks"));
        Assert.InRange(host.SimTime, 4.99, 5.05);                                     // it played on to the end (no rewind)
    }

    // ── CE-1303 P2b (§11o) — CR-7 segments and CR-11 subtitles on a host that CANNOT seek ─────────────────────────────────────────────────

    private (DirectorPack Pack, LiveHost Host) LivePackWith(IReadOnlyList<TimeSegment> segments, IReadOnlyList<Subtitle>? subtitles = null)
    {
        var (pack, host) = LivePack();
        var take = pack.Takes.Load("s-a", "main").Value!;
        pack.Takes.Save(take with { Segments = segments, Subtitles = subtitles ?? Array.Empty<Subtitle>(), EndAt = new AtExpr(8d) });
        return (pack, host);
    }

    private static TimeSegment Seg(double from, double to, double speed = 1d) => new() { From = new AtExpr(from), To = new AtExpr(to), Speed = speed };

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-7 on a live host (§11o S3 / S4; R-3034, R-3038): segments [0–1] [3–4] [0.5–1 — back] [4–5]. The gap 1–3 is a
    /// FAST-FORWARD — stepped one step a frame (never two) with no frame held; the back segment cannot be played on a live run: it is skipped and said
    /// ONCE; the run plays [3–4] and [4–5] paced and ends at the last segment's end (not the take's 8 s). The film clock counts the skipped replay's
    /// length (film 3.0 at source 4.5). 🔴 Mutation: <c>AdvanceSegments</c> not skipping a back segment on a host that cannot seek ⇒ the runner waits
    /// in that segment's "gap" (source past its end) — no "goes back" line, and the run never reaches [4–5] paced ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_OnALiveHost_AGapFastForwards_AndABackSegmentIsSkippedOnce()
    {
        FilmStudio.Reset();
        var (pack, host) = LivePackWith(new[] { Seg(0, 1), Seg(3, 4), Seg(0.5, 1), Seg(4, 5) });
        Assert.Empty(pack.Runner.Play(new PlayRequest(Reel.Of("s-a", "main"), Cinema: false)));
        double? filmAt45 = null;
        for (int i = 0; i < 3000 && pack.Runner.Busy; i++)
        {
            if (pack.Runner.Phase == DirectorPhase.Playing && Math.Abs(host.SimTime - 4.5) < 0.5 / 60) filmAt45 = pack.Runner.FilmTime;
            pack.Tick();
        }
        Assert.False(pack.Runner.Busy);
        var report = pack.Runner.Reports[0];
        Assert.True(report.Status == "ok", $"{report.Status}: {string.Join("; ", report.Problems)}");
        Assert.Equal(1, report.MaxStepsInAFrame);                                      // the stepping rule, the fast-forward included
        const double step = 1d / 60;
        Assert.DoesNotContain(host.Holds, sim => sim > 1 + 2 * step && sim < 3 - 2 * step);   // the gap: no frame held
        Assert.Contains(host.Holds, sim => sim > 0.2 && sim < 0.9);                       // … the segments: paced
        Assert.Contains(host.Holds, sim => sim > 3.1 && sim < 3.9);
        Assert.Contains(host.Holds, sim => sim > 4.1 && sim < 4.9);
        Assert.Single(report.Problems, p => p.Contains("segment 3 goes back to 0.5 s"));
        Assert.InRange(host.SimTime, 5d - 1e-9, 5d + 2 * step);                          // the film ends with its last segment
        Assert.NotNull(filmAt45);
        Assert.InRange(filmAt45!.Value, 3.0 - step, 3.0 + step);                         // 1 + 1 + 0.5 (the skipped replay) + 0.5
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-11 (§11o S9) on a live host: the subtitle the cinema overlay draws (<c>WindowCinema.Shown</c>, the model of the drawing,
    /// set every frame even OFF the cinema) is the take's subtitle at the SOURCE second the film shows — with segments [0–1] [2–5 @0.5×], film second 2.0
    /// is source 2.5, inside "Contact" (2–4, top); before it nothing — and an UNSAVED edit of its text is what the next play shows. 🔴 Mutation: the runner
    /// not handing the cinema the subtitle (<c>_cinema.Subtitle(_subNow)</c> removed) ⇒ <c>Shown</c> stays null ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_TheOverlaysSubtitle_IsTheTakesAtTheFilmSecond_AndAnEditShowsNextPlay()
    {
        FilmStudio.Reset();
        var contact = new Subtitle { At = new AtExpr(2d), Until = new AtExpr(4d), Text = "Contact", Position = SubtitlePosition.Top };
        var (pack, host) = LivePackWith(new[] { Seg(0, 1), Seg(2, 5, 0.5) }, new[] { contact });
        Assert.Empty(pack.Session.Open("s-a", "main"));
        var request = new PlayRequest(Reel.Of("s-a", "main"), Cinema: false);

        SubtitleFrame? ShownAtFilm(double film, out double source)
        {
            Assert.Empty(pack.Runner.Play(request));
            for (int i = 0; i < 3000 && pack.Runner.Busy && !(pack.Runner.Phase == DirectorPhase.Playing && pack.Runner.FilmTime >= film - 1e-9); i++) pack.Tick();
            pack.Tick();   // the frame AT film: its subtitle is set before its step
            source = host.SimTime - 1d / 60;
            var shown = pack.Cinema.Shown;
            Assert.Equal(shown, pack.Runner.SubtitleNow);
            pack.Runner.Stop();
            return shown;
        }

        Assert.Null(ShownAtFilm(0.5, out _));
        var at2 = ShownAtFilm(2.0, out double source);
        Assert.InRange(source, 2.5 - 1d / 60, 2.5 + 1d / 60);                           // film 2.0 = source 2.5 (the slow segment)
        Assert.Equal(("Contact", SubtitlePosition.Top), (at2!.Text, at2.Position));
        Assert.False(pack.Cinema.Active);                                                // drawn off the cinema (an iteration pass)

        Assert.True(pack.Session.Editor!.Apply(new TakeEdit.SetSubtitle(0, contact with { Text = "Contact, 300 m" })));
        Assert.Equal("Contact, 300 m", ShownAtFilm(2.0, out _)!.Text);                 // the open take, unsaved (CR-4)
        Assert.Equal("Contact", pack.Takes.Load("s-a", "main").Value!.Subtitles[0].Text);
    }

    // ── CE-4059 (§11q) — a key belongs to a SEGMENT (CR-7) · the drafter wired (P3) ────────────────────────────────────────────────────────

    /// <summary>A recording's kind of host (the Replay Browser's): it SEEKS, each step is taken at once.</summary>
    private sealed class SeekHost : IDirectorHost
    {
        public SeekHost(TakeStore takes)
        {
            Takes = takes;
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        }
        private readonly EntityRepository _world = new();
        public EntityRepository? World => _world;
        public void LoadScenario(string scenario) => SimTime = 0d;
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; }
        public void ExitPreview() => InPreview = false;
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;
        public void StepBy(float seconds) => SimTime += seconds;
        public double SimTime { get; private set; }
        public double WallNow { get; private set; }
        public void Hold(double seconds) => WallNow += Math.Max(seconds, 0d);
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
        public bool CanSeek => true;
        public void Seek(double sourceSim) => SimTime = sourceSim;
    }

    private static readonly System.Numerics.Vector3 ReplayAt = new(-7f, -7f, 0f);

    /// <summary>
    /// ⭐ <c>CE-4059</c> / CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11q K1, success condition 1 — the fake-host rail): segments <c>main</c> [0–5] then
    /// <c>replay</c> [1–2.5] (back), keys 1 s (key 1) · 3 s (key 2) unscoped and a key of the REPLAY at 1.5 s — a source second INSIDE the main pass. Through the
    /// whole main pass the camera never shows the replay's key (1.5–3 s is key 1); in the replay it is key 1 until 1.5 s and the replay's key from there to the
    /// replay's end. 🔴 Mutation: the runner looking the camera up among ALL keys (<c>DirectUpcoming</c> over <c>_keys</c>, not <c>CameraKeys.OnScreenIn</c>) ⇒
    /// the main pass shows the replay's key at 1.5–3 s ⇒ red.
    /// </summary>
    [Fact]
    public void CE4059_CR7_AKeyOfTheReplay_IsSeenOnlyInTheReplay_OnASeekingHost()
    {
        FilmStudio.Reset();
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        store.Save(ScopedTake());
        var host = new SeekHost(store);
        var pack = DirectorPack.Attach(new DirectorPackHost(host, () => null, () => null, Headless: true, Pilot: () => null,
                                                            Timeline: new Fdp.Presentation.Panels.Timeline.TimelinePanel()));
        Assert.Empty(pack.Runner.Play(new PlayRequest(Reel.Of("s-a", "main"), Speed: 1000d, Cinema: false)));
        var seen = new List<(double Sim, double Film, System.Numerics.Vector3? Camera)>();
        for (int i = 0; i < 3000 && pack.Runner.Busy; i++)
        {
            double sim = host.SimTime, film = pack.Runner.FilmTime;
            bool playing = pack.Runner.Phase == DirectorPhase.Playing;
            pack.Tick();
            if (playing && pack.Runner.Phase == DirectorPhase.Playing) seen.Add((sim, film, FilmStudio.Shot?.Pose?.LookAt));
        }
        var report = pack.Runner.Reports[0];
        Assert.True(report.Status == "ok", $"{report.Status}: {string.Join("; ", report.Problems)}");
        AssertScopedPlay(seen);
    }

    /// <summary>The take of the scoped-key rails: <see cref="Key1At"/> at 1 s, <see cref="Key2At"/> at 3 s, <see cref="ReplayAt"/> at 1.5 s in segment <c>replay</c>;
    /// segments <c>main</c> [0–5] and <c>replay</c> [1–2.5].</summary>
    internal static Take ScopedTake(string scenario = "s-a") => new()
    {
        Scenario = scenario, Name = "main",
        Keys = new[]
        {
            new CameraKey { At = new AtExpr(1d), Camera = new CameraSpec { LookAt = Key1At } },
            new CameraKey { At = new AtExpr(3d), Camera = new CameraSpec { LookAt = Key2At } },
            new CameraKey { At = new AtExpr(1.5), Camera = new CameraSpec { LookAt = ReplayAt }, Segment = "replay" },
        },
        Segments = new[]
        {
            new TimeSegment { Id = "main", From = new AtExpr(0d), To = new AtExpr(5d) },
            new TimeSegment { Id = "replay", From = new AtExpr(1d), To = new AtExpr(2.5) },
        },
        EndAt = new AtExpr(5d),
    };

    /// <summary>What a play of <see cref="ScopedTake"/> must show, frame by frame (source second, film second, camera): the main pass (film &lt; 5) never the
    /// replay's key; the replay (film ≥ 5) key 1 before source 1.5 and the replay's key from 1.5 on.</summary>
    internal static void AssertScopedPlay(IReadOnlyList<(double Sim, double Film, System.Numerics.Vector3? Camera)> seen)
    {
        const double step = 1d / 60;
        var main = seen.Where(s => s.Film < 5d - step).ToList();
        var replay = seen.Where(s => s.Film > 5d + step).ToList();
        Assert.True(main.Count > 200, $"main pass frames {main.Count}");
        Assert.True(replay.Count > 60, $"replay frames {replay.Count}");
        Assert.DoesNotContain(main, s => s.Camera == ReplayAt);                                            // ⭐ the main pass never uses the replay's key
        Assert.All(main.Where(s => s.Sim > 1.5 + step && s.Sim < 3 - step), s => Assert.Equal(Key1At, s.Camera));
        Assert.All(main.Where(s => s.Sim > 3 + step), s => Assert.Equal(Key2At, s.Camera));
        Assert.All(replay, s => Assert.InRange(s.Sim, 1d - step, 2.5 + step));                            // the replay plays source 1–2.5 again …
        Assert.All(replay.Where(s => s.Sim < 1.5 - step), s => Assert.Equal(Key1At, s.Camera));          // … key 1 until the replay's own key …
        var own = replay.Where(s => s.Sim > 1.5 + step).ToList();
        Assert.True(own.Count > 30, $"replay frames after 1.5 s: {own.Count}");
        Assert.All(own, s => Assert.Equal(ReplayAt, s.Camera));                                         // … then its own key, to its end
    }

    /// <summary>Two units 60 m apart: Alpha (network id 1) shoots Bravo (2) at 10 s, a hit at 10.8 s; frames every 0.1 s, a sample every 0.5 s.</summary>
    internal static Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex ShotIndex()
    {
        const double dt = 0.1;
        int frames = 251;
        var shot = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(100, "combat.shot", 1, 2, 0f, 0f, 1f);
        var hit = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(108, "combat.hit", 2, 0, 0f, 0f, 0f);
        var lanes = new Dictionary<string, Fdp.Toolkit.ReplayBrowser.Timeline.SharedList<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent>>
        {
            ["combat.shot"] = new(new[] { shot }),
            ["combat.hit"] = new(new[] { hit }),
        };
        var alpha = new List<Fdp.Toolkit.ReplayBrowser.Timeline.PositionSample>();
        var bravo = new List<Fdp.Toolkit.ReplayBrowser.Timeline.PositionSample>();
        for (int f = 0; f < frames; f += 5)
        {
            alpha.Add(new(f, (float)(0.2 * f), 0f, 0f, 0f));
            bravo.Add(new(f, 80f, (float)(0.4 * (f - 100)), 0f, MathF.PI / 2));
        }
        var positions = new Fdp.Toolkit.ReplayBrowser.Timeline.PositionTrack(
            new Dictionary<long, Fdp.Toolkit.ReplayBrowser.Timeline.PositionSample[]> { [1] = alpha.ToArray(), [2] = bravo.ToArray() });
        return new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex(lanes,
            new Fdp.Toolkit.ReplayBrowser.Timeline.SharedList<long>(Enumerable.Range(0, frames).Select(f => (long)f * 1_000_000).ToArray()), frames, frames, null,
            new Fdp.Toolkit.ReplayBrowser.Timeline.SharedList<double>(Enumerable.Range(0, frames).Select(f => f * dt).ToArray()), positions);
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> P3 (§11q D1 / D2, success condition 4 — the shared panel model): the pack puts <i>"Draft a shot sequence here"</i> on the menu of an EVENT
    /// of a draftable lane (the panel's <c>EventMenu</c>, handed the index the panel draws); choosing it OPENS the drafter's take in the take session — exactly
    /// <c>Drafter.Draft</c> of that event, with the actor's kind from the WORLD (Alpha is tracked ⇒ a vehicle's height), a new take <c>draft</c>, unsaved (dirty, no
    /// file), its tracks on the panel, no undo history. A second draft is refused while the first has unsaved edits; closing drops it (the draft's undo); then a
    /// second draft is <c>draft</c> again. An event of another lane gets no item. 🔴 Mutations: the pack not setting <c>Timeline.EventMenu</c> ⇒ no item;
    /// <c>DirectorDrafts</c> not passing <c>Kind</c> ⇒ the take differs (pitch −20 vs −6) ⇒ red.
    /// </summary>
    [Fact]
    public void CE4059_P3_DraftHere_OnAnEvent_OpensTheDraftersTake_InTheSession()
    {
        FilmStudio.Reset();
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        var host = new LiveHost(store);
        var world = host.World;
        world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
        foreach (var (id, name, cls) in new[] { (1L, "Alpha", Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked), (2L, "Bravo", Fdp.Toolkit.Tkb.Domain.LocomotionClass.Pedestrian) })
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = id });
            world.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
            world.AddComponent(e, new Fdp.Toolkit.Motion.Locomotion { Class = cls });
        }
        var index = ShotIndex();
        var panel = new Fdp.Presentation.Panels.Timeline.TimelinePanel();
        var pack = DirectorPack.Attach(new DirectorPackHost(host, () => null, () => null, Headless: true, Pilot: () => null, Timeline: panel,
                                                            Index: () => index, Scenario: () => "s-a"));
        Assert.Equal(SubjectKind.Vehicle, DirectorUnits.KindOf(world, 1));
        Assert.Equal(SubjectKind.Person, DirectorUnits.KindOf(world, 2));

        var shot = index.Lane("combat.shot")[0];
        var menu = panel.EventMenu!(index, new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuContext("combat.shot", 0, shot.Frame, shot));
        var item = Assert.Single(menu);
        Assert.Equal(DirectorDrafts.MenuLabel, item.Label);
        Assert.True(item.Enabled);
        item.Run();

        var editor = pack.Session.Editor!;
        Assert.NotNull(editor);
        Func<long, string?> names = id => DirectorUnits.NameOf(world, id);
        var expected = Drafter.Draft(index, Drafter.MomentAt(index, "combat.shot", 0, names)!, names,
                                     new DraftOptions { Scenario = "s-a", Name = "draft", Replay = true, Kind = _ => SubjectKind.Vehicle });
        Assert.Equal(TakeJson.WriteTake(expected with { Title = editor.Take.Title }).ToJsonString(), TakeJson.WriteTake(editor.Take).ToJsonString());
        Assert.Equal(("s-a", "draft"), (editor.Take.Scenario, editor.Take.Name));
        Assert.True(editor.Dirty);
        Assert.False(File.Exists(store.TakePath("s-a", "draft")));                                     // unsaved: nothing written
        Assert.Equal(5, pack.Session.Tracks.Count);                                                      // the panel shows its tracks (CE-1304 / CE-1305 P6: + View, Graphics)
        Assert.False(pack.Session.CanUndo);                                                              // a fresh history
        Assert.Same(editor.Take, pack.Runner.OpenTake!("s-a", "draft"));                                 // and Play plays it

        var (refused, why) = pack.Drafts!.Draft("combat.hit", 0);
        Assert.Null(refused);
        Assert.Contains(why, p => p.Contains("unsaved edits"));
        Assert.Same(editor, pack.Session.Editor);

        pack.Session.Close();                                                                            // the draft's undo: close it
        Assert.Null(pack.Session.Editor);
        var (again, _) = pack.Drafts.Draft("combat.hit", 0);
        Assert.Equal("draft", again!.Name);

        var other = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(5, "life.spawned", 1, 0, 0f, 0f, 0f);
        Assert.Empty(panel.EventMenu(index, new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuContext("life.spawned", 0, 5, other)));
    }

    // ── CE-1304 P4 (§11s) — CR-5 on a LIVE host: the event anchor is found in the live index as the run plays ─────────────────────────────

    /// <summary>
    /// A live host whose world ticks a sim clock and whose LIVE index is the real one every node runs (<see cref="Fdp.Toolkit.ReplayBrowser.Timeline.LiveTimelineSource"/>
    /// over the shipped lanes), stepped after each step as a node steps it after the kernel. <c>Alpha</c> (network id 1) fires once, at <see cref="FireAt"/>.
    /// </summary>
    private sealed class LiveIndexHost : IDirectorHost
    {
        public const double FireAt = 2.0;
        private readonly Entity _alpha;
        private int _frame;

        public LiveIndexHost(TakeStore takes)
        {
            Takes = takes;
            World.RegisterComponent<SimTransform>();
            World.RegisterComponent<EntityInfo>();
            World.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            World.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireNotification>();
            _alpha = World.CreateEntity();
            World.AddComponent(_alpha, new EntityInfo { Name = new FixedString64("Alpha") });
            World.AddComponent(_alpha, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1 });
            World.AddComponent(_alpha, new SimTransform { Position = new System.Numerics.Vector3(0, 0, 0), Rotation = System.Numerics.Quaternion.Identity });
            Live = new Fdp.Toolkit.ReplayBrowser.Timeline.LiveTimelineSource(Fdp.Toolkit.ReplayBrowser.Timeline.ShippedTimelineLanes.Catalog(),
                new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext(Live: true),
                new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCompiler(new Fdp.Toolkit.ReplayBrowser.Search.EventScannerCompiler(
                    new StructEdit.Reflection.ComponentEditServiceBuilder().Build())));
        }
        public Fdp.Toolkit.ReplayBrowser.Timeline.LiveTimelineSource Live { get; }
        public EntityRepository World { get; } = new();
        EntityRepository? IDirectorHost.World => World;
        public void LoadScenario(string scenario) { SimTime = 0d; _frame = 0; }
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; }
        public void ExitPreview() => InPreview = false;
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;

        /// <summary>One sim frame: the clock, Alpha's round on the frame that reaches <see cref="FireAt"/>, then the live index's step (published at once).</summary>
        public void StepBy(float seconds)
        {
            double before = SimTime;
            SimTime += seconds;
            World.SetSingleton(new GlobalTime { FrameNumber = ++_frame, TotalTime = SimTime });
            if (before < FireAt - 1e-9 && SimTime >= FireAt - 1e-9)
                World.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification { Shooter = _alpha, Quantity = 1 });
            World.Bus.SwapBuffers();
            Live.Step(World, _frame * 1000L, force: true);
        }
        public double SimTime { get; private set; }
        public double WallNow { get; private set; }
        public void Hold(double seconds) => WallNow += Math.Max(seconds, 0d);
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1304</c> P4 — CR-5 (§11s, success condition 3): on a LIVE host the take's event anchor <c>shot:Alpha#1</c> (NOT baked) is unknown until Alpha's round
    /// appears in the live index — the cut it places is unresolved before — and then resolves to the round's frame time: the camera cuts to key 2 at round + 0.5 s,
    /// and the item ends <c>ok</c>, the anchor found at 2.0 s. The index is the one the pack hands the runner (<see cref="DirectorPackHost.Index"/>).
    /// 🔴 Mutation: <c>DirectorPack.Attach</c> not handing the runner the host's index (<c>runner.Index = h.Index</c> removed) ⇒ the anchor is never found ⇒ key 1 at 2.7 s
    /// and <c>beat-missing</c>.
    /// </summary>
    [Fact]
    public void CE1304_CR5_OnALiveHost_AnEventAnchor_ResolvesWhenItsEventAppearsInTheLiveIndex()
    {
        FilmStudio.Reset();
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        store.Save(new Take
        {
            Scenario = "s-a", Name = "main",
            Anchors = new Dictionary<string, AnchorSpec> { ["shot:Alpha#1"] = new() { On = "shot", By = "Alpha", N = 1 } },
            Keys = new[]
            {
                new CameraKey { At = new AtExpr(0.5d), Camera = new CameraSpec { LookAt = Key1At } },
                new CameraKey { At = new AtExpr(0.5d, "shot:Alpha#1"), Camera = new CameraSpec { LookAt = Key2At } },
            },
            EndAt = new AtExpr(4d),
        });
        var host = new LiveIndexHost(store);
        var pack = DirectorPack.Attach(new DirectorPackHost(host, () => null, () => null, Headless: true, Pilot: () => null,
                                                            Timeline: new Fdp.Presentation.Panels.Timeline.TimelinePanel(), Index: () => host.Live.Index));
        Assert.Empty(pack.Runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));

        Assert.Equal(Key1At, CameraAt(pack, host, 1.9));
        Assert.Contains(pack.Runner.Reports[0].Unresolved, u => u.Contains("shot:Alpha#1"));   // not in the live index yet: the cut it places is unknown
        Assert.Equal(Key1At, CameraAt(pack, host, 2.4));
        Assert.Equal(Key2At, CameraAt(pack, host, 2.7));                      // the round at 2.0 s + 0.5
        for (int i = 0; i < 2000 && pack.Runner.Busy; i++) pack.Tick();

        var report = pack.Runner.Reports[0];
        Assert.True(report.Status == "ok", $"{report.Status}: missing {string.Join(", ", report.Missing)}; {string.Join("; ", report.Problems)}");
        Assert.InRange(report.Found["shot:Alpha#1"], LiveIndexHost.FireAt - 1e-6, LiveIndexHost.FireAt + 1d / 60d + 1e-6);
    }

    private static System.Numerics.Vector3? CameraAt(DirectorPack pack, LiveIndexHost host, double sim)
    {
        for (int i = 0; i < 2000 && pack.Runner.Busy && !(pack.Runner.Phase == DirectorPhase.Playing && host.SimTime >= sim - 1e-9); i++) pack.Tick();
        pack.Tick();
        return FilmStudio.Shot?.Pose?.LookAt;
    }

    // ── ⭐ CE-1391 (§11u "as built: CE-1391", TE-16) — a reel over TWO recordings, one run, on a fake seeking host ─────────────────────────────────

    /// <summary>The ONE opener of a fake Replay Browser: it knows the recordings under <c>known</c> (each a folder's <c>node_1.fdp</c>) and records every open.</summary>
    private sealed class FakeRecordings : Hrot.Presentation.DebugApi.IRecordingOpener
    {
        private readonly HashSet<string> _known;
        public FakeRecordings(params string[] known) => _known = new HashSet<string>(known.Select(Path.GetFullPath));
        public readonly List<string> Opens = new();
        public Action? Opened;
        public Hrot.Presentation.DebugApi.RecordingName? Shown { get; private set; }
        public string? Open(Hrot.Presentation.DebugApi.RecordingName recording, double atSeconds)
        {
            Opens.Add(recording.Text);
            if (recording.IsExercise || !recording.Paths.All(p => _known.Contains(Path.GetFullPath(p)))) return $"no such recording '{recording.Text}'";
            Shown = recording;
            Opened?.Invoke();
            return null;
        }
        /// <summary>The folder name of the recording open now ("A", "B"; none: null).</summary>
        public string? ShownKey => Shown is { Paths.Count: > 0 } s ? Path.GetFileName(Path.GetDirectoryName(s.Paths[0])) : null;
    }

    /// <summary>A Replay Browser's kind of host: it SEEKS, each step is taken at once, a recording opened starts at sim 0, and its takes are the ones BESIDE the
    /// recording open now (TE-1) — so a take is found only while its recording is open.</summary>
    private sealed class RecordingHost : IDirectorHost
    {
        public RecordingHost(FakeRecordings recordings, string root)
        {
            Takes = new TakeStore(() => Path.Combine(root, recordings.ShownKey ?? "none"), () => null);
            recordings.Opened = () => SimTime = 0d;
        }
        public EntityRepository? World => null;
        public void LoadScenario(string scenario) => SimTime = 0d;
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; }
        public void ExitPreview() => InPreview = false;
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;
        public void StepBy(float seconds) => SimTime += seconds;
        public double SimTime { get; private set; }
        public double WallNow { get; private set; }
        public void Hold(double seconds) => WallNow += Math.Max(seconds, 0d);
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
        public bool CanSeek => true;
        public void Seek(double sourceSim) => SimTime = sourceSim;
    }

    /// <summary>A sink that writes nothing and notes, for every captured film frame, the reel item playing and the recording open at that moment.</summary>
    private sealed class NotingSink : IFrameSink
    {
        private readonly Func<int> _item;
        private readonly Func<string?> _recording;
        public NotingSink(Func<int> item, Func<string?> recording) { _item = item; _recording = recording; }
        public readonly List<(string Path, int Item, string? Recording)> Frames = new();
        public System.Threading.Tasks.Task Frame(string path, int width, int height, int supersample, SubtitleFrame? subtitle)
        {
            Frames.Add((path, _item(), _recording()));
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public System.Threading.Tasks.Task Card(string path, int width, int height, string text)
        {
            File.WriteAllText(path, "");   // the runner copies the card frame for the card's length
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>A sink that notes, for every captured film frame, the host's sim time when the frame was ASKED for (the moment it was framed).</summary>
    private sealed class MomentSink : IFrameSink
    {
        private readonly Func<double> _sim;
        public MomentSink(Func<double> sim) => _sim = sim;
        public readonly List<double> AskedAt = new();
        public System.Threading.Tasks.Task Frame(string path, int width, int height, int supersample, SubtitleFrame? subtitle)
        {
            AskedAt.Add(_sim());
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public System.Threading.Tasks.Task Card(string path, int width, int height, string text)
        {
            File.WriteAllText(path, "");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4127</c> (PLAN_Feature_Films §1d, F-18) — on a SEEKING host each captured frame is DRAWN at the moment it was framed. The map draws after the
    /// runner's tick; a recording moves at once when stepped, so a step taken at the END of the tick drew every picture one step past the moment its camera
    /// was placed for — an over-the-shoulder camera and the body it follows jumped against each other (measured ±20 px at 30 fps on an ~18 Hz recording).
    /// Here: after the tick that asked for a frame, the host is still at that frame's sim time.
    /// 🔴 Mutation: <c>DirectorRunner.TickPlaying</c> stepping at the end of the tick again (<c>StepOnce(paced)</c> for a seeking host) ⇒ the host is one step on ⇒ red.
    /// </summary>
    [Fact]
    public void CE4127_OnASeekingHost_EachCapturedFrame_IsDrawnAtTheMomentItWasFramed()
    {
        FilmStudio.Reset();
        var (a, _) = TwoRecordingsWithTheirTakes();
        var recordings = new FakeRecordings(a);
        Assert.Null(recordings.Open(Hrot.Presentation.DebugApi.RecordingName.Files(new[] { a }), 0d));
        var host = new RecordingHost(recordings, _root);
        var sink = new MomentSink(() => host.SimTime);
        var runner = new DirectorRunner(host, new WindowCinema(() => null), new MapLayerOverride(() => null), sink) { Recordings = recordings };
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"), Capture: new CaptureOptions(Path.Combine(_root, "film-4127"), Fps: 30, Encode: false))));

        var drawnAt = new List<double>();
        for (int i = 0; i < 5000 && runner.Busy; i++)
        {
            int before = sink.AskedAt.Count;
            runner.Tick();
            if (sink.AskedAt.Count > before) drawnAt.Add(host.SimTime);   // what the map would draw after this tick
        }
        Assert.True(drawnAt.Count >= 20, $"{drawnAt.Count} frames captured");
        for (int i = 0; i < drawnAt.Count; i++)
            Assert.True(Math.Abs(drawnAt[i] - sink.AskedAt[sink.AskedAt.Count - drawnAt.Count + i]) < 1e-9,
                        $"frame {i}: framed at {sink.AskedAt[sink.AskedAt.Count - drawnAt.Count + i]:0.####} s, drawn at {drawnAt[i]:0.####} s");
    }

    private static readonly System.Numerics.Vector3 AAt = new(1f, 0f, 0f), BAt = new(0f, 2f, 0f);

    /// <summary>Recording folders A and B under <see cref="_root"/>, each with its take s-a/main BESIDE it: A's looks at <see cref="AAt"/> for 1 s, B's at
    /// <see cref="BAt"/> for 1.5 s.</summary>
    private (string A, string B) TwoRecordingsWithTheirTakes()
    {
        string a = Path.Combine(_root, "A", "node_1.fdp"), b = Path.Combine(_root, "B", "node_1.fdp");
        foreach (var (file, at, end) in new[] { (a, AAt, 1d), (b, BAt, 1.5d) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "");
            new TakeStore(() => Path.GetDirectoryName(file)!, () => null).Save(new Take
            {
                Scenario = "s-a", Name = "main",
                Keys = new[] { new CameraKey { At = new AtExpr(0d), Camera = new CameraSpec { LookAt = at } } },
                EndAt = new AtExpr(end),
            });
        }
        return (a, b);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1391</c> (TE-16; §11u "as built: CE-1391"; success condition 1 — the fake seeking host) — a reel whose items come from DIFFERENT recordings is ONE run,
    /// each item over its own recording. ① The pack hands its host's ONE opener (<see cref="DirectorPackHost.Recordings"/>) to the runner and the render queue. With A
    /// open, a reel [s-a/main over B, s-a/main naming none] plays: the runner opens B before item 1 and A again before item 2 (the recording the play STARTED on);
    /// every playing frame of item 1 is over B with B's take (its camera), every one of item 2 over A with A's; each item reports its recording. ② A RENDER of
    /// [none, B, none] queued with recording A: the queue opens A, the runner B then A — and every captured frame of the ONE capture (one folder, one job) comes
    /// from its item's recording: items 1 and 3 from A, item 2 from B. 🔴 Mutation: <c>DirectorRunner.TickItemRecording</c> returning false at once (no item
    /// opens its recording) ⇒ item 1 plays over A (B never opens) ⇒ red.
    /// </summary>
    [Fact]
    public void CE1391_AReelOverTwoRecordings_PlaysAsOneRun_EachItemOverItsOwnRecording_OnASeekingHost()
    {
        FilmStudio.Reset();
        var (a, b) = TwoRecordingsWithTheirTakes();
        var recordings = new FakeRecordings(a, b);
        Assert.Null(recordings.Open(Hrot.Presentation.DebugApi.RecordingName.Files(new[] { a }), 0d));   // the person has A open
        recordings.Opens.Clear();
        var host = new RecordingHost(recordings, _root);

        // ① the pack wires the ONE opener; a play spans the recordings
        var pack = DirectorPack.Attach(new DirectorPackHost(host, () => null, () => null, Headless: true, Pilot: () => null,
                                                            Timeline: new Fdp.Presentation.Panels.Timeline.TimelinePanel(), Recordings: recordings));
        Assert.Same(recordings, pack.Runner.Recordings);
        Assert.Same(recordings, pack.Renders.Recordings);
        var reel = new Reel { Name = "two", Items = new[] { new ReelItem { Scenario = "s-a", Take = "main", Recording = b }, new ReelItem { Scenario = "s-a", Take = "main" } } };
        Assert.Empty(pack.Runner.Play(new PlayRequest(reel, Speed: 1000d, Cinema: false)));
        var seen = new List<(int Item, string? Recording, System.Numerics.Vector3? Camera)>();
        for (int i = 0; i < 5000 && pack.Runner.Busy; i++)
        {
            pack.Tick();
            if (pack.Runner.Phase == DirectorPhase.Playing) seen.Add((pack.Runner.CurrentItem, recordings.ShownKey, FilmStudio.Shot?.Pose?.LookAt));
        }
        Assert.Equal(DirectorPhase.Done, pack.Runner.Phase);
        Assert.All(pack.Runner.Reports, r => Assert.True(r.Status == "ok", $"{r.Status}: {string.Join("; ", r.Problems)}"));
        Assert.Equal(new[] { b, a }, recordings.Opens);                                                  // B for item 1, A again for item 2
        Assert.Equal(new[] { b, a }, pack.Runner.Reports.Select(r => r.Recording));
        var item1 = seen.Where(s => s.Item == 0).ToList();
        var item2 = seen.Where(s => s.Item == 1).ToList();
        Assert.True(item1.Count > 30 && item2.Count > 30, $"{item1.Count} / {item2.Count} playing frames");
        Assert.All(item1, s => Assert.Equal(("B", (System.Numerics.Vector3?)BAt), (s.Recording, s.Camera)));
        Assert.All(item2, s => Assert.Equal(("A", (System.Numerics.Vector3?)AAt), (s.Recording, s.Camera)));

        // ② one RENDER over the recordings: the job's recording is the default of the items naming none
        recordings.Opens.Clear();
        DirectorRunner? runner = null;
        var sink = new NotingSink(() => runner!.CurrentItem, () => recordings.ShownKey);
        runner = new DirectorRunner(host, new WindowCinema(() => null), new MapLayerOverride(() => null), sink) { Recordings = recordings };
        var stills = new DirectorStills(runner, sink, () => true);
        var renders = new DirectorRenders(runner, stills, sink, () => true, () => host.Takes, () => "s-a") { Recordings = recordings };
        var three = new Reel { Name = "three", Items = new[] { new ReelItem { Scenario = "s-a" }, new ReelItem { Scenario = "s-a", Recording = b }, new ReelItem { Scenario = "s-a" } } };
        var (job, why) = renders.Enqueue(new RenderRequest { Reel = three, Recording = a, Fps = 10, Width = 320, Height = 180, Encode = false,
                                                             Dir = Path.Combine(_root, "render") });
        Assert.True(job != null, why);
        for (int i = 0; i < 20_000 && (renders.Busy || renders.Queued > 0); i++) { runner.Tick(); stills.Tick(); renders.Tick(); }
        Assert.True(job!.State == RenderState.Done, job.ToJson().ToJsonString());
        Assert.Equal(new[] { a, b, a }, recordings.Opens);                                              // the job's A, item 2's B, A again for item 3
        Assert.Equal(3, job.Items);
        var frames = sink.Frames;
        Assert.All(frames, f => Assert.StartsWith(Path.Combine(_root, "render", "frames"), f.Path));    // ONE capture
        foreach (var (item, rec) in new[] { (0, "A"), (1, "B"), (2, "A") })
        {
            var of = frames.Where(f => f.Item == item).ToList();
            Assert.True(of.Count >= 9, $"item {item + 1}: {of.Count} frames");
            Assert.All(of, f => Assert.Equal(rec, f.Recording));
        }
        Assert.True(frames.Count(f => f.Item == 1) > frames.Count(f => f.Item == 0), "B's take (1.5 s) is longer than A's (1 s)");
    }

    // ── CE-4081 — Esc stops a take played on the editor's cinema ───────────────────────────────────────────────────────────────────────────

    /// <summary>A live pack on a REAL window manager (the cinema hides its panels and draws its overlay through it), headless of any renderer.</summary>
    private (DirectorPack Pack, LiveHost Host) LivePackOn(Fdp.Presentation.WindowManager.WindowManager wm)
    {
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        store.Save(new Take
        {
            Scenario = "s-a", Name = "main",
            Keys = new[] { new CameraKey { At = new AtExpr(1d), Camera = new CameraSpec { LookAt = Key1At } } },
            EndAt = new AtExpr(50d),
        });
        var host = new LiveHost(store);
        var pack = DirectorPack.Attach(new DirectorPackHost(host, () => wm, () => null, Headless: true, Pilot: () => null,
                                                            Timeline: new Fdp.Presentation.Panels.Timeline.TimelinePanel()));
        return (pack, host);
    }

    /// <summary>
    /// The editor's input chain, as one frame runs it (<c>Program.cs</c>): the director ticks, the map's canvas routes the keys (it DRAINS Raylib's one key queue
    /// unless ImGui wants the keyboard — <c>RaylibInputProvider.IsKeyboardCaptured</c> = <c>io.WantCaptureKeyboard</c>), then ImGui begins and receives the keys
    /// still in that queue (rlImGui — ⚠ emulated here: measured on the real rlImGui 3.2.0 under Xvfb, a queue drained before <c>rlImGui.Begin</c> gives ImGui
    /// 0 of 10 Esc taps, an undrained one 10 of 10), and the window manager draws — the cinema's overlay among its frame overlays.
    /// </summary>
    private sealed class EditorFrame : IDisposable
    {
        private sealed class QueueInput : Fdp.Toolkit.Vis2D.Abstractions.IInputProvider
        {
            public readonly Queue<int> Keys = new();
            public System.Numerics.Vector2 MousePosition => default;
            public System.Numerics.Vector2 MouseDelta => default;
            public float MouseWheelMove => 0f;
            public bool IsMouseButtonPressed(Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton b) => false;
            public bool IsMouseButtonDown(Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton b) => false;
            public bool IsMouseButtonReleased(Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton b) => false;
            public bool IsKeyPressed(Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey k) => false;
            public bool IsKeyDown(Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey k) => false;
            public bool IsKeyReleased(Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey k) => false;
            public int GetKeyPressed() => Keys.Count > 0 ? Keys.Dequeue() : 0;
            public bool IsMouseCaptured => false;
            public bool IsKeyboardCaptured => ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;   // the real provider's definition
        }

        private sealed class QuietCamera : Fdp.Toolkit.Vis2D.Components.MapCamera
        {
            public override void BeginMode() { }
            public override void EndMode() { }
            public override void Update(float dt) { }
            public override bool HandleInput(Fdp.Toolkit.Vis2D.Abstractions.IInputProvider input) => false;
        }

        private readonly QueueInput _input = new();
        private readonly Fdp.Toolkit.Vis2D.MapCanvas _canvas;
        private readonly ImGuiTestFixture _imgui = new();
        private readonly Fdp.Presentation.Icons.IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
        private bool _escapeDown;

        public Fdp.Presentation.WindowManager.WindowManager Wm { get; }
        public int KeysTheMapDrained;

        public EditorFrame()
        {
            Wm = new Fdp.Presentation.WindowManager.WindowManager(_atlas);
            _canvas = new Fdp.Toolkit.Vis2D.MapCanvas(_input) { Camera = new QuietCamera() };
        }

        /// <summary>One frame: <paramref name="tick"/> (the director), a raylib key press when <paramref name="escape"/>, the map's update, ImGui, the window manager.</summary>
        public void Run(Action tick, bool escape = false)
        {
            tick();
            if (escape) _input.Keys.Enqueue((int)Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey.Escape);
            int before = _input.Keys.Count;
            _canvas.Update(1f / 60f);
            KeysTheMapDrained += before - _input.Keys.Count;
            var io = ImGuiNET.ImGui.GetIO();
            bool fed = false;
            while (_input.Keys.Count > 0) { if (_input.Keys.Dequeue() == (int)Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey.Escape) fed = true; }
            if (fed) { io.AddKeyEvent(ImGuiNET.ImGuiKey.Escape, true); _escapeDown = true; }
            else if (_escapeDown) { io.AddKeyEvent(ImGuiNET.ImGuiKey.Escape, false); _escapeDown = false; }
            _imgui.NewFrame();
            Wm.Render();
            _imgui.Render();
        }

        /// <summary>Whether ImGui holds the keyboard now (what the map's router reads next frame).</summary>
        public bool ImGuiWantsTheKeyboard => ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;

        public void Dispose() { _atlas.Dispose(); _imgui.Dispose(); }
    }

    /// <summary>
    /// ⭐ <c>CE-4081</c> (docs/DESIGN_Film_Director.md §8.3 "CinemaMode … Esc stops") — Esc stops a TAKE played in the editor, through the ONE stop (<c>EscapePressed</c> →
    /// <c>runner.Stop()</c>, the Stop button's own), and the editor is back as it was (preview left, cinema off, the film camera released, the map's keys again its own).
    /// 📐 Root cause, measured: the cinema hides every panel, so ImGui no longer wants the keyboard — and the map's canvas then DRAINS Raylib's one key queue for its layers
    /// BEFORE ImGui begins, so the cinema's <c>ImGui.IsKeyPressed(Escape)</c> never fired. 🔴 Mutation: <c>WindowCinema.Draw</c> not asking ImGui for the keyboard
    /// (<c>SetNextFrameWantCaptureKeyboard</c> removed) ⇒ the map drains the Esc and the run plays on ⇒ red. 🔴 Mutation 2: <c>cinema.EscapePressed</c> not set in
    /// <c>DirectorPack.Attach</c> ⇒ red.
    /// </summary>
    [Fact]
    public void CE4081_Esc_StopsATake_PlayedOnTheCinema_ThroughTheMapsKeyRouter_AndTheEditorIsBackAsItWas()
    {
        FilmStudio.Reset();
        using var frame = new EditorFrame();
        var (pack, host) = LivePackOn(frame.Wm);
        void Tick() => pack.Tick();

        for (int i = 0; i < 5; i++) frame.Run(Tick);
        Assert.False(frame.ImGuiWantsTheKeyboard);                                         // no film: the map keeps its keys

        Assert.Empty(pack.Runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        for (int i = 0; i < 40; i++) frame.Run(Tick);
        Assert.Equal(DirectorPhase.Playing, pack.Runner.Phase);
        Assert.True(frame.Wm.Cinema && pack.Cinema.Active && host.InPreview);              // the film: panels hidden, in the preview
        Assert.NotNull(FilmStudio.Shot);
        int stepsBefore = host.Steps;

        frame.Run(Tick, escape: true);                                                     // the person presses Esc
        for (int i = 0; i < 3; i++) frame.Run(Tick);

        Assert.True(frame.KeysTheMapDrained == 0, $"the map drained {frame.KeysTheMapDrained} key(s) the cinema needed");
        Assert.False(pack.Runner.Busy, $"still {pack.Runner.Phase}");
        Assert.Equal(DirectorPhase.Stopped, pack.Runner.Phase);
        Assert.Equal("stopped", pack.Runner.Reports[0].Status);
        Assert.False(host.InPreview);                                                      // back out of the preview, as the Stop button leaves it
        Assert.False(pack.Cinema.Active);
        Assert.False(frame.Wm.Cinema);                                                     // the panels are back
        Assert.Null(FilmStudio.Shot);                                                      // the film camera released
        Assert.InRange(host.Steps - stepsBefore, 0, 2);                                    // no longer stepping
        for (int i = 0; i < 5; i++) frame.Run(Tick);
        Assert.False(frame.ImGuiWantsTheKeyboard);                                         // the map's key router is its own again
        Assert.InRange(host.Steps - stepsBefore, 0, 2);
    }

    /// <summary>
    /// ⭐ <c>CE-4081</c> — the same for a REEL: Esc stops the whole reel (not just the item on screen — the next item never starts), and the editor is back as it was.
    /// 🔴 Mutation: as above (<c>SetNextFrameWantCaptureKeyboard</c> removed ⇒ the map drains the Esc ⇒ the reel plays on).
    /// </summary>
    [Fact]
    public void CE4081_Esc_StopsAReel_PlayedOnTheCinema_NotJustItsFirstItem()
    {
        FilmStudio.Reset();
        using var frame = new EditorFrame();
        var (pack, host) = LivePackOn(frame.Wm);
        void Tick() => pack.Tick();
        var reel = new Reel { Name = "two", Items = new[] { new ReelItem { Scenario = "s-a", Take = "main" }, new ReelItem { Scenario = "s-a", Take = "main" } } };

        Assert.Empty(pack.Runner.Play(new PlayRequest(reel)));
        for (int i = 0; i < 40; i++) frame.Run(Tick);
        Assert.Equal((DirectorPhase.Playing, 0), (pack.Runner.Phase, pack.Runner.CurrentItem));
        Assert.True(frame.Wm.Cinema && host.InPreview);

        frame.Run(Tick, escape: true);
        for (int i = 0; i < 3; i++) frame.Run(Tick);

        Assert.True(frame.KeysTheMapDrained == 0, $"the map drained {frame.KeysTheMapDrained} key(s) the cinema needed");
        Assert.Equal(DirectorPhase.Stopped, pack.Runner.Phase);
        Assert.Equal(new[] { "stopped", "pending" }, pack.Runner.Reports.Select(r => r.Status));   // item 2 never began
        Assert.False(host.InPreview);
        Assert.False(frame.Wm.Cinema);
        Assert.Null(FilmStudio.Shot);
        for (int i = 0; i < 30; i++) frame.Run(Tick);
        Assert.Equal((DirectorPhase.Stopped, -1), (pack.Runner.Phase, pack.Runner.CurrentItem));   // and it stays stopped
    }

    /// <summary>
    /// ⭐ <c>CE-4119</c> part 1 — a stills plan whose take hangs its SEGMENTS on an anchor the run does not show names it in <c>problems</c>, for <c>times:[…]</c> and for every key alike — never a silent 1:1 film map
    /// (<c>FilmTimeMap.Of</c> falls back to identity when a segment's anchor is unresolved). 🔴 Mutation: the <c>film.Unresolved</c> forwarding removed from <c>DirectorStills.PlanOf</c> ⇒ no problem ⇒ red.
    /// </summary>
    [Fact]
    public void CE4119_AStillsPlan_NamesAnUnresolvedSegmentAnchor_InItsProblems_NeverASilentOneToOneMap()
    {
        var take = new Take
        {
            Scenario = "s", Name = "main",
            Anchors = new Dictionary<string, AnchorSpec> { ["blast#1"] = new() { On = "blast", N = 1 } },
            Keys = new[] { new CameraKey { At = new AtExpr(1d), Camera = new CameraSpec { LookAt = System.Numerics.Vector3.Zero, Distance = 20f } } },
            Segments = new[] { new TimeSegment { From = new AtExpr(-2d, "blast#1"), To = new AtExpr(0.5d, "blast#1") } },
            EndAt = new AtExpr(4d),
        };

        var (plan, problems) = DirectorStills.PlanOf(take, new double[] { 1d, 2d }, everyKey: false, anchors: _ => null);   // the run shows no blast
        Assert.Contains(problems, p => p.Contains("blast#1") && p.Contains("1:1"));
        Assert.Equal(2, plan.Count);                                                  // the plan is still made (1:1) — but it SAYS so
        Assert.All(plan, p => Assert.Equal(-1, p.Segment));   // the identity map: no segment

        var (_, keyed) = DirectorStills.PlanOf(take, null, everyKey: true, anchors: _ => null);
        Assert.Contains(keyed, p => p.Contains("blast#1") && p.Contains("1:1"));

        var (_, resolved) = DirectorStills.PlanOf(take, new double[] { 1d }, everyKey: false, anchors: n => n == "blast#1" ? 3d : null);   // the run shows it: nothing to say
        Assert.DoesNotContain(resolved, p => p.Contains("1:1"));
    }

    /// <summary>⭐ <c>CE-4119</c> part 1 — <c>DirectorUnits.Census</c>: the shown world's entity count and how many carry a name (the figures <c>skippedWhy</c> quotes); null for no world. 🔴 Mutation: counting unnamed entities as named ⇒ red.</summary>
    [Fact]
    public void CE4119_TheCensus_CountsTheShownWorldsEntities_AndTheNamedOnes()
    {
        Assert.Null(DirectorUnits.Census(null));
        using var world = new Fdp.Core.EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        world.RegisterComponent<Fdp.Core.EntityInfo>();
        var named = world.CreateEntity(); world.AddComponent(named, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1 }); world.AddComponent(named, new Fdp.Core.EntityInfo { Name = "Alpha" });
        var blank = world.CreateEntity(); world.AddComponent(blank, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 2 }); world.AddComponent(blank, new Fdp.Core.EntityInfo());
        world.CreateEntity();
        Assert.Equal((3, 1), DirectorUnits.Census(world));
    }
}
