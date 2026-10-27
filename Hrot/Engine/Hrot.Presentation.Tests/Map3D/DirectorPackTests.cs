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
}
