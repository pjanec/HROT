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

        // ⭐ CE-3299 B5 — the fake's checkpoint: the clock and the verify world's health (the position follows the clock)
        public FakeVerifyWorld? Verifier;
        public double? CheckpointWrittenAt;
        public int Restores;
        /// <summary>A checkpoint that does not carry the health — what R1 exists to catch.</summary>
        public bool LoseHealthOnRestore;
        private double? _snapTo;

        public void WriteCheckpoint(string path, string label)
        {
            CheckpointWrittenAt = SimTime;
            var health = new System.Text.Json.Nodes.JsonObject();
            foreach (var (id, hp) in Verifier!.Health) health[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = hp;
            System.IO.File.WriteAllText(path, new System.Text.Json.Nodes.JsonObject { ["t"] = SimTime, ["health"] = health }.ToJsonString());
        }

        public double RestoreCheckpoint(string path)
        {
            Restores++;
            var o = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(path))!;
            Verifier!.Fresh();
            if (!LoseHealthOnRestore)
                foreach (var (id, hp) in o["health"]!.AsObject()) Verifier.Health[long.Parse(id, System.Globalization.CultureInfo.InvariantCulture)] = hp!.GetValue<double>();
            _snapTo = o["t"]!.GetValue<double>();   // the clock moves on a later frame, as ITimeCommands.SnapTo does
            return _snapTo.Value;
        }

        // ⭐ CE-4045 H1a — a LIVE run: it records EVERY frame with its sim frame (paused ones too, as the editor's recorder does — CE-4053),
        //   and a stop closes the file two frames later (the recorder's module is uninstalled by the master's Idle transition)
        public string? RecordDir;
        /// <summary>A recorder that loses the health — what H1a exists to catch.</summary>
        public bool RecorderCarriesHealth = true;
        public int LiveLoads, LiveStops, RecordingsOpened;
        public FakeRecordedRun? Opened;
        private bool _liveLoad, _recording;
        private int _stopFrames = -1;
        private string? _recordingFile;
        private readonly System.Text.Json.Nodes.JsonArray _recorded = new();

        public void LoadLive(string scenario) { LoadScenario(scenario); _liveLoad = true; LiveLoads++; }
        public bool IsOperatingLive { get; private set; }
        public void StopLive() { if (IsOperatingReplay) ReplayStops++; else LiveStops++; _stopFrames = 2; }
        public bool IsIdle { get; private set; } = true;
        public string? LiveRecordingPath => _recording ? _recordingFile : null;
        public IRecordedRun OpenRecording(string path) { RecordingsOpened++; return Opened = new FakeRecordedRun(path); }

        // ⭐ CE-4045 H5 — a review of the live run: entered, sought and left a frame after it is asked (the master's round); it shows the
        //   run's own recorded frames and returns to a checkpoint of the moment it left
        /// <summary>A return that does not bring the health back — what H5 exists to catch.</summary>
        public bool ReturnLosesHealth;
        public int Reviews, ReviewSeeks, ReviewExits;
        public bool IsOperatingReview { get; private set; }
        private double? _reviewAsk;
        private bool _exitAsk;
        private (double SimTime, Dictionary<long, double> Health)? _checkpoint;
        public void EnterReview(double simSeconds) { Reviews++; _reviewAsk = simSeconds; }
        public void SeekReview(double simSeconds) { ReviewSeeks++; SeekAsks.Add(simSeconds); _reviewAsk = simSeconds; }

        // ⭐ CE-4045 H1b — the product's REPLAY of the finished run: entered from Idle (a frame after it is asked), it shows the run's recorded
        //   frames (a seek is SeekReview's — the same SeekReplayIntent), and a stop leaves it for Idle two frames later
        /// <summary>A replay that does not show the health — what H1b exists to catch.</summary>
        public bool ReplayLosesHealth;
        public int Replays, ReplayStops;
        /// <summary>The sim seconds each replay entry and each seek asked for, in order.</summary>
        public readonly List<double> ReplayEntries = new(), SeekAsks = new();
        public bool IsOperatingReplay { get; private set; }
        private double? _replayAsk;
        public void EnterReplay(double simSeconds) { Replays++; ReplayEntries.Add(simSeconds); _replayAsk = simSeconds; }
        public void ExitReview() { ReviewExits++; _exitAsk = true; }

        private void ShowRecorded(double simSeconds)
        {
            long f = (long)Math.Round(simSeconds * 60d);
            var frame = _recorded.FirstOrDefault(n => n!["f"]!.GetValue<long>() >= f) ?? _recorded[^1]!;
            SimTime = frame["f"]!.GetValue<long>() / 60d;
            foreach (var e in Repo.Query().With<NetworkIdentity>().Build())
            {
                var u = frame["units"]![Repo.GetComponentRO<NetworkIdentity>(e).Value.ToString(System.Globalization.CultureInfo.InvariantCulture)];
                if (u == null) continue;
                Repo.GetComponentRW<SimTransform>(e).Position = new Vector3(u["x"]!.GetValue<float>(), 0f, 0f);
                Verifier!.Health[Repo.GetComponentRO<NetworkIdentity>(e).Value] = ReplayLosesHealth && IsOperatingReplay ? 100d : u["hp"]!.GetValue<double>();
            }
        }

        private void ServeReview()
        {
            if (_replayAsk is { } rask)
            {
                _replayAsk = null;
                IsOperatingReplay = true;
                IsIdle = false;
                ShowRecorded(rask);
            }
            if (_reviewAsk is { } ask)
            {
                _reviewAsk = null;
                // ⭐ H1b — a seek in a replay is the same SeekReplayIntent; only a review (re)enters from the live run, with its checkpoint
                if (!IsOperatingReplay && !IsOperatingReview) { _checkpoint = (SimTime, new Dictionary<long, double>(Verifier!.Health)); IsOperatingReview = true; IsOperatingLive = false; }
                ShowRecorded(ask);
            }
            if (_exitAsk && _checkpoint is { } cp)
            {
                _exitAsk = false;
                IsOperatingReview = false;
                IsOperatingLive = true;
                SimTime = cp.SimTime;
                if (Moves) foreach (var e in Repo.Query().With<SimTransform>().Build())
                    Repo.GetComponentRW<SimTransform>(e).Position = new Vector3((float)SimTime, 0f, 0f);
                Verifier!.Health.Clear();
                if (!ReturnLosesHealth) foreach (var (id, hp) in cp.Health) Verifier.Health[id] = hp;
            }
        }

        private void Record()
        {
            var units = new System.Text.Json.Nodes.JsonObject();
            foreach (var e in Repo.Query().With<NetworkIdentity>().With<EntityInfo>().Build())
            {
                long id = Repo.GetComponentRO<NetworkIdentity>(e).Value;
                units[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new System.Text.Json.Nodes.JsonObject
                {
                    ["name"] = Repo.GetComponentRO<EntityInfo>(e).Name.ToString(),
                    ["x"] = Repo.GetComponentRO<SimTransform>(e).Position.X,
                    ["hp"] = RecorderCarriesHealth && Verifier != null && Verifier.Health.TryGetValue(id, out var hp) ? hp : 100d,
                };
            }
            _recorded.Add(new System.Text.Json.Nodes.JsonObject { ["f"] = (long)Math.Round(SimTime * 60d), ["units"] = units });
        }

        private void CloseRecording()
        {
            _recording = false;
            IsOperatingLive = false;
            IsIdle = true;
            File.WriteAllText(_recordingFile!, new System.Text.Json.Nodes.JsonObject { ["frames"] = _recorded.DeepClone() }.ToJsonString());
            var manifest = new Dictionary<int, Fdp.Core.FlightRecorder.ComponentSchemaInfo>();
            foreach (var (i, name) in new[] { "SimTransform", "NetworkIdentity", "EntityInfo", "Health" }.Select((n, i) => (i, n)))
                manifest[i] = new Fdp.Core.FlightRecorder.ComponentSchemaInfo { Name = "Fdp.Some." + name };
            File.WriteAllText(_recordingFile + ".meta.json", Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Serialize(
                new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata { SchemaManifest = manifest }));
        }

        /// <summary>The rest of the frame after the director's tick: the kernel takes the step, a load progresses, the map draws.</summary>
        public void EndFrame()
        {
            ServeReview();
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
            if (_snapTo is { } snap)
            {
                SimTime = snap;
                _snapTo = null;
                if (Moves) foreach (var e in Repo.Query().With<SimTransform>().Build())
                    Repo.GetComponentRW<SimTransform>(e).Position = new Vector3((float)SimTime, 0f, 0f);
            }
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
                if (_liveLoad)
                {
                    _liveLoad = false;
                    IsOperatingLive = true;
                    IsIdle = false;
                    _recording = true;
                    _recordingFile = Path.Combine(RecordDir!, $"run{LiveLoads}.fdp");
                    _recorded.Clear();
                }
                else IsOperatingEdit = true;
                SimTime = 0d;
                _pending = null;
                _loadFrames = -1;
            }
            if (_recording && !IsOperatingReview) Record();   // ⭐ H5 — the recorder is suspended during a review
            if (_stopFrames > 0 && --_stopFrames == 0)
            {
                if (IsOperatingReplay) { IsOperatingReplay = false; IsIdle = true; }   // ⭐ H1b — a replay is left for Idle
                else CloseRecording();
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
    /// <summary>
    /// ⭐ CE-1364 T4 — the runner plays a take's KEYS (not shots): Cut@0 (distance 10), Fly@2 (distance 30 over the second before it, linear),
    /// Dip@3 (distance 50, black 0.5 s out and 0.5 s in), ending at 4. The film camera holds 10 at t=1, the in-between at t=1.5, 30 at t=2.5,
    /// the OLD camera while the black rises (2.75), 50 after the dip; the black is half at 2.75 and 3.25, full at 3, none at 3.6, and the
    /// run releases it at the end. 🔴 Red-proof: have <c>DirectUpcoming</c> set <c>FilmStudio.Fade</c> to 0 ⇒ the fade rows go red; make
    /// <c>ProgramAt</c> start the fly AT its key ⇒ the t=1.5 row reads 10 ⇒ red.
    /// </summary>
    [Fact]
    public void CE1364_T4_TheRunner_PlaysTheKeys_CutFlyDip_AndTheFadeReachesTheFilm()
    {
        var host = new FakeEditor(Store()) { Moves = false };
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        CameraSpec At(float distance) => new() { LookAt = Vector3.Zero, Distance = distance };
        var take = new Take
        {
            Scenario = "s-a", Name = "keys", TickHz = 60, EndAt = new AtExpr(4),
            Keys = new[]
            {
                new CameraKey { At = new AtExpr(0), Camera = At(10) },
                new CameraKey { At = new AtExpr(2), Camera = At(30), In = KeyTransition.Fly(1, Ease.Linear) },
                new CameraKey { At = new AtExpr(3), Camera = At(50), In = KeyTransition.Dip(0.5, 0.5) },
            },
        };
        var reel = new Reel { Name = "r", Items = new[] { new ReelItem { Scenario = "s-a", Take = "keys" } } };
        Assert.Empty(runner.Play(new PlayRequest(reel, Inline: new Dictionary<string, Take> { ["s-a/keys"] = take })));

        var samples = new List<(double Rel, float? Distance, float Fade)>();
        double? t0 = null;
        for (int f = 0; f < 20000 && runner.Busy; f++)
        {
            runner.Tick();
            if (runner.Phase == DirectorPhase.Playing)
            {
                t0 ??= host.SimTime;
                samples.Add((host.SimTime - t0.Value, FilmStudio.Shot?.Pose?.Distance, FilmStudio.Fade));
            }
            host.EndFrame();
        }
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.Equal("ok", runner.Reports[0].Status);

        (float? Distance, float Fade) Near(double rel) => samples.OrderBy(s => Math.Abs(s.Rel - rel)).Select(s => (s.Distance, s.Fade)).First();
        Assert.Equal(10f, Near(1.0).Distance!.Value, 0.01f);
        Assert.Equal(20f, Near(1.5).Distance!.Value, 0.5f);                         // halfway through the linear fly
        Assert.Equal(30f, Near(2.5).Distance!.Value, 0.01f);
        Assert.Equal(30f, Near(2.75).Distance!.Value, 0.01f);                       // the old camera while the black rises
        Assert.Equal(0.5f, Near(2.75).Fade, 0.05f);
        Assert.Equal(1f, Near(3.0).Fade, 0.05f);
        Assert.Equal(50f, Near(3.25).Distance!.Value, 0.01f);
        Assert.Equal(0.5f, Near(3.25).Fade, 0.05f);
        Assert.Equal(0f, Near(3.6).Fade, 0.001f);
        Assert.Equal(0f, Near(2.0).Fade, 0.001f);
        Assert.Equal(0f, FilmStudio.Fade);                                          // released with the film
        Assert.Null(FilmStudio.Shot);
    }

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
        /// <summary>⭐ H1a — a component that changes every read and that no recorder keeps (a debug counter): never the verdict's.</summary>
        public bool WithScratch;
        private int _scratch;
        /// <summary>⭐ CE-3334 — each unit's creation request id, as <c>CreationRequestId</c> dumps it (a fresh Guid per run).</summary>
        public Dictionary<long, Guid>? RequestIds;

        /// <summary>A load rebuilds the world: the edited health goes with the old one (⛔ a fake that kept it would leak like CE-3294).</summary>
        public void Fresh()
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
                var comps = new System.Text.Json.Nodes.JsonObject
                {
                    ["SimTransform"] = new System.Text.Json.Nodes.JsonObject { ["Position"] = new System.Text.Json.Nodes.JsonObject { ["X"] = p.X, ["Y"] = p.Y, ["Z"] = p.Z } },
                    ["Health"] = new System.Text.Json.Nodes.JsonObject { ["Current"] = Health.TryGetValue(id, out var hp) ? hp : 100d },
                };
                if (WithScratch) comps["Scratch"] = new System.Text.Json.Nodes.JsonObject { ["Seen"] = ++_scratch };
                if (RequestIds != null && RequestIds.TryGetValue(id, out var request))
                    comps["CreationRequestId"] = new System.Text.Json.Nodes.JsonObject { ["Value"] = request.ToString() };
                return comps;
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

    /// <summary>⭐ CE-4045 H1a — the fake's recording, played: the first recorded frame of each sim frame, read as the verify world reads.</summary>
    private sealed class FakeRecordedRun : IRecordedRun, IVerifyWorld
    {
        private readonly System.Text.Json.Nodes.JsonArray _frames;
        private int _i = -1;
        public bool Disposed;

        public FakeRecordedRun(string path) => _frames = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!["frames"]!.AsArray();

        public long SimFrame => _i < 0 ? -1 : _frames[_i]!["f"]!.GetValue<long>();

        public bool AdvanceToSimFrame(long simFrame)
        {
            if (_i < 0) _i = 0;
            while (SimFrame < simFrame) { if (_i + 1 >= _frames.Count) return false; _i++; }
            return SimFrame == simFrame;
        }

        public IVerifyWorld World => this;
        public void Dispose() => Disposed = true;

        private System.Text.Json.Nodes.JsonObject Units => _frames[_i]!["units"]!.AsObject();
        EntityRepository? IVerifyWorld.World => null;
        IShotWorld IVerifyWorld.Units => throw new NotSupportedException();
        public string? BehaviourName(int hash) => null;
        public IReadOnlyList<(long Id, string Name)> Named() =>
            Units.Select(kv => (long.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture), kv.Value!["name"]!.GetValue<string>())).ToList();
        public string? NameOfEntityIndex(int index) => null;
        public System.Text.Json.Nodes.JsonObject? Components(long id)
        {
            if (Units[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] is not { } u) return null;
            return new System.Text.Json.Nodes.JsonObject
            {
                ["SimTransform"] = new System.Text.Json.Nodes.JsonObject { ["Position"] = new System.Text.Json.Nodes.JsonObject { ["X"] = u["x"]!.GetValue<float>(), ["Y"] = 0f, ["Z"] = 0f } },
                ["Health"] = new System.Text.Json.Nodes.JsonObject { ["Current"] = u["hp"]!.GetValue<double>() },
            };
        }
        public System.Text.Json.Nodes.JsonArray Shots() => new();
        public System.Text.Json.Nodes.JsonArray Blasts() => new();
        public string? SetComponent(long id, string component, System.Text.Json.Nodes.JsonObject patch) => "read only";
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4045</c> H1a (docs/DESIGN_Scenario_Regression.md §10-1, R-4085) — a replay check: the scenario is loaded LIVE (no preview —
    /// a live run records), stepped once per frame with the master a frame behind, its checks judged and its world snapshotted every
    /// second; then the run is STOPPED, and once the recorder has closed the file every snapshot is matched to the FIRST recorded frame of
    /// its sim frame (the recording keeps paused frames too) and compared. ① a faithful recording: SAME, and the item passes; ② a recorder
    /// that loses the health: DIFFERENT, from the first snapshot after the action, naming the unit and the component — and it fails;
    /// ③ a component the recording's manifest does not carry is listed apart, never the verdict. A replay check with capture is refused.
    /// 🔴 Red-proof: map a snapshot to the sim frame AFTER its own (<c>SimFrameAt</c> + 1) ⇒ ① is DIFFERENT at t=1 s (the runner a step east).
    /// </summary>
    [Fact]
    public void CE4045_H1a_TheLiveRun_IsComparedWithItsOwnRecording_FrameBySimFrame()
    {
        var store = Store();
        System.IO.File.WriteAllText(System.IO.Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        var rec = Path.Combine(_root, "rec");
        Directory.CreateDirectory(rec);
        (DirectorRunner Runner, FakeEditor Host) Fresh()
        {
            var h = new FakeEditor(store) { Lag = 1, RecordDir = rec };
            return (new DirectorRunner(h, new Cinema(), new Layers(), verifyWorld: u => h.Verifier = new FakeVerifyWorld(h, u)), h);
        }
        var replay = new VerifyOptions(Replay: true);

        // ① a faithful recording
        var (runner, host) = Fresh();
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: replay)));
        Run(runner, host);
        var r = runner.Reports.Single();
        Assert.Equal((1, 0, 1, 1), (host.LiveLoads, host.Enters, host.LiveStops, host.RecordingsOpened));   // ⭐ live, never a preview
        Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));
        Assert.Equal("same", r.Verify!.Replay);
        Assert.Equal(4, r.Verify.ReplaySnapshots);                                    // t = 0, 1, 2, 3
        Assert.Empty(r.Verify.ReplayDiff);
        Assert.Equal("none", r.Verify.Golden);                                         // a live run is not compared with the preview's golden
        Assert.Equal("pass", r.Status);
        Assert.True(host.Opened!.Disposed);
        Assert.True(host.IsIdle);

        // ② a recorder that loses the health
        var (lossy, lossyHost) = Fresh();
        lossyHost.RecorderCarriesHealth = false;
        Assert.Empty(lossy.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: replay)));
        Run(lossy, lossyHost);
        var l = lossy.Reports.Single();
        Assert.Equal(("different", "fail"), (l.Verify!.Replay, l.Status));
        Assert.StartsWith("FIRST differing snapshot at t=2 s", l.Verify.ReplayDiff[0]);   // the action is at 1.5 s
        Assert.Contains("Runner", l.Verify.ReplayDiff[0]);
        Assert.Contains(l.Verify.ReplayDiff, d => d.Contains("Health"));
        Assert.All(l.Verify.Verdicts, v => Assert.True(v.Ok));                          // ⭐ the live run itself was right

        // ③ what the manifest does not carry is listed apart
        var (scratch, scratchHost) = Fresh();
        Assert.Empty(scratch.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: replay)));
        scratch.Tick(); scratchHost.EndFrame();
        scratchHost.Verifier!.WithScratch = true;
        Run(scratch, scratchHost);
        var sc = scratch.Reports.Single();
        Assert.Equal(("same", "pass"), (sc.Verify!.Replay, sc.Status));
        Assert.Contains(sc.Verify.ReplayNotCarried, d => d.Contains("Scratch"));

        // ⛔ a replay check records no golden
        Assert.Contains(Fresh().Runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(Capture: true, Replay: true))),
                        p => p.Contains("records no golden"));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3299</c> B5 — R1's two halves (restore design §1k): A runs straight and writes the checkpoint at T1 (after that moment's
    /// snapshot, before its actions); B puts it back at the start and runs on from T1 — its snapshots on A's grid, an action A applied
    /// before T1 NOT applied again, one after T1 applied on its exact step — and the two runs compare SAME from T1. A checkpoint that does
    /// not carry a value is DIFFERENT, naming the unit and the component. 🔴 Red-proof: drop <c>MarkDone</c> ⇒ the T1 = 2 s half applies
    /// the 1.5 s action again (one edit, not zero).
    /// </summary>
    [Fact]
    public void CE3299_B5_R1_ARestoredRun_GoesOnFromTheCheckpoint_AndMatchesTheStraightRun()
    {
        var store = Store();
        System.IO.File.WriteAllText(System.IO.Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        var cinema = new Cinema();
        (DirectorRunner Runner, FakeEditor Host) Fresh()
        {
            var h = new FakeEditor(store);
            return (new DirectorRunner(h, cinema, new Layers(), verifyWorld: u => h.Verifier = new FakeVerifyWorld(h, u)), h);
        }
        System.Text.Json.Nodes.JsonObject RunOf(string path) => System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsObject();
        (string Straight, string Restored) Halves(double at, bool lose)
        {
            string cp = System.IO.Path.Combine(_root, $"r1-{at}.fdp"), a = cp + ".a.json", b = cp + ".b.json";
            var (straight, sh) = Fresh();
            Assert.Empty(straight.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(R1: new RestoreCheck(at, cp, false, a)))));
            Run(straight, sh);
            Assert.Equal(at, sh.CheckpointWrittenAt!.Value, 6);                  // on its moment, once
            var (restored, rh) = Fresh();
            rh.LoseHealthOnRestore = lose;
            Assert.Empty(restored.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(R1: new RestoreCheck(at, cp, true, b)))));
            Run(restored, rh);
            Assert.Equal(1, rh.Restores);
            Assert.Null(rh.CheckpointWrittenAt);                                  // the restored half writes none
            Assert.All(rh.StepsPerFrame, n => Assert.Equal(1, n));                // the stepping rule holds after a restore
            Assert.Equal(at < 1.5 ? 1 : 0, rh.Verifier!.Edits.Count);             // ⭐ the 1.5 s action: applied once, in one half or the other
            if (at < 1.5) Assert.Equal(1.5, rh.Verifier.Edits.Single().SimTime, 6);
            Assert.Equal(at, ConditionBook.Num(RunOf(b)["snapshots"]![0]!["t"])!.Value, 6);   // B's snapshots begin at the checkpoint
            return (a, b);
        }

        foreach (var at in new[] { 1d, 2d })   // before the action, and after it
        {
            var (a, b) = Halves(at, lose: false);
            Assert.Empty(ScenarioGolden.CompareFrom(RunOf(a), RunOf(b), at));
        }

        var (la, lb) = Halves(2d, lose: true);   // ⛔ a checkpoint that does not carry the health
        var diff = ScenarioGolden.CompareFrom(RunOf(la), RunOf(lb), 2d);
        Assert.StartsWith("FIRST differing snapshot at t=2 s", diff[0]);
        Assert.Contains("Runner", diff[0]);
        Assert.Contains(diff, d => d.Contains("Health"));

        // ⭐ a component the checkpoint does not carry BY DESIGN is not the verdict's — it is listed apart
        Func<string, bool> carried = name => name != "Health";
        var straightRun = RunOf(la);
        var restoredRun = RunOf(lb);
        foreach (var unit in new[] { straightRun, restoredRun }.SelectMany(r => r["snapshots"]!.AsArray()).SelectMany(sn => sn!["units"]!.AsObject()))
            unit.Value!.AsObject()["health"] = null;   // the unit-level health field mirrors the component; drop it for this half
        Assert.Empty(ScenarioGolden.CompareFrom(straightRun, restoredRun, 2d, carried));
        Assert.Contains(ScenarioGolden.CompareNotCarried(straightRun, restoredRun, 2d, carried), d => d.Contains("Health"));
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

    /// <summary>
    /// ⭐⭐ <c>CE-3334</c> (docs/DESIGN_Scenario_Regression.md §4) — a creation request id is minted fresh every run, so its raw value can never
    /// repeat: the golden writes it BY MEANING (<c>"@request:&lt;the first unit it made&gt;"</c>). ① a capture, then ② a fresh run with NEW ids
    /// compares SAME; ③ two units made by ONE request differ from two requests (what the id means is still compared); ④ a golden captured
    /// before the rule (no <c>"meaning"</c>, raw hashes) compares without the ids. 🔴 Red-proof: drop the <c>@request:</c> case in
    /// <c>ScenarioGolden.Meant</c> ⇒ ② is DIFFERENT at t=0, in <c>CreationRequestId</c>.
    /// </summary>
    [Fact]
    public void CE3334_APerRunRequestId_IsComparedByMeaning_SoAGoldenRepeats()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        (DirectorRunner Runner, FakeEditor Host) Fresh()   // ⭐ a fresh process: fresh request ids
        {
            var h = new FakeEditor(store);
            return (new DirectorRunner(h, new Cinema(), new Layers(), verifyWorld: u => h.Verifier = new FakeVerifyWorld(h, u)
            {
                RequestIds = new Dictionary<long, Guid> { [1] = Guid.NewGuid(), [2] = Guid.NewGuid() },
            }), h);
        }
        VerifyResult Verify(bool capture, Action<FakeEditor>? before = null)
        {
            var (runner, host) = Fresh();
            Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(Capture: capture))));
            runner.Tick(); host.EndFrame();
            before?.Invoke(host);
            Run(runner, host);
            return runner.Reports.Single().Verify!;
        }
        var goldenPath = Path.Combine(_root, "s-a", ScenarioGolden.FileName);

        Assert.Equal("captured", Verify(capture: true).Golden);                                                   // ①
        Assert.Equal(ScenarioGolden.MeaningVersion, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(goldenPath))!["meaning"]!.GetValue<int>());

        var again = Verify(capture: false);                                                                        // ②
        Assert.True(again.Golden == "same", string.Join("\n", again.GoldenDiff));

        var shared = Verify(capture: false, h => h.Verifier!.RequestIds![2] = h.Verifier.RequestIds[1]);          // ③
        Assert.Equal("different", shared.Golden);
        Assert.Contains(shared.GoldenDiff, d => d.Contains("CreationRequestId"));

        var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(goldenPath))!.AsObject();              // ④
        legacy.Remove("meaning");
        foreach (var snap in legacy["snapshots"]!.AsArray())
            foreach (var (_, unit) in snap!["units"]!.AsObject())
                unit!["components"]!["CreationRequestId"] = "0123456789abcdef";   // a raw per-run hash, as captured before CE-3334
        File.WriteAllText(goldenPath, legacy.ToJsonString());
        var old = Verify(capture: false);
        Assert.True(old.Golden == "same", string.Join("\n", old.GoldenDiff));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4045</c> H5 (docs/DESIGN_Scenario_Regression.md §10-3, R-4086) — the review check's two halves over the fake editor: A runs LIVE
    /// straight; B runs LIVE to 2 s, waits for the step in flight, reviews back (an entry and a seek per earlier snapshot — 0, 1, 2 s), each
    /// compared with its live snapshot, returns, compares the world with the one it left, and runs on. The halves compare SAME from 2 s.
    /// A return that drops the health is caught twice: the return's own compare and the run after it. 🔴 Red-proof: compare the return
    /// against the review's last snapshot instead of the world it left ⇒ ① reports a RETURN difference.
    /// </summary>
    [Fact]
    public void CE4045_H5_AReviewShowsTheLiveRun_ComesBackExact_AndTheRunGoesOnAsIfStraight()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        var rec = Path.Combine(_root, "rec");
        Directory.CreateDirectory(rec);
        (DirectorRunner Runner, FakeEditor Host) Fresh()
        {
            var h = new FakeEditor(store) { Lag = 1, RecordDir = rec };
            return (new DirectorRunner(h, new Cinema(), new Layers(), verifyWorld: u => h.Verifier = new FakeVerifyWorld(h, u)), h);
        }
        System.Text.Json.Nodes.JsonObject RunOf(string path) => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        (ItemReport Report, FakeEditor Host) Half(bool straight, string runOut, bool losesHealth = false)
        {
            var (runner, host) = Fresh();
            host.ReturnLosesHealth = losesHealth;
            Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(Review: new ReviewCheck(2d, straight, runOut)))));
            Run(runner, host);
            return (runner.Reports.Single(), host);
        }

        string a = Path.Combine(_root, "h5-a.json"), b = Path.Combine(_root, "h5-b.json");
        var (straight, sh) = Half(true, a);
        Assert.Equal(0, sh.Reviews);
        Assert.Null(straight.Verify!.Review);

        var (reviewed, rh) = Half(false, b);                                                               // ①
        var v = reviewed.Verify!;
        Assert.Equal((1, 2, 1), (rh.Reviews, rh.ReviewSeeks, rh.ReviewExits));
        Assert.Equal(0, rh.Enters);                                                                         // live, never a preview
        Assert.All(rh.StepsPerFrame, n => Assert.Equal(1, n));
        Assert.True(v.Review!.Verdict == "same", string.Join("\n", v.Review.Diff.Concat(v.Review.Problems)));
        Assert.Equal(3, v.Review.Snapshots);
        Assert.True(v.ReturnDiff.Count == 0, string.Join("\n", v.ReturnDiff));
        Assert.Equal("pass", reviewed.Status);
        Assert.Empty(ScenarioGolden.CompareFrom(RunOf(a), RunOf(b), 2d));                                 // the run after = the straight run

        var (lossy, _) = Half(false, b, losesHealth: true);                                                 // ② a return that drops the health
        Assert.Equal("fail", lossy.Status);
        Assert.Contains(lossy.Verify!.ReturnDiff, d => d.Contains("Health"));
        Assert.NotEmpty(ScenarioGolden.CompareFrom(RunOf(a), RunOf(b), 2d));

        Assert.Contains(Fresh().Runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: new VerifyOptions(Replay: true, Review: new ReviewCheck(2d, false, b)))),
                        p => p.Contains("review check runs LIVE on its own"));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4045</c> H1b (docs/DESIGN_Scenario_Regression.md §10-2 ②, R-4086) — after H1a, the PRODUCT's cluster replay of the finished
    /// run: the stopped run is entered as a replay ONCE (from Idle, at the first live snapshot's sim time), sought to every later live snapshot
    /// time by recorded SIM time (the same <c>SeekReplayIntent</c> a review seeks with), each world snapshotted once the clock stands there,
    /// then the replay is left for Idle and compared with the live snapshots by the review's comparator. ① a replay that shows the run: SAME
    /// at all four snapshots (0, 1, 2, 3 s), one entry, three seeks asking 1, 2, 3 s, ending Idle, the item passes; ② a replay that does not
    /// show the health: DIFFERENT from the first snapshot after the action, naming the unit and the component — and it fails, while H1a's
    /// check of the recording itself stays SAME. 🔴 Red-proof: skip the replay's seek (<c>if (inLook &amp;&amp; _look == LookMode.Review)</c>) ⇒ ①
    /// fails on its verdict, "the replay did not stand at 1 s / 2 s / 3 s within 30 s (clock 0 s)" (a PROBLEM per target); H1a's rail fails
    /// too (its item no longer passes).
    /// </summary>
    [Fact]
    public void CE4045_H1b_TheProductsReplay_SoughtBySimTime_ShowsTheLiveRun()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        var rec = Path.Combine(_root, "rec");
        Directory.CreateDirectory(rec);
        (DirectorRunner Runner, FakeEditor Host) Fresh()
        {
            var h = new FakeEditor(store) { Lag = 1, RecordDir = rec };
            return (new DirectorRunner(h, new Cinema(), new Layers(), verifyWorld: u => h.Verifier = new FakeVerifyWorld(h, u)), h);
        }
        var replay = new VerifyOptions(Replay: true);

        // ① a replay that shows the live run
        var (runner, host) = Fresh();
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: replay)));
        Run(runner, host);
        var r = runner.Reports.Single();
        Assert.Equal("same", r.Verify!.Replay);                                                    // H1a, unchanged
        var pb = r.Verify.Playback!;
        Assert.True(pb.Verdict == "same", string.Join("\n", pb.Diff.Concat(pb.Problems)));
        Assert.Equal(4, pb.Snapshots);                                                              // t = 0, 1, 2, 3
        Assert.Empty(pb.Problems);
        Assert.Equal(new[] { 0d }, host.ReplayEntries);                                             // ONE entry, at the first snapshot
        Assert.Equal(new[] { 1d, 2d, 3d }, host.SeekAsks);                                          // ⭐ by recorded sim time, one seek per later snapshot
        Assert.Equal((1, 1, 0, 0), (host.Replays, host.ReplayStops, host.Reviews, host.Enters));    // a replay, not a review; live, never a preview
        Assert.Equal(1, host.LiveStops);                                                            // the live run stopped once (for H1a); the replay left once
        Assert.True(host.IsIdle);
        Assert.False(host.IsOperatingReplay);
        Assert.Equal("pass", r.Status);
        Assert.Equal("same", r.Verify.ToJson()["playback"]!["verdict"]!.ToString());
        Assert.Null(r.Verify.ToJson()["review"]);                                                   // not asked

        // ② a replay that does not show the health
        var (bad, badHost) = Fresh();
        badHost.ReplayLosesHealth = true;
        Assert.Empty(bad.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Verify: replay)));
        Run(bad, badHost);
        var b = bad.Reports.Single();
        Assert.Equal("same", b.Verify!.Replay);                                                    // ⭐ the recording itself is right
        Assert.Equal("different", b.Verify.Playback!.Verdict);
        Assert.StartsWith("FIRST differing snapshot at t=2 s", b.Verify.Playback.Diff[0]);          // the action is at 1.5 s
        Assert.Contains("Runner", b.Verify.Playback.Diff[0]);
        Assert.Contains(b.Verify.Playback.Diff, d => d.Contains("Health"));
        Assert.Equal("fail", b.Status);
        Assert.True(badHost.IsIdle);                                                                // a failed check leaves nothing running
    }

}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DirectorCollection
{
    /// <summary>FilmStudio is process-wide.</summary>
    public const string Name = "Film director (FilmStudio is process-wide)";
}
