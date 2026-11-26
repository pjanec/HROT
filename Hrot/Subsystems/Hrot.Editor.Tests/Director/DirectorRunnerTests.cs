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
    private class FakeEditor : IDirectorHost, IVerifyHost
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
        public void LoadScenario(string scenario) { Loads.Add(scenario); _pending = scenario; _loadFrames = 3; IsOperatingEdit = false; _clockOffset = 0d; }
        public bool IsOperatingEdit { get; private set; } = true;
        public bool HasLoadedAScenario => Loads.Count > 0;
        public int? EntityCount => Repo.EntityCount;
        public bool InPreview { get; private set; }
        /// <summary>⭐ R-1046 — the shown map's 3-D build (null: this host has none to wait for).</summary>
        public Fdp.Toolkit.Vis3D.Map3DBuild? MapBuild { get; set; }
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
                foreach (var (id, hp) in o["health"]!.AsObject()) Verifier.Health[int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)] = hp!.GetValue<double>();
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
        public void StopLive() { if (IsOperatingReplay) ReplayStops++; else if (_branched) BranchStops++; else LiveStops++; _stopFrames = 2; }
        public int BranchStops;
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
        private (double SimTime, Dictionary<int, double> Health)? _checkpoint;
        public void EnterReview(double simSeconds) { Reviews++; _reviewAsk = simSeconds; }
        public void SeekReview(double simSeconds) { ReviewSeeks++; SeekAsks.Add(simSeconds); _reviewAsk = simSeconds; }

        // ⭐ CE-4045 H1b — the product's REPLAY of the finished run: entered from Idle (a frame after it is asked), it shows the run's recorded
        //   frames (a seek is SeekReview's — the same SeekReplayIntent), and a stop leaves it for Idle two frames later
        /// <summary>A replay that does not show the health — what H1b exists to catch.</summary>
        public bool ReplayLosesHealth;
        /// <summary>⭐ CE-4045 H2 — CE-4056's shape: a seek BACK in a replay moves the clock to its target but the world stays where the last seek
        /// left it (live, a seek back stood one seek behind).</summary>
        public bool SeekBackStandsBehind;
        public int Replays, ReplayStops;
        /// <summary>The sim seconds each replay entry and each seek asked for, in order.</summary>
        public readonly List<double> ReplayEntries = new(), SeekAsks = new();
        public bool IsOperatingReplay { get; private set; }
        private double? _replayAsk;
        public void EnterReplay(double simSeconds) { Replays++; ReplayEntries.Add(simSeconds); _replayAsk = simSeconds; }
        public void ExitReview() { ReviewExits++; _exitAsk = true; }

        // ⭐ CE-4045 H3 — the D18 branch: from the replay, a frame after it is asked, a new LIVE run from the world the replay shows, on a clock
        //   BranchClockOffset seconds off the replay's (📐 CE-4065: the branch came up on the wall seconds); the world moves on from where it
        //   was, not from the clock
        public double BranchClockOffset;
        /// <summary>A branch that does not carry the health (the world is rebuilt without it) — what H3 exists to catch.</summary>
        public bool BranchLosesHealth;
        public int Branches;
        private bool _branchAsk, _branched;
        private double _clockOffset;
        public void BranchLive() { Branches++; _branchAsk = true; }
        private float X => (float)(SimTime - _clockOffset);

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
                if (!IsOperatingReplay && !IsOperatingReview) { _checkpoint = (SimTime, new Dictionary<int, double>(Verifier!.Health)); IsOperatingReview = true; IsOperatingLive = false; }
                double before = SimTime;
                ShowRecorded(ask);
                if (SeekBackStandsBehind && IsOperatingReplay && ask < before - 1e-9)
                {
                    double landed = SimTime;
                    ShowRecorded(before);   // the world of the last seek …
                    SimTime = landed;       // … under the clock of this one
                }
            }
            if (_branchAsk && IsOperatingReplay)
            {
                _branchAsk = false;
                IsOperatingReplay = false;
                IsOperatingLive = true;
                _branched = true;
                SimTime += BranchClockOffset;
                _clockOffset = BranchClockOffset;
                if (BranchLosesHealth) Verifier!.Health.Clear();
            }
            if (_exitAsk && _checkpoint is { } cp)
            {
                _exitAsk = false;
                IsOperatingReview = false;
                IsOperatingLive = true;
                SimTime = cp.SimTime;
                if (Moves) foreach (var e in Repo.Query().With<SimTransform>().Build())
                    Repo.GetComponentRW<SimTransform>(e).Position = new Vector3(X, 0f, 0f);
                Verifier!.Health.Clear();
                if (!ReturnLosesHealth) foreach (var (id, hp) in cp.Health) Verifier.Health[id] = hp;
            }
        }

        private void Record()
        {
            var units = new System.Text.Json.Nodes.JsonObject();
            foreach (var e in Repo.Query().With<NetworkIdentity>().With<EntityInfo>().Build())
            {
                int id = Repo.GetComponentRO<NetworkIdentity>(e).Value;
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
                Repo.GetComponentRW<SimTransform>(e).Position = new Vector3(X, 0f, 0f);
            _frame++;
            if (_loadFrames > 0 && --_loadFrames == 1)
            {
                var all = new List<Entity>();
                foreach (var e in Repo.Query().With<EntityInfo>().Build()) all.Add(e);
                foreach (var e in all) Repo.DestroyEntity(e);   // the load clears…
            }
            else if (_loadFrames == 0 && _pending != null)
            {
                foreach (var (name, id) in new[] { ("Runner", 1), ("Watcher " + _pending, 2) })    // …and builds the scenario
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
                else if (_branched) { _branched = false; IsOperatingLive = false; IsIdle = true; }   // ⭐ H3 — so is the branch
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
        /// <summary>⭐ CE-1304 — the set in force (on, off) after the last call; null after a clear.</summary>
        public (IReadOnlyList<string> On, IReadOnlyList<string> Off)? Now;
        public int Clears;
        public void Apply(IReadOnlyList<string> on, IReadOnlyList<string> off) { Applied.Add(string.Join(",", on)); Now = (on, off); }
        public void Clear() { Clears++; Now = null; }
    }

    private sealed class Sink : IFrameSink
    {
        public readonly List<string> Frames = new();
        public readonly List<string> Cards = new();
        public readonly List<SubtitleFrame?> Subtitles = new();
        /// <summary>⭐ CE-1305 — each frame's overlay as the runner handed it (only through the overlay overload — the subtitle-only one records none).</summary>
        public readonly List<FilmOverlay> Overlays = new();
        public Task Frame(string path, int width, int height, int supersample, SubtitleFrame? subtitle) { Frames.Add(path); Subtitles.Add(subtitle); return Task.CompletedTask; }
        public Task Frame(string path, int width, int height, int supersample, FilmOverlay overlay)
        {
            Overlays.Add(overlay);
            return Frame(path, width, height, supersample, overlay.Subtitle);
        }
        public Task Card(string path, int width, int height, string text) { Cards.Add(text); File.WriteAllText(path, ""); return Task.CompletedTask; }
    }

    // ── ⭐ CE-11018 (docs/designs/IG/DESIGN_Linked_Client_Director.md §2c, Q113-B / D-3 / D-4) — a job's frames rendered by the node's linked client ──────────

    /// <summary>A linked client, faked: every film frame it is handed, its Begin / End, and an optional frame it fails.</summary>
    private sealed class FakeClient : Hrot.Common.Infrastructure.ILinkedClient
    {
        public readonly List<Hrot.Common.Infrastructure.FilmFrame> Frames = new();
        public readonly List<Hrot.Common.Infrastructure.FilmJob> Begins = new();
        public readonly List<Hrot.Common.Infrastructure.FilmJobOutcome> Ends = new();
        public int FailAt = -1;
        /// <summary>⭐ CE-11021 — the track it hands back: the film's own total plus this many samples (null track: <see cref="NoTrack"/>).</summary>
        public long SampleOffset;
        public bool NoTrack;
        public string Name => "fake client";
        public void BeginWorld(Guid loadId) { }
        public bool IsWorldReady(Guid loadId, out string? failure, out string? step) { failure = null; step = null; return true; }
        public bool Films = true;
        public bool CanFilm => Films;
        public Task BeginFilm(Hrot.Common.Infrastructure.FilmJob job) { lock (Begins) Begins.Add(job); return Task.CompletedTask; }
        public Task Frame(Hrot.Common.Infrastructure.FilmFrame frame)
        {
            lock (Frames) Frames.Add(frame);
            return frame.Index == FailAt ? Task.FromException(new InvalidOperationException($"frame {frame.Index} lost (the client)")) : Task.CompletedTask;
        }
        public Task<Hrot.Common.Infrastructure.FilmTrack?> EndFilm(Hrot.Common.Infrastructure.FilmJob job, Hrot.Common.Infrastructure.FilmJobOutcome outcome)
        {
            lock (Ends) Ends.Add(outcome);
            if (NoTrack) return Task.FromResult<Hrot.Common.Infrastructure.FilmTrack?>(null);
            int frames; lock (Frames) frames = Frames.Count;
            long total = (long)Math.Round(frames * 48000.0 / job.Fps, MidpointRounding.AwayFromZero) + SampleOffset;
            return Task.FromResult<Hrot.Common.Infrastructure.FilmTrack?>(new Hrot.Common.Infrastructure.FilmTrack("r1.wav", total, 48000, 2));
        }
    }

    /// <summary>Two takes with a title card each (a reel of s-a and s-b), captured at 30 fps.</summary>
    private static PlayRequest TwoTitledTakes(string dir, Hrot.Common.Infrastructure.FilmFrames frames = Hrot.Common.Infrastructure.FilmFrames.Auto)
        => new(new Reel { Name = "lc", Items = new[] { "s-a", "s-b" }.Select(s => new ReelItem { Scenario = s, Take = "main", Title = s }).ToList() },
               Capture: new CaptureOptions(dir, Fps: 30, Encode: false, Frames: frames));

    /// <summary>
    /// ⭐⭐ CE-11018 (§2c, D-3, D-4) — a captured reel with a linked client offered goes to the CLIENT: ONE job (Begin once, End once with Ok and the client's track), every
    /// film frame in order — scene frames with the pose the map's REAL film layer served for them (the camera it placed for the shot), a Silence frame for every card
    /// frame — and the map draws the cards and NO scene frame. 🔴 Mutations: route every job to the map (Begin ignores the client) ⇒ no client frames; send the scene
    /// frame without the pose capture (a default camera) ⇒ the camera row; skip the card's Silence frames ⇒ the indices are not contiguous.
    /// </summary>
    [Fact]
    public void CE11018_AReel_RenderedByALinkedClient_GetsEveryFrameInOrder_WithTheFilmLayersPose_AndTheCardsStayTheMaps()
    {
        var store = Store();
        var host = new FakeEditor(store);
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var view = new Fdp.Toolkit.Vis3D.MapViewSwitch(canvas, canvas.Camera);
        using var film = new FilmCaptureLayer(canvas, view, () => host.Repo);
        view.Set(true, animate: false);
        var clients = new Hrot.Common.Infrastructure.LinkedClients();
        var client = new FakeClient();
        clients.Offer(client);
        var map = new Sink();
        var sink = new RoutingFrameSink(map, () => clients);
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        var request = TwoTitledTakes(Path.Combine(_root, "lc-client"));
        Assert.Empty(runner.Play(request));

        var served = new List<Hrot.Common.Infrastructure.FilmCamera>();   // the pose the film layer had each time it served
        for (int f = 0; f < 40000 && runner.Busy; f++)
        {
            runner.Tick();
            film.Draw3D(default);                                         // the map was drawn ⇒ it serves this frame's captures (a pose-only serve draws nothing)
            int before = film.Served;
            film.Update(1f / 60f);
            if (film.Served > before)
            {
                var cam = view.Camera3D;
                served.Add(new Hrot.Common.Infrastructure.FilmCamera(cam.Eye, cam.Forward, cam.Up, cam.FovyDegrees));
            }
            host.EndFrame();
        }
        for (int i = 0; i < 200 && client.Ends.Count == 0; i++) System.Threading.Thread.Sleep(5);
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.Null(runner.Error);

        var job = Assert.Single(client.Begins);
        Assert.Equal((Hrot.Common.Infrastructure.FilmJobKind.Reel, 30, 1280, 720), (job.Kind, job.Fps, job.Width, job.Height));
        Assert.Equal(Hrot.Common.Infrastructure.FilmJobEnd.Ok, Assert.Single(client.Ends).End);
        Assert.Equal("r1.wav", runner.ClientTrack?.Path);

        List<Hrot.Common.Infrastructure.FilmFrame> frames;
        lock (client.Frames) frames = client.Frames.ToList();
        Assert.Equal(Enumerable.Range(0, frames.Count), frames.Select(x => x.Index));   // every film frame, in order, none skipped
        Assert.All(frames, x => Assert.Equal(job.JobId, x.JobId));
        var scenes = frames.Where(x => x.Kind == Hrot.Common.Infrastructure.FilmFrameKind.Scene).ToList();
        Assert.True(scenes.Count >= 20, $"anti-vacuity: scene frames {scenes.Count}");
        Assert.Equal(served.Count, scenes.Count);
        for (int i = 0; i < scenes.Count; i++) Assert.Equal(served[i], scenes[i].Camera);   // ⭐ D-3 — exactly the film layer's camera
        Assert.All(scenes, x => Assert.True(x.Camera.FovDeg > 0f && x.Path.EndsWith(".png", StringComparison.Ordinal)));
        Assert.True(scenes.Zip(scenes.Skip(1)).Count(p => p.Second.Step < p.First.Step) <= request.Reel.Items.Count - 1, "the step goes back only where the next item starts");
        Assert.True(scenes.Max(x => x.Step) > 0, "anti-vacuity: the step counts");

        int perCard = (int)Math.Round(request.Reel.GapSeconds * 30);
        Assert.Equal(2, map.Cards.Count);                                 // the cards are the map's (D-4)
        Assert.Equal(map.Cards.Count * perCard, frames.Count(x => x.Kind == Hrot.Common.Infrastructure.FilmFrameKind.Silence));
        Assert.Empty(map.Frames);                                         // and no scene frame was drawn by the map
    }

    /// <summary>
    /// ⭐ CE-11018 (Q113-B) — the map keeps a job it is given: with NO linked client the frames are the map's as before; with a client offered and
    /// <c>frames: map</c>, the client sees nothing (no Begin, no frame). 🔴 Mutation: ignore <c>Frames</c> in <c>RoutingFrameSink.Begin</c> ⇒ the client films the forced-map job.
    /// </summary>
    [Fact]
    public void CE11018_WithNoClient_OrFramesMap_TheMapFilmsTheJob_AndTheClientSeesNothing()
    {
        var store = Store();
        foreach (var (offer, frames) in new[] { (false, Hrot.Common.Infrastructure.FilmFrames.Auto), (true, Hrot.Common.Infrastructure.FilmFrames.Map) })
        {
            var host = new FakeEditor(store);
            var clients = new Hrot.Common.Infrastructure.LinkedClients();
            var client = new FakeClient();
            if (offer) clients.Offer(client);
            var map = new Sink();
            var runner = new DirectorRunner(host, new Cinema(), new Layers(), new RoutingFrameSink(map, () => clients));
            Assert.Empty(runner.Play(TwoTitledTakes(Path.Combine(_root, "lc-map-" + offer), frames)));
            Run(runner, host);
            Assert.Equal(DirectorPhase.Done, runner.Phase);
            Assert.True(map.Frames.Count >= 20, $"{(offer ? "frames: map" : "no client")}: the map filmed {map.Frames.Count} frames");
            Assert.Empty(client.Begins);
            Assert.Empty(client.Frames);
        }
    }

    /// <summary>
    /// ⭐ CE-11067 (unreal-ig director test H-3) — the sink says where each job's frames went, so a fallback to HROT's map is never silent: the client, frames: map, a client that
    /// cannot film now (named), no client. 🔴 Mutation: Route not set in Begin ⇒ null rows.
    /// </summary>
    [Fact]
    public void CE11067_TheSink_NamesWhereAJobsFramesWent()
    {
        foreach (var (offer, films, frames, expected) in new[]
        {
            (true, true, Hrot.Common.Infrastructure.FilmFrames.Auto, "the linked client 'fake client'"),
            (true, true, Hrot.Common.Infrastructure.FilmFrames.Map, "HROT's map (frames: map)"),
            (true, false, Hrot.Common.Infrastructure.FilmFrames.Auto, "HROT's map: the linked client 'fake client' cannot film now"),
            (false, true, Hrot.Common.Infrastructure.FilmFrames.Auto, "HROT's map (no linked client)"),
        })
        {
            var clients = new Hrot.Common.Infrastructure.LinkedClients();
            if (offer) clients.Offer(new FakeClient { Films = films });
            var sink = new RoutingFrameSink(new Sink(), () => clients);
            Assert.Null(sink.Route);
            sink.Begin(new Hrot.Common.Infrastructure.FilmJob(Guid.NewGuid(), Hrot.Common.Infrastructure.FilmJobKind.Reel, "r", 30, 640, 360, 1, "f", Frames: frames));
            Assert.Equal(expected, sink.Route);
            Assert.Equal(expected, ((IFrameSink)sink).Route);
        }
    }

    /// <summary>
    /// ⭐ CE-11018 — a frame the client FAILS fails the job with the client's reason, and the client is told the job FAILED. 🔴 Mutation: drop the capture-fault check in
    /// the runner's capture wait ⇒ the run ends Done.
    /// </summary>
    [Fact]
    public void CE11018_AFrameTheClientFails_FailsTheJobWithItsReason()
    {
        var store = Store();
        var host = new FakeEditor(store);
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var view = new Fdp.Toolkit.Vis3D.MapViewSwitch(canvas, canvas.Camera);
        using var film = new FilmCaptureLayer(canvas, view, () => host.Repo);
        view.Set(true, animate: false);
        var clients = new Hrot.Common.Infrastructure.LinkedClients();
        var client = new FakeClient { FailAt = 12 };
        clients.Offer(client);
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), new RoutingFrameSink(new Sink(), () => clients));
        Assert.Empty(runner.Play(TwoTitledTakes(Path.Combine(_root, "lc-fail"))));
        for (int f = 0; f < 40000 && runner.Busy; f++) { runner.Tick(); film.Draw3D(default); film.Update(1f / 60f); host.EndFrame(); }
        for (int i = 0; i < 200 && client.Ends.Count == 0; i++) System.Threading.Thread.Sleep(5);
        Assert.Equal(DirectorPhase.Stopped, runner.Phase);
        Assert.Contains("frame 12 lost (the client)", runner.Error);
        var end = Assert.Single(client.Ends);
        Assert.Equal(Hrot.Common.Infrastructure.FilmJobEnd.Failed, end.End);
        Assert.Contains("frame 12 lost", end.Reason);
    }

    /// <summary>
    /// ⭐ CE-11018 (D-3) — a take showing the 2-D map cannot go to a linked client: the job fails with the reason (render it with <c>frames: map</c>). 🔴 Mutation: drop the
    /// 2-D check in <c>RoutingFrameSink.Frame</c> ⇒ the job hangs on a pose no 2-D map serves, or ends Done.
    /// </summary>
    [Fact]
    public void CE11018_A2DTake_RoutedToALinkedClient_IsRefusedWithTheReason()
    {
        var store = Store();
        store.Save(new Take
        {
            Scenario = "s-a", Name = "flat", TickHz = 60, EndAt = new AtExpr(1.0),
            Keys = new[] { new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { Follow = "Runner", Distance = 40 } } },
            Views = new[] { new ViewChange { At = new AtExpr(0), TwoD = true } },
        });
        var host = new FakeEditor(store);
        var clients = new Hrot.Common.Infrastructure.LinkedClients();
        var client = new FakeClient();
        clients.Offer(client);
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), new RoutingFrameSink(new Sink(), () => clients));
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "flat"), Capture: new CaptureOptions(Path.Combine(_root, "lc-2d"), Fps: 30, Encode: false))));
        Run(runner, host);
        for (int i = 0; i < 200 && client.Ends.Count == 0; i++) System.Threading.Thread.Sleep(5);
        Assert.Contains(RoutingFrameSink.TwoDRefused, runner.Error);
        Assert.Equal(Hrot.Common.Infrastructure.FilmJobEnd.Failed, Assert.Single(client.Ends).End);
        Assert.DoesNotContain(client.Frames, x => x.Kind == Hrot.Common.Infrastructure.FilmFrameKind.Scene);
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

    /// <summary>
    /// ⭐ CE-4267 — an editor whose load goes through the scenario DOOR: <see cref="Answer"/> decides each load — it goes (the fake's 3-frame load), it is refused for
    /// good (the user's unsaved edits / a run), it is "not ready" for <see cref="NotReadyAsks"/> asks first, or it never goes (a question nobody answers — today's defect).
    /// </summary>
    private sealed class DoorEditor : FakeEditor, IDirectorHost, IDirectorLoadOutcome
    {
        public enum DoorAnswer { Goes, Refuses, NeverGoes }
        public DoorAnswer Answer = DoorAnswer.Goes;
        public int NotReadyAsks;
        public int Asks;
        public DoorEditor(TakeStore takes) : base(takes) { }
        public bool LoadWent { get; private set; }
        public string? LoadRefusal { get; private set; }
        public bool LoadMayRetry { get; private set; }
        public new void LoadScenario(string scenario)
        {
            Asks++;
            LoadWent = false; LoadRefusal = null; LoadMayRetry = false;
            if (NotReadyAsks > 0) { NotReadyAsks--; LoadRefusal = "the editor is not ready (loading)"; LoadMayRetry = true; return; }
            switch (Answer)
            {
                case DoorAnswer.Refuses: LoadRefusal = "This scenario has unsaved changes. — save or discard it in the editor, then play again"; return;
                case DoorAnswer.NeverGoes: return;
                default: base.LoadScenario(scenario); LoadWent = true; return;
            }
        }
    }

    /// <summary>
    /// ⭐ CE-4267 — a take whose load the door REFUSES (the user's own unsaved edits) fails WITH the reason: the card keeps the take's title and "cannot play: …"
    /// for <see cref="DirectorRunner.FailCardSeconds"/> before the next title (⛔ was: the next title in the same frame — "a title on black, then the next title"),
    /// and the next take still plays. 🔴 Red-proof (compiles): <c>Fail</c> calling <c>FinishItem()</c> at once ⇒ no "cannot play" card.
    /// </summary>
    [Fact]
    public void CE4267_ATakeTheDoorRefuses_SaysWhyOnScreen_ThenTheNextTakePlays()
    {
        var host = new DoorEditor(Store()) { Answer = DoorEditor.DoorAnswer.Refuses };
        var cinema = new Cinema();
        var runner = new DirectorRunner(host, cinema, new Layers());
        var reel = new Reel { Name = "r", Items = new[] { "s-a", "s-b" }.Select(s => new ReelItem { Scenario = s, Take = "main", Title = s }).ToList() };
        Assert.Empty(runner.Play(new PlayRequest(reel)));
        for (int f = 0; f < 50 && runner.Phase != DirectorPhase.Failing; f++) { runner.Tick(); host.EndFrame(); }
        Assert.Equal(DirectorPhase.Failing, runner.Phase);                                  // held on the reason, not on to the next title
        Assert.Contains(cinema.Cards, c => c!.StartsWith("s-a") && c.Contains("cannot play:") && c.Contains("unsaved changes"));
        host.Answer = DoorEditor.DoorAnswer.Goes;                                          // the second take's load goes
        Run(runner, host);
        Assert.Equal("failed", runner.Reports[0].Status);
        Assert.Contains(runner.Reports[0].Problems, p => p.Contains("was not loaded") && p.Contains("unsaved changes"));
        Assert.Equal("ok", runner.Reports[1].Status);
        Assert.Equal(new[] { "s-b" }, host.Loads);                                          // the refused load never reached the fake's world
    }

    /// <summary>
    /// ⭐ CE-4267 — a load the door answers "not ready" (the run gate) is asked AGAIN until it goes, and the take then plays; ⛔ the readiness probe never
    /// starts before the load went, so an old world is never taken for the new one. 🔴 Red-proof (compiles): the probe created on the first ask
    /// (before <c>LoadWent</c>) ⇒ the take plays on the world that was already there (no second ask, the fake never loaded).
    /// </summary>
    [Fact]
    public void CE4267_ANotReadyLoad_IsAskedAgain_AndTheTakePlaysOnItsOwnWorld()
    {
        var host = new DoorEditor(Store()) { NotReadyAsks = 3 };
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, host);
        Assert.Equal("ok", runner.Reports[0].Status);
        Assert.Equal(4, host.Asks);                                                         // three "not ready", then it went
        Assert.Equal(new[] { "s-a" }, host.Loads);
    }

    /// <summary>
    /// ⭐ CE-4267 — a load that NEVER goes (today's defect: the door's question shown to nobody) fails after the load timeout with that reason — ⛔ never
    /// accepted as "loaded" because the old world sat still for the probe's grace. 🔴 Red-proof: as above (the probe started before the load went ⇒ "ok").
    /// </summary>
    [Fact]
    public void CE4267_ALoadThatNeverGoes_FailsWithTheReason_NeverPlaysTheOldWorld()
    {
        var host = new DoorEditor(Store()) { Answer = DoorEditor.DoorAnswer.NeverGoes };
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, host, maxFrames: 200000);
        Assert.Equal("failed", runner.Reports[0].Status);
        Assert.Contains(runner.Reports[0].Problems, p => p.Contains("was not loaded within") && p.Contains("did not take the load"));
        Assert.Empty(host.Loads);
    }

    /// <summary>A load that takes <see cref="LoadSeconds"/> of wall time (a large terrain) and reports its progress the way the run gate does.</summary>
    private sealed class SlowEditor : FakeEditor, IDirectorHost
    {
        public double LoadSeconds = 45d;
        public bool Advances = true;
        public string? Error;
        private double _readyAt;
        public SlowEditor(TakeStore takes) : base(takes) { }
        public new void LoadScenario(string scenario) { base.LoadScenario(scenario); _readyAt = Wall + LoadSeconds; }
        public new bool IsOperatingEdit => base.IsOperatingEdit && Wall >= _readyAt;
        public string? LoadProgress => Wall >= _readyAt ? null : Advances ? $"terrain: valley {(int)(Wall / 5d)}" : "terrain: valley";
        public string? LoadError => Error;
    }

    /// <summary>
    /// ⭐ CE-11023 — no fixed wall while the load ADVANCES: a 45 s load whose step keeps changing plays its take; the same load reporting nothing new fails
    /// after <see cref="DirectorRunner.LoadStallSeconds"/> saying so; the cluster's error state fails it at once with the reason.
    /// 🔴 Mutations: the stall clock never reset ⇒ the advancing load fails at 30 s; the error ignored ⇒ the error case fails as a stall, not with its reason.
    /// </summary>
    [Fact]
    public void CE11023_ALongLoadThatAdvances_Plays_AStalledOneFails_AndTheClustersErrorFailsAtOnce()
    {
        var advancing = new SlowEditor(Store());
        var runner = new DirectorRunner(advancing, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, advancing, maxFrames: 200000);
        Assert.Equal("ok", runner.Reports[0].Status);

        var stalled = new SlowEditor(Store()) { Advances = false };
        runner = new DirectorRunner(stalled, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, stalled, maxFrames: 200000);
        Assert.Equal("failed", runner.Reports[0].Status);
        Assert.Contains(runner.Reports[0].Problems, p => p.Contains("made no progress for 30 s") && p.Contains("terrain: valley"));

        var broken = new SlowEditor(Store()) { Error = "Brain (CGF, node 3) lost" };
        runner = new DirectorRunner(broken, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner, broken, maxFrames: 200000);
        Assert.Contains(runner.Reports[0].Problems, p => p.Contains("the cluster is in error — Brain (CGF, node 3) lost"));
        Assert.True(broken.Wall < 10d, $"the error failed the load only at {broken.Wall:0.0} s");
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

    /// <summary>
    /// ⭐ CE-11035 (R-11015) — a live play hands the subtitle to <c>FilmStudio.Caption</c> (what the suggested camera's caption reads): the line, its position, an alpha that
    /// fades in and out, none outside it — and the studio's serial grew exactly once for the one line. 🔴 Mutation: the runner not setting <c>FilmStudio.Caption</c> ⇒ no frame carries it.
    /// </summary>
    [Fact]
    public void CE11035_ALivePlay_HandsTheSubtitleToTheStudio_ForTheSuggestedCamera()
    {
        var store = Store();
        var host = new FakeEditor(store);
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), new Sink());
        var take = new Take
        {
            Scenario = "s-a", Name = "cap", TickHz = 60, EndAt = new AtExpr(2.5),
            Keys = new[] { new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { LookAt = Vector3.Zero, Distance = 10 } } },
            Subtitles = new[] { new Subtitle { At = new AtExpr(0.5), Until = new AtExpr(1.5), Text = "Contact", Position = SubtitlePosition.Top, Fade = 0.5 } },
        };
        var reel = new Reel { Name = "r", Items = new[] { new ReelItem { Scenario = "s-a", Take = "cap" } } };
        Assert.Empty(runner.Play(new PlayRequest(reel, Inline: new Dictionary<string, Take> { ["s-a/cap"] = take })));

        int shown = 0, withText = 0, topFrames = 0; float maxAlpha = 0f, minWhileShown = 1f, lastAlpha = 0f; int serial = 0;
        for (int f = 0; f < 20000 && runner.Busy; f++)
        {
            runner.Tick();
            if (runner.Phase == DirectorPhase.Playing)
            {
                FilmStudio.ReadCaption(out serial, out var text, out var pos, out var alpha);
                if (alpha > 0f) { shown++; maxAlpha = Math.Max(maxAlpha, alpha); minWhileShown = Math.Min(minWhileShown, alpha); if (text == "Contact") withText++; if (pos == (byte)SubtitlePosition.Top) topFrames++; }
                lastAlpha = alpha;
            }
            host.EndFrame();
        }
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.True(shown >= 25, $"frames with a subtitle: {shown}");
        Assert.Equal((shown, shown), (withText, topFrames));
        Assert.True(maxAlpha >= 0.999f && minWhileShown < 0.8f, $"alpha {minWhileShown} .. {maxAlpha}");
        Assert.Equal(1, serial);          // one line = one serial
        Assert.Equal(0f, lastAlpha);      // none after its end
    }

    /// <summary>
    /// ⭐ <c>CE-1370</c> — a captured frame is handed the subtitle WITH its position and fade, as the cinema overlay gets it (not just its text): a take's
    /// top subtitle fading over 0.5 s reaches the sink at <c>Top</c>, its alpha rising from ~0 to 1 and falling again, and no frame past its end carries one.
    /// 🔴 Mutation: the runner handing the sink <c>new SubtitleFrame(_subNow.Text, SubtitlePosition.Bottom, 1f)</c> (the text only, as before) ⇒ no <c>Top</c>
    /// frame, no mid-fade alpha ⇒ red.
    /// </summary>
    [Fact]
    public void CE1370_ACapturedFrame_IsHandedTheSubtitle_WithItsPositionAndFade()
    {
        var store = Store();
        store.Save(new Take
        {
            Scenario = "s-a", Name = "fade",
            Shots = new[] { new ShotSpec { At = new AtExpr(0.5), Seconds = 1.0, Camera = new CameraSpec { Follow = "Runner", Distance = 10 } } },
            Subtitles = new[] { new Subtitle { At = new AtExpr(0.5), Until = new AtExpr(1.5), Text = "Contact", Position = SubtitlePosition.Top, Fade = 0.5 } },
        });
        var host = new FakeEditor(store);
        var sink = new Sink();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "fade"), Capture: new CaptureOptions(Path.Combine(_root, "film-fade"), Fps: 30, Encode: false))));
        Run(runner, host);
        Assert.Equal(DirectorPhase.Done, runner.Phase);

        var shown = sink.Subtitles.Where(s => s != null).Select(s => s!).ToList();
        Assert.True(shown.Count >= 25, $"frames with a subtitle: {shown.Count} of {sink.Subtitles.Count}");
        Assert.All(shown, s => { Assert.Equal(("Contact", SubtitlePosition.Top), (s.Text, s.Position)); });
        Assert.Contains(shown, s => s.Alpha is > 0.2f and < 0.8f);   // mid-fade, in or out
        Assert.Contains(shown, s => s.Alpha >= 0.999f);              // and fully opaque between
        Assert.True(shown[0].Alpha < 0.2f && shown[^1].Alpha < 0.2f, $"the ends fade: {shown[0].Alpha} … {shown[^1].Alpha}");
    }

    // ── ⭐ CE-1304 / CE-1305 P6 (docs/DESIGN_Director_Timeline_Editor.md §11t) — the View track and the Graphics track, played ──────────────────────

    /// <summary>A keys take of 3 s on the moving "Runner" with a View track (3-D + cards from 0, the 2-D map + labels from 1, the 3-D map from 2 — layers kept) and
    /// a ring on the Runner from 0.6 to 1.2 s that fades over 0.2 s.</summary>
    /// <summary>⭐ CE-1305 TE-11 — the sound seam, faked: every rate the runner hands it, and its resets.</summary>
    private sealed class Sound : IDirectorSound
    {
        public readonly List<double> Rates = new();
        public int Resets;
        public void SetTakeSound(bool? on) { }
        public void BeginLog() { }
        public bool EndLog(string wavPath, IReadOnlyList<double> frameSimTimes, int fps) { Resets++; return false; }
        public void Reset() => Resets++;
        public void SetRate(double rate) => Rates.Add(rate);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1305</c> TE-11 (docs/DESIGN_Director_Timeline_Editor.md §11v A2, success condition 1 — the live half): while a take plays, the runner hands the
    /// sound seam the film's rate EVERY playing frame — a 0.5× segment, then a 4× one, each × the request's speed (what <see cref="DirectorRunner.Rate"/> says that
    /// frame), 0 while paused — and resets it when the film ends. 🔴 Mutation: the runner's per-frame <c>_sound?.SetRate(…)</c> removed ⇒ no rate reaches the
    /// sound ⇒ red.
    /// </summary>
    [Fact]
    public void CE1305_TE11_TheSceneIsHeard_AtTheRunnersRate_FrameByFrame_SilentWhilePaused()
    {
        var host = new FakeEditor(Store());
        var sound = new Sound();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink: null, sound: sound);
        var take = new Take
        {
            Scenario = "s-a", Name = "te11", TickHz = 60, EndAt = new AtExpr(2),
            Keys = new[] { new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { Follow = "Runner", Distance = 20 } } },
            Segments = new[]
            {
                new TimeSegment { From = new AtExpr(0), To = new AtExpr(1), Speed = 0.5 },
                new TimeSegment { From = new AtExpr(1), To = new AtExpr(2), Speed = 4 },
            },
        };
        var reel = new Reel { Name = "r", Items = new[] { new ReelItem { Scenario = "s-a", Take = "te11" } } };
        Assert.Empty(runner.Play(new PlayRequest(reel, Speed: 1d, Inline: new Dictionary<string, Take> { ["s-a/te11"] = take })));
        var heard = new List<(double Rate, double Sound)>();
        bool paused = false;
        for (int f = 0; f < 40000 && runner.Busy; f++)
        {
            runner.Tick();
            if (runner.Phase == DirectorPhase.Playing && sound.Rates.Count > 0)
            {
                if (paused) { Assert.Equal(0d, sound.Rates[^1]); runner.Resume(); paused = false; }
                else
                {
                    heard.Add((runner.Rate, sound.Rates[^1]));
                    if (heard.Count == 20) { runner.Pause(); paused = true; }   // once: the next frame must be silent
                }
            }
            host.EndFrame();
        }
        Assert.False(runner.Busy, $"{heard.Count} heard; " + runner.Status().ToJsonString());
        Assert.True(heard.Count > 30, $"{heard.Count} playing frames");
        Assert.All(heard, h => Assert.Equal(h.Rate, h.Sound, 6));                    // the rate of THIS frame
        Assert.Contains(heard, h => Math.Abs(h.Sound - 0.5) < 1e-6);                 // the slow segment …
        Assert.Contains(heard, h => Math.Abs(h.Sound - 4d) < 1e-6);                  // … then the fast one
        Assert.Contains(0d, sound.Rates);                                             // paused: silent
        Assert.True(sound.Resets > 0);                                                // the film's end hands the sound back
    }

    private static Take ViewAndGraphicsTake() => new()
    {
        Scenario = "s-a", Name = "p6", TickHz = 60, EndAt = new AtExpr(3),
        Keys = new[] { new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { Follow = "Runner", Distance = 20 } } },
        Views = new[]
        {
            new ViewChange { At = new AtExpr(0), TwoD = false, Layers = new LayerChange(new[] { "Units.Cards" }, Array.Empty<string>()) },
            new ViewChange { At = new AtExpr(1), TwoD = true, Layers = new LayerChange(new[] { "Units.Labels" }, new[] { "Map.Roads" }) },
            new ViewChange { At = new AtExpr(2), TwoD = false },
        },
        Graphics = new[] { new FilmGraphic { At = new AtExpr(0.6), Until = new AtExpr(1.2), Kind = GraphicKind.Ring, Who = "Runner", Text = "Runner", Fade = 0.2 } },
    };

    /// <summary>
    /// ⭐⭐ <c>CE-1304</c> P6 (§11t V4, success condition 1 — the fake host): at film second t the runner applies the take's VIEW through the EXISTING seams — the
    /// layer set through <see cref="ILayerOverride"/> (the cards before 1 s; labels on and roads off from 1 s; the same set after the 2 s item, which switches only
    /// the map) and the map through <see cref="FilmStudio.TwoD"/> (3-D, 2-D in [1, 2), 3-D again) — each change applied ONCE, not every frame; the film's end clears
    /// the override and gives the 3-D map back. 🔴 Mutation: <c>ApplyView</c> not setting <c>FilmStudio.TwoD</c> ⇒ the 2-D stretch reads 3-D ⇒ red.
    /// </summary>
    [Fact]
    public void CE1304_P6_TheRunner_AppliesTheViewTrack_ThroughTheLayerOverride_AndTheMapMode()
    {
        var host = new FakeEditor(Store());
        var layers = new Layers();
        var runner = new DirectorRunner(host, new Cinema(), layers);
        var reel = new Reel { Name = "r", Items = new[] { new ReelItem { Scenario = "s-a", Take = "p6" } } };
        Assert.Empty(runner.Play(new PlayRequest(reel, Inline: new Dictionary<string, Take> { ["s-a/p6"] = ViewAndGraphicsTake() })));

        var samples = new List<(double Rel, bool TwoD, string On, string Off)>();
        double? t0 = null;
        int appliedBefore = 0;
        for (int f = 0; f < 20000 && runner.Busy; f++)
        {
            runner.Tick();
            if (runner.Phase == DirectorPhase.Playing && layers.Now is { } now)
            {
                t0 ??= host.SimTime;
                appliedBefore = appliedBefore == 0 ? layers.Applied.Count : appliedBefore;
                samples.Add((host.SimTime - t0.Value, FilmStudio.TwoD, string.Join(",", now.On), string.Join(",", now.Off)));
            }
            host.EndFrame();
        }
        Assert.Equal("ok", runner.Reports[0].Status);
        Assert.True(samples.Count > 150, $"{samples.Count} playing frames");
        Assert.All(samples.Where(s => s.Rel < 1 - 0.02), s => Assert.Equal((false, "Units.Cards", ""), (s.TwoD, s.On, s.Off)));
        Assert.All(samples.Where(s => s.Rel > 1 + 0.02 && s.Rel < 2 - 0.02), s => Assert.Equal((true, "Units.Labels", "Map.Roads"), (s.TwoD, s.On, s.Off)));
        Assert.All(samples.Where(s => s.Rel > 2 + 0.02), s => Assert.Equal((false, "Units.Labels", "Map.Roads"), (s.TwoD, s.On, s.Off)));   // the map only
        Assert.True(layers.Applied.Count <= appliedBefore + 3, $"applied {layers.Applied.Count} times — a view is applied when it changes, not every frame");
        Assert.Null(layers.Now);                                                       // the film's end: the user's own layers …
        Assert.False(FilmStudio.TwoD);                                                 // … and the 3-D map
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1305</c> P6 (§11t G2, success condition 2 — the play rail): at film second t the runner puts the take's GRAPHICS on screen
    /// (<see cref="FilmStudio.Graphics"/>, the map's screen pass reads it): none before 0.6 s, the ring ON THE RUNNER as it moves (its x = its position that
    /// frame), fading in over 0.2 s and out before 1.2 s, none after; released with the film. 🔴 Mutation: the runner skipping the per-frame
    /// <c>FilmStudio.Graphics = _graphicsNow</c> ⇒ the screen never gets the ring ⇒ red.
    /// </summary>
    [Fact]
    public void CE1305_P6_TheRunner_PutsTheGraphicsOnScreen_AtTheirFilmTime_OnTheirUnit()
    {
        var host = new FakeEditor(Store());
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        var reel = new Reel { Name = "r", Items = new[] { new ReelItem { Scenario = "s-a", Take = "p6" } } };
        Assert.Empty(runner.Play(new PlayRequest(reel, Inline: new Dictionary<string, Take> { ["s-a/p6"] = ViewAndGraphicsTake() })));
        var samples = new List<(double Rel, IReadOnlyList<GraphicFrame> Shown, float RunnerX)>();
        double? t0 = null;
        for (int f = 0; f < 20000 && runner.Busy; f++)
        {
            runner.Tick();
            if (runner.Phase == DirectorPhase.Playing)
            {
                t0 ??= host.SimTime;
                float runnerAt = float.NaN;
                foreach (var e in host.Repo.Query().With<SimTransform>().With<EntityInfo>().Build())
                    if (host.Repo.GetComponentRO<EntityInfo>(e).Name.ToString() == "Runner") runnerAt = host.Repo.GetComponentRO<SimTransform>(e).Position.X;
                samples.Add((host.SimTime - t0.Value, FilmStudio.Graphics, runnerAt));
            }
            host.EndFrame();
        }
        Assert.Equal("ok", runner.Reports[0].Status);
        Assert.All(samples.Where(s => s.Rel < 0.6 - 0.02 || s.Rel > 1.2 + 0.02), s => Assert.Empty(s.Shown));
        var on = samples.Where(s => s.Rel > 0.6 + 0.02 && s.Rel < 1.2 - 0.02).ToList();
        Assert.True(on.Count > 30, $"{on.Count} frames with the ring");
        Assert.All(on, s =>
        {
            var g = Assert.Single(s.Shown);
            Assert.Equal((GraphicKind.Ring, "Runner"), (g.Kind, g.Text));
            Assert.Equal(s.RunnerX, g.Points[0].X, 0.001f);                             // on the unit, where it is this frame
        });
        Assert.Contains(on, s => s.Shown[0].Alpha is > 0.2f and < 0.8f);              // fading
        Assert.Contains(on, s => s.Shown[0].Alpha >= 0.999f);
        Assert.True(on[^1].RunnerX - on[0].RunnerX > 0.4f, "the Runner moved, and the ring with it");
        Assert.Empty(FilmStudio.Graphics);                                              // released with the film
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1305</c> P6 (§11t G3, success condition 2 — the capture rail, as CE-1370's): a captured frame is handed the graphics WITH the subtitle in ONE
    /// <see cref="FilmOverlay"/> — the frames in [0.6, 1.2) carry the ring on the Runner with its fade, the others none, and the subtitle still rides along.
    /// 🔴 Mutation: the runner's capture calling the subtitle-only <c>Frame(path, w, h, ss, _subNow)</c> (as before CE-1305) ⇒ the sink gets no overlay ⇒ red.
    /// </summary>
    [Fact]
    public void CE1305_ACapturedFrame_IsHandedTheGraphics_WithTheSubtitle()
    {
        var store = Store();
        store.Save(ViewAndGraphicsTake() with
        {
            Subtitles = new[] { new Subtitle { At = new AtExpr(0.5), Until = new AtExpr(1.5), Text = "Contact" } },
        });
        var host = new FakeEditor(store);
        var sink = new Sink();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "p6"), Capture: new CaptureOptions(Path.Combine(_root, "film-p6"), Fps: 30, Encode: false))));
        Run(runner, host);
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.Equal(sink.Frames.Count, sink.Overlays.Count);                          // ⭐ every frame through the overlay
        var withRing = sink.Overlays.Where(o => o.Graphics.Count > 0).ToList();
        Assert.InRange(withRing.Count, 15, 20);                                        // 0.6 s at 30 fps
        Assert.All(withRing, o =>
        {
            var g = Assert.Single(o.Graphics);
            Assert.Equal((GraphicKind.Ring, "Runner"), (g.Kind, g.Text));
            Assert.Equal("Contact", o.Subtitle?.Text);
        });
        Assert.Contains(withRing, o => o.Graphics[0].Alpha is > 0.2f and < 0.8f);
        Assert.Contains(sink.Overlays, o => o.Graphics.Count == 0 && o.Subtitle?.Text == "Contact");   // 0.5–0.6 and 1.2–1.5: the subtitle alone
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1304</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §11t V7, "CE-1380 / CE-1379 as built") — a film that ENDS in 2-D gives the person the map
    /// mode they had before it: the person works in 3-D, the take's View track holds the 2-D map, and when the film ends — and again when a second run is
    /// STOPPED half way — the map on screen (a real <see cref="FilmCaptureLayer"/> over a headless map) is 3-D again, through the one switch. 🔴 Mutation:
    /// <c>FilmCaptureLayer.Direct</c> not restoring <c>_personsMap</c> on release ⇒ the map stays 2-D ⇒ red.
    /// </summary>
    [Fact]
    public void CE1304_P6_AFilmEndingIn2D_GivesThePersonTheirMapModeBack_AtItsEnd_AndOnStop()
    {
        var store = Store();
        store.Save(new Take
        {
            Scenario = "s-a", Name = "v2d", TickHz = 60, EndAt = new AtExpr(1.5),
            Keys = new[] { new CameraKey { At = new AtExpr(0), Camera = new CameraSpec { Follow = "Runner", Distance = 40 } } },
            Views = new[] { new ViewChange { At = new AtExpr(0), TwoD = true } },
        });
        var host = new FakeEditor(store);
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var view = new Fdp.Toolkit.Vis3D.MapViewSwitch(canvas, canvas.Camera);
        using var film = new FilmCaptureLayer(canvas, view, () => host.Repo);
        view.Set(true, animate: false);                                                  // the person works in the 3-D map
        Assert.True(view.Is3D);

        (bool SawTwoD, bool StoppedHalfWay) Play(bool stop)
        {
            var runner = new DirectorRunner(host, new Cinema(), new Layers());
            Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "v2d"))));
            bool sawTwoD = false, stopped = false;
            for (int f = 0; f < 20000 && (runner.Busy || view.IsSwitching || f < 3); f++)
            {
                runner.Tick();
                film.Update(1f / 60f);
                view.Camera3D.Update(1f / 60f);                                           // the switch animates through the camera's update
                if (runner.Phase == DirectorPhase.Playing && !view.Is3D && !view.IsSwitching) sawTwoD = true;
                if (stop && sawTwoD && !stopped) { runner.Stop(); stopped = true; }
                host.EndFrame();
            }
            for (int f = 0; f < 120; f++) { film.Update(1f / 60f); view.Camera3D.Update(1f / 60f); }   // a switch still landing lands
            return (sawTwoD, stopped);
        }

        var ended = Play(stop: false);
        Assert.True(ended.SawTwoD, "anti-vacuity: the film held the 2-D map");
        Assert.True(view.Is3D, "the film's end gives the person's 3-D map back");

        var halted = Play(stop: true);
        Assert.True(halted.SawTwoD && halted.StoppedHalfWay, "anti-vacuity: stopped while the film held the 2-D map");
        Assert.True(view.Is3D, "Stop gives the person's 3-D map back");
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
        Assert.Equal(new[] { "One", "S-B" }, sink.Cards);   // ⭐ CE-4121: an item with no title of its own opens on its TAKE's title (S-B), not the scenario's name
        Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));
        Assert.Empty(host.Holds.Where(h => h > 0));
    }

    /// <summary>
    /// ⭐ <c>CE-4121</c> — a single-take capture whose take is titled "Level bomb" opens on a card that says so and names its chapter so (not the scenario's name, <c>s-a</c>); an item's own title still wins.
    /// 🔴 Mutation: <c>Item.Title ?? take.Title ?? Item.Scenario</c> → <c>Item.Title ?? Item.Scenario</c> (and the card likewise) ⇒ "s-a" ⇒ red.
    /// </summary>
    [Fact]
    public void CE4121_ACaptureOfOneTake_IsCalledByTheTakesTitle_NotTheScenariosName()
    {
        var store = Store();
        store.Save(new Take { Scenario = "s-a", Name = "bomb", Title = "Level bomb", Shots = new[] { new ShotSpec { At = new AtExpr(0.5), Seconds = 0.5, Camera = new CameraSpec { Follow = "Runner", Distance = 10 } } } });
        var host = new FakeEditor(store);
        var sink = new Sink();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        var dir = Path.Combine(_root, "film-title");
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "bomb"), Capture: new CaptureOptions(dir, Fps: 30))));
        for (int f = 0; f < 20000 && runner.Busy && runner.Phase != DirectorPhase.Encoding; f++) { runner.Tick(); host.EndFrame(); }   // to the encoding: chapters.txt is written as it starts
        Assert.Equal("Level bomb", sink.Cards[0]);                                              // the opening card
        string chapters = File.ReadAllText(Path.Combine(dir, "chapters.txt"));
        Assert.Contains("title=Level bomb", chapters);                                          // the movie's chapter
        Assert.DoesNotContain("title=s-a\n", chapters);   // (the movie's own title is the reel name, s-a/bomb)
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

    /// <summary>
    /// ⭐ <c>R-1046</c> (DESIGN_Cluster_Load_Phase §11.9) — the probe's <c>mapBuilt</c> input: a load that is otherwise ready (target state, world seen changing and settled) is NOT ready while the shown map's 3-D build is not built;
    /// the default (a host with no 3-D map) is built. 🔴 Mutation: <c>mapBuilt</c> dropped from the return ⇒ the first cell reds.
    /// </summary>
    [Fact]
    public void R1046_TheReadyProbe_AlsoWaitsForTheMapBuild()
    {
        var p = new ScenarioReadyProbe(8);
        Assert.False(p.Observe(true, 0, 0.1, mapBuilt: false));
        Assert.False(p.Observe(true, 8, 0.2, mapBuilt: false));
        Assert.False(p.Observe(true, 8, 0.3, mapBuilt: false), "the world has changed and settled, the map is still building");
        Assert.True(p.Observe(true, 8, 0.4, mapBuilt: true), "built: ready — the bookkeeping kept running while it waited");
        var noMap = new ScenarioReadyProbe(8);
        noMap.Observe(true, 0, 0.1); noMap.Observe(true, 8, 0.2);
        Assert.True(noMap.Observe(true, 8, 0.3));                                    // no map to wait for: unchanged
    }

    /// <summary>A background-built layer with no GPU: its built flag is the test's to set; it counts the builds asked of it.</summary>
    private sealed class HeldMapLayer : Fdp.Toolkit.Vis3D.IMeshLayer3D
    {
        public bool Built; public int Requests; public string? Failed;
        public string BuildName => "terrain";
        public bool HasSource => true;
        public bool IsBuilt => Built;
        public string? BuildFailure => Failed;
        public void RequestBuild() => Requests++;
        public void PumpBuild() { }
    }

    /// <summary>
    /// ⭐⭐ <c>R-1046</c> — <b>the director waits for the map</b>: the runner asks for the shown map's 3-D build when it requests the load, and a take does not start (the runner stays in Loading, the preview is not entered) while it is not built;
    /// once built the take plays; a failed build fails the item with the message. A host with no map (<c>MapBuild</c> null) is unchanged.
    /// 🔴 Mutation: the runner not passing <c>mapBuilt</c> to the probe ⇒ the preview is entered while the map builds.
    /// </summary>
    [Fact]
    public void R1046_TheRunner_DoesNotStartATake_WhileTheMapIsBuilding()
    {
        var layer = new HeldMapLayer();
        var host = new FakeEditor(Store()) { MapBuild = new Fdp.Toolkit.Vis3D.Map3DBuild(new Fdp.Toolkit.Vis3D.IMeshLayer3D[] { layer }, Fdp.Toolkit.Vis3D.Map3DBuildPolicy.OnFirst3DRequest, available: () => true) };
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        for (int f = 0; f < 600 && runner.Busy; f++) { runner.Tick(); host.EndFrame(); }   // the load completes in a few frames; the map does not
        Assert.True(layer.Requests >= 1 || host.MapBuild.Requested, "the runner asked for the 3-D map");
        Assert.True(host.MapBuild.Requested);
        Assert.Equal(DirectorPhase.Loading, runner.Phase);                             // still waiting …
        Assert.Equal(0, host.Enters);                                                  // … no take started

        layer.Built = true;                                                            // the map is built
        Run(runner, host);
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.Equal(1, host.Enters);
        Assert.Equal("ok", runner.Reports[0].Status);

        var broken = new HeldMapLayer { Failed = "no GPU memory" };
        var host2 = new FakeEditor(Store()) { MapBuild = new Fdp.Toolkit.Vis3D.Map3DBuild(new Fdp.Toolkit.Vis3D.IMeshLayer3D[] { broken }, Fdp.Toolkit.Vis3D.Map3DBuildPolicy.OnFirst3DRequest, available: () => true) };
        var runner2 = new DirectorRunner(host2, new Cinema(), new Layers());
        Assert.Empty(runner2.Play(new PlayRequest(Reel.Of("s-a", "main"))));
        Run(runner2, host2);
        Assert.Equal(0, host2.Enters);
        Assert.Contains("no GPU memory", string.Join(" ", runner2.Reports.SelectMany(r => r.Problems)));
    }

    // ── CE-4042 — the Verify mode (docs/DESIGN_Scenario_Regression.md §2a) ──────────────────────────────────────────────────

    /// <summary>The verifier's world over the fake editor: the units' transforms from the repo, a Health the checks may edit.</summary>
    private sealed class FakeVerifyWorld : IVerifyWorld
    {
        private readonly FakeEditor _h;
        public readonly Dictionary<int, double> Health = new();
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

        /// <summary>A load rebuilds the world: the edited health goes with the old one (⛔ a fake that kept it would leak like CE-3294).</summary>
        public void Fresh()
        {
            if (_h.Loads.Count == _loads) return;
            _loads = _h.Loads.Count;
            Health.Clear();
        }

        public System.Text.Json.Nodes.JsonObject? Components(int id)
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
                return comps;
            }
            return null;
        }

        public IReadOnlyList<(int Id, string Name)> Named()
        {
            var list = new List<(int, string)>();
            foreach (var e in _h.Repo.Query().With<NetworkIdentity>().With<EntityInfo>().Build())
                list.Add((_h.Repo.GetComponentRO<NetworkIdentity>(e).Value, _h.Repo.GetComponentRO<EntityInfo>(e).Name.ToString()));
            return list;
        }

        public string? NameOfEntityIndex(int index) => null;

        public System.Text.Json.Nodes.JsonArray Shots() => new();
        public System.Text.Json.Nodes.JsonArray Blasts() => new();

        public string? SetComponent(int id, string component, System.Text.Json.Nodes.JsonObject patch)
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
        public IReadOnlyList<(int Id, string Name)> Named() =>
            Units.Select(kv => (int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture), kv.Value!["name"]!.GetValue<string>())).ToList();
        public string? NameOfEntityIndex(int index) => null;
        public System.Text.Json.Nodes.JsonObject? Components(int id)
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
        public string? SetComponent(int id, string component, System.Text.Json.Nodes.JsonObject patch) => "read only";
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
            return (new DirectorRunner(h, new Cinema(), new Layers(), extension: new ScenarioVerifyRun(h, u => h.Verifier = new FakeVerifyWorld(h, u))), h);
        }
        var replay = new VerifyOptions(Replay: true);

        // ① a faithful recording
        var (runner, host) = Fresh();
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: replay)));
        Run(runner, host);
        var r = runner.Reports.Single();
        Assert.Equal((1, 0, 1, 1), (host.LiveLoads, host.Enters, host.LiveStops, host.RecordingsOpened));   // ⭐ live, never a preview
        Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));
        Assert.Equal("same", r.Verify()!.Replay);
        Assert.Equal(4, r.Verify()!.ReplaySnapshots);                                    // t = 0, 1, 2, 3
        Assert.Empty(r.Verify()!.ReplayDiff);
        Assert.Equal("none", r.Verify()!.Golden);                                         // a live run is not compared with the preview's golden
        Assert.Equal("pass", r.Status);
        Assert.True(host.Opened!.Disposed);
        Assert.True(host.IsIdle);

        // ② a recorder that loses the health
        var (lossy, lossyHost) = Fresh();
        lossyHost.RecorderCarriesHealth = false;
        Assert.Empty(lossy.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: replay)));
        Run(lossy, lossyHost);
        var l = lossy.Reports.Single();
        Assert.Equal(("different", "fail"), (l.Verify()!.Replay, l.Status));
        Assert.StartsWith("FIRST differing snapshot at t=2 s", l.Verify()!.ReplayDiff[0]);   // the action is at 1.5 s
        Assert.Contains("Runner", l.Verify()!.ReplayDiff[0]);
        Assert.Contains(l.Verify()!.ReplayDiff, d => d.Contains("Health"));
        Assert.All(l.Verify()!.Verdicts, v => Assert.True(v.Ok));                          // ⭐ the live run itself was right

        // ③ what the manifest does not carry is listed apart
        var (scratch, scratchHost) = Fresh();
        Assert.Empty(scratch.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: replay)));
        scratch.Tick(); scratchHost.EndFrame();
        scratchHost.Verifier!.WithScratch = true;
        Run(scratch, scratchHost);
        var sc = scratch.Reports.Single();
        Assert.Equal(("same", "pass"), (sc.Verify()!.Replay, sc.Status));
        Assert.Contains(sc.Verify()!.ReplayNotCarried, d => d.Contains("Scratch"));

        // ⛔ a replay check records no golden
        Assert.Contains(Fresh().Runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(Capture: true, Replay: true))),
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
            return (new DirectorRunner(h, cinema, new Layers(), extension: new ScenarioVerifyRun(h, u => h.Verifier = new FakeVerifyWorld(h, u))), h);
        }
        System.Text.Json.Nodes.JsonObject RunOf(string path) => System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsObject();
        (string Straight, string Restored) Halves(double at, bool lose)
        {
            string cp = System.IO.Path.Combine(_root, $"r1-{at}.fdp"), a = cp + ".a.json", b = cp + ".b.json";
            var (straight, sh) = Fresh();
            Assert.Empty(straight.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(R1: new RestoreCheck(at, cp, false, a)))));
            Run(straight, sh);
            Assert.Equal(at, sh.CheckpointWrittenAt!.Value, 6);                  // on its moment, once
            var (restored, rh) = Fresh();
            rh.LoseHealthOnRestore = lose;
            Assert.Empty(restored.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(R1: new RestoreCheck(at, cp, true, b)))));
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
            return (new DirectorRunner(h, cinema, new Layers(), extension: new ScenarioVerifyRun(h, u => w = new FakeVerifyWorld(h, u))), h, () => w!);
        }

        // ① capture each in a fresh process: judged, the action on its exact step, the golden written
        foreach (var s in new[] { "s-a", "s-b" })
        {
            var (runner, host, world) = Fresh();
            Assert.Empty(runner.Play(new PlayRequest(Reel.Of(s, "_verify"), Plan: new VerifyOptions(Capture: true))));
            Run(runner, host);
            var r = runner.Reports.Single();
            Assert.Equal("pass", r.Status);
            Assert.Equal(180, r.Steps);                                           // 3 s at 60 Hz
            Assert.All(host.StepsPerFrame, n => Assert.Equal(1, n));              // ⭐ the stepping rule
            Assert.Equal(0, cinema.Enters);                                      // the editor left as it is
            Assert.Equal(1.5, world().Edits.Single().SimTime, 6);                 // ⭐ its exact step (step 90; a step is 0.0167 s)
            Assert.Equal("captured", r.Verify()!.Golden);
            Assert.False(r.Verify()!.Reload);
            Assert.Equal(1.0, r.Verify()!.Verdicts.Single(x => x.Name == "runs a metre").Time!.Value, 6);
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(_root, s, ScenarioGolden.FileName)));
        }

        // ② a reel of both in ONE process compares SAME — the second is a reload, and says so
        var (one, oneHost, _) = Fresh();
        Assert.Empty(one.Play(new PlayRequest(new Reel { Name = "verify", Items = new[] { "s-a", "s-b" }.Select(s => new ReelItem { Scenario = s, Take = "_verify" }).ToList() },
                                              Plan: new VerifyOptions())));
        Run(one, oneHost);
        Assert.All(one.Reports, r => Assert.Equal("same", r.Verify()!.Golden));
        Assert.Equal(new[] { false, true }, one.Reports.Select(r => r.Verify()!.Reload));

        // ③ ⛔ a capture on a reload is refused, with the way to do it right
        Assert.Empty(one.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(Capture: true))));
        Run(one, oneHost);
        var refusedCapture = one.Reports.Single().Verify()!;
        Assert.Equal("not-captured", refusedCapture.Golden);
        Assert.Contains("RELOAD", refusedCapture.GoldenDiff[0]);

        // ④ a regression: the runner stands still ⇒ the golden is DIFFERENT from the first snapshot it shows, and the check fails
        var (bad, badHost, _) = Fresh();
        badHost.Moves = false;
        Assert.Empty(bad.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions())));
        Run(bad, badHost);
        var b = bad.Reports.Single();
        Assert.Equal("fail", b.Status);
        Assert.Equal("different", b.Verify()!.Golden);
        Assert.StartsWith("FIRST differing snapshot at t=1 s", b.Verify()!.GoldenDiff[0]);
        Assert.Contains("Runner", b.Verify()!.GoldenDiff[0]);
        Assert.False(b.Verify()!.Verdicts.Single(x => x.Name == "runs a metre").Ok);

        var refused = new DirectorRunner(badHost, cinema, new Layers(), extension: new ScenarioVerifyRun(badHost, _ => null))
            .Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions()));
        Assert.Contains(refused, p => p.Contains("cannot verify"));
        // ⭐ CE-4061 — a runner with no extension refuses a plan (the film runner knows no verify)
        var noExtension = new DirectorRunner(badHost, cinema, new Layers()).Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions()));
        Assert.Contains(noExtension, p => p.Contains("no extension"));
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
            return (new DirectorRunner(h, new Cinema(), new Layers(), extension: new ScenarioVerifyRun(h, u => h.Verifier = new FakeVerifyWorld(h, u))), h);
        }
        System.Text.Json.Nodes.JsonObject RunOf(string path) => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        (ItemReport Report, FakeEditor Host) Half(bool straight, string runOut, bool losesHealth = false)
        {
            var (runner, host) = Fresh();
            host.ReturnLosesHealth = losesHealth;
            Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(Review: new ReviewCheck(2d, straight, runOut)))));
            Run(runner, host);
            return (runner.Reports.Single(), host);
        }

        string a = Path.Combine(_root, "h5-a.json"), b = Path.Combine(_root, "h5-b.json");
        var (straight, sh) = Half(true, a);
        Assert.Equal(0, sh.Reviews);
        Assert.Null(straight.Verify()!.Review);

        var (reviewed, rh) = Half(false, b);                                                               // ①
        var v = reviewed.Verify()!;
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
        Assert.Contains(lossy.Verify()!.ReturnDiff, d => d.Contains("Health"));
        Assert.NotEmpty(ScenarioGolden.CompareFrom(RunOf(a), RunOf(b), 2d));

        Assert.Contains(Fresh().Runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(Replay: true, Review: new ReviewCheck(2d, false, b)))),
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
            return (new DirectorRunner(h, new Cinema(), new Layers(), extension: new ScenarioVerifyRun(h, u => h.Verifier = new FakeVerifyWorld(h, u))), h);
        }
        var replay = new VerifyOptions(Replay: true);

        // ① a replay that shows the live run
        var (runner, host) = Fresh();
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: replay)));
        Run(runner, host);
        var r = runner.Reports.Single();
        Assert.Equal("same", r.Verify()!.Replay);                                                    // H1a, unchanged
        var pb = r.Verify()!.Playback!;
        Assert.True(pb.Verdict == "same", string.Join("\n", pb.Diff.Concat(pb.Problems)));
        Assert.Equal(4, pb.Snapshots);                                                              // t = 0, 1, 2, 3
        Assert.Empty(pb.Problems);
        Assert.Equal(new[] { 0d }, host.ReplayEntries);                                             // ONE entry, at the first snapshot
        Assert.Equal(new[] { 1d, 2d, 3d }, host.SeekAsks.Take(3));                                  // ⭐ by recorded sim time, one seek per later snapshot
        // ⭐ H2 — then the SAME replay, sought again to every moment in a shuffled, seeded order (backward seeks included)
        var h2 = host.SeekAsks.Skip(3).ToList();
        Assert.Equal(new[] { 0d, 1d, 2d, 3d }, h2.OrderBy(x => x));
        Assert.NotEqual(new[] { 0d, 1d, 2d, 3d }, h2);
        Assert.Equal("same", r.Verify()!.Seek!.Verdict);
        Assert.Equal(4, r.Verify()!.Seek!.Snapshots);
        Assert.Equal((1, 1, 0, 0), (host.Replays, host.ReplayStops, host.Reviews, host.Enters));    // a replay, not a review; live, never a preview
        Assert.Equal(1, host.LiveStops);                                                            // the live run stopped once (for H1a); the replay left once
        Assert.True(host.IsIdle);
        Assert.False(host.IsOperatingReplay);
        Assert.Equal("pass", r.Status);
        Assert.Equal("same", r.Verify()!.ToJson()["playback"]!["verdict"]!.ToString());
        Assert.Null(r.Verify()!.ToJson()["review"]);                                                   // not asked

        // ② a replay that does not show the health
        var (bad, badHost) = Fresh();
        badHost.ReplayLosesHealth = true;
        Assert.Empty(bad.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: replay)));
        Run(bad, badHost);
        var b = bad.Reports.Single();
        Assert.Equal("same", b.Verify()!.Replay);                                                    // ⭐ the recording itself is right
        Assert.Equal("different", b.Verify()!.Playback!.Verdict);
        Assert.StartsWith("FIRST differing snapshot at t=2 s", b.Verify()!.Playback.Diff[0]);          // the action is at 1.5 s
        Assert.Contains("Runner", b.Verify()!.Playback.Diff[0]);
        Assert.Contains(b.Verify()!.Playback.Diff, d => d.Contains("Health"));
        Assert.Equal("fail", b.Status);
        Assert.True(badHost.IsIdle);                                                                // a failed check leaves nothing running
    }

    /// <summary>
    /// ⭐ CE-4045 H2 (docs/DESIGN_Scenario_Regression.md §10, RH-6) — the replay sought again in a shuffled order catches a seek that goes
    /// wrong only when it goes BACK — CE-4056's shape, found live and only live: the in-order playback (H1b) never seeks back, so it stays
    /// "same" while H2 is "different", and the item fails.
    /// </summary>
    [Fact]
    public void CE4045_H2_ShuffledSeeks_CatchASeekBackThatStandsOneSeekBehind()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        var rec = Path.Combine(_root, "rec");
        Directory.CreateDirectory(rec);
        var host = new FakeEditor(store) { Lag = 1, RecordDir = rec, SeekBackStandsBehind = true };
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), extension: new ScenarioVerifyRun(host, u => host.Verifier = new FakeVerifyWorld(host, u)));
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(Replay: true))));
        Run(runner, host);
        var r = runner.Reports.Single();
        Assert.Equal("same", r.Verify()!.Replay);                     // H1a: the recording is right
        Assert.Equal("same", r.Verify()!.Playback!.Verdict);          // H1b: in order, every seek forward — the defect never shows
        Assert.Equal("different", r.Verify()!.Seek!.Verdict);         // ⭐ H2: a seek back shows the last seek's world
        Assert.Contains(r.Verify()!.Seek!.Diff, d => d.StartsWith("FIRST differing snapshot"));
        Assert.Equal("fail", r.Status);
        Assert.True(host.IsIdle);                                     // the replay is left, nothing runs on
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4045</c> H3 (docs/DESIGN_Scenario_Regression.md §10-7, RH-7) — after H1b and H2 the SAME replay is sought to T, BRANCHED live
    /// there (the D18 go-live, once) and run on to runFor, one step per frame, on the branch's OWN clock (📐 CE-4065: it comes up 7.71 s off),
    /// and the run from T is compared with the straight live run from T. ① a branch that carries the world: SAME at 2 and 3 s; the action
    /// at 1.5 s (applied before T) is NOT applied again (one edit in all); the clock it came up on is reported; Idle at the end; the item
    /// passes. ② a branch that drops the health: DIFFERENT from t=2 s naming the Health — and the item fails, while H1a, H1b and H2 stay same.
    /// 🔴 Red-proof: ① resume without the new clock (<c>_ctx.ResumePlaying()</c>) ⇒ the branch's run time starts 7.71 s late, past runFor:
    /// one snapshot, DIFFERENT; ② drop <c>MarkDone</c> ⇒ the action lands again in the branch (two edits).
    /// </summary>
    [Fact]
    public void CE4045_H3_TheReplay_BranchedLiveAtT_RunsOnAsTheStraightRun()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_root, "s-a", ChecksJson.FileName), RunnerChecks);
        var rec = Path.Combine(_root, "rec");
        Directory.CreateDirectory(rec);
        (DirectorRunner Runner, FakeEditor Host, Func<FakeVerifyWorld> World) Fresh()
        {
            var h = new FakeEditor(store) { Lag = 1, RecordDir = rec, BranchClockOffset = 7.71 };
            FakeVerifyWorld? w = null;
            return (new DirectorRunner(h, new Cinema(), new Layers(), extension: new ScenarioVerifyRun(h, u => h.Verifier = w = new FakeVerifyWorld(h, u))), h, () => w!);
        }
        var branch = new VerifyOptions(Replay: true, BranchAt: 2d);

        // ① a branch that carries the world
        var (runner, host, world) = Fresh();
        Assert.Empty(runner.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: branch)));
        Run(runner, host);
        var r = runner.Reports.Single();
        var v = r.Verify()!;
        Assert.Equal(("same", "same", "same"), (v.Replay, v.Playback!.Verdict, v.Seek!.Verdict));   // H1a, H1b, H2 unchanged
        var b = v.Branch!;
        Assert.True(b.Verdict == "same", string.Join("\n", b.Diff.Concat(b.Problems)));
        Assert.Equal(2, b.Snapshots);                                                   // t = 2, 3 — the branch's own run from T
        Assert.Equal(1, host.Branches);                                                 // ONE go-live, from the replay
        Assert.Equal(2d, host.SeekAsks.Last());                                         // the replay was sought to T before it
        Assert.Single(world().Edits);                                                   // ⭐ the action at 1.5 s ran in the straight run only
        Assert.Equal(2d, v.BranchAt);
        Assert.Equal(9.71, v.BranchClock!.Value, 3);                                    // 📐 the clock it came up on (CE-4065's offset)
        Assert.Equal(1, host.BranchStops);
        Assert.True(host.IsIdle);
        Assert.Equal("pass", r.Status);
        Assert.Equal("same", v.ToJson()["branch"]!["verdict"]!.ToString());

        // ② a branch that drops the health
        var (bad, badHost, _) = Fresh();
        badHost.BranchLosesHealth = true;
        Assert.Empty(bad.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: branch)));
        Run(bad, badHost);
        var bv = bad.Reports.Single().Verify()!;
        Assert.Equal(("same", "same", "same"), (bv.Replay, bv.Playback!.Verdict, bv.Seek!.Verdict));
        Assert.Equal("different", bv.Branch!.Verdict);
        Assert.StartsWith("FIRST differing snapshot at t=2 s", bv.Branch.Diff[0]);
        Assert.Contains(bv.Branch.Diff, d => d.Contains("Health"));
        Assert.Equal("fail", bad.Reports.Single().Status);
        Assert.True(badHost.IsIdle);

        // a branch outside the replay check, or at 0, is refused
        var (refused, _, _) = Fresh();
        Assert.Contains(refused.Play(new PlayRequest(Reel.Of("s-a", "_verify"), Plan: new VerifyOptions(BranchAt: 2d))), p => p.Contains("H3"));
    }

    /// <summary>
    /// ⭐ CE-4061 (docs/DESIGN_Director_Timeline_Editor.md §11h, CR-1) — the film runner can be SHARED (the Replay Browser, P1): neither it nor
    /// what it is handed (the film host, the request, the report) names a verify type in a field, a property or a signature. The verifier is an
    /// editor extension behind the one seam (its rails above run through it).
    /// </summary>
    [Fact]
    public void CE4061_TheFilmRunner_NamesNoVerifyType_TheVerifierIsAnExtension()
    {
        var verify = new[]
        {
            typeof(ScenarioVerifyRun), typeof(VerifyOptions), typeof(VerifyResult), typeof(ScenarioVerification), typeof(ScenarioGolden),
            typeof(IVerifyWorld), typeof(IRecordedRun), typeof(ScenarioChecks), typeof(LookBackResult), typeof(ReviewCheck),
            typeof(RestoreCheck), typeof(IVerifyHost), typeof(ConditionBook),
        };
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                                  | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                                                  | System.Reflection.BindingFlags.DeclaredOnly;
        IEnumerable<Type> Named(Type t) =>
            t.GetFields(all).Select(f => f.FieldType)
             .Concat(t.GetProperties(all).Select(p => p.PropertyType))
             .Concat(t.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
             .Concat(t.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
             .Concat(t.GetInterfaces())
             .SelectMany(x => x.IsGenericType ? x.GetGenericArguments().Append(x) : new[] { x });
        foreach (var t in new[] { typeof(DirectorRunner), typeof(IDirectorHost), typeof(PlayRequest), typeof(ItemReport), typeof(IRunExtension), typeof(IRunContext) })
        {
            var leaked = Named(t).Where(x => verify.Contains(x)).Distinct().ToList();
            Assert.True(leaked.Count == 0, $"{t.Name} names {string.Join(", ", leaked.Select(x => x.Name))}");
        }
        Assert.Contains(typeof(IRunExtension), typeof(ScenarioVerifyRun).GetInterfaces());   // the verifier plugs in through the seam
    }

    // ── ⭐ CE-1306 R4 (docs/DESIGN_Director_Timeline_Editor.md §11u) — the reel composer played, the render queue on the fake editor ──────────────────

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (§11u D1; success condition 1 — "a composed reel plays its items in order on a fake host"): a reel composed from three takes, the last
    /// moved to the front and the middle one removed, is played UNSAVED by the runner: the fake editor loads s-c then s-a, each item reported ok, and the card
    /// over each load is that item's title. 🔴 Mutation: <c>ReelComposer.Move</c> inserting at <c>from</c> instead of <c>to</c> ⇒ the order stays s-a, s-b
    /// … ⇒ the loads are s-a, s-c ⇒ red.
    /// </summary>
    [Fact]
    public void CE1306_AComposedReel_PlaysItsItemsInOrder_Unsaved()
    {
        var store = Store();
        var composer = new ReelComposer(() => store);
        Assert.Empty(composer.New("composed"));
        composer.Add("s-a", "main", title: "First A");
        composer.Add("s-b", "main");
        composer.Add("s-c", "main", title: "Then C");
        Assert.True(composer.Move(2, 0));
        Assert.True(composer.Remove(2));
        Assert.True(composer.Dirty);

        var host = new FakeEditor(store);
        var cinema = new Cinema();
        var runner = new DirectorRunner(host, cinema, new Layers());
        Assert.Empty(runner.Play(new PlayRequest(composer.Reel!)));
        Run(runner, host);

        Assert.Equal(DirectorPhase.Done, runner.Phase);
        Assert.Equal(new[] { "s-c", "s-a" }, host.Loads);
        Assert.Equal(new[] { "s-c", "s-a" }, runner.Reports.Select(r => r.Scenario));
        Assert.All(runner.Reports, r => Assert.Equal("ok", r.Status));
        Assert.Equal(new[] { "Then C", "First A" }, cinema.Cards.Take(2));
        Assert.False(File.Exists(store.ReelPath("composed")));   // never saved: played as it stands
    }

    private (DirectorRunner Runner, DirectorStills Stills, DirectorRenders Renders, FakeEditor Host, Sink Sink) RenderQueue(TakeStore store)
    {
        var host = new FakeEditor(store);
        var sink = new Sink();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        var stills = new DirectorStills(runner, sink, () => false);
        var renders = new DirectorRenders(runner, stills, sink, () => false, () => store, () => null);
        return (runner, stills, renders, host, sink);
    }

    /// <summary>The pack's frame (runner, stills, queue — <see cref="DirectorPack.Tick"/>'s order) then the editor's, until the queue is idle; the job holding
    /// the director at each frame. ⭐ Asserts every frame that at most ONE job is started and that the runner plays THAT job's capture.</summary>
    private static List<string?> RunQueue((DirectorRunner Runner, DirectorStills Stills, DirectorRenders Renders, FakeEditor Host, Sink Sink) q,
                                          Func<int, bool>? atFrame = null, int maxFrames = 40_000)
    {
        var holding = new List<string?>();
        for (int f = 0; f < maxFrames && (q.Renders.Busy || q.Renders.Queued > 0 || q.Runner.Busy); f++)
        {
            q.Runner.Tick();
            q.Stills.Tick();
            q.Renders.Tick();
            q.Host.EndFrame();
            var started = q.Renders.Jobs.Where(j => j.State is RenderState.Running or RenderState.Opening).ToList();
            Assert.True(started.Count <= 1, $"frame {f}: {started.Count} renders started at once");
            if (started.Count == 1 && q.Runner.Busy) Assert.Equal(started[0].Dir, q.Runner.Request!.Capture!.Dir);   // the runner plays THAT job
            holding.Add(q.Renders.Running?.Id);
            if (atFrame?.Invoke(f) == false) break;
        }
        Assert.False(q.Renders.Busy || q.Renders.Queued > 0, $"the queue is not idle after {maxFrames} frames");
        return holding;
    }

    /// <summary>
    /// ⭐ <c>CE-1305</c> TE-10 (docs/DESIGN_Director_Timeline_Editor.md §11v S2, success condition 3): a STILLS render of a recording waits, after the open, until the
    /// recording's timeline index is read — its keys are planned through that index (the one resolver), so planning on a half-scanned index would miss anchors —
    /// and goes on the frame it is complete. 🔴 Mutation: the wait in <c>DirectorRenders.Watch</c> removed ⇒ the job leaves Opening after the two settle frames ⇒ red.
    /// </summary>
    [Fact]
    public void CE1305_TE10_AStillsRenderOfARecording_WaitsForItsIndex_BeforeItPlans()
    {
        var store = Store();
        var host = new FakeEditor(store);
        var sink = new Sink();
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
        var stills = new DirectorStills(runner, sink, () => true);
        var renders = new DirectorRenders(runner, stills, sink, () => true, () => store, () => "s-a") { Recordings = new OpensAnyRecording() };
        var reading = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex(new Dictionary<string, Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent[]>(), new long[10], 10, 600);
        var read = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex(new Dictionary<string, Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent[]>(), new long[600], 600, 600);
        var index = reading;
        runner.Index = () => index;
        var (job, why) = renders.Enqueue(new RenderRequest { Kind = RenderKind.Stills, Recording = "night/verify-1/node_1.fdp", Scenario = "s-a", Take = "main", Width = 320, Height = 180 });
        Assert.True(job != null, why);

        for (int f = 0; f < DirectorRenders.OpenSettleFrames + 30; f++) renders.Tick();
        Assert.Equal(RenderState.Opening, job!.State);                    // the index is still being read: not planned yet

        index = read;
        renders.Tick();
        Assert.NotEqual(RenderState.Opening, job.State);                  // read: it plans (and goes on)
    }

    /// <summary>⭐ CE-4130 — a still waits for the shown 3-D scene: with <c>SceneReady</c> false the capture is held for <see cref="DirectorStills.SceneFrames"/> frames after the settle, then taken anyway
    /// (never a stuck job); ready ⇒ it is taken at once. ✅ Red-proof: the <c>SceneReady</c> test removed from <c>DirectorStills.Tick</c> ⇒ both runs capture at the same frame ⇒ red.</summary>
    [Fact]
    public void CE4130_AStill_WaitsForTheSceneToBeBuilt_BeforeItCaptures()
    {
        int FirstCapture(bool ready)
        {
            var store = Store();
            var host = new FakeEditor(store);
            var sink = new Sink();
            var runner = new DirectorRunner(host, new Cinema(), new Layers(), sink);
            var stills = new DirectorStills(runner, sink, () => true) { SceneReady = () => ready };
            var renders = new DirectorRenders(runner, stills, sink, () => true, () => store, () => "s-a");
            var (job, why) = renders.Enqueue(new RenderRequest { Kind = RenderKind.Stills, Scenario = "s-a", Take = "main", Times = new[] { 1d }, Width = 320, Height = 180 });
            Assert.True(job != null, why);
            for (int f = 0; f < 2000; f++)
            {
                runner.Tick(); stills.Tick(); renders.Tick(); host.EndFrame();
                if (sink.Frames.Count > 0) return f;
            }
            Assert.Fail("no still was captured");
            return -1;
        }
        int now = FirstCapture(ready: true), waited = FirstCapture(ready: false);
        Assert.True(waited - now >= DirectorStills.SceneFrames - 2, $"ready: frame {now}, not ready: frame {waited}");
        Assert.True(waited - now <= DirectorStills.SceneFrames + 5, $"a not-ready scene only delays the capture ({now} -> {waited})");
    }

    /// <summary>CE-1391's one opener seam, faked: every open succeeds (this rail is about the index wait, not the open).</summary>
    private sealed class OpensAnyRecording : Hrot.Presentation.DebugApi.IRecordingOpener
    {
        public string? Open(Hrot.Presentation.DebugApi.RecordingName recording, double atSeconds) { Shown = recording; return null; }
        public Hrot.Presentation.DebugApi.RecordingName? Shown { get; private set; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1306</c> R4 (§11u D3, D5; success condition 2) — TWO renders queued at once (s-a/main, s-b/main, frames only) run ONE AFTER THE OTHER through the
    /// one runner: the first holds the director until it is done, then the second; each writes into a folder of its own beside its take
    /// (<c>takes/main.render/r1</c>, <c>…/r2</c>) and yields that output; every captured frame of r1 comes before every frame of r2; the editor loads s-a then
    /// s-b. 🔴 Mutation: <c>DirectorRenders.Begin</c> not holding the director (<c>_running = job</c> removed) ⇒ r1 is never watched and r2 starts once the runner
    /// is idle ⇒ "2 renders started at once" ⇒ red.
    /// </summary>
    [Fact]
    public void CE1306_TwoRenders_RunOneAfterTheOther_EachIntoItsOwnFolder()
    {
        var store = Store();
        var q = RenderQueue(store);
        var (r1, why1) = q.Renders.Enqueue(new RenderRequest { Scenario = "s-a", Take = "main", Fps = 30, Width = 320, Height = 180, Encode = false });
        var (r2, why2) = q.Renders.Enqueue(new RenderRequest { Scenario = "s-b", Take = "main", Fps = 30, Width = 320, Height = 180, Encode = false });
        Assert.True(r1 != null && r2 != null, why1 ?? why2);
        Assert.Equal(("r1", "r2", 2), (r1!.Id, r2!.Id, q.Renders.Queued));

        var holding = RunQueue(q);

        Assert.Equal((RenderState.Done, RenderState.Done), (r1.State, r2.State));
        string dir1 = Path.Combine(_root, "s-a", "takes", "main.render", "r1"), dir2 = Path.Combine(_root, "s-b", "takes", "main.render", "r2");
        Assert.Equal((dir1, dir2), (r1.Dir, r2.Dir));
        Assert.Equal((Path.Combine(dir1, "frames"), Path.Combine(dir2, "frames")), (r1.Output, r2.Output));
        Assert.Equal(new[] { "s-a", "s-b" }, q.Host.Loads);
        int lastOf1 = q.Sink.Frames.FindLastIndex(p => p.StartsWith(dir1, StringComparison.Ordinal));
        int firstOf2 = q.Sink.Frames.FindIndex(p => p.StartsWith(dir2, StringComparison.Ordinal));
        Assert.True(lastOf1 >= 10 && firstOf2 > lastOf1, $"r1's frames end at {lastOf1}, r2's begin at {firstOf2}");
        Assert.Equal(q.Sink.Frames.Count, q.Sink.Frames.Count(p => p.StartsWith(dir1, StringComparison.Ordinal) || p.StartsWith(dir2, StringComparison.Ordinal)));
        // the director was held by r1, then by r2 — never back
        var order = holding.Where(h => h != null).Distinct().ToList();
        Assert.Equal(new[] { "r1", "r2" }, order);
        Assert.True(r1.Frames > 0 && r2.Frames > 0, $"{r1.Frames} / {r2.Frames} frames reported");
        Assert.All(new[] { r1, r2 }, j => Assert.True(j.Finished >= j.Started, j.Id));
    }

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (§11u D6; success condition 2) — three renders queued; the THIRD is cancelled while queued (it never runs: s-c is never loaded, nothing
    /// is written for it), the FIRST is cancelled while running (the runner is STOPPED at once, the job reads cancelled, its folder holds fewer frames than a whole
    /// take), and the queue goes on to the second, which completes. A second cancel of an ended job is refused; an unknown id is not found.
    /// 🔴 Mutation: <c>DirectorRenders.Cancel</c> not stopping the runner for the running job ⇒ the runner is still busy after the cancel ⇒ red.
    /// </summary>
    [Fact]
    public void CE1306_Cancel_TakesAQueuedRenderOff_AndStopsTheRunningOne()
    {
        var store = Store();
        var q = RenderQueue(store);
        RenderJob Queue(string scenario) => q.Renders.Enqueue(new RenderRequest { Scenario = scenario, Width = 320, Height = 180, Encode = false }).Job!;
        var r1 = Queue("s-a");
        var r2 = Queue("s-b");
        var r3 = Queue("s-c");
        Assert.Equal((true, (string?)null), q.Renders.Cancel(r3.Id));
        Assert.Equal(RenderState.Cancelled, r3.State);
        Assert.Equal((false, (string?)null), q.Renders.Cancel("r99"));

        bool cancelled = false;
        RunQueue(q, f =>
        {
            if (!cancelled && r1.State == RenderState.Running && r1.Frames >= 5)
            {
                Assert.Equal((true, (string?)null), q.Renders.Cancel(r1.Id));
                Assert.False(q.Runner.Busy);                                   // ⭐ stopped at once
                Assert.Equal(DirectorPhase.Stopped, q.Runner.Phase);
                cancelled = true;
            }
            return true;
        });

        Assert.True(cancelled, "r1 never ran long enough to be cancelled while running");
        Assert.Equal((RenderState.Cancelled, RenderState.Done, RenderState.Cancelled), (r1.State, r2.State, r3.State));
        Assert.Equal(new[] { "s-a", "s-b" }, q.Host.Loads);                     // r3 never ran
        Assert.Null(r3.Dir);
        Assert.DoesNotContain(q.Sink.Frames, p => p.Contains(Path.Combine("s-c", "takes"), StringComparison.Ordinal));
        int framesOf1 = q.Sink.Frames.Count(p => p.StartsWith(r1.Dir!, StringComparison.Ordinal));
        int framesOf2 = q.Sink.Frames.Count(p => p.StartsWith(r2.Dir!, StringComparison.Ordinal));
        Assert.True(framesOf1 < framesOf2, $"the cancelled r1 wrote {framesOf1} frames, the whole r2 {framesOf2}");
        Assert.NotNull(q.Renders.Cancel(r1.Id).Refused);                       // it has ended
    }

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (§11u D3; success condition 2) — a render that FAILS (a take that does not exist) is reported — failed, with the store's own reason
    /// — and the queue goes on: the next render completes. A stills job on a host that cannot seek (the editor) is refused when it is queued, and a reel with no
    /// items too. 🔴 Mutation: <c>DirectorRenders.End</c> not releasing the director (<c>_running</c> kept) ⇒ the queue never starts r2 ⇒ red.
    /// </summary>
    [Fact]
    public void CE1306_AFailingRender_IsReported_AndTheQueueGoesOn()
    {
        var store = Store();
        var q = RenderQueue(store);
        var bad = q.Renders.Enqueue(new RenderRequest { Scenario = "s-a", Take = "no-such-take", Encode = false }).Job!;
        var good = q.Renders.Enqueue(new RenderRequest { Scenario = "s-b", Width = 320, Height = 180, Encode = false }).Job!;
        Assert.Equal(DirectorStills.CannotSeek, q.Renders.Enqueue(new RenderRequest { Kind = RenderKind.Stills, Scenario = "s-a", Times = new[] { 1d } }).Refused);
        Assert.NotNull(q.Renders.Enqueue(new RenderRequest { Reel = new Reel { Name = "empty" } }).Refused);

        RunQueue(q);

        Assert.Equal(RenderState.Failed, bad.State);
        Assert.Contains(bad.Problems, p => p.Contains("no take 'no-such-take'", StringComparison.Ordinal));
        Assert.Equal(RenderState.Done, good.State);
        Assert.Equal(new[] { "s-b" }, q.Host.Loads);
        Assert.Equal("failed", bad.ToJson()["state"]!.GetValue<string>());
        Assert.Equal(2, q.Renders.Jobs.Count);
    }

    /// <summary>
    /// ⭐ <c>CE-1391</c> (TE-16; docs/DESIGN_Director_Timeline_Editor.md §11u "as built: CE-1391"; success condition 3) — on the EDITOR's kind of host (it opens no
    /// recordings: the pack hands its runner no opener) a reel whose item names a recording is REFUSED at Play — the item, its take and the reason
    /// (<see cref="DirectorRunner.NoRecordings"/>) — and nothing loads; queued as a render it fails with the same reason, and the next render (a reel naming
    /// none) still plays. 🔴 Mutation: <c>DirectorRunner.Play</c> not refusing such an item when <c>Recordings</c> is null (the check removed) ⇒ Play starts ⇒ red.
    /// </summary>
    [Fact]
    public void CE1391_OnTheEditor_AReelItemNamingARecording_IsRefused_WithTheReason()
    {
        var store = Store();
        var host = new FakeEditor(store);
        var runner = new DirectorRunner(host, new Cinema(), new Layers());
        Assert.Null(runner.Recordings);                                                    // the editor opens no recordings
        var reel = new Reel { Name = "two", Items = new[] { new ReelItem { Scenario = "s-a" }, new ReelItem { Scenario = "s-b", Recording = "/runs/b/node_1.fdp" } } };
        var refused = runner.Play(new PlayRequest(reel));
        var why = Assert.Single(refused);
        Assert.Equal("item 2 (s-b/main) " + DirectorRunner.NoRecordings, why);
        Assert.Equal(DirectorPhase.Idle, runner.Phase);
        Run(runner, host);
        Assert.Empty(host.Loads);                                                           // nothing loaded

        var q = RenderQueue(store);
        var bad = q.Renders.Enqueue(new RenderRequest { Reel = reel, Width = 320, Height = 180, Encode = false, Dir = Path.Combine(_root, "r-bad") }).Job!;
        var good = q.Renders.Enqueue(new RenderRequest { Reel = Reel.Of("s-a", "main"), Width = 320, Height = 180, Encode = false, Dir = Path.Combine(_root, "r-good") }).Job!;
        RunQueue(q);
        Assert.Equal(RenderState.Failed, bad.State);
        Assert.Contains(bad.Problems, p => p.Contains(DirectorRunner.NoRecordings, StringComparison.Ordinal));
        Assert.Equal(RenderState.Done, good.State);
        Assert.Equal(new[] { "s-a" }, q.Host.Loads);
    }

    // ── ⭐ CE-11021 (DESIGN_Linked_Client_Director §3 row E, Q113-E) — a job's sound from the client ──────────────────────────────────────

    private sealed class HrotSound : IDirectorSound
    {
        public void SetTakeSound(bool? on) { }
        public void BeginLog() { }
        public bool EndLog(string wavPath, IReadOnlyList<double> frameSimTimes, int fps) => true;   // HROT's mixer made a track
        public void Reset() { }
    }

    /// <summary>Plays the two titled takes through a fake linked client with a recording encoder (no ffmpeg); returns the runner and what the encoder was given.</summary>
    private (DirectorRunner Runner, List<(string? Wav, int Fps)> Encodes, FakeClient Client) RunWithEncoder(Hrot.Common.Infrastructure.FilmSound sound, long offset = 0, bool noTrack = false)
    {
        var store = Store();
        var host = new FakeEditor(store);
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var view = new Fdp.Toolkit.Vis3D.MapViewSwitch(canvas, canvas.Camera);
        using var film = new FilmCaptureLayer(canvas, view, () => host.Repo);
        view.Set(true, animate: false);
        var clients = new Hrot.Common.Infrastructure.LinkedClients();
        var client = new FakeClient { SampleOffset = offset, NoTrack = noTrack };
        clients.Offer(client);
        var runner = new DirectorRunner(host, new Cinema(), new Layers(), new RoutingFrameSink(new Sink(), () => clients), new HrotSound());
        var encodes = new List<(string? Wav, int Fps)>();
        runner.Encoder = (frames, fps, chapters, wav, movie) => { lock (encodes) encodes.Add((wav, fps)); return null; };
        var request = new PlayRequest(new Reel { Name = "cs", Items = new[] { "s-a", "s-b" }.Select(sc => new ReelItem { Scenario = sc, Take = "main", Title = sc }).ToList() },
                                      Capture: new CaptureOptions(Path.Combine(_root, "cs-" + sound + offset + noTrack), Fps: 30, Encode: true, Sound: sound));
        Assert.Empty(runner.Play(request));
        for (int f = 0; f < 40000 && runner.Busy; f++)
        {
            runner.Tick();
            film.Draw3D(default);
            film.Update(1f / 60f);
            host.EndFrame();
            if (runner.Phase == DirectorPhase.Encoding) System.Threading.Thread.Sleep(2);
        }
        Assert.Equal(DirectorPhase.Done, runner.Phase);
        return (runner, encodes, client);
    }

    /// <summary>⭐ CE-11021 — <c>sound: client</c>: the encode muxes the CLIENT's WAV (not HROT's, though HROT's mixer made one), once; the job carried <c>Sound = Client</c>. 🔴 Mutation: HROT's sound muxed despite Client.</summary>
    [Fact]
    public void CE11021_ClientSound_MuxesTheClientsTrack()
    {
        var (runner, encodes, client) = RunWithEncoder(Hrot.Common.Infrastructure.FilmSound.Client);
        Assert.Null(runner.Error);
        Assert.Equal(Hrot.Common.Infrastructure.FilmSound.Client, Assert.Single(client.Begins).Sound);
        Assert.Equal(("r1.wav", 30), Assert.Single(encodes));
    }

    /// <summary>⭐ CE-11021 — the track's total may miss <c>round(frames x rate / fps)</c> by <see cref="DirectorRunner.TrackSampleTolerance"/> (1024) either way and no more; a larger miss fails the job with both numbers and nothing is encoded. 🔴 Mutation: the check off.</summary>
    [Theory]
    [InlineData(1024L, true)]
    [InlineData(-1024L, true)]
    [InlineData(1025L, false)]
    [InlineData(-1025L, false)]
    public void CE11021_ATrackTotal_WithinTheTolerance_Passes_APastItFailsWithBothNumbers(long offset, bool ok)
    {
        Assert.Equal(1024L, DirectorRunner.TrackSampleTolerance);
        var (runner, encodes, client) = RunWithEncoder(Hrot.Common.Infrastructure.FilmSound.Client, offset);
        if (ok)
        {
            Assert.Null(runner.Error);
            Assert.Single(encodes);
            return;
        }
        int frames; lock (client.Frames) frames = client.Frames.Count;
        long expected = (long)Math.Round(frames * 48000.0 / 30, MidpointRounding.AwayFromZero);
        Assert.Contains((expected + offset).ToString(), runner.Error);   // the client's total …
        Assert.Contains(expected.ToString(), runner.Error);               // … and what the film needs
        Assert.Empty(encodes);
    }

    /// <summary>⭐ CE-11021 — <c>sound: client</c> with no track back (no client filmed the job) fails the job with a reason; nothing is encoded.</summary>
    [Fact]
    public void CE11021_ClientSound_WithNoTrack_FailsTheJob()
    {
        var (runner, encodes, _) = RunWithEncoder(Hrot.Common.Infrastructure.FilmSound.Client, noTrack: true);
        Assert.Contains("no track", runner.Error);
        Assert.Empty(encodes);
    }

    /// <summary>⭐ CE-11021 — <c>sound: hrot</c> (the default) is unchanged: HROT's own track is muxed, the client's total is never checked (even a 100 000-sample miss passes) and its path is not used.</summary>
    [Fact]
    public void CE11021_HrotSound_IsUnchanged_TheClientsTrackIsIgnored()
    {
        var (runner, encodes, _) = RunWithEncoder(Hrot.Common.Infrastructure.FilmSound.Hrot, offset: 100_000);
        Assert.Null(runner.Error);
        var (wav, _) = Assert.Single(encodes);
        Assert.EndsWith("sound.wav", wav);   // HROT's mixer's file, not the client's r1.wav
    }

}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DirectorCollection
{
    /// <summary>FilmStudio is process-wide.</summary>
    public const string Name = "Film director (FilmStudio is process-wide)";
}
