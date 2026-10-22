using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>How the director runs a reel: on screen in real time, frame by frame to files, as a scout that bakes anchor times, or as
    /// a VERIFY run that judges each scenario's checks and compares it with its golden (CE-4042, docs/DESIGN_Scenario_Regression.md).</summary>
    public enum DirectorMode { Live, Capture, Scout, Verify }

    /// <summary>⭐ <c>CE-4042</c> — a verify run: <see cref="Capture"/> re-records each scenario's golden (refused for a failing run).</summary>
    public sealed record VerifyOptions(bool Capture = false);

    /// <summary>Where and how a capture writes: frames under <see cref="Dir"/>, one movie at <see cref="Movie"/> (§4a: a reel is ONE movie).</summary>
    public sealed record CaptureOptions(string Dir, int Fps = 30, int Width = 1280, int Height = 720, int Supersample = 1,
                                        bool BurnCaptions = true, string? Movie = null, bool Encode = true);

    /// <summary>A reel to play, at <see cref="Speed"/> (live only), live / to files (<see cref="Capture"/>) / as a scout.</summary>
    /// <remarks><see cref="Inline"/>: takes given with the request (by <c>scenario/take</c>), not read from a file — film.py's one-off
    /// plans (D-9). A scout of an inline take bakes nothing.</remarks>
    public sealed record PlayRequest(Reel Reel, double Speed = 1d, bool Loop = false, CaptureOptions? Capture = null, bool Scout = false,
                                     IReadOnlyDictionary<string, Take>? Inline = null, bool Bake = true, VerifyOptions? Verify = null)
    {
        public DirectorMode Mode => Verify != null ? DirectorMode.Verify : Scout ? DirectorMode.Scout : Capture != null ? DirectorMode.Capture : DirectorMode.Live;
    }

    public enum DirectorPhase { Idle, Loading, Entering, Playing, Encoding, Done, Stopped }

    /// <summary>⭐ What one reel item did — the per-take report of §3a (missing beats, anchor mismatches, the realtime factor) and the
    /// stepping rule's own evidence (the most steps taken in one frame, steps the master had not retired by the next frame).</summary>
    public sealed class ItemReport
    {
        public string Scenario { get; init; } = "";
        public string Take { get; init; } = "";
        public string? Title { get; init; }
        /// <summary>pending · loading · playing · ok · beat-missing · failed · stopped — a verify run: pass · fail</summary>
        public string Status { get; set; } = "pending";
        public List<string> Problems { get; } = new();
        public List<string> Missing { get; } = new();
        public List<string> Mismatches { get; } = new();
        public List<int> SkippedReplays { get; } = new();
        public List<string> Unresolved { get; } = new();
        public Dictionary<string, double> Found { get; } = new(StringComparer.Ordinal);
        public int TickHz { get; set; }
        public bool UsedBake { get; set; }
        public double RealtimeFactor { get; set; } = 1d;
        public long Steps { get; set; }
        public long LateFrames { get; set; }
        /// <summary>Frames with no step because the master had not taken the last TWO (it fell behind; 0 on a healthy run).</summary>
        public long WaitFrames { get; set; }
        /// <summary>⭐ The most steps the master took in one frame, measured from the clock — 1 when the stepping rule holds at the KERNEL.</summary>
        public int MaxRetiredInAFrame { get; set; }
        public int MaxStepsInAFrame { get; set; }
        public int Frames { get; set; }
        public double SimEnd { get; set; }
        public double WallSeconds { get; set; }
        /// <summary>⭐ B0′ (R-3038) — the world at every cut: each named unit's position and health, and the rounds fired so far — what two
        /// runs of one take must agree on once stepped runs repeat (CE-3293 S5).</summary>
        public List<CutSnapshot> Cuts { get; } = new();
        /// <summary>⭐ CE-4042 — a verify run's verdicts and golden (null on a film or a scout).</summary>
        public VerifyResult? Verify { get; set; }

        public JsonObject ToJson()
        {
            static JsonArray A(IEnumerable<string> xs) => new(xs.Select(x => (JsonNode?)x).ToArray());
            var found = new JsonObject();
            foreach (var (k, v) in Found.OrderBy(kv => kv.Value)) found[k] = Math.Round(v, 3);
            return new JsonObject
            {
                ["scenario"] = Scenario, ["take"] = Take, ["title"] = Title, ["status"] = Status,
                ["problems"] = A(Problems), ["missing"] = A(Missing), ["mismatches"] = A(Mismatches),
                ["skippedReplays"] = new JsonArray(SkippedReplays.Select(x => (JsonNode?)x).ToArray()), ["unresolved"] = A(Unresolved),
                ["found"] = found, ["tickHz"] = TickHz, ["usedBake"] = UsedBake,
                ["realtimeFactor"] = Math.Round(RealtimeFactor, 3), ["steps"] = Steps, ["lateFrames"] = LateFrames,
                ["waitFrames"] = WaitFrames, ["maxStepsInAFrame"] = MaxStepsInAFrame, ["maxRetiredInAFrame"] = MaxRetiredInAFrame, ["frames"] = Frames,
                ["simEnd"] = Math.Round(SimEnd, 3), ["wallSeconds"] = Math.Round(WallSeconds, 2),
                ["cuts"] = new JsonArray(Cuts.Select(c => (JsonNode?)c.ToJson()).ToArray()),
                ["verify"] = Verify?.ToJson(),
            };
        }
    }

    /// <summary>⭐ B0′ — the world at one cut (sim time <see cref="Time"/>, the shot starting): every named unit's position and health.</summary>
    public sealed record CutSnapshot(int Shot, double Time, long Rounds, IReadOnlyDictionary<string, (System.Numerics.Vector3 At, float Health)> Units)
    {
        public JsonObject ToJson()
        {
            var units = new JsonObject();
            foreach (var (name, u) in Units.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                units[name] = new JsonArray(Math.Round(u.At.X, 3), Math.Round(u.At.Y, 3), Math.Round(u.At.Z, 3),
                                            float.IsNaN(u.Health) ? null : (JsonNode)Math.Round(u.Health, 2));   // ⚠ JSON has no NaN
            return new JsonObject { ["shot"] = Shot, ["t"] = Math.Round(Time, 4), ["rounds"] = Rounds, ["units"] = units };
        }

        /// <summary>The world now, as a cut sees it.</summary>
        public static CutSnapshot Of(Fdp.Core.EntityRepository world, int shot, double time, long rounds)
        {
            var units = new Dictionary<string, (System.Numerics.Vector3, float)>(StringComparer.Ordinal);
            bool hp = world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>();
            foreach (var e in world.Query().With<Fdp.Core.EntityInfo>().With<Fdp.Core.SimTransform>().Build())
            {
                var name = world.GetComponentRO<Fdp.Core.EntityInfo>(e).Name.ToString();
                if (name.Length == 0) continue;
                float health = hp && world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e)
                    ? world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current : float.NaN;
                units.TryAdd(name, (world.GetComponentRO<Fdp.Core.SimTransform>(e).Position, health));
            }
            return new CutSnapshot(shot, time, rounds, units);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4038</c> B3 (docs/DESIGN_Film_Director.md §0, §1a, §3a, §3b; D-1, D-11, D-17; R-3038) — plays a reel in the editor: for
    /// each item it opens the scenario from its file, waits for the load (<see cref="ScenarioReadyProbe"/>), enters the PREVIEW paused, and
    /// steps the run while it holds the take's shot on the film camera (<see cref="FilmStudio"/>), then leaves the preview and loads the next
    /// (⛔ never a rewind — R-3034). One runner, three modes (live · capture · scout), two remotes (the <c>/director/*</c> routes and the panel).
    ///
    /// <para>⭐⭐⭐ <b>THE STEPPING RULE (§0): exactly one step per rendered frame — never zero, never two.</b> <see cref="Tick"/> is called once
    /// per frame (<c>EditorSubsystem.Update</c>, after the debug jobs drain) and takes at most one step through the ONE place that steps
    /// (<see cref="StepOnce"/>, which throws on a second). Live, the frame is HELD to real time (<see cref="PacedClock"/>) instead of skipping
    /// or doubling a step. 📐 <b>Measured in the editor (`2026-10-21`): the master takes a step ONE FRAME after it is asked for</b> (the intent
    /// crosses the orchestration bus), so one step is always in flight; a frame that finds TWO outstanding takes none (counted,
    /// <see cref="ItemReport.WaitFrames"/>), so the master can never be handed two to take at once. The most it took in one frame is measured
    /// from the clock (<see cref="ItemReport.MaxRetiredInAFrame"/>). ⛔ SUPERSEDED: "a step not retired by the next frame is not followed by
    /// another" — with the editor's one-frame lag that stepped every OTHER frame, a paused frame between every two steps.</para>
    ///
    /// <para>⭐ Every system it adds runs every frame, paused or not (it is the director, and the preview is paused): intended
    /// <c>[TickMode(EveryFrame)]</c> once CE-3293 S1 lands — it is not a kernel system today, it is called by the editor's update.</para>
    /// </summary>
    public sealed class DirectorRunner
    {
        public const double LoadTimeoutSeconds = 30d;
        /// <summary>How long a scout or a take with an unseen anchor runs before it gives up (sim seconds).</summary>
        public const double CapSeconds = 180d;

        private readonly IDirectorHost _host;
        private readonly ICinema _cinema;
        private readonly ILayerOverride _layers;
        private readonly IFrameSink? _sink;
        private readonly IDirectorSound? _sound;
        private readonly IVerifyWorld? _verifyWorld;
        private readonly WorldUnits _units;
        private ScenarioVerification? _verification;

        private PlayRequest? _request;
        private int _item;
        private Take? _take;
        private AnchorBook? _book;
        private TakeTimeline? _timeline;
        private PacedClock? _clock;
        private ScenarioReadyProbe? _probe;
        private double _phaseStartWall, _itemStartWall, _t0;
        private long _roundsAtStart;
        private bool _exitedForLoad;
        private int _enterFrames;
        private TimelineShot? _shot;
        private bool _inGap = true;
        private int _stepsThisTick;
        private long _issued, _retiredSeen;
        private bool _paused;
        private int _liveCount;
        // capture
        private int _frameNo;
        private double _nextFrameAt;
        private Task? _pendingCapture;
        private readonly List<(int Frame, int Item, int Shot, double SimTime)> _manifest = new();
        private readonly List<(int FirstFrame, string Title)> _chapters = new();
        private Task<string?>? _encoding;

        /// <param name="verifyWorld">⭐ CE-4042 — what a verify run reads and edits (the editor's entity dump, combat rows and component edit);
        /// a host without one refuses a verify run.</param>
        public DirectorRunner(IDirectorHost host, ICinema cinema, ILayerOverride layers, IFrameSink? sink = null, IDirectorSound? sound = null,
                              Func<IShotWorld, IVerifyWorld?>? verifyWorld = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cinema = cinema ?? throw new ArgumentNullException(nameof(cinema));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _sink = sink;
            _sound = sound;
            _units = new WorldUnits(() => _host.World);
            _verifyWorld = verifyWorld?.Invoke(_units);
        }

        public DirectorPhase Phase { get; private set; } = DirectorPhase.Idle;
        public DirectorMode Mode => _request?.Mode ?? DirectorMode.Live;
        public bool Busy => Phase is DirectorPhase.Loading or DirectorPhase.Entering or DirectorPhase.Playing or DirectorPhase.Encoding;
        public IReadOnlyList<ItemReport> Reports => _reports;
        private readonly List<ItemReport> _reports = new();
        public string? Error { get; private set; }
        public string? MoviePath { get; private set; }
        /// <summary>Frames <see cref="Tick"/> has run (rails).</summary>
        public long Frames { get; private set; }

        // ── remote control (the routes and the panel call these) ──────────────────────────────

        /// <summary>Starts <paramref name="request"/> (stopping whatever plays). Every take of the reel must load, or nothing starts.</summary>
        public IReadOnlyList<string> Play(PlayRequest request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            var problems = new List<string>();
            if (request.Reel.Items.Count == 0) problems.Add("the reel has no items");
            foreach (var it in request.Reel.Items)
            {
                if (request.Mode == DirectorMode.Verify)
                {
                    foreach (var p in LoadChecks(it.Scenario).Problems) problems.Add($"{it.Scenario}: {p}");
                    continue;
                }
                var t = LoadTake(request, it);
                foreach (var p in t.Problems) problems.Add($"{it.Scenario}/{it.Take}: {p}");
            }
            if (request.Capture != null && _sink == null) problems.Add("this host cannot capture (no map to film)");
            if (request.Mode == DirectorMode.Verify && _verifyWorld == null) problems.Add("this host cannot verify (no entity dump / component edit wired)");
            if (problems.Count > 0) return problems;

            if (Busy) Stop();
            _request = request;
            _reports.Clear();
            foreach (var it in request.Reel.Items) _reports.Add(new ItemReport { Scenario = it.Scenario, Take = it.Take, Title = it.Title });
            _manifest.Clear();
            _chapters.Clear();
            _frameNo = 0;
            _encoding = null;
            Error = null;
            MoviePath = null;
            _paused = false;
            if (request.Capture != null) Directory.CreateDirectory(FramesDir);
            if (request.Mode == DirectorMode.Capture) _sound?.BeginLog();
            if (request.Mode != DirectorMode.Verify) _cinema.Enter();   // ⭐ a verify run leaves the editor as it is: no camera, no cinema
            BeginItem(0);
            return Array.Empty<string>();
        }

        public void Stop()
        {
            if (!Busy) return;
            if (_item < _reports.Count && _reports[_item].Status is "loading" or "playing") _reports[_item].Status = "stopped";
            EndRun();
            Phase = DirectorPhase.Stopped;
        }

        /// <summary>Ends this item (reported as stopped) and goes on to the next.</summary>
        public void Next()
        {
            if (Phase is not (DirectorPhase.Loading or DirectorPhase.Entering or DirectorPhase.Playing)) return;
            if (_reports[_item].Status is "loading" or "playing") _reports[_item].Status = "stopped";
            FinishItem();
        }

        /// <summary>⚠ A pause between steps changes the run (§0) — reported, so a paused live show is not mistaken for the film.</summary>
        public void Pause() { if (Phase == DirectorPhase.Playing) { _paused = true; _reports[_item].Problems.Add($"paused at {Rel:0.##} s (a paused frame changes the run)"); } }

        public void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _clock?.Start(_host.WallNow);
        }

        public bool Paused => _paused;

        public void SetSpeed(double speed)
        {
            if (_request == null || !(speed > 0)) return;
            _request = _request with { Speed = speed };
            if (_clock != null && _shot != null) _clock.SetSpeed(speed * _shot.Shot.Speed, _host.WallNow);
        }

        // ── the frame ─────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ Once per rendered frame, on the main thread (after the debug jobs drain, before the map's update).</summary>
        public void Tick()
        {
            if (!Busy) return;
            Frames++;
            _stepsThisTick = 0;
            try
            {
                switch (Phase)
                {
                    case DirectorPhase.Loading: TickLoading(); break;
                    case DirectorPhase.Entering: TickEntering(); break;
                    case DirectorPhase.Playing: TickPlaying(); break;
                    case DirectorPhase.Encoding: TickEncoding(); break;
                }
            }
            catch (Exception ex) when (ex is not DirectorSteppingException)
            {
                Error = $"{ex.GetType().Name}: {ex.Message}";
                if (_item < _reports.Count) { _reports[_item].Status = "failed"; _reports[_item].Problems.Add(Error); }
                EndRun();
                Phase = DirectorPhase.Stopped;
            }
        }

        private double Rel => _host.SimTime - _t0;

        /// <summary>The film's one timeline for the sound track: this item's sim time, offset by its place in the reel.</summary>
        public double FilmSimTime => Phase == DirectorPhase.Playing ? ItemOffset(_item) + Rel : double.NaN;

        /// <summary>The reel item playing (−1 when none).</summary>
        public int CurrentItem => Busy ? _item : -1;
        private ReelItem Item => _request!.Reel.Items[_item];
        private ItemReport Report => _reports[_item];
        private string FramesDir => Path.Combine(_request!.Capture!.Dir, "frames");

        private void BeginItem(int index)
        {
            _item = index;
            _take = null; _book = null; _timeline = null; _clock = null; _probe = null; _shot = null; _inGap = true;
            _exitedForLoad = false; _enterFrames = 0; _issued = 0; _retiredSeen = 0; _pendingCapture = null; _liveCount = 0; _verification = null;
            Phase = DirectorPhase.Loading;
            _phaseStartWall = _itemStartWall = _host.WallNow;
            Report.Status = "loading";
            var card = Item.ChapterCard is { } chapter ? chapter + "\n" + (Item.Title ?? Item.Scenario) : Item.Title ?? Item.Scenario;
            _cinema.Card(card);
            _cinema.Caption(null);
            if (_request!.Mode == DirectorMode.Capture) WriteCard(card);
        }

        private void TickLoading()
        {
            if (_probe == null)
            {
                // ⛔ never a rewind as a run (R-3034): leave the last preview, then open the file again
                if (_host.InPreview && !_exitedForLoad) { _host.ExitPreview(); _exitedForLoad = true; return; }
                int? before = _host.EntityCount;
                _host.LoadScenario(Item.Scenario);
                _probe = new ScenarioReadyProbe(before);
                _phaseStartWall = _host.WallNow;
                return;
            }
            double waited = _host.WallNow - _phaseStartWall;
            if (_probe.Observe(_host.IsOperatingEdit, _host.EntityCount, waited))
            {
                Phase = DirectorPhase.Entering;
                _host.EnterPreview(startPaused: true);
                return;
            }
            if (waited > LoadTimeoutSeconds) Fail($"the scenario did not load within {LoadTimeoutSeconds:0} s ({_probe.Polls} polls, count {_probe.LastCount})");
        }

        private void TickEntering()
        {
            if (!_host.InPreview) { if (++_enterFrames > 120) Fail("the preview did not start"); return; }
            if (!_host.Paused) { _host.Pause(); return; }

            if (_request!.Mode == DirectorMode.Verify) { BeginVerify(); return; }
            var loaded = LoadTake(_request!, Item);
            if (loaded.Value is not { } take) { Fail(string.Join("; ", loaded.Problems)); return; }
            _units.Reset();
            var invalid = TakeStore.Validate(take, _units);
            if (invalid.Count > 0) { Report.Problems.AddRange(invalid); Fail("the take names what this scenario does not have"); return; }
            _take = take;
            Report.TickHz = take.TickHz;

            var baked = take.Baked;
            bool fresh = baked != null && _request!.Mode != DirectorMode.Scout
                         && !baked.IsStaleFor(_host.Takes.ScenarioHash(take.Scenario), "", take.TickHz);
            if (baked != null && !fresh && _request!.Mode != DirectorMode.Scout)
                Report.Problems.Add($"the baked anchors are stale (scouted at {baked.TickHz} Hz for scenario {baked.ScenarioHash}) — re-scout; cutting on live cues");
            Report.UsedBake = fresh;
            _book = new AnchorBook(take.Anchors, fresh ? baked!.Times : null, _host.BehaviourName);
            _t0 = _host.SimTime;
            _roundsAtStart = _host.World is { } w0 ? Fdp.Toolkit.Combat.ShotLog.Peek(w0)?.Count ?? 0 : 0;
            Replan();
            Report.SkippedReplays.AddRange(_timeline!.SkippedReplays);
            _clock = new PacedClock(take.TickHz, _request!.Speed);
            if (take.Layers != null) _layers.Apply(take.Layers.On, take.Layers.Off);
            else _layers.Apply(Array.Empty<string>(), Array.Empty<string>());
            _sound?.SetTakeSound(take.Sound);
            if (_request.Mode == DirectorMode.Capture && Item.Title != null) _chapters.Add((_frameNo, Item.Title));
            else if (_request.Mode == DirectorMode.Capture) _chapters.Add((_frameNo, Item.Scenario));
            DirectUpcoming(0d);
            _cinema.Card(null);
            Phase = DirectorPhase.Playing;
            Report.Status = "playing";
            _clock.Start(_host.WallNow);
        }

        /// <summary>⭐ CE-4042 — the scenario's checks (and its golden): no take, no camera; the run lasts <c>runFor</c>, one step per frame.</summary>
        private void BeginVerify()
        {
            var loaded = LoadChecks(Item.Scenario);
            if (loaded.Value is not { } checks) { Fail(string.Join("; ", loaded.Problems)); return; }
            _units.Reset();
            var missing = ScenarioVerification.MissingNames(checks, _units);
            if (missing.Count > 0) { Report.Problems.AddRange(missing); Fail("the checks name what this scenario does not have"); return; }
            var golden = checks.GoldenEvery is null ? null : _host.Takes.LoadScenarioJson(Item.Scenario, ScenarioGolden.FileName).Value;
            _take = new Take { Scenario = Item.Scenario, Name = "_verify", TickHz = 60, EndAt = new AtExpr(checks.RunFor) };
            Report.TickHz = 60;
            _book = new AnchorBook(_take.Anchors, null, _host.BehaviourName);
            _t0 = _host.SimTime;
            _roundsAtStart = _host.World is { } w0 ? Fdp.Toolkit.Combat.ShotLog.Peek(w0)?.Count ?? 0 : 0;
            _verification = new ScenarioVerification(checks, golden, _request!.Verify!.Capture, _verifyWorld!, _t0);
            Replan();
            _clock = new PacedClock(60, 1d);
            Phase = DirectorPhase.Playing;
            Report.Status = "playing";
            _clock.Start(_host.WallNow);
        }

        private Loaded<ScenarioChecks> LoadChecks(string scenario)
        {
            var json = _host.Takes.LoadScenarioJson(scenario, ChecksJson.FileName);
            if (json.Value is null) return new Loaded<ScenarioChecks>(null, json.Problems);
            var problems = new List<string>();
            var checks = ChecksJson.Parse(json.Value, scenario, problems);
            return new Loaded<ScenarioChecks>(problems.Count == 0 ? checks : null, problems);
        }

        private Loaded<Take> LoadTake(PlayRequest request, ReelItem item)
            => request.Inline != null && request.Inline.TryGetValue(item.Scenario + "/" + item.Take, out var inline)
                ? new Loaded<Take>(inline, Array.Empty<string>())
                : _host.Takes.Load(item.Scenario, item.Take);

        private void Replan()
        {
            _timeline = TakeTimeline.Plan(_take!, a => _book!.TimeOf(a), Item.From, Item.To);
            Report.Unresolved.Clear();
            Report.Unresolved.AddRange(_timeline.Unresolved);
        }

        private void TickPlaying()
        {
            var mode = _request!.Mode;
            double rel = Rel;

            // ⭐ the master takes a step a frame after it is asked (one is in flight); with TWO outstanding it would take both at once
            long retired = (long)Math.Round(rel / _clock!.StepSeconds);
            Report.MaxRetiredInAFrame = Math.Max(Report.MaxRetiredInAFrame, (int)(retired - _retiredSeen));
            _retiredSeen = retired;
            bool masterBehind = _issued - retired >= 2;

            _book!.Observe(_host.World!, _units, rel);
            _verification?.Observe(rel);
            if (_book.Live.Count != _liveCount)
            {
                _liveCount = _book.Live.Count;
                if (_timeline!.Unresolved.Count > 0 || mode == DirectorMode.Scout) Replan();
            }

            if (Finished(rel)) { FinishItem(); return; }

            var shot = _timeline!.At(rel);
            if (!ReferenceEquals(shot, _shot) || (shot == null) != _inGap) EnterShot(shot);
            DirectUpcoming(rel);

            if (_paused) return;
            if (masterBehind) { Report.WaitFrames++; return; }

            if (mode == DirectorMode.Capture)
            {
                if (_pendingCapture is { IsCompleted: false }) { Report.LateFrames++; return; }   // the map has not served it yet: hold
                if (_pendingCapture is { IsFaulted: true } faulted) throw new InvalidOperationException("capture failed: " + faulted.Exception!.GetBaseException().Message);
                if (shot != null && rel >= _nextFrameAt - 1e-6) Capture(shot, rel);
            }
            else if (mode == DirectorMode.Live && shot != null)
            {
                _host.Hold(_clock!.HoldFor(_host.WallNow));
            }
            _verification?.ApplyDue(rel);   // ⭐ an action lands on its exact step (every frame is a step boundary)
            StepOnce();
        }

        /// <summary>The ONE place a step is taken. ⛔ A second in the same frame is a defect of the director, not of the run.</summary>
        private void StepOnce()
        {
            if (++_stepsThisTick > 1) throw new DirectorSteppingException($"two steps in one frame (frame {Frames})");
            _issued++;
            _host.StepBy(_clock!.StepSeconds);
            if (_shot != null && _request!.Mode == DirectorMode.Live) _clock.Stepped(_host.WallNow);
            Report.Steps++;
            Report.MaxStepsInAFrame = Math.Max(Report.MaxStepsInAFrame, _stepsThisTick);
        }

        /// <summary>How long a take waits, past its last placed shot, for a beat it has not seen (then the beat is reported missing).</summary>
        public const double UnseenGraceSeconds = 30d;

        private bool Finished(double rel)
        {
            double cap = _take!.EndAt is { IsFixed: true } end ? end.Offset : CapSeconds;
            if (_request!.Mode == DirectorMode.Verify) return rel >= cap - 1e-9;
            if (rel >= cap) return true;
            if (_request!.Mode == DirectorMode.Scout) return !_book!.Unseen.Any() && rel >= _timeline!.End;
            if (_timeline!.Unresolved.Count > 0) return rel >= _timeline.End + UnseenGraceSeconds;
            return rel >= _timeline.End - 1e-6;
        }

        private void EnterShot(TimelineShot? shot)
        {
            _shot = shot;
            _inGap = shot == null;
            var take = _take!;
            if (shot == null) { _cinema.Caption(null); return; }
            var s = shot.Shot;
            if (_host.World is { } world)
                Report.Cuts.Add(CutSnapshot.Of(world, shot.Number, Rel, (Fdp.Toolkit.Combat.ShotLog.Peek(world)?.Count ?? 0) - _roundsAtStart));
            _cinema.Caption(s.Caption ?? take.Caption);
            var on = new List<string>(take.Layers?.On ?? Array.Empty<string>());
            var off = new List<string>(take.Layers?.Off ?? Array.Empty<string>());
            if (s.Layers != null) { on.AddRange(s.Layers.On); off.AddRange(s.Layers.Off); on.RemoveAll(s.Layers.Off.Contains); }
            _layers.Apply(on, off);
            _sound?.SetTakeSound(s.Sound ?? take.Sound);
            _clock!.SetSpeed(_request!.Speed * s.Speed, _host.WallNow);
            _nextFrameAt = Math.Max(shot.Start, Rel);
        }

        /// <summary>Holds the shot of this moment — in a gap, the NEXT shot at its start, so the camera is there when it begins.</summary>
        private void DirectUpcoming(double rel)
        {
            var target = _timeline!.At(rel) ?? _timeline.Next(rel);
            if (target == null) return;
            var fs = ShotEvaluator.Evaluate(target.Shot, Math.Max(0d, rel - target.Start), _units);
            if (fs != null) FilmStudio.Direct(fs);
        }

        private void Capture(TimelineShot shot, double rel)
        {
            var c = _request!.Capture!;
            var path = Path.Combine(FramesDir, $"{_frameNo:D6}.png");
            string? caption = c.BurnCaptions ? shot.Shot.Caption ?? _take!.Caption : null;
            _pendingCapture = _sink!.Frame(path, c.Width, c.Height, c.Supersample, caption);
            _manifest.Add((_frameNo, _item, shot.Number, rel));
            _frameNo++;
            Report.Frames++;
            double perFrame = shot.Shot.Speed / c.Fps;
            _nextFrameAt += perFrame;
            // ⭐ slow motion past one frame per step: the same picture again (a step cannot be split)
            while (_nextFrameAt <= rel + _clock!.StepSeconds - 1e-6 && shot.Shot.Speed * _take!.TickHz < c.Fps)
            {
                var dup = Path.Combine(FramesDir, $"{_frameNo:D6}.png");
                var src = path;
                _pendingCapture = _pendingCapture.ContinueWith(_ => File.Copy(src, dup, overwrite: true), TaskScheduler.Default);
                _manifest.Add((_frameNo, _item, shot.Number, rel));
                _frameNo++;
                Report.Frames++;
                _nextFrameAt += perFrame;
            }
        }

        private void WriteCard(string text)
        {
            var c = _request!.Capture!;
            int n = Math.Max(1, (int)Math.Round(_request.Reel.GapSeconds * c.Fps));
            var first = Path.Combine(FramesDir, $"{_frameNo:D6}.png");
            var task = _sink!.Card(first, c.Width, c.Height, text);
            for (int i = 1; i < n; i++)
            {
                var dup = Path.Combine(FramesDir, $"{_frameNo + i:D6}.png");
                task = task.ContinueWith(_ => File.Copy(first, dup, overwrite: true), TaskScheduler.Default);
            }
            for (int i = 0; i < n; i++) _manifest.Add((_frameNo + i, _item, 0, 0d));
            _frameNo += n;
            _pendingCapture = task;
        }

        private void Fail(string why)
        {
            Report.Status = "failed";
            Report.Problems.Add(why);
            FinishItem();
        }

        private void FinishItem()
        {
            var r = Report;
            if (_book != null)
            {
                foreach (var (k, v) in _book.Live) r.Found[k] = v;
                r.Missing.AddRange(_book.Unseen.Where(k => !r.Missing.Contains(k)));
                foreach (var m in _book.Mismatches(double.PositiveInfinity)) r.Mismatches.Add(m.ToString());   // the run is over: unseen = not seen
            }
            if (_clock != null) { r.RealtimeFactor = _clock.RealtimeFactor; r.LateFrames += _clock.LateFrames; }
            r.SimEnd = _take != null ? Rel : 0d;
            r.WallSeconds = _host.WallNow - _itemStartWall;
            if (_verification != null && r.Status == "playing")
            {
                var v = _verification.Finish(Rel, r.Steps);
                if (v.Golden == "captured" && v.GoldenJson != null)
                    v.Written.AddRange(_host.Takes.SaveScenarioJson(Item.Scenario, ScenarioGolden.FileName, v.GoldenJson));
                r.Verify = v;
                r.Status = v.Passed ? "pass" : "fail";
                _verification = null;
            }
            if (r.Status == "playing") r.Status = r.Missing.Count > 0 ? "beat-missing" : "ok";
            if (_request!.Mode == DirectorMode.Scout && _request.Bake && _take != null && _request.Inline?.ContainsKey(Item.Scenario + "/" + Item.Take) != true) Bake(r);

            FilmStudio.Release();
            _layers.Clear();
            _cinema.Caption(null);
            if (_host.InPreview) _host.ExitPreview();

            int next = _item + 1;
            if (next >= _request.Reel.Items.Count && _request.Loop && _request.Mode == DirectorMode.Live) next = 0;
            if (next < _request.Reel.Items.Count)
            {
                if (next == 0) foreach (var it in _reports) it.Status = "pending";
                BeginItem(next);
                return;
            }
            if (_request.Mode == DirectorMode.Capture && _request.Capture!.Encode)
            {
                Phase = DirectorPhase.Encoding;
                _cinema.Card("encoding the movie…");
                _encoding = StartEncoding();
                return;
            }
            EndRun();
            Phase = DirectorPhase.Done;
        }

        /// <summary>⭐ B7 — the scout's run becomes the take's baked anchors (its step rate, the scenario's hash), saved to the take's file.</summary>
        private void Bake(ItemReport r)
        {
            var take = _take!;
            var baked = new BakedAnchors
            {
                Times = new Dictionary<string, double>(r.Found, StringComparer.Ordinal),
                TickHz = take.TickHz,
                ScenarioHash = _host.Takes.ScenarioHash(take.Scenario),
                BuildSha = BuildSha(),
                Missing = r.Missing.ToList(),
            };
            if (take.Baked != null && take.Baked.TickHz == take.TickHz)
                foreach (var (k, v) in take.Baked.Times)
                    if (r.Found.TryGetValue(k, out var now) && Math.Abs(now - v) > AnchorBook.Tolerance
                        && take.Baked.ScenarioHash == baked.ScenarioHash)
                        r.Mismatches.Add($"{k}: this scout {now:0.###} s, the last {v:0.###} s (a determinism finding — R-3038)");
            _host.Takes.Save(take with { Baked = baked });
        }

        private static string BuildSha() =>
            Environment.GetEnvironmentVariable("HROT_BUILD_SHA")
            ?? typeof(DirectorRunner).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                   .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";

        private void TickEncoding()
        {
            if (_encoding is not { IsCompleted: true } done) return;
            if (done.IsFaulted) Error = "encoding failed: " + done.Exception!.GetBaseException().Message;
            else if (done.Result is { } err) Error = err;
            EndRun();
            Phase = DirectorPhase.Done;
        }

        /// <summary>⭐ B8 (§4a, D-15) — ONE movie of the whole reel: the frames numbered across items, a chapter per item, the sound track.</summary>
        private Task<string?> StartEncoding()
        {
            var c = _request!.Capture!;
            var movie = Path.GetFullPath(c.Movie ?? Path.Combine(c.Dir, (_request.Reel.Name.Replace('/', '-')) + ".mp4"));
            MoviePath = movie;
            var frames = FramesDir;
            var dir = c.Dir;
            var manifest = new JsonArray(_manifest.Select(m => (JsonNode?)new JsonObject
            {
                ["frame"] = m.Frame, ["item"] = m.Item, ["shot"] = m.Shot, ["simTime"] = Math.Round(m.SimTime, 4),
            }).ToArray());
            File.WriteAllText(Path.Combine(dir, "manifest.json"), new JsonObject
            {
                ["reel"] = _request.Reel.Name, ["fps"] = c.Fps, ["frames"] = _frameNo, ["items"] = new JsonArray(_reports.Select(r => (JsonNode?)r.ToJson()).ToArray()),
                ["map"] = manifest,
            }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var meta = new System.Text.StringBuilder(";FFMETADATA1\ntitle=" + (_request.Reel.Title ?? _request.Reel.Name) + "\n");
            for (int i = 0; i < _chapters.Count; i++)
            {
                long start = _chapters[i].FirstFrame * 1000L / c.Fps;
                long end = (i + 1 < _chapters.Count ? _chapters[i + 1].FirstFrame : _frameNo) * 1000L / c.Fps;
                meta.Append($"[CHAPTER]\nTIMEBASE=1/1000\nSTART={start}\nEND={end}\ntitle={_chapters[i].Title}\n");
            }
            var metaPath = Path.Combine(dir, "chapters.txt");
            File.WriteAllText(metaPath, meta.ToString());
            string? wav = Path.Combine(dir, "sound.wav");
            var simTimes = _manifest.OrderBy(m => m.Frame).Select(m => m.Shot == 0 ? double.NaN : m.SimTime + ItemOffset(m.Item)).ToList();
            if (_sound == null || !_sound.EndLog(wav, simTimes, c.Fps)) wav = null;
            int fps = c.Fps;
            var pending = _pendingCapture ?? Task.CompletedTask;
            return pending.ContinueWith(_ => Ffmpeg.Encode(frames, fps, metaPath, wav, movie), TaskScheduler.Default);
        }

        /// <summary>Items restart at sim time 0; the sound track needs one timeline, so each item is offset far apart.</summary>
        internal static double ItemOffset(int item) => item * 100_000d;

        private void EndRun()
        {
            FilmStudio.Release();
            _layers.Clear();
            _sound?.Reset();
            _cinema.Card(null);
            _cinema.Caption(null);
            _cinema.Exit();
            if (_host.InPreview) _host.ExitPreview();
        }

        // ── status ────────────────────────────────────────────────────────────────────────────

        public JsonObject Status()
        {
            var o = new JsonObject
            {
                ["phase"] = Phase.ToString(),
                ["mode"] = _request?.Mode.ToString(),
                ["reel"] = _request?.Reel.Name,
                ["item"] = _request == null ? null : _item,
                ["items"] = _request?.Reel.Items.Count,
                ["paused"] = _paused,
                ["speed"] = _request?.Speed,
                ["shot"] = _shot?.Number,
                ["simTime"] = Phase == DirectorPhase.Playing ? Math.Round(Rel, 3) : null,
                ["realtimeFactor"] = _clock is { Started: true } ? Math.Round(_clock.RealtimeFactor, 3) : null,
                ["tickHz"] = _take?.TickHz,
                ["frames"] = _request?.Capture != null ? _frameNo : null,
                ["movie"] = MoviePath,
                ["error"] = Error,
                ["reports"] = new JsonArray(_reports.Select(r => (JsonNode?)r.ToJson()).ToArray()),
            };
            if (Phase == DirectorPhase.Playing && _book != null)
            {
                var live = new JsonObject();
                foreach (var (k, v) in _book.Live) live[k] = Math.Round(v, 3);
                o["anchorsSeen"] = live;
                o["mismatches"] = new JsonArray(_book.Mismatches(Rel).Select(m => (JsonNode?)m.ToString()).ToArray());
            }
            return o;
        }
    }

    /// <summary>⛔ The director took two steps in one frame — the stepping rule (§0) broken by the director itself.</summary>
    public sealed class DirectorSteppingException : InvalidOperationException
    {
        public DirectorSteppingException(string message) : base(message) { }
    }

    /// <summary>⭐ B8 — joins the frames into the movie (ffmpeg): chapters from the metadata, the sound track muxed when there is one.</summary>
    internal static class Ffmpeg
    {
        /// <summary>Null when the movie was written; else why not.</summary>
        public static string? Encode(string framesDir, int fps, string chaptersPath, string? wavPath, string moviePath)
        {
            var args = new List<string> { "-y", "-loglevel", "error", "-framerate", fps.ToString(), "-i", Path.Combine(framesDir, "%06d.png"),
                                          "-i", chaptersPath };
            if (wavPath != null) args.AddRange(new[] { "-i", wavPath });
            args.AddRange(new[] { "-map_metadata", "1", "-map", "0:v" });
            if (wavPath != null) args.AddRange(new[] { "-map", "2:a", "-c:a", "aac", "-shortest" });
            args.AddRange(new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-movflags", "+faststart", moviePath });
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = System.Diagnostics.Process.Start(psi)!;
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? null : $"ffmpeg exited {p.ExitCode}: {err.Trim()}";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "ffmpeg is not installed — the frames are in " + framesDir;
            }
        }
    }
}
