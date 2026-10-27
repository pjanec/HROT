using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hrot.UI.Common.Map3D;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>How the director runs a reel: on screen in real time, frame by frame to files, as a scout that bakes anchor times, or as
    /// an extension's <see cref="RunPlan"/> (the editor's verifier — CE-4042, CE-4061).</summary>
    public enum DirectorMode { Live, Capture, Scout, Plan }

    /// <summary>Where and how a capture writes: frames under <see cref="Dir"/>, one movie at <see cref="Movie"/> (§4a: a reel is ONE movie).</summary>
    public sealed record CaptureOptions(string Dir, int Fps = 30, int Width = 1280, int Height = 720, int Supersample = 1,
                                        bool BurnCaptions = true, string? Movie = null, bool Encode = true);

    /// <summary>A reel to play, at <see cref="Speed"/> (live only), live / to files (<see cref="Capture"/>) / as a scout.</summary>
    /// <remarks><see cref="Inline"/>: takes given with the request (by <c>scenario/take</c>), not read from a file — film.py's one-off
    /// plans (D-9). A scout of an inline take bakes nothing. ⭐ <c>CE-4061</c> — <see cref="Plan"/>: run by the runner's
    /// <see cref="IRunExtension"/> instead of takes. ⭐ <c>CE-1303</c> P2a (§11m D5, D7) — <see cref="StartAt"/>: the first item starts at that
    /// film second (a seekable source seeks there; the editor fast-forwards); <see cref="Cinema"/> false keeps the panels on screen (an
    /// iteration pass, not a film).</remarks>
    public sealed record PlayRequest(Reel Reel, double Speed = 1d, bool Loop = false, CaptureOptions? Capture = null, bool Scout = false,
                                     IReadOnlyDictionary<string, Take>? Inline = null, bool Bake = true, RunPlan? Plan = null,
                                     double StartAt = 0d, bool Cinema = true)
    {
        public DirectorMode Mode => Plan != null ? DirectorMode.Plan : Scout ? DirectorMode.Scout : Capture != null ? DirectorMode.Capture : DirectorMode.Live;
    }

    /// <remarks><see cref="Extension"/>: ⭐ CE-4061 — the runner's extension has the frames (its own phases, named by
    /// <see cref="IRunExtension.PhaseName"/>).</remarks>
    public enum DirectorPhase { Idle, Loading, Entering, Playing, Encoding, Done, Stopped, Extension }

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
        /// <summary>⭐ CE-4061 — what the runner's extension judged the item to be (the verifier's verdicts and golden); null on a film or a scout.</summary>
        public IRunResult? Result { get; set; }

        public JsonObject ToJson()
        {
            static JsonArray A(IEnumerable<string> xs) => new(xs.Select(x => (JsonNode?)x).ToArray());
            var found = new JsonObject();
            foreach (var (k, v) in Found.OrderBy(kv => kv.Value)) found[k] = Math.Round(v, 3);
            var o = new JsonObject
            {
                ["scenario"] = Scenario, ["take"] = Take, ["title"] = Title, ["status"] = Status,
                ["problems"] = A(Problems), ["missing"] = A(Missing), ["mismatches"] = A(Mismatches),
                ["skippedReplays"] = new JsonArray(SkippedReplays.Select(x => (JsonNode?)x).ToArray()), ["unresolved"] = A(Unresolved),
                ["found"] = found, ["tickHz"] = TickHz, ["usedBake"] = UsedBake,
                ["realtimeFactor"] = Math.Round(RealtimeFactor, 3), ["steps"] = Steps, ["lateFrames"] = LateFrames,
                ["waitFrames"] = WaitFrames, ["maxStepsInAFrame"] = MaxStepsInAFrame, ["maxRetiredInAFrame"] = MaxRetiredInAFrame, ["frames"] = Frames,
                ["simEnd"] = Math.Round(SimEnd, 3), ["wallSeconds"] = Math.Round(WallSeconds, 2),
                ["cuts"] = new JsonArray(Cuts.Select(c => (JsonNode?)c.ToJson()).ToArray()),
            };
            if (Result != null) o[Result.Kind] = Result.ToJson();
            return o;
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
    public sealed class DirectorRunner : IRunContext
    {
        public const double LoadTimeoutSeconds = 30d;
        /// <summary>How long a scout or a take with an unseen anchor runs before it gives up (sim seconds).</summary>
        public const double CapSeconds = 180d;

        private readonly IDirectorHost _host;
        private readonly ICinema _cinema;
        private readonly ILayerOverride _layers;
        private readonly IFrameSink? _sink;
        private readonly IDirectorSound? _sound;
        /// <summary>⭐ CE-4061 (§11h) — the one seam a host adds a kind of run through (the editor: the scenario verifier).</summary>
        private readonly IRunExtension? _ext;
        private readonly WorldUnits _units;

        private PlayRequest? _request;
        private int _item;
        private Take? _take;
        private AnchorBook? _book;
        private TakeTimeline? _timeline;
        private IReadOnlyList<CameraKey> _keys = Array.Empty<CameraKey>();
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
        // ⭐ CE-1303 P2a — play from a film second (D5) · loop refused once on a host that cannot seek (D6)
        private double _startAt;
        private bool _loopRefused;
        // ⭐ CE-1303 P2b — CR-7 (§11o): the film's segments (identity = none), the one playing, the live run's fast-forward, the replays it skipped
        private FilmTimeMap _film = FilmTimeMap.Identity;
        private int _seg = -1;
        private bool _filmOver, _fastForwarding, _gapCard;
        private readonly HashSet<int> _skippedBack = new();
        // ⭐ CR-11 — the take's subtitles (its captions read as subtitles when it has none) and the one on screen
        private IReadOnlyList<Subtitle> _subs = Array.Empty<Subtitle>();
        private SubtitleFrame? _subNow;
        // capture
        private int _frameNo;
        private double _nextFrameAt;
        private Task? _pendingCapture;
        private readonly List<(int Frame, int Item, int Shot, double SimTime)> _manifest = new();
        private readonly List<(int FirstFrame, string Title)> _chapters = new();
        private Task<string?>? _encoding;

        /// <param name="extension">⭐ CE-4061 — a kind of run the host adds (the editor: <c>ScenarioVerifyRun</c>); a request with a
        /// <see cref="PlayRequest.Plan"/> is refused without one.</param>
        public DirectorRunner(IDirectorHost host, ICinema cinema, ILayerOverride layers, IFrameSink? sink = null, IDirectorSound? sound = null,
                              IRunExtension? extension = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cinema = cinema ?? throw new ArgumentNullException(nameof(cinema));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _sink = sink;
            _sound = sound;
            _units = new WorldUnits(() => _host.World);
            _ext = extension;
            _ext?.Attach(this);
        }

        public DirectorPhase Phase { get; private set; } = DirectorPhase.Idle;

        /// <summary>
        /// ⭐ <c>CE-1303</c> P2a — CR-4 (docs/DESIGN_Director_Timeline_Editor.md §11m D4): the take OPEN for editing, by scenario and take (null =
        /// none open). A film or a capture plays it instead of the saved file, so an unsaved edit is what the next play — and the next pass of a
        /// looped shot — shows. ⛔ Never a scout: a scout bakes and SAVES its take, which would write the unsaved edits. Set by
        /// <see cref="DirectorPack"/> from its <see cref="DirectorTakeSession"/>.
        /// </summary>
        public Func<string, string, Take?>? OpenTake { get; set; }

        /// <summary>
        /// ⭐⭐ <c>CE-1304</c> P4 — CR-5 (docs/DESIGN_Director_Timeline_Editor.md §11s): the run's timeline index — the live index on a live host, the open
        /// recording's on a recording — that the take's EVENT anchors (<c>shot</c> / <c>blast</c> / <c>hit</c>) are found in (<see cref="AnchorBook"/>). Set by
        /// <see cref="DirectorPack"/> from the host's own shown index (<see cref="DirectorPackHost.Index"/>), so every host's runner has it by construction.
        /// Null: event anchors stay unseen (a baked time still places them).
        /// </summary>
        public Func<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineIndex?>? Index { get; set; }

        /// <summary>⭐ CR-5 — the take's anchors in this run, over the run's index (a recording's index leads over a bake: <see cref="AnchorBook"/>).</summary>
        private AnchorBook NewBook(Take take, IReadOnlyDictionary<string, double>? baked)
            => new(take.Anchors, baked, _host.BehaviourName, Index, _t0, indexIsTheRun: _host.CanSeek);

        /// <summary>⭐ CE-1303 P2a (§11m D6) — at the end of the shot on screen the source goes back to the shot's start (a seekable host) and the
        /// take is read again (D4), so an edit shows on the next pass. A host that cannot seek reports it once and plays on.</summary>
        public bool LoopShot { get; set; }

        /// <summary>⭐ CE-1303 — the host's one extension (the editor's verifier); null on a films-only host.</summary>
        public IRunExtension? Extension => _ext;
        public DirectorMode Mode => _request?.Mode ?? DirectorMode.Live;
        public bool Busy => Phase is DirectorPhase.Loading or DirectorPhase.Entering or DirectorPhase.Playing or DirectorPhase.Encoding
                                  or DirectorPhase.Extension;

        // ── ⭐ CE-4061 — IRunContext: what the extension reads and asks ─────────────────────────────
        IDirectorHost IRunContext.Host => _host;
        WorldUnits IRunContext.Units => _units;
        PlayRequest IRunContext.Request => _request!;
        ReelItem IRunContext.Item => Item;
        ItemReport IRunContext.Report => Report;
        double IRunContext.Rel => Rel;
        void IRunContext.Fail(string why) => Fail(why);
        void IRunContext.EndItem() => EndItem();
        void IRunContext.EnterExtensionPhase() => Phase = DirectorPhase.Extension;
        void IRunContext.ResumePlaying(double? t0)
        {
            if (!_host.Paused) _host.Pause();
            if (t0 is { } start && _clock != null)   // ⭐ CE-4045 H3 — the steps "retired" are counted from the new clock, as SeekSource does
            {
                _t0 = start;
                _issued = _retiredSeen = (long)Math.Round(Rel / _clock.StepSeconds);
            }
            Phase = DirectorPhase.Playing;
            _clock?.Start(_host.WallNow);
        }

        /// <summary>The item runs LIVE through the extension (it records) rather than in the preview.</summary>
        private bool LiveRun => _ext != null && _request!.Mode == DirectorMode.Plan && _ext.LoadsLive(_request);
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
            if (request.Mode == DirectorMode.Plan)   // ⭐ CE-4061 — the extension's plan, judged by the extension
            {
                if (_ext == null) problems.Add($"this host has no extension to run a {request.Plan!.GetType().Name}");
                else problems.AddRange(_ext.Validate(request));
            }
            else
                foreach (var it in request.Reel.Items)
                {
                    var t = LoadTake(request, it);
                    foreach (var p in t.Problems) problems.Add($"{it.Scenario}/{it.Take}: {p}");
                }
            if (request.Capture != null && _sink == null) problems.Add("this host cannot capture (no map to film)");
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
            if (request.Capture != null) { Directory.CreateDirectory(FramesDir); FilmStudio.Pilot = false; }   // ⭐ P2b — a capture films the program
            if (request.Mode == DirectorMode.Capture) _sound?.BeginLog();
            // ⭐ an extension's run (verify) leaves the editor as it is: no camera, no cinema; ⭐ CE-1303 D7 — nor does an iteration pass
            if (request.Mode != DirectorMode.Plan && request.Cinema) _cinema.Enter();
            BeginItem(0);
            return Array.Empty<string>();
        }

        /// <summary>
        /// ⭐ <c>CE-1303</c> P2a — CR-4 (§11m D5): plays <paramref name="request"/> from film second <paramref name="filmTime"/> of its first item —
        /// the shot, camera and layers of that moment. A seekable source (a recording) is SOUGHT there; a live one (the editor) is fast-forwarded:
        /// stepped one step per frame with no hold and no capture until it gets there (R-3038 — never two steps in a frame).
        /// </summary>
        public IReadOnlyList<string> PlayFrom(PlayRequest request, double filmTime)
            => Play((request ?? throw new ArgumentNullException(nameof(request))) with { StartAt = Math.Max(0d, filmTime) });

        /// <summary>The film second the item playing starts at (0 = its start); on a live host, until the fast-forward reaches it.</summary>
        public double StartAt => _startAt;

        /// <summary>
        /// ⭐ <c>CE-1303</c> P2b — CR-7 (§11o S5): the FILM second of <paramref name="request"/>'s first take that shows SOURCE second <paramref name="sourceSim"/>
        /// (the first segment that holds it, else the next one after it) — what <i>Play from playhead</i> hands <see cref="PlayFrom"/>, since a playhead is a
        /// source second. A take with no segments: the same second. The take is the one a play would read (the open one, unsaved edits included).
        /// </summary>
        public double FilmTimeAt(PlayRequest request, double sourceSim)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (request.Reel.Items.Count == 0 || LoadTake(request, request.Reel.Items[0]).Value is not { } take) return sourceSim;
            var times = take.Baked?.Times;
            return FilmTimeMap.Of(take, n => times != null && times.TryGetValue(n, out var t) ? t : null).FilmOf(sourceSim);
        }

        /// <summary>⭐ <c>CE-4059</c> P3 (§11r) — the take a play of <paramref name="request"/> reads for its first item (the open one, unsaved edits
        /// included; else the file) — what the stills plan is made from, so a still shows what Play would.</summary>
        public Loaded<Take> TakeOf(PlayRequest request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            return request.Reel.Items.Count == 0 ? new Loaded<Take>(null, new[] { "the reel has no items" }) : LoadTake(request, request.Reel.Items[0]);
        }

        /// <summary>⭐ CR-7 — the film second now (NaN when not playing): the source second inside the segment on screen, through <see cref="FilmTimeMap"/>.</summary>
        public double FilmTime => Phase == DirectorPhase.Playing ? FilmAt(Rel) : double.NaN;

        /// <summary>⭐ CR-7 — sim seconds per film second now: the request's speed × the shot's × the segment's (the clock holds each step for it).</summary>
        public double Rate => (_request?.Speed ?? 1d) * SourcePerFilmSecond;

        /// <summary>⭐ CR-11 — the subtitle on screen now (null: none) — what the cinema was handed this frame.</summary>
        public SubtitleFrame? SubtitleNow => _subNow;

        /// <summary>
        /// ⭐ <c>CE-4059</c> / CR-7 (§11q K3) — the segment on screen while the runner PLAYS <paramref name="scenario"/>/<paramref name="take"/> (null: another take, not
        /// playing, or a take with no segments). K and <i>update key from view</i> scope a new key to it when it is a replay (<see cref="FilmSegment.Back"/>).
        /// </summary>
        public FilmSegment? SegmentOf(string scenario, string take)
            => Phase == DirectorPhase.Playing && _request != null && _item < _request.Reel.Items.Count
               && Item.Scenario == scenario && Item.Take == take ? Segment : null;

        /// <summary>The segment playing (null: the take has none, or none plays).</summary>
        private FilmSegment? Segment => _seg >= 0 && _seg < _film.Segments.Count ? _film.Segments[_seg] : null;

        private double SourcePerFilmSecond => (_shot?.Shot.Speed ?? 1d) * (Segment?.Speed ?? 1d);

        private double FilmAt(double rel) => _film.IsIdentity ? rel : Segment is { } s ? s.FilmOf(rel) : _film.FilmEnd;

        /// <summary>⭐ CR-7 — the clock paces each step at the request × shot × segment rate.</summary>
        private void ApplyRate() { if (_clock != null && _request != null) _clock.SetSpeed(_request.Speed * SourcePerFilmSecond, _host.WallNow); }

        public void Stop()
        {
            if (!Busy) return;
            _ext?.Stop();
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
            if (_shot != null || Segment != null) ApplyRate();
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
                    case DirectorPhase.Extension: _ext!.Tick(); break;
                }
            }
            catch (Exception ex) when (ex is not DirectorSteppingException)
            {
                Error = $"{ex.GetType().Name}: {ex.Message}";
                // ⭐ the report keeps the message only; the stack goes to the log so a failed run says WHERE (2026-10-23: six verify runs
                //   failed on "infinity cannot be written as JSON" with no trace to follow).
                Console.Error.WriteLine($"[Director] the run stopped on an exception:\n{ex}");
                _ext?.Stop();
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
            _exitedForLoad = false; _enterFrames = 0; _issued = 0; _retiredSeen = 0; _pendingCapture = null; _liveCount = 0;
            _startAt = index == 0 && _request!.Mode is DirectorMode.Live or DirectorMode.Capture ? Math.Max(0d, _request.StartAt) : 0d;
            _loopRefused = false;
            _film = FilmTimeMap.Identity; _seg = -1; _filmOver = false; _fastForwarding = false; _gapCard = false; _skippedBack.Clear();
            _subs = Array.Empty<Subtitle>(); _subNow = null;
            _ext?.Reset();
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
                // ⚠ a live run can read as "in preview" (CE-4054) — never left as a preview (that is a rewind); the live load stops it
                if (_host.InPreview && !_exitedForLoad && !LiveRun) { _host.ExitPreview(); _exitedForLoad = true; return; }
                int? before = _host.EntityCount;
                if (_request!.Mode == DirectorMode.Plan) _ext!.BeforeLoad();
                if (LiveRun) _ext!.Load(Item.Scenario);   // ⭐ a live run records (R-3053); the preview never does
                else _host.LoadScenario(Item.Scenario);
                _probe = new ScenarioReadyProbe(before);
                _phaseStartWall = _host.WallNow;
                return;
            }
            double waited = _host.WallNow - _phaseStartWall;
            if (_probe.Observe(LiveRun ? _ext!.IsLoaded : _host.IsOperatingEdit, _host.EntityCount, waited))
            {
                Phase = DirectorPhase.Entering;
                if (!LiveRun) _host.EnterPreview(startPaused: true);
                return;
            }
            if (waited > LoadTimeoutSeconds) Fail($"the scenario did not load within {LoadTimeoutSeconds:0} s ({_probe.Polls} polls, count {_probe.LastCount})");
        }

        private void TickEntering()
        {
            if (!LiveRun && !_host.InPreview) { if (++_enterFrames > 120) Fail("the preview did not start"); return; }
            if (!_host.Paused) { _host.Pause(); return; }

            if (_request!.Mode == DirectorMode.Plan)   // ⭐ CE-4061 — the extension prepares its item, then hands back the take it runs
            {
                if (_ext!.BeginItem() is { } start) StartPlan(start);
                return;
            }
            var loaded = LoadTake(_request!, Item);
            if (loaded.Value is not { } take) { Fail(string.Join("; ", loaded.Problems)); return; }
            _units.Reset();
            var invalid = TakeStore.Validate(take, _units);
            if (invalid.Count > 0) { Report.Problems.AddRange(invalid); Fail("the take names what this scenario does not have"); return; }
            _take = take;
            Report.TickHz = take.TickHz;

            var baked = take.Baked;
            bool fresh = BakeIsFresh(take);
            if (baked != null && !fresh && _request!.Mode != DirectorMode.Scout)
                Report.Problems.Add($"the baked anchors are stale (scouted at {baked.TickHz} Hz for scenario {baked.ScenarioHash}) — re-scout; cutting on live cues");
            Report.UsedBake = fresh;
            // ⭐ CE-1303 D3 — a seekable source's sim clock IS the run's (a recording of it), so a take's times mean the same there as in the editor
            _t0 = _host.CanSeek ? 0d : _host.SimTime;
            _book = NewBook(take, fresh ? baked!.Times : null);
            _book.ObserveEvents(_units);   // ⭐ CR-5 — a recording's index already holds the run: the plan below places every event anchor it names
            _roundsAtStart = _host.World is { } w0 ? Fdp.Toolkit.Combat.ShotLog.Peek(w0)?.Count ?? 0 : 0;
            Replan();
            Report.SkippedReplays.AddRange(_timeline!.SkippedReplays);
            _clock = new PacedClock(take.TickHz, _request!.Speed);
            if (take.Layers != null) _layers.Apply(take.Layers.On, take.Layers.Off);
            else _layers.Apply(Array.Empty<string>(), Array.Empty<string>());
            _sound?.SetTakeSound(take.Sound);
            if (_request.Mode == DirectorMode.Capture && Item.Title != null) _chapters.Add((_frameNo, Item.Title));
            else if (_request.Mode == DirectorMode.Capture) _chapters.Add((_frameNo, Item.Scenario));
            // ⭐ CE-1303 D5 — play from a film second: a recording is sought there (the shot, camera and layers of that moment follow from the
            //   first frame's EnterShot); a live run fast-forwards (TickPlaying)
            //   ⭐ CE-1303 P2b — CR-7 (§11o S5): through the FILM map — the segment that plays film second t, at its source second
            _seg = _film.IsIdentity ? -1 : 0;
            if (_host.CanSeek && (_startAt > 0d || !_film.IsIdentity))
            {
                if (_film.At(_startAt) is { } at) { _seg = at.Segment; SeekSource(at.Source); }
                else { _seg = _film.Segments.Count - 1; SeekSource(_film.Segments[^1].To); }   // past the film's end: it ends at once
                _startAt = 0d;
            }
            ApplyRate();
            DirectUpcoming(Rel);
            _cinema.Card(null);
            Phase = DirectorPhase.Playing;
            Report.Status = "playing";
            _clock.Start(_host.WallNow);
        }

        /// <summary>⭐ CE-4061 — the extension's item: its take (no camera, no layers, no sound — a verify run leaves the editor as it is),
        /// one step per frame from <see cref="RunStart.T0"/>.</summary>
        private void StartPlan(RunStart start)
        {
            _take = start.Take;
            Report.TickHz = start.Take.TickHz;
            _t0 = start.T0;
            _book = NewBook(start.Take, null);
            _roundsAtStart = _host.World is { } w0 ? Fdp.Toolkit.Combat.ShotLog.Peek(w0)?.Count ?? 0 : 0;
            Replan();
            _clock = new PacedClock(start.Take.TickHz, 1d);
            Phase = DirectorPhase.Playing;
            Report.Status = "playing";
            _clock.Start(_host.WallNow);
        }

        private Loaded<Take> LoadTake(PlayRequest request, ReelItem item)
        {
            if (request.Inline != null && request.Inline.TryGetValue(item.Scenario + "/" + item.Take, out var inline))
                return new Loaded<Take>(inline, Array.Empty<string>());
            // ⭐ CE-1303 CR-4 (§11m D4) — the OPEN take, edits and all (a film or a capture; ⛔ never a scout, which saves what it bakes)
            if (OpenTakeFor(request, item) is { } open) return new Loaded<Take>(open, Array.Empty<string>());
            return _host.Takes.Load(item.Scenario, item.Take);
        }

        private Take? OpenTakeFor(PlayRequest request, ReelItem item)
            => request.Mode is DirectorMode.Live or DirectorMode.Capture && request.Inline?.ContainsKey(item.Scenario + "/" + item.Take) != true
                ? OpenTake?.Invoke(item.Scenario, item.Take) : null;

        private bool BakeIsFresh(Take take)
            => take.Baked is { } baked && _request!.Mode != DirectorMode.Scout
               && !baked.IsStaleFor(_host.Takes.ScenarioHash(take.Scenario), "", take.TickHz);

        /// <summary>⭐ CE-1303 D4 — at a loop wrap: the open take again, if it was edited since this pass began (its anchors' book with it).</summary>
        private void RefreshOpenTake()
        {
            if (OpenTakeFor(_request!, Item) is not { } now || ReferenceEquals(now, _take)) return;
            bool anchorsMoved = !ReferenceEquals(now.Anchors, _take!.Anchors) || !ReferenceEquals(now.Baked, _take.Baked);
            _take = now;
            if (anchorsMoved) { _book = NewBook(now, BakeIsFresh(now) ? now.Baked!.Times : null); _book.ObserveEvents(_units); }
            Replan();
        }

        /// <summary>
        /// ⭐ CE-1303 D5 / D6 — the source to film second <paramref name="rel"/> of this item (a seekable host only), and the stepping rule's
        /// bookkeeping rebased on it: the steps "retired" are counted from the sim clock, which a seek moves backwards or far forwards.
        /// </summary>
        private void SeekSource(double rel)
        {
            _host.Seek(_t0 + rel);
            _issued = _retiredSeen = (long)Math.Round(Rel / _clock!.StepSeconds);
            _clock.Start(_host.WallNow);
            _nextFrameAt = Rel;   // ⭐ P2b — a capture's next frame is due HERE (a replay seeks back past it)
        }

        private void Replan()
        {
            Func<string, double?> anchors = a => _book!.TimeOf(a);
            _timeline = TakeTimeline.Plan(_take!, anchors, Item.From, Item.To);
            _keys = CameraKeys.Played(_take!, _timeline);
            // ⭐ CE-1303 P2b — CR-7: the film's segments play on a film or a capture; ⛔ a scout and an extension's run see the whole source
            _film = _request!.Mode is DirectorMode.Live or DirectorMode.Capture ? FilmTimeMap.Of(_take!, anchors) : FilmTimeMap.Identity;
            if (_seg >= _film.Segments.Count) _seg = _film.Segments.Count - 1;
            if (!_film.IsIdentity && _seg < 0 && Phase == DirectorPhase.Playing) _seg = 0;
            _subs = TakeSubtitles.Of(_take!, _timeline);   // ⭐ CR-11
            Report.Unresolved.Clear();
            Report.Unresolved.AddRange(_timeline.Unresolved);
            Report.Unresolved.AddRange(_film.Unresolved);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1303</c> P2b — CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11o S3 / S4): at the end of the segment on screen, the next one. A seekable source
        /// SEEKS to its start — a skip forward or an instant replay back; a live one cannot rewind (R-3034): a segment that would go back is skipped and said
        /// once, a gap forward is stepped through (the fast-forward of <see cref="TickPlaying"/>). Past the last segment the film is over.
        /// </summary>
        private void AdvanceSegments(ref double rel)
        {
            double step = _clock!.StepSeconds;   // a source within one step of a segment's start is AT it (a step can overshoot an end)
            while (Segment is { } seg && rel >= seg.To - 1e-9)
            {
                int next = _seg + 1;
                for (; next < _film.Segments.Count && !_host.CanSeek && _film.Segments[next].From < rel - step - 1e-9; next++)
                    if (_skippedBack.Add(_film.Segments[next].Number))
                        Report.Problems.Add($"segment {_film.Segments[next].Number} goes back to {_film.Segments[next].From:0.###} s: a live run cannot replay (R-3034) — skipped");
                if (next >= _film.Segments.Count) { _filmOver = true; return; }
                var n = _film.Segments[next];
                _seg = next;
                if (_host.CanSeek && Math.Abs(n.From - rel) > step + 1e-9) { SeekSource(n.From); rel = Rel; }   // a cut: forward over a gap, or back (a replay)
                ApplyRate();
            }
        }

        private void TickPlaying()
        {
            var mode = _request!.Mode;
            double rel = Rel;

            // ⭐ CE-1303 P2b — CR-7: the segment on screen ended ⇒ the next one (a seek, a skipped replay on live, or the film's end)
            if (!_film.IsIdentity)
            {
                AdvanceSegments(ref rel);
                if (_filmOver) { FinishItem(); return; }
            }

            // ⭐ CE-1303 D6 — loop the shot: past its end, back to its start (the take read again — D4); ⛔ a live run never rewinds (R-3034)
            //   ⭐ P2b (§11o S6) — within the segment on screen: a replay's loop stays in the replay
            if (LoopShot && mode == DirectorMode.Live && _shot != null && rel >= Math.Min(_shot.End, Segment?.To ?? double.PositiveInfinity) - 1e-6)
            {
                if (_host.CanSeek)
                {
                    int number = _shot.Number;
                    RefreshOpenTake();
                    var again = _timeline!.Shots.FirstOrDefault(s => s.Number == number) ?? _timeline.At(_shot.Start) ?? _shot;
                    SeekSource(Math.Max(again.Start, Segment?.From ?? double.NegativeInfinity));
                    rel = Rel;
                }
                else if (!_loopRefused)
                {
                    _loopRefused = true;
                    Report.Problems.Add("Loop shot needs a source that seeks (a recording): a live run plays on (R-3034)");
                }
            }
            // ⭐ CE-1303 D5 — a live run fast-forwards to the film second it plays from; ⭐ P2b (§11o S3) — and through a gap between two segments
            //   (it cannot seek): one step a frame, no hold, no capture; once over, real time from here
            bool inGap = Segment is { } playing && rel < playing.From - 1e-9;
            bool fastForward = (_startAt > 0d && FilmAt(rel) + 1e-9 < _startAt) || inGap;
            if (_startAt > 0d && !(FilmAt(rel) + 1e-9 < _startAt)) _startAt = 0d;
            if (!fastForward && (_fastForwarding || _startAt > 0d)) { _clock!.Start(_host.WallNow); _nextFrameAt = Math.Max(_nextFrameAt, rel); }
            _fastForwarding = fastForward;
            if (inGap != _gapCard) { _gapCard = inGap; if (!inGap) _cinema.Card(null); }
            if (inGap) _cinema.Card($"fast-forward +{Segment!.From - rel:0} s");   // TE-7's card (drawn in the cinema only)

            // ⭐ the master takes a step a frame after it is asked (one is in flight); with TWO outstanding it would take both at once
            long retired = (long)Math.Round(rel / _clock!.StepSeconds);
            Report.MaxRetiredInAFrame = Math.Max(Report.MaxRetiredInAFrame, (int)(retired - _retiredSeen));
            _retiredSeen = retired;
            bool masterBehind = _issued - retired >= 2;

            _book!.Observe(_host.World!, _units, rel);
            bool plan = mode == DirectorMode.Plan;
            if (plan) _ext!.OnFrame(rel);
            if (_book.Live.Count != _liveCount)
            {
                _liveCount = _book.Live.Count;
                if (_timeline!.Unresolved.Count > 0 || mode == DirectorMode.Scout) Replan();
            }

            if (Finished(rel)) { FinishItem(); return; }
            if (plan && _ext!.HoldsStep(rel, retired, _issued)) return;   // ⭐ CE-4061 — the extension holds the step (the H5 review takes over)

            var shot = _timeline!.At(rel);
            if (!ReferenceEquals(shot, _shot) || (shot == null) != _inGap) EnterShot(shot);
            DirectUpcoming(rel);
            // ⭐ CE-1303 P2b — CR-11 (§11o S9): the subtitle of this source second, to the cinema every frame (its text is also what a capture burns in)
            _subNow = TakeSubtitles.At(_subs, a => _book!.TimeOf(a), rel);
            _cinema.Subtitle(_subNow);
            if (!_film.IsIdentity) FilmStudio.Fade = Math.Max(FilmStudio.Fade, _film.GapFade(FilmAt(rel)));   // ⭐ CR-7 S3 — a dip gap's black

            if (_paused) return;
            if (masterBehind) { Report.WaitFrames++; return; }

            // a film frame is paced in a shot — ⭐ P2b: and anywhere inside a segment of a take that has them
            bool paced = !fastForward && (shot != null || Segment != null);
            if (fastForward) { }   // ⭐ CE-1303 D5 — no hold, no capture: one step per frame, as fast as frames come
            else if (mode == DirectorMode.Capture)
            {
                if (_pendingCapture is { IsCompleted: false }) { Report.LateFrames++; return; }   // the map has not served it yet: hold
                if (_pendingCapture is { IsFaulted: true } faulted) throw new InvalidOperationException("capture failed: " + faulted.Exception!.GetBaseException().Message);
                if (shot != null && rel >= _nextFrameAt - 1e-6) Capture(shot, rel);
            }
            else if (mode == DirectorMode.Live && paced)
            {
                _host.Hold(_clock!.HoldFor(_host.WallNow));
            }
            if (plan) _ext!.BeforeStep(rel);   // ⭐ CE-4061 — e.g. R1's checkpoint, an action due on this exact step
            StepOnce(paced);
            // ⭐ CE-1303 P2a — a seekable source steps at once; one that did not move has ENDED (a take longer than its recording): the item ends
            //   there, said in the report — ⛔ not a run that waits forever on a clock that cannot advance
            if (_host.CanSeek && Rel <= rel + 1e-9)
            {
                Report.Problems.Add($"the source ends at {rel:0.###} s, before the take does ({_timeline!.End:0.###} s)");
                FinishItem();
            }
        }

        /// <summary>The ONE place a step is taken. ⛔ A second in the same frame is a defect of the director, not of the run.</summary>
        private void StepOnce(bool paced)
        {
            if (++_stepsThisTick > 1) throw new DirectorSteppingException($"two steps in one frame (frame {Frames})");
            _issued++;
            _host.StepBy(_clock!.StepSeconds);
            if (paced && _request!.Mode == DirectorMode.Live) _clock.Stepped(_host.WallNow);
            Report.Steps++;
            Report.MaxStepsInAFrame = Math.Max(Report.MaxStepsInAFrame, _stepsThisTick);
        }

        /// <summary>How long a take waits, past its last placed shot, for a beat it has not seen (then the beat is reported missing).</summary>
        public const double UnseenGraceSeconds = 30d;

        private bool Finished(double rel)
        {
            double cap = _take!.EndAt is { IsFixed: true } end ? end.Offset : CapSeconds;
            if (_request!.Mode == DirectorMode.Plan) return rel >= cap - 1e-9;
            if (rel >= cap) return true;
            if (_request!.Mode == DirectorMode.Scout) return !_book!.Unseen.Any() && rel >= _timeline!.End;
            if (!_film.IsIdentity) return _filmOver;   // ⭐ CE-1303 P2b — CR-7: the segments ARE the film (it ends after the last one, or at the cap)
            if (_timeline!.Unresolved.Count > 0) return rel >= _timeline.End + UnseenGraceSeconds;
            // ⭐ CE-1304 CR-5 (§11s D4) — a live index publishes at most every quarter second: an event anchor it has not shown yet is not judged missing
            //   until it has caught up with the run (or IndexGraceSeconds past the end)
            return rel >= _timeline.End - 1e-6 && !(rel < _timeline.End + IndexGraceSeconds && _book!.AwaitsIndex(rel));
        }

        /// <summary>⭐ CR-5 — how long past its end a live take plays on while the live index catches up with an event anchor not seen yet.</summary>
        public const double IndexGraceSeconds = 1d;

        /// <summary>
        /// ⭐ <c>CE-1376</c> — the rounds fired since the take began: counted on the run's INDEX (<see cref="AnchorBook.RoundsUntil"/>), which a recording has and the
        /// live run publishes, so both report the same figure. ⚠ A LIVE index lags the run by up to a quarter second of wall time; until it covers the cut the
        /// world's own <c>ShotLog</c> (exact, but a side store a recording never carries) gives the figure — the one place the director still names it.
        /// </summary>
        private long RoundsFired(Fdp.Core.EntityRepository world)
            => _book?.RoundsUntil(Rel) ?? (Fdp.Toolkit.Combat.ShotLog.Peek(world)?.Count ?? 0) - _roundsAtStart;

        private void EnterShot(TimelineShot? shot)
        {
            _shot = shot;
            _inGap = shot == null;
            var take = _take!;
            if (shot == null) return;   // ⭐ CE-1303 P2b — the caption is the subtitle of each frame now (TakeSubtitles: a gap has none)
            var s = shot.Shot;
            if (_host.World is { } world)
                Report.Cuts.Add(CutSnapshot.Of(world, shot.Number, Rel, RoundsFired(world)));
            var on = new List<string>(take.Layers?.On ?? Array.Empty<string>());
            var off = new List<string>(take.Layers?.Off ?? Array.Empty<string>());
            if (s.Layers != null) { on.AddRange(s.Layers.On); off.AddRange(s.Layers.Off); on.RemoveAll(s.Layers.Off.Contains); }
            _layers.Apply(on, off);
            _sound?.SetTakeSound(s.Sound ?? take.Sound);
            ApplyRate();   // ⭐ P2b — request × shot × segment
            _nextFrameAt = Math.Max(shot.Start, Rel);
        }

        /// <summary>
        /// Holds the camera of this moment — in a gap, the NEXT shot at its start, so the camera is there when it begins. ⭐ <c>T4</c> (TE-2, TE-6):
        /// the camera is what <see cref="TakeTimeline.ProgramAt"/> says over the take's KEYS (<see cref="CameraKeys.Played"/> — its own, else its shots
        /// read as keys where the layout put them), and the dip's black is the same frame's fade; the shots here only gate WHEN the film runs (captions, layers, speed).
        /// </summary>
        private void DirectUpcoming(double rel)
        {
            var inShot = _timeline!.At(rel);
            var target = inShot ?? _timeline.Next(rel);
            if (target == null) return;
            Func<string, double?> anchors = a => _book!.TimeOf(a);
            double at = inShot != null ? rel : target.Start;
            // ⭐ CE-4059 / CR-7 (§11q K1) — the keys of the segment on screen: the unscoped ones, and its own from its first on (none playing: unscoped only)
            var keys = CameraKeys.OnScreenIn(_keys, Segment?.Id, anchors);
            var frame = TakeTimeline.ProgramAt(keys, anchors, at);
            if (frame != null && ShotEvaluator.Evaluate(frame, _units) is { } fs) FilmStudio.Direct(fs);
            FilmStudio.Fade = (at == rel ? frame : TakeTimeline.ProgramAt(keys, anchors, rel))?.Fade ?? 0f;   // the black is of NOW, the camera may be the next shot's
        }

        private void Capture(TimelineShot shot, double rel)
        {
            var c = _request!.Capture!;
            var path = Path.Combine(FramesDir, $"{_frameNo:D6}.png");
            var subtitle = c.BurnCaptions ? _subNow : null;   // ⭐ CE-1303 P2b / CE-1370 — the subtitle on screen, with its position and fade
            _pendingCapture = _sink!.Frame(path, c.Width, c.Height, c.Supersample, subtitle);
            _manifest.Add((_frameNo, _item, shot.Number, rel));
            _frameNo++;
            Report.Frames++;
            double speed = SourcePerFilmSecond;   // ⭐ P2b — the shot's speed × the segment's
            double perFrame = speed / c.Fps;
            _nextFrameAt += perFrame;
            // ⭐ slow motion past one frame per step: the same picture again (a step cannot be split)
            while (_nextFrameAt <= rel + _clock!.StepSeconds - 1e-6 && speed * _take!.TickHz < c.Fps)
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
            // ⭐ CE-4061 — the extension judges its item; it may take the end over (its own phases, then EndItem)
            if (_request!.Mode == DirectorMode.Plan && r.Status == "playing" && _ext!.Finish(r)) return;
            EndItem();
        }

        /// <summary>The item is over (judged): release what it held, then the next item, the movie, or the end.</summary>
        private void EndItem()
        {
            var r = Report;
            if (r.Status == "playing") r.Status = r.Missing.Count > 0 ? "beat-missing" : "ok";
            if (_request!.Mode == DirectorMode.Scout && _request.Bake && _take != null && _request.Inline?.ContainsKey(Item.Scenario + "/" + Item.Take) != true) Bake(r);

            FilmStudio.Release();
            _layers.Clear();
            _cinema.Caption(null);
            if (_host.InPreview && !LiveRun) _host.ExitPreview();   // ⚠ a live run may read as "in preview" (CE-4054) — it is stopped, not left

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
            }.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented));
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
                ["phase"] = Phase == DirectorPhase.Extension ? _ext!.PhaseName : Phase.ToString(),
                ["mode"] = _request?.Mode == DirectorMode.Plan ? _ext?.ModeName : _request?.Mode.ToString(),
                ["reel"] = _request?.Reel.Name,
                ["item"] = _request == null ? null : _item,
                ["items"] = _request?.Reel.Items.Count,
                ["paused"] = _paused,
                ["speed"] = _request?.Speed,
                ["loopShot"] = LoopShot,   // ⭐ CE-1303 D6
                ["shot"] = _shot?.Number,
                ["simTime"] = Phase == DirectorPhase.Playing ? Math.Round(Rel, 3) : null,
                // ⭐ CE-1303 P2b — CR-7 / CR-11: the film second, the segment (1-based) and the rate; the subtitle on screen
                ["filmTime"] = Phase == DirectorPhase.Playing ? Math.Round(FilmAt(Rel), 3) : null,
                ["segment"] = Segment?.Number,
                ["rate"] = Phase == DirectorPhase.Playing ? Math.Round(Rate, 3) : null,
                ["subtitle"] = _subNow?.Text,
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
