using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>One still asked for: its film second, the source second it shows (through the take's segments), the segment (−1: the take has none) and the PNG's name.</summary>
    public sealed record StillPlan(int Number, double FilmTime, double SourceTime, int Segment, string Name);

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (docs/DESIGN_Director_Timeline_Editor.md §11u D3) — the stills ASKED for, before the take is read: <c>POST /director/stills</c>'s body and a
    /// render-queue job of kind <c>stills</c> are this one record, so both plan through <see cref="DirectorStills.Prepare"/> (one place resolves the scenario,
    /// the take, the plan and the folder). <see cref="Scenario"/> null = the one the open recording names; <see cref="Dir"/> null = beside the take.
    /// </summary>
    public sealed record StillsRequest(string? Scenario, string Take = "main", IReadOnlyList<double>? Times = null, bool EveryKey = false,
                                       int Width = 1280, int Height = 720, string? Dir = null, bool Captions = true);

    /// <summary>A <see cref="StillsRequest"/> resolved against the host: what to play, the plan, the folder, the planning problems (not fatal).</summary>
    public sealed record StillsPrepared(string Scenario, string Take, PlayRequest Play, IReadOnlyList<StillPlan> Plan, string Dir, int Width, int Height,
                                        bool Captions, IReadOnlyList<string> Problems);

    /// <summary>
    /// ⭐ <c>CE-4059</c> P3 (docs/DESIGN_Director_Timeline_Editor.md §11r, CR-10, CR-12; Film Production §4a) — the STILLS of a take: for each film second a
    /// PNG of the program's camera there, and one CONTACT SHEET (a grid of them, each labelled with its film and source second). ⭐ A still is ONE seek and ONE
    /// frame: the job asks the pack's runner to <see cref="DirectorRunner.PlayFrom"/> the film second (a recording SEEKS there), pauses on its first playing
    /// frame (the camera of that second is held, the source does not step), lets the camera settle, and captures through the pack's frame sink — the
    /// capture path <c>POST /film/capture</c> and a film use (<see cref="FilmStudio.Capture"/>). ⛔ No camera logic of its own, no second capture path.
    /// <list type="bullet">
    /// <item>⛔ A host that cannot SEEK (the editor's live run) is refused: each still would reload and re-run the scenario from its start (CR-12 — rendering
    /// is the Replay Browser's).</item>
    /// <item>⛔ A headless host is refused: no map is drawn, so there is no frame to capture (the frame sink is null).</item>
    /// </list>
    /// Main thread only: <see cref="Tick"/> is called by <see cref="DirectorPack.Tick"/>, after the runner's.
    /// </summary>
    public sealed class DirectorStills
    {
        /// <summary>Frames the camera is held on the paused second before the capture (the shot is set on the first, placed on the next).</summary>
        public const int SettleFrames = 3;
        /// <summary>Frames a still may wait for the runner to reach Playing, and for the map to serve its capture.</summary>
        public const int WaitFrames = 600;
        public const int MaxStills = 64;
        public const string NoRenderer = "stills need a renderer: this host is headless (no map is drawn, so there is no frame to capture) — run the Replay Browser with a window (on a server: Xvfb, DISPLAY=:99)";
        /// <summary>⭐ <c>CE-1305</c> — an anchor not found while the open recording's index is still being read (ask again; a render job waits for it).</summary>
        public const string IndexStillReading = "the recording's timeline index is still being read — an anchor may not be found yet (ask again in a moment)";
        public const string CannotSeek = "stills need a source that seeks (a recording): this host plays a live run, where each still would reload and re-run the scenario — open the recording in the Replay Browser (POST /director/stills {recording})";

        private readonly DirectorRunner _runner;
        private readonly IFrameSink? _sink;
        private readonly Func<bool> _canSeek;

        private enum Step { Idle, Begin, Wait, Settle, Capture, Done, Failed }
        private Step _step = Step.Idle;
        private PlayRequest? _play;
        private IReadOnlyList<StillPlan> _plan = Array.Empty<StillPlan>();
        private string _dir = "";
        private int _width, _height;
        private bool _captions;
        private int _i, _frames;
        private Task? _capture;
        private string? _path;
        private double _shownSim;
        private readonly List<JsonObject> _done = new();
        private readonly List<string> _problems = new();
        private string? _sheet;

        public DirectorStills(DirectorRunner runner, IFrameSink? sink, Func<bool> canSeek)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _sink = sink;
            _canSeek = canSeek ?? throw new ArgumentNullException(nameof(canSeek));
        }

        /// <summary>A stills job is running (it holds the runner).</summary>
        public bool Busy => _step is Step.Begin or Step.Wait or Step.Settle or Step.Capture;

        /// <summary>Null when the host can take stills, else why not (<see cref="CannotSeek"/> before <see cref="NoRenderer"/>).</summary>
        public string? Refusal => !_canSeek() ? CannotSeek : _sink == null ? NoRenderer : null;

        /// <summary>
        /// The stills of <paramref name="take"/>: at <paramref name="filmTimes"/>; else, with <paramref name="everyKey"/>, at each camera key once its camera is on
        /// screen (its cut, or the end of its fly / dip-in); else at the keys that name a <see cref="CameraKey.Still"/> (the documentation take's, CE-4060).
        /// Each film second is mapped to the source second it shows through the take's segments (<see cref="FilmTimeMap"/>) and its anchors' times:
        /// <paramref name="anchors"/> — ⭐ <c>CE-1305</c> TE-10 (§11v S1) the run's own (<see cref="DirectorRunner.AnchorTimesOf"/>, what <see cref="Prepare"/> passes) —
        /// else the take's baked ones.
        /// </summary>
        public static (IReadOnlyList<StillPlan> Plan, IReadOnlyList<string> Problems) PlanOf(Take take, IReadOnlyList<double>? filmTimes, bool everyKey,
                                                                                             Func<string, double?>? anchors = null, Func<string, string?>? whyUnseen = null)
        {
            if (take == null) throw new ArgumentNullException(nameof(take));
            var problems = new List<string>();
            var times = take.Baked?.Times;
            anchors ??= n => times != null && times.TryGetValue(n, out var t) ? t : null;
            var film = FilmTimeMap.Of(take, anchors);
            // ⭐ CE-4119 part 1 — a segment on an anchor this run does not show makes FilmTimeMap fall back to a 1:1 map: say so, for keys AND for times:[…]
            foreach (var u in film.Unresolved) problems.Add($"{u} — this run does not show it, so film time is mapped 1:1 (segment null), not through the take's segments");
            // ⭐ CE-4125 — and for each anchor the take hangs something on that has no time here, WHY (no such unit · no such event · never met · awaits play)
            if (whyUnseen != null)
                foreach (var name in Referenced(take))
                    if (anchors(name) is null && whyUnseen(name) is { } why) problems.Add($"anchor '{name}': {why}");
            var wanted = new List<(double Film, string? Name)>();
            if (filmTimes is { Count: > 0 })
                foreach (var t in filmTimes)
                {
                    if (!(t >= 0) || double.IsInfinity(t)) { problems.Add($"film time {t} is not a second ≥ 0"); continue; }
                    wanted.Add((t, null));
                }
            else
            {
                var keys = CameraKeys.Of(take);
                int n = 0;
                foreach (var k in keys)
                {
                    n++;
                    if (!everyKey && k.Still == null) continue;
                    if (k.At.Resolve(anchors) is not { } at) { problems.Add($"key {n}: its time '{k.At}' names an anchor this run does not show (nor a bake) — scout the take, or ask for times"); continue; }
                    double settle = k.In.Kind switch { KeyTransitionKind.Fly => k.In.Seconds, KeyTransitionKind.Dip => k.In.InSeconds, _ => 0d };
                    int from = k.Segment is { } id ? Math.Max(0, IndexOfSegment(film, id)) : 0;
                    wanted.Add((film.FilmOf(at + settle, from), k.Still));
                }
                if (wanted.Count == 0 && problems.Count == 0)
                    problems.Add(everyKey ? "the take has no camera keys" : "the take has no still keys — name film times (times:[…]) or ask for every key (everyKey:true)");
            }
            var plan = new List<StillPlan>();
            foreach (var (t, name) in wanted.OrderBy(w => w.Film))
            {
                if (plan.Count > 0 && Math.Abs(plan[^1].FilmTime - t) < 1e-6) continue;   // two keys on one second: one still
                if (plan.Count == MaxStills) { problems.Add($"more than {MaxStills} stills asked: the first {MaxStills} are taken"); break; }
                if (film.At(t) is not { } at) { problems.Add($"film {t:0.###} s is past the film's end ({film.FilmEnd:0.###} s)"); continue; }
                int number = plan.Count + 1;
                plan.Add(new StillPlan(number, t, at.Source, at.Segment, name ?? $"{number:D2}-film-{t:0.00}s".Replace(',', '.')));
            }
            return (plan, problems);
        }

        /// <summary>
        /// ⭐ <c>CE-1306</c> (§11u D3) — <paramref name="request"/> resolved: its scenario (else <paramref name="openScenario"/>'s), the take the runner would play
        /// (the open one first — <see cref="DirectorRunner.TakeOf"/>), the plan (<see cref="PlanOf"/>) and the folder (default <c>takes/&lt;take&gt;.stills/</c> beside the
        /// take). Null with an HTTP-shaped refusal: 400 nothing named / no stills, 404 no such take. Moved from the route so a render-queue job plans the same way.
        /// </summary>
        public static (StillsPrepared? Prepared, int Code, string? Error) Prepare(StillsRequest request, DirectorRunner runner, TakeStore store, Func<string?>? openScenario)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var scenario = string.IsNullOrWhiteSpace(request.Scenario) ? openScenario?.Invoke() : request.Scenario;
            if (string.IsNullOrWhiteSpace(scenario)) return (null, 400, "name the scenario (or open a recording that names one: {recording})");
            var takeName = string.IsNullOrWhiteSpace(request.Take) ? "main" : request.Take;
            var play = new PlayRequest(Reel.Of(scenario!, takeName), Cinema: false);
            var loaded = runner.TakeOf(play);
            if (loaded.Value is not { } take) return (null, 404, string.Join("; ", loaded.Problems));
            // ⭐ CE-1305 TE-10 (§11v S1) — the keys at the times THIS run places them (the one resolver), not only the take's bake: a verify run's recording
            var anchors = runner.AnchorsOf(take);
            var (plan, problems) = PlanOf(take, request.Times, request.EveryKey, anchors.TimeOf, anchors.WhyUnseen);
            if (runner.Index?.Invoke() is { IsComplete: false } && problems.Count > 0)
                problems = problems.Append(IndexStillReading).ToList();
            if (plan.Count == 0) return (null, 400, "no stills: " + string.Join("; ", problems));
            var dir = string.IsNullOrWhiteSpace(request.Dir)
                ? Path.Combine(Path.GetDirectoryName(store.TakePath(scenario!, takeName))!, takeName + ".stills")
                : request.Dir!;
            return (new StillsPrepared(scenario!, takeName, play, plan, Path.GetFullPath(dir), request.Width, request.Height, request.Captions, problems), 200, null);
        }

        /// <summary>Starts a prepared job (<see cref="Start(PlayRequest, IReadOnlyList{StillPlan}, string, int, int, bool)"/>).</summary>
        public IReadOnlyList<string> Start(StillsPrepared p)
            => Start((p ?? throw new ArgumentNullException(nameof(p))).Play, p.Plan, p.Dir, p.Width, p.Height, p.Captions);

        /// <summary>⭐ <c>CE-1306</c> (§11u D6) — stops a running job (the render queue's cancel): the runner stopped, the job <c>failed</c> with "cancelled".
        /// False when none runs.</summary>
        public bool Cancel()
        {
            if (!Busy) return false;
            Fail(CancelledProblem);
            return true;
        }

        /// <summary>The problem a cancelled job carries.</summary>
        public const string CancelledProblem = "cancelled";

        /// <summary>The anchors <paramref name="take"/> places something on (its keys, shots, segments and end), in first-use order.</summary>
        private static IEnumerable<string> Referenced(Take take)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<AtExpr?> All()
            {
                foreach (var k in take.Keys) yield return k.At;
                foreach (var sh in take.Shots) yield return sh.At;
                foreach (var g in take.Segments) { yield return g.From; yield return g.To; }
                yield return take.EndAt;
            }
            foreach (var at in All())
                if (at?.Anchor is { } a && take.Anchors.ContainsKey(a) && seen.Add(a)) yield return a;
        }

                private static int IndexOfSegment(FilmTimeMap film, string id)
        {
            for (int i = 0; i < film.Segments.Count; i++) if (film.Segments[i].Id == id) return i;
            return -1;
        }

        /// <summary>
        /// Starts the job: the stills of <paramref name="play"/>'s first take at <paramref name="plan"/>, written into <paramref name="dir"/> at
        /// <paramref name="width"/>×<paramref name="height"/>, then <c>contact-sheet.png</c> beside them. Returns why it did not start (empty: started).
        /// </summary>
        public IReadOnlyList<string> Start(PlayRequest play, IReadOnlyList<StillPlan> plan, string dir, int width, int height, bool captions = true)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            if (Refusal is { } why) return new[] { why };
            if (Busy) return new[] { "a stills job is running (GET /director/stills)" };
            if (_runner.Busy) return new[] { "the director is playing: stop it first (POST /director/stop)" };
            if (plan == null || plan.Count == 0) return new[] { "no stills to take" };
            if (width < 16 || height < 16 || width > 4096 || height > 4096) return new[] { $"a still of {width}×{height}: each side 16…4096 px" };
            _play = play with { Capture = null, Scout = false, Plan = null, Loop = false, Cinema = false, Speed = 1000d };
            _plan = plan;
            _dir = Path.GetFullPath(dir);
            _width = width; _height = height; _captions = captions;
            _i = 0; _frames = 0; _capture = null; _path = null; _sheet = null;
            _done.Clear(); _problems.Clear();
            Directory.CreateDirectory(_dir);
            _step = Step.Begin;
            return Array.Empty<string>();
        }

        /// <summary>One frame of the job (after the runner's tick).</summary>
        public void Tick()
        {
            switch (_step)
            {
                case Step.Begin:
                {
                    var refused = _runner.PlayFrom(_play!, _plan[_i].FilmTime);
                    if (refused.Count > 0) { Fail("not started: " + string.Join("; ", refused)); return; }
                    // ⭐ CE-1306 (§11u, found by the render queue's rail) — a still is a FILM frame: its 2-D ⇄ 3-D switch CUTS, as a capture's does. The
                    //   runner animates it for a play without a capture (a person watching); after a 2-D stretch (a movie that ended in the person's 2-D
                    //   map, CE-1380 V7) every still but the first landed mid-animation and the capture refused ("the map is switching")
                    FilmStudio.AnimateViewSwitch = false;
                    _frames = 0;
                    _step = Step.Wait;
                    return;
                }
                case Step.Wait:
                    if (_runner.Phase == DirectorPhase.Playing)
                    {
                        _runner.Pause();   // ⭐ held on the second: the camera of it is directed every frame, the source does not step
                        _frames = 0;
                        _step = Step.Settle;
                    }
                    else if (!_runner.Busy)
                    {
                        _problems.Add($"still {_plan[_i].Number} (film {_plan[_i].FilmTime:0.###} s): the director {_runner.Phase} before it played — " +
                                      string.Join("; ", _runner.Reports.SelectMany(r => r.Problems)));
                        Next();
                    }
                    else if (++_frames > WaitFrames) Fail($"still {_plan[_i].Number}: the director did not reach Playing in {WaitFrames} frames ({_runner.Phase})");
                    return;
                case Step.Settle:
                    if (++_frames < SettleFrames) return;
                    _path = Path.Combine(_dir, _plan[_i].Name + ".png");
                    _shownSim = _runner.Status()["simTime"]?.GetValue<double>() ?? double.NaN;
                    _capture = _sink!.Frame(_path, _width, _height, 1, _runner.OverlayNow(_captions));   // ⭐ CE-1305 — and the film graphics, as a movie frame
                    _frames = 0;
                    _step = Step.Capture;
                    return;
                case Step.Capture:
                    if (_capture is { IsCompleted: true } c)
                    {
                        var s = _plan[_i];
                        if (c.IsFaulted) _problems.Add($"still {s.Number}: the capture failed — {c.Exception!.GetBaseException().Message}");
                        else
                            _done.Add(new JsonObject
                            {
                                ["number"] = s.Number, ["name"] = s.Name, ["filmTime"] = Math.Round(s.FilmTime, 3), ["sourceTime"] = Math.Round(s.SourceTime, 3),
                                ["segment"] = s.Segment < 0 ? null : s.Segment + 1, ["shownSim"] = double.IsNaN(_shownSim) ? null : Math.Round(_shownSim, 3),
                                ["path"] = _path, ["width"] = _width, ["height"] = _height,
                            });
                        Next();
                    }
                    else if (++_frames > WaitFrames) Fail($"still {_plan[_i].Number}: the map did not serve the capture in {WaitFrames} frames (is the map drawn, in 3-D?)");
                    return;
            }
        }

        private void Next()
        {
            _runner.Stop();
            if (++_i < _plan.Count) { _step = Step.Begin; return; }
            if (_done.Count > 0)
            {
                try
                {
                    var labels = _done.Select(d => $"#{d["number"]}  film {d["filmTime"]} s  src {d["sourceTime"]} s").ToList();
                    _sheet = ContactSheet.Compose(_done.Select(d => d["path"]!.GetValue<string>()).ToList(), labels, Path.Combine(_dir, ContactSheet.FileName));
                }
                catch (Exception ex) { _problems.Add("the contact sheet failed: " + ex.Message); }
            }
            _step = _done.Count == _plan.Count ? Step.Done : Step.Failed;
        }

        private void Fail(string why)
        {
            _problems.Add(why);
            if (_runner.Busy) _runner.Stop();
            _step = Step.Failed;
        }

        /// <summary>The job: <c>{state, scenario, take, dir, planned, stills[], sheet, problems[]}</c> (state idle · running · done · failed).</summary>
        public JsonObject Status() => new()
        {
            ["state"] = _step switch { Step.Idle => "idle", Step.Done => "done", Step.Failed => "failed", _ => "running" },
            ["scenario"] = _play?.Reel.Items.FirstOrDefault()?.Scenario,
            ["take"] = _play?.Reel.Items.FirstOrDefault()?.Take,
            ["dir"] = _step == Step.Idle ? null : _dir,
            ["planned"] = PlanJson(_plan),
            ["stills"] = new JsonArray(_done.Select(d => (JsonNode?)d.DeepClone()).ToArray()),
            ["sheet"] = _sheet,
            ["sheetGrid"] = _sheet == null ? null : ContactSheet.Layout(_done.Count, _width, _height).ToJson(),
            ["problems"] = new JsonArray(_problems.Select(p => (JsonNode?)p).ToArray()),
        };

        /// <summary>A plan as JSON: <c>[{number, filmTime, sourceTime, segment, name}]</c>.</summary>
        public static JsonArray PlanJson(IReadOnlyList<StillPlan> plan) => new(plan.Select(s => (JsonNode?)new JsonObject
        {
            ["number"] = s.Number, ["filmTime"] = Math.Round(s.FilmTime, 3), ["sourceTime"] = Math.Round(s.SourceTime, 3),
            ["segment"] = s.Segment < 0 ? null : s.Segment + 1, ["name"] = s.Name,
        }).ToArray());
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> P3 (§11r) — the contact sheet: the stills of a take in one PNG, a grid of thumbnails in time order, each with its label under it
    /// (<c>#n film t s src s s</c>). <see cref="Layout"/> is pure; <see cref="Compose"/> draws with raylib's CPU image calls (the label's font is raylib's
    /// default, loaded with the window — a headless host never gets here: it takes no stills).
    /// </summary>
    public static class ContactSheet
    {
        public const string FileName = "contact-sheet.png";
        public const int MaxColumns = 4, CellWidth = 320, Gap = 6, LabelHeight = 22;

        public sealed record Grid(int Columns, int Rows, int CellWidth, int CellHeight, int Width, int Height)
        {
            public JsonObject ToJson() => new()
            {
                ["columns"] = Columns, ["rows"] = Rows, ["cellWidth"] = CellWidth, ["cellHeight"] = CellHeight, ["width"] = Width, ["height"] = Height,
            };

            /// <summary>The thumbnail of still <paramref name="i"/> (0-based): its top-left pixel; the label sits under it.</summary>
            public (int X, int Y) Cell(int i) => (Gap + (i % Columns) * (CellWidth + Gap), Gap + (i / Columns) * (CellHeight + LabelHeight + Gap));
        }

        /// <summary>The grid of <paramref name="count"/> stills of <paramref name="stillWidth"/>×<paramref name="stillHeight"/>: up to
        /// <see cref="MaxColumns"/> columns (the square root, rounded up), thumbnails <see cref="CellWidth"/> wide (never wider than the still) at its aspect.</summary>
        public static Grid Layout(int count, int stillWidth, int stillHeight)
        {
            if (count <= 0) return new Grid(0, 0, 0, 0, 0, 0);
            int cols = Math.Min(MaxColumns, (int)Math.Ceiling(Math.Sqrt(count)));
            int rows = (count + cols - 1) / cols;
            int cw = Math.Min(CellWidth, stillWidth);
            int ch = Math.Max(1, (int)Math.Round(cw * (double)stillHeight / stillWidth));
            return new Grid(cols, rows, cw, ch, Gap + cols * (cw + Gap), Gap + rows * (ch + LabelHeight + Gap));
        }

        /// <summary>Writes the sheet of <paramref name="paths"/> (all one size) with <paramref name="labels"/> to <paramref name="outPath"/>; returns its full path.</summary>
        public static string Compose(IReadOnlyList<string> paths, IReadOnlyList<string> labels, string outPath)
        {
            if (paths.Count == 0) throw new ArgumentException("no stills", nameof(paths));
            var first = Raylib_cs.Raylib.LoadImage(paths[0]);
            int sw = first.Width, sh = first.Height;
            Raylib_cs.Raylib.UnloadImage(first);
            if (sw <= 0 || sh <= 0) throw new IOException($"could not read {paths[0]}");
            var grid = Layout(paths.Count, sw, sh);
            var sheet = Raylib_cs.Raylib.GenImageColor(grid.Width, grid.Height, new Raylib_cs.Color(24, 24, 28, 255));
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    var still = Raylib_cs.Raylib.LoadImage(paths[i]);
                    try
                    {
                        if (still.Width <= 0) throw new IOException($"could not read {paths[i]}");
                        Raylib_cs.Raylib.ImageResize(ref still, grid.CellWidth, grid.CellHeight);
                        var (x, y) = grid.Cell(i);
                        Raylib_cs.Raylib.ImageDraw(ref sheet, still, new Raylib_cs.Rectangle(0, 0, grid.CellWidth, grid.CellHeight),
                                                   new Raylib_cs.Rectangle(x, y, grid.CellWidth, grid.CellHeight), Raylib_cs.Color.White);
                        if (i < labels.Count) Raylib_cs.Raylib.ImageDrawText(ref sheet, labels[i], x + 2, y + grid.CellHeight + 4, 14, Raylib_cs.Color.RayWhite);
                    }
                    finally { Raylib_cs.Raylib.UnloadImage(still); }
                }
                var full = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                if (!Raylib_cs.Raylib.ExportImage(sheet, full)) throw new IOException($"could not write {full}");
                return full;
            }
            finally { Raylib_cs.Raylib.UnloadImage(sheet); }
        }
    }
}
