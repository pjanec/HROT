using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Editor.Tests.Director;

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §0, §1a, §3a, §3b; R-3038) — the director runner driven frame by frame over a fake editor:
/// a reel of three takes from three scenarios plays unattended (load → ready → preview → steps → exit → the next loads AGAIN), ⭐⭐ with
/// EXACTLY ONE step in every frame it steps and the frame held to real time; a misspelt take is refused with the name and the reel goes on;
/// capture writes one frame per film frame; the scout bakes the anchor times and the next play cuts on them. And the ready probe the load
/// route shares (B4).
/// </summary>
[Collection(DirectorCollection.Name)]
public sealed class DirectorRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ce4038-{Guid.NewGuid():N}");

    public DirectorRunnerTests() => FilmStudio.Reset();

    public void Dispose()
    {
        FilmStudio.Reset();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>The editor, faked: a load takes three frames (the world clears, then the scenario's units appear and the state is
    /// OperatingEdit); a step asked for in a frame is taken at the frame's end (the kernel); the renderer draws a frame in 1/120 s.</summary>
    private sealed class FakeEditor : IDirectorHost
    {
        public readonly EntityRepository Repo = new();
        public readonly List<string> Loads = new();
        public readonly List<int> StepsPerFrame = new();
        /// <summary>Steps the KERNEL took in each frame (after the lag).</summary>
        public readonly List<int> RetiredPerFrame = new();
        /// <summary>Frames between a step being asked for and the kernel taking it: 0, or 1 as the editor's master measured.</summary>
        public int Lag;
        private readonly Queue<(int Frame, float Seconds)> _inFlight = new();
        private int _frame;
        public readonly List<double> Holds = new();
        public readonly List<FilmShot?> Shots = new();
        public int Exits, Enters;
        private int _loadFrames = -1;
        private string? _pending;
        private float _askedStep;
        private int _askedSteps;
        public double Wall;
        public double FrameSeconds = 1d / 120d;
        /// <summary>Move "Runner" east at 1 m/s (a beat for the scout).</summary>
        public bool Moves = true;

        public FakeEditor(TakeStore takes)
        {
            Takes = takes;
            Repo.RegisterComponent<SimTransform>();
            Repo.RegisterComponent<NetworkIdentity>();
            Repo.RegisterComponent<EntityInfo>();
        }

        public EntityRepository? World => Repo;
        public void LoadScenario(string scenario) { Loads.Add(scenario); _pending = scenario; _loadFrames = 3; IsOperatingEdit = false; }
        public bool IsOperatingEdit { get; private set; } = true;
        public bool HasLoadedAScenario => Loads.Count > 0;
        public int? EntityCount => Repo.EntityCount;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; Enters++; }
        public void ExitPreview() { InPreview = false; Exits++; SimTime = 0d; _inFlight.Clear(); }
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;
        public void StepBy(float seconds) { _askedStep = seconds; _askedSteps++; }
        public double SimTime { get; private set; }
        public double WallNow => Wall;
        public void Hold(double seconds) { Holds.Add(seconds); Wall += seconds; }
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }

        /// <summary>The rest of the frame after the director's tick: the kernel takes the step, a load progresses, the map draws.</summary>
        public void EndFrame()
        {
            Shots.Add(FilmStudio.Shot);
            if (_askedSteps > 0) StepsPerFrame.Add(_askedSteps);
            for (int i = 0; i < _askedSteps; i++) _inFlight.Enqueue((_frame, _askedStep));
            _askedSteps = 0;
            int retired = 0;
            while (_inFlight.Count > 0 && _inFlight.Peek().Frame + Lag <= _frame)
            {
                SimTime += _inFlight.Dequeue().Seconds;
                retired++;
            }
            RetiredPerFrame.Add(retired);
            if (retired > 0 && Moves) foreach (var e in Repo.Query().With<SimTransform>().Build())
                Repo.GetComponentRW<SimTransform>(e).Position = new Vector3((float)SimTime, 0f, 0f);
            _frame++;
            if (_loadFrames > 0 && --_loadFrames == 1)
            {
                var all = new List<Entity>();
                foreach (var e in Repo.Query().With<EntityInfo>().Build()) all.Add(e);
                foreach (var e in all) Repo.DestroyEntity(e);   // the load clears…
            }
            else if (_loadFrames == 0 && _pending != null)
            {
                foreach (var (name, id) in new[] { ("Runner", 1L), ("Watcher " + _pending, 2L) })    // …and builds the scenario
                {
                    var e = Repo.CreateEntity();
                    Repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                    Repo.AddComponent(e, new NetworkIdentity { Value = id });
                    Repo.AddComponent(e, new EntityInfo { Name = name });
                }
                IsOperatingEdit = true;
                SimTime = 0d;
                _pending = null;
                _loadFrames = -1;
            }
            Wall += FrameSeconds;
        }
    }

    private sealed class Cinema : ICinema
    {
        public readonly List<string?> Cards = new();
        public readonly List<string?> Captions = new();
        public int Enters, Exits;
        public bool Active { get; private set; }
        public void Enter() { Active = true; Enters++; }
        public void Exit() { Active = false; Exits++; }
        public void Card(string? text) { if (text != null) Cards.Add(text); }
        public void Caption(string? text) { if (text != null) Captions.Add(text); }
    }

    private sealed class Layers : ILayerOverride
    {
        public readonly List<string> Applied = new();
        public int Clears;
        public void Apply(IReadOnlyList<string> on, IReadOnlyList<string> off) => Applied.Add(string.Join(",", on));
        public void Clear() => Clears++;
    }

    private sealed class Sink : IFrameSink
    {
        public readonly List<string> Frames = new();
        public readonly List<string> Cards = new();
        public Task Frame(string path, int width, int height, int supersample, string? caption) { Frames.Add(path); return Task.CompletedTask; }
        public Task Card(string path, int width, int height, string text) { Cards.Add(text); File.WriteAllText(path, ""); return Task.CompletedTask; }
    }

    private TakeStore Store()
    {
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        foreach (var s in new[] { "s-a", "s-b", "s-c" })
        {
            Directory.CreateDirectory(Path.Combine(_root, s));
            File.WriteAllText(Path.Combine(_root, s, "scenario.json"), "{\"name\":\"" + s + "\"}");
            store.Save(new Take
            {
                Scenario = s, Name = "main", Title = s.ToUpperInvariant(), Caption = "caption of " + s,
                Layers = new LayerChange(new[] { "Ai.VisionCones" }, Array.Empty<string>()),
                Anchors = new Dictionary<string, AnchorSpec> { ["far"] = new() { On = "near", Who = "Runner", Point = new Vector2(1.5f, 0), Within = 0.05f } },
                Shots = new[]
                {
                    new ShotSpec { At = new AtExpr(0.5), Seconds = 0.5, Camera = new CameraSpec { Follow = "Runner", Distance = 10 } },
                    new ShotSpec { At = new AtExpr(-0.25, "far"), Seconds = 0.5, Camera = new CameraSpec { LookAt = new Vector3(1, 2, 3) } },
                },
            });
        }
        return store;
    }

    private static void Run(DirectorRunner runner, FakeEditor host, int maxFrames = 20000)
    {
        for (int f = 0; f < maxFrames && runner.Busy; f++) { runner.Tick(); host.EndFrame(); }
        Assert.False(runner.Busy, $"still {runner.Phase} after {maxFrames} frames");
    }

    /// <summary>
    /// ⭐⭐ B3 — a reel of three takes from three scenarios plays live, unattended: each scenario is LOADED (never rewound — R-3034), the preview
    /// entered paused and left after, the shots held on the film camera with the take's caption and layers, a card over each load. ⭐⭐ The
    /// stepping rule: every frame that steps takes EXACTLY ONE step, and real time is kept by holding the frame (the renderer here is twice
    /// as fast as 60 Hz). 🔴 Red-proof: make <c>StepOnce</c> step twice ⇒ <c>StepsPerFrame</c> holds 2s and the director throws.
    /// </summary>
    [Fact]
    public void CE4038_AReelOfThreeScenarios_PlaysLive_OneStepPerFrame_HeldToRealTime()
    {
        var host = new FakeEditor(Store());
        var cinema = new Cinema();
        var layers = new Layers();
        var runner = new DirectorRunner(host, cinema, layers);
        var reel = new Reel { Name = "r", Items = new[] { "s-a", "s-b", "s-c" }.Select(s => new ReelItem { Scenario = s, Take = "main", Title = s }).ToList() };
        Assert.Empty(runner.Play(new PlayRequest(reel)));
        Run(runner, host);

        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.Equal(new[] { "s-a", "s-b", "s-c" }, host.Loads);
        Assert.Equal(3, host.Enters);
        Assert.True(host.Exits >= 3, $"{host.Exits} exits");
        Assert.All(runner.Reports, r => Assert.Equal("ok", r.Status));
        Assert.All(runner.Reports, r => Assert.Equal(1, r.MaxStepsInAFrame));
        Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));                       // ⭐⭐ never two
        Assert.All(runner.Reports, r => Assert.InRange(r.RealtimeFactor, 0.95, 1.05));  // ⭐ held to real time
        Assert.Contains(host.Holds, h => h > 0.004);                                   // the frame WAS held (1/60 s steps, 1/120 s frames)
        Assert.All(runner.Reports, r => Assert.InRange(r.Found["far"], 1.44, 1.56));   // the beat, seen live in each run
        Assert.Equal(new[] { "s-a", "s-b", "s-c" }, cinema.Cards.Take(3));
        Assert.Contains("caption of s-b", cinema.Captions);
        Assert.Contains("Ai.VisionCones", layers.Applied);
        Assert.True(layers.Clears >= 3);
        Assert.Contains(host.Shots, s => s?.Follow == 1);
        Assert.Contains(host.Shots, s => s?.Pose?.LookAt == new Vector3(1, 2, 3));
        Assert.Null(FilmStudio.Shot);                                                  // released at the end
        Assert.Equal((1, 1), (cinema.Enters, cinema.Exits));
    }

    /// <summary>
    /// ⭐⭐ B3 — the editor's master takes a step ONE FRAME after it is asked for (measured `2026-10-21`): the director keeps one in flight, so
    /// the KERNEL still takes exactly one step in every frame of a shot — never a paused frame between two, never two at once — and the
    /// report says so (<c>maxRetiredInAFrame</c> 1, no wait frames). 🔴 Red-proof: the old guard (wait for each step to land before the
    /// next) leaves a 0 between every two 1s in <c>RetiredPerFrame</c>.
    /// </summary>
    [Fact]
    public void CE4038_WithTheMasterOneFrameBehind_TheKernelStillStepsEveryFrame()
    {
        var host = new FakeEditor(Store()) { Lag = 1 };
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, host);
        var r = runner.Reports[0];
        Assert.Equal("ok", r.Status);
        Assert.Equal((1, 1, 0L), (r.MaxStepsInAFrame, r.MaxRetiredInAFrame, r.WaitFrames));
        var steps = host.RetiredPerFrame.SkipWhile(n => n == 0).Reverse().SkipWhile(n => n == 0).Reverse().ToList();
        Assert.All(steps, n => Assert.Equal(1, n));                 // ⭐ one step EVERY frame, from the first to the last
        Assert.True(steps.Count > 90, $"{steps.Count} frames stepped");
    }

    /// <summary>B1 — a take naming a unit the scenario lacks is refused WITH the name; the reel goes on to the next item (R-106).</summary>
    [Fact]
    public void CE4038_AMisspeltTake_IsRefusedWithTheName_AndTheReelGoesOn()
    {
        var store = Store();
        var bad = store.Load("s-b", "main").Value! with { Shots = new[] { new ShotSpec { At = new AtExpr(0.5), Seconds = 0.5, Camera = new CameraSpec { Follow = "Runer" } } } };
        store.Save(bad);
        var host = new FakeEditor(store);
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        var reel = new Reel { Name = "r", Items = new[] { "s-a", "s-b", "s-c" }.Select(s => new ReelItem { Scenario = s }).ToList() };
        Assert.Empty(runner.Play(new PlayRequest(reel)));
        Run(runner, host);
        Assert.Equal(new[] { "ok", "failed", "ok" }, runner.Reports.Select(r => r.Status));
        Assert.Contains(runner.Reports[1].Problems, p => p.Contains("no unit named 'Runer'") && p.Contains("'Runner'"));
    }

    /// <summary>
    /// B7 — the scout runs a take once, stepped as the film will be, and BAKES its anchor times (with the step rate and the scenario's hash)
    /// into the take's file; the next play places its shots by them. A baked time the live run does not repeat is a determinism finding.
    /// </summary>
    [Fact]
    public void CE4038_TheScout_BakesTheAnchors_AndThePlayCutsOnThem()
    {
        var store = Store();
        var host = new FakeEditor(store);
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"), Scout: true)));
        Run(runner, host);
        var baked = store.Load("s-a", "main").Value!.Baked!;
        Assert.InRange(baked.Times["far"], 1.44, 1.56);
        Assert.Equal((60, store.ScenarioHash("s-a")), (baked.TickHz, baked.ScenarioHash));

        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, host);
        Assert.True(runner.Reports[0].UsedBake);
        Assert.Empty(runner.Reports[0].Mismatches);

        host.Moves = false;   // the beat does not come this run
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, host);
        Assert.Equal("beat-missing", runner.Reports[0].Status);
        Assert.Contains(runner.Reports[0].Mismatches, m => m.StartsWith("far: baked ") && m.EndsWith("not seen live"));
    }

    /// <summary>B8 — capture: one frame per film frame of each shot (30 fps, so every other 60 Hz step), card frames before each item, one
    /// step per rendered frame still; nothing is held to real time.</summary>
    [Fact]
    public void CE4038_ACapture_WritesOneFramePerFilmFrame_AndStillStepsOnce()
    {
        var host = new FakeEditor(Store());
        var sink = new Sink();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        var dir = Path.Combine(_root, "film");
        var reel = new Reel { Name = "r", GapSeconds = 0.1, Items = new[] { new ReelItem { Scenario = "s-a", Title = "One" }, new ReelItem { Scenario = "s-b" } } };
        Assert.Empty(runner.Play(new PlayRequest(reel, Capture: new CaptureOptions(dir, Fps: 30, Encode: false))));
        Run(runner, host);
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        // per item: shot 1 is 0.5 s (15 frames); shot 2 is "far-0.25" with no bake, so it cuts LATE, when the beat is seen live (~1.45 s),
        // and runs to its end (1.7 s): ~8 frames. A scouted take would give 15 (the scout rail shows the bake placing it).
        Assert.InRange(sink.Frames.Count, 2 * 22 - 2, 2 * 23 + 2);
        Assert.Equal(sink.Frames.Count, runner.Reports.Sum(r => r.Frames));
        Assert.Equal(new[] { "One", "s-b" }, sink.Cards);
        Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));
        Assert.Empty(host.Holds.Where(h => h > 0));
    }

    /// <summary>
    /// ⭐⭐ B4 — the ready probe the load route and the director share: a RELOAD starts at the target state, so a level check would say "ready"
    /// on the first poll; the probe waits for the world to change AND settle. The grace covers a count that never moves; no count ⇒ the state
    /// alone. 🔴 Red-proof: drop the sawChange term ⇒ the first poll answers ready.
    /// </summary>
    [Fact]
    public void CE4038_TheReadyProbe_WaitsForTheEdge_NotTheLevel()
    {
        var p = new ScenarioReadyProbe(8);
        Assert.False(p.Observe(true, 8, 0.1), "the old world, still standing in OperatingEdit");
        Assert.False(p.Observe(true, 0, 0.2), "cleared");
        Assert.False(p.Observe(true, 8, 0.3), "rebuilt, not settled yet");
        Assert.True(p.Observe(true, 8, 0.4));
        Assert.True(p.SawWorldChange);

        var grace = new ScenarioReadyProbe(0);
        Assert.False(grace.Observe(true, 0, 1.0));
        Assert.True(grace.Observe(true, 0, ScenarioReadyProbe.EdgeGraceSeconds + 0.1));
        Assert.False(grace.SawWorldChange);

        var noWorld = new ScenarioReadyProbe(null);
        Assert.False(noWorld.Observe(false, null, 0.1));
        Assert.True(noWorld.Observe(true, null, 0.2));
        Assert.False(noWorld.HadWorldAnchor);
    }

    // ── CE-4042 — the Verify mode (docs/DESIGN_Scenario_Regression.md §2a) ──────────────────────────────────────────────────

    /// <summary>The verifier's world over the fake editor: the units' transforms from the repo, a Health the checks may edit.</summary>
    private sealed class FakeVerifyWorld : IVerifyWorld
    {
        private readonly FakeEditor _h;
        public readonly Dictionary<long, double> Health = new();
        /// <summary>Each edit, at the sim time it landed.</summary>
        public readonly List<(double SimTime, long Id, string Component)> Edits = new();

        public FakeVerifyWorld(FakeEditor host, IShotWorld units) { _h = host; Units = units; }

        public EntityRepository? World => _h.Repo;
        public IShotWorld Units { get; }
        public string? BehaviourName(int hash) => null;

        private int _loads;

        /// <summary>A load rebuilds the world: the edited health goes with the old one (⛔ a fake that kept it would leak like CE-3294).</summary>
        private void Fresh()
        {
            if (_h.Loads.Count == _loads) return;
            _loads = _h.Loads.Count;
            Health.Clear();
        }

        public System.Text.Json.Nodes.JsonObject? Components(long id)
        {
            Fresh();
            foreach (var e in _h.Repo.Query().With<NetworkIdentity>().Build())
            {
                if (_h.Repo.GetComponentRO<NetworkIdentity>(e).Value != id) continue;
                var p = _h.Repo.GetComponentRO<SimTransform>(e).Position;
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["SimTransform"] = new System.Text.Json.Nodes.JsonObject { ["Position"] = new System.Text.Json.Nodes.JsonObject { ["X"] = p.X, ["Y"] = p.Y, ["Z"] = p.Z } },
                    ["Health"] = new System.Text.Json.Nodes.JsonObject { ["Current"] = Health.TryGetValue(id, out var hp) ? hp : 100d },
                };
            }
            return null;
        }

        public IReadOnlyList<(long Id, string Name)> Named()
        {
            var list = new List<(long, string)>();
            foreach (var e in _h.Repo.Query().With<NetworkIdentity>().With<EntityInfo>().Build())
                list.Add((_h.Repo.GetComponentRO<NetworkIdentity>(e).Value, _h.Repo.GetComponentRO<EntityInfo>(e).Name.ToString()));
            return list;
        }

        public string? NameOfEntityIndex(int index) => null;

        public System.Text.Json.Nodes.JsonArray Shots() => new();
        public System.Text.Json.Nodes.JsonArray Blasts() => new();

        public string? SetComponent(long id, string component, System.Text.Json.Nodes.JsonObject patch)
        {
            Fresh();
            if (component != "Health") return "only Health here";
            Health[id] = patch["Current"]!.GetValue<double>();
            Edits.Add((_h.SimTime, id, component));
            return null;
        }
    }

    private const string RunnerChecks = """
        { "runFor": 3, "golden": {"every": 1},
          "do": [{"name": "hurt", "at": 1.5, "set": {"who": "Runner", "component": "Health", "patch": {"Current": 40}}}],
          "expect": [{"name": "runs a metre", "on": "near", "who": "Runner", "point": [1, 0], "within": 0.05, "before": 2},
                     {"name": "hurt after the action", "on": "field", "who": "Runner", "path": "Health.Current", "op": "<", "value": 50, "after": "hurt"},
                     {"name": "never west", "never": {"on": "field", "who": "Runner", "path": "SimTransform.Position.X", "op": "<", "value": -1}},
                     {"name": "ends east", "on": "field", "who": "Runner", "path": "SimTransform.Position.X", "op": ">", "value": 2.9, "atEnd": true}] }
        """;

    /// <summary>
    /// ⭐⭐ A verify reel of two scenarios: each LOADED (a fresh run, never rewound), stepped EXACTLY ONCE per frame for runFor with no cinema
    /// and no camera, the checks' action landing on its EXACT step (1.5 s = step 90), every expectation judged; capture writes the golden,
    /// the next run compares SAME, and a run that moves differently is DIFFERENT with the first snapshot named — and fails. A host with no
    /// verifier world refuses. 🔴 Red-proof: call ApplyDue after StepOnce ⇒ the edit lands at 1.5167 s.
    /// </summary>
    [Fact]
    public void CE4042_AVerifyReel_JudgesTheChecks_LandsTheActionOnItsStep_AndKeepsTheGolden()
    {
        var store = Store();
        foreach (var s in new[] { "s-a", "s-b" })
            System.IO.File.WriteAllText(System.IO.Path.Combine(_root, s, ChecksJson.FileName), RunnerChecks);
        var cinema = new Cinema();
        (DirectorRunner Runner, FakeEditor Host, Func<FakeVerifyWorld> World) Fresh()   // ⭐ a fresh process: nothing loaded yet
        {
            var h = new FakeEditor(store);
            FakeVerifyWorld? w = null;
            return (new DirectorRunner(h, cinema, new Layers(), verifyWorld: u => w = new FakeVerifyWorld(h, u)), h, () => w!);
        }

        // ① capture each in a fresh process: judged, the action on its exact step, the golden written
        foreach (var s in new[] { "s-a", "s-b" })
        {
            var (runner, host, world) = Fresh();
            Assert.Empty(runner.Play(new PlayRequest(Reel.Of(s, "_verify"), Verify: new VerifyOptions(Capture: true))));
            Run(runner, host);
            var r = runner.Reports.Single();
            Assert.Equal("pass", r.Status);
            Assert.Equal(180, r.Steps);                                           // 3 s at 60 Hz
            Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));              // ⭐ the stepping rule
            Assert.Equal(0, cinema.Enters);                                      // the editor left as it is
            Assert.Equal(1.5, world().Edits.Single().SimTime, 6);                 // ⭐ its exact step (step 90; a step is 0.0167 s)
            Assert.Equal("captured", r.Verify!.Golden);
            Assert.False(r.Verify.Reload);
            Assert.Equal(1.0, r.Verify.Verdicts.Single(x => x.Name == "runs a metre").Time!.Value, 6);
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(_root, s, ScenarioGolden.FileName)));
        }

        // ② a reel of both in ONE process compares SAME — the second is a reload, and says so
        var (one, oneHost, _) = Fresh();
        Assert.Empty(one.Play(new PlayRequest(new Reel { Name = "verify", Items = new[] { "s-a", "s-b" }.Select(s => new ReelItem { Scenario = s, Take = "_verify" }).ToList() },
                                              Verify: new VerifyOptions())));
        Run(one, oneHost);
        Assert.All(one.Reports, r => Assert.Equal("same", r.Verify!.Golden));
        Assert.Equal(new[] { false, true }, one.Reports.Select(r => r.Verify!.Reload));

        // ③ ⛔ a capture on a reload is refused, with the way to do it right
        Assert.Empty(one.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(Capture: true))));
        Run(one, oneHost);
        var refusedCapture = one.Reports.Single().Verify!;
        Assert.Equal("not-captured", refusedCapture.Golden);
        Assert.Contains("RELOAD", refusedCapture.GoldenDiff[0]);

        // ④ a regression: the runner stands still ⇒ the golden is DIFFERENT from the first snapshot it shows, and the check fails
        var (bad, badHost, _) = Fresh();
        badHost.Moves = false;
        Assert.Empty(bad.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions())));
        Run(bad, badHost);
        var b = bad.Reports.Single();
        Assert.Equal("fail", b.Status);
        Assert.Equal("different", b.Verify!.Golden);
        Assert.StartsWith("FIRST differing snapshot at t=1 s", b.Verify.GoldenDiff[0]);
        Assert.Contains("Runner", b.Verify.GoldenDiff[0]);
        Assert.False(b.Verify.Verdicts.Single(x => x.Name == "runs a metre").Ok);

        var refused = new DirectorRunner(badHost, cinema, new Layers()).Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions()));
        Assert.Contains(refused, p => p.Contains("cannot verify"));
    }

}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DirectorCollection
{
    /// <summary>FilmStudio is process-wide.</summary>
    public const string Name = "Film director (FilmStudio is process-wide)";
}
