using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> B5 / B10 / B11 (docs/DESIGN_Film_Director.md §4, §4b, §4c) — the film director over HTTP (and MCP, through the route
    /// docs): list and author takes, play a reel live or to film, scout it; the map's layers as the user's click sets them; the Sound switch.
    /// Every handler runs on the main thread, as every world-touching route does, and calls the SAME runner methods the Director panel calls.
    /// </summary>
    public sealed partial class DebugApiHost
    {
        private DirectorRunner? _director;
        private TakeStore? _takes;
        private Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?>? _layerControl;
        private DirectorDrafts? _drafts;
        private DirectorStills? _stills;
        private TakePromotion? _promotion;
        private DirectorRenders? _renders;
        /// <summary>⭐ CE-4042 — verifies run in a FRESH child process (<c>{fresh: true}</c>).</summary>
        private readonly FreshVerifyJobs _freshVerify = new();

        /// <summary>⭐ CE-4038 — hands the host the editor's director, its take store and the map's layer control (held by the editor).
        /// ⭐ CE-4059 (§11q) — and the pack's drafter (<c>/director/moments</c>, <c>/director/draft</c>; null: those answer 503); ⭐ §11r — and its
        /// stills (<c>/director/stills</c>; null: 503).</summary>
        public void AttachDirector(DirectorRunner director, TakeStore takes, Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> layerControl,
                                   DirectorDrafts? drafts = null, DirectorStills? stills = null, TakePromotion? promotion = null,
                                   DirectorRenders? renders = null)
        {
            _renders = renders;
            // ⭐ CE-1306 (§11u D4) — a render naming a recording opens it at ITS turn through the ONE opener: the call POST /replay makes
            if (renders != null) renders.OpenRecording = rec => OpenRecording(JsonValue.Create(rec)) is { } refused ? refused.Error : null;
            _drafts = drafts;
            _stills = stills;
            _promotion = promotion;
            _director = director ?? throw new ArgumentNullException(nameof(director));
            _takes = takes ?? throw new ArgumentNullException(nameof(takes));
            _layerControl = layerControl ?? throw new ArgumentNullException(nameof(layerControl));
        }

        /// <summary>⭐ <c>CE-1303</c> P3 — any host's director, from its <see cref="DirectorPack"/> (the editor's, the Replay Browser's): the same
        /// routes over the same runner, takes and layer control the host's Director window uses.</summary>
        public void AttachDirector(DirectorPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            AttachDirector(pack.Runner, pack.Takes, pack.LayerControl, pack.Drafts, pack.Stills, pack.Promotion, pack.Renders);   // ⭐ CE-4059 / CE-1304 / CE-1306 — drafter, stills, promote, renders: every host
        }

        private const string NoDirector = "This host has no film director (the editor and the Replay Browser have one).";

        /// <summary>⭐ CE-4059 (TE-15, §11r) — the ONE refusal of <c>recording=</c> on a host that does not open recordings (the editor), every route alike.</summary>
        internal const string RecordingRefused =
            "recording= opens a recording first: only the Replay Browser opens recordings (not this host) — here the director works on the scenario this host shows";

        /// <summary>
        /// ⭐ <c>CE-4059</c> P3 (TE-15, docs/DESIGN_Director_Timeline_Editor.md §11r R1) — <c>recording=</c> (a <c>.fdp</c> path, a folder of a recording's node
        /// files, or an exercise id): opened FIRST through the very call <c>POST /replay</c> makes (<see cref="DebugApiService.EnterReplay"/>: a path through the
        /// Replay Browser's control, an id through its master's transition) — ⛔ no second open path. Null: nothing named, or opened; else the refusal.
        /// </summary>
        private RouteResult? OpenRecording(JsonNode? recording)
        {
            if (recording is null) return null;
            var text = recording is JsonValue v && v.TryGetValue<string>(out var str) ? str : recording.ToJsonString();
            if (string.IsNullOrWhiteSpace(text)) return Fail(400, "recording names nothing: a .fdp path, a recording's folder or an exercise id (GET /exercises)");
            if (_service is not { OpensRecordingFiles: true } service) return Fail(400, RecordingRefused);
            JsonObject open;
            if (Guid.TryParse(text, out _)) open = new JsonObject { ["exerciseId"] = text };
            else if (System.IO.Directory.Exists(text))
            {
                var files = System.IO.Directory.GetFiles(text, "*.fdp").OrderBy(f => f, StringComparer.Ordinal).ToArray();
                if (files.Length == 0) return Fail(400, $"recording '{text}': the folder holds no .fdp file");
                open = new JsonObject { ["paths"] = new JsonArray(files.Select(f => (JsonNode?)f).ToArray()) };
            }
            else open = new JsonObject { ["path"] = text };
            var (_, error, hint) = service.EnterReplay(open);
            return error != null ? Fail(400, $"recording '{text}' did not open: {error}", hint) : null;
        }

        private void BuildDirectorRoutes()
        {
            _routes.Add(new("GET", "/director/takes", ctx => Main(() => TakesList(ctx.Query("scenario")))));
            _routes.Add(new("GET", "/director/reels", _ => Main(ReelsList)));
            _routes.Add(new("GET", "/director/take/{scenario}/{take}", ctx => Main(() => TakeRead(ctx.RouteValue("scenario")!, ctx.RouteValue("take")!))));
            _routes.Add(new("PUT", "/director/take/{scenario}/{take}", ctx => Main(() => TakeWrite(ctx.RouteValue("scenario")!, ctx.RouteValue("take")!, ctx.Body))));
            _routes.Add(new("POST", "/director/play", ctx => Main(() => OpenRecording(ctx.Body?["recording"]) ?? DirectorPlay(ctx.Body, scout: false))));
            _routes.Add(new("POST", "/director/scout", ctx => Main(() => DirectorPlay(ctx.Body, scout: true))));
            _routes.Add(new("POST", "/director/stop", _ => Main(() => Transport(d => d.Stop()))));
            _routes.Add(new("POST", "/director/next", _ => Main(() => Transport(d => d.Next()))));
            _routes.Add(new("POST", "/director/pause", _ => Main(() => Transport(d => d.Pause()))));
            _routes.Add(new("POST", "/director/resume", _ => Main(() => Transport(d => d.Resume()))));
            _routes.Add(new("GET", "/director/status", _ => Main(() => _director is { } d ? Ok(d.Status()) : Fail(503, NoDirector))));
            // ⭐ CE-4059 P3 (docs/DESIGN_Director_Timeline_Editor.md §11q) — the drafter: the moments of the shown index, and a draft opened in the take session
            _routes.Add(new("GET", "/director/moments", ctx => Main(() => OpenRecording(ctx.Query("recording") is { } rec ? JsonValue.Create(rec) : null) ?? DirectorMoments())));
            _routes.Add(new("POST", "/director/draft", ctx => Main(() => DirectorDraft(ctx.Body))));
            // ⭐ CE-4059 P3 (§11r) — a take's stills + its contact sheet (a host that seeks, with a renderer); by default answered when done
            _routes.Add(new("POST", "/director/stills", DirectorStillsRoute));
            _routes.Add(new("GET", "/director/stills", _ => Main(() => _stills is { } st ? Ok(st.Status()) : Fail(503, NoDirector))));
            // ⭐ CE-1306 R4 (§11u) — the render queue: renders run one after another in the background of this host
            _routes.Add(new("POST", "/director/renders", ctx => Main(() => RendersEnqueue(ctx.Body))));
            _routes.Add(new("GET", "/director/renders", _ => Main(() => _renders is { } q ? Ok(q.Status()) : Fail(503, NoDirector))));
            _routes.Add(new("DELETE", "/director/renders/{id}", ctx => Main(() => RendersCancel(ctx.RouteValue("id")!))));
            // ⭐ CE-1304 P4 (§11s) — promote: a recording's take copied into its scenario's own takes
            _routes.Add(new("POST", "/director/promote", ctx => Main(() => OpenRecording(ctx.Body?["recording"]) ?? DirectorPromote(ctx.Body))));
            // ⭐ CE-4042 (docs/DESIGN_Scenario_Regression.md) — the scenario regression check: author a scenario's checks, verify it
            _routes.Add(new("GET", "/director/checks/{scenario}", ctx => Main(() => ChecksRead(ctx.RouteValue("scenario")!))));
            _routes.Add(new("PUT", "/director/checks/{scenario}", ctx => Main(() => ChecksWrite(ctx.RouteValue("scenario")!, ctx.Body))));
            _routes.Add(new("POST", "/director/verify", ctx => Main(() => DirectorVerify(ctx.Body))));
            _routes.Add(new("GET", "/director/verify/jobs", _ => Task.FromResult(Ok(new JsonObject { ["jobs"] = _freshVerify.List() }))));
            _routes.Add(new("GET", "/director/verify/jobs/{job}", ctx => Task.FromResult(_freshVerify.Status(ctx.RouteValue("job")!) is { } j
                ? Ok(j) : Fail(404, $"no verify job '{ctx.RouteValue("job")}' — GET /director/verify/jobs lists them"))));
            _routes.Add(new("GET", "/map/layers", ctx => Main(() => LayersRead(ctx.Query("view")))));
            _routes.Add(new("POST", "/map/layers", ctx => Main(() => LayersWrite(ctx.Body))));
            _routes.Add(new("GET", "/audio", _ => Main(AudioRead)));
            _routes.Add(new("POST", "/audio", ctx => Main(() => AudioWrite(ctx.Body))));
        }

        private Task<RouteResult> Main(Func<RouteResult> work) => _jobQueue.RunOnMainThread(work);

        // ── takes and reels ───────────────────────────────────────────────────────────────────

        private RouteResult TakesList(string? scenario)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var arr = new JsonArray();
            var all = string.IsNullOrWhiteSpace(scenario) ? store.AllTakes() : new[] { (scenario!, store.TakesOf(scenario!)) };
            foreach (var (s, takes) in all)
                arr.Add(new JsonObject { ["scenario"] = s, ["takes"] = new JsonArray(takes.Select(t => (JsonNode?)t).ToArray()) });
            return Ok(new JsonObject { ["root"] = store.ScenariosRoot, ["count"] = all.Sum(x => x.Item2.Count), ["scenarios"] = arr });
        }

        private RouteResult ReelsList()
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var arr = new JsonArray();
            foreach (var name in store.ReelNames())
            {
                var reel = store.LoadReel(name);
                arr.Add(new JsonObject
                {
                    ["name"] = name, ["title"] = reel.Value?.Title, ["items"] = reel.Value?.Items.Count,
                    ["problems"] = new JsonArray(reel.Problems.Select(p => (JsonNode?)p).ToArray()),
                });
            }
            return Ok(new JsonObject { ["root"] = store.FilmsRoot, ["reels"] = arr });
        }

        private RouteResult TakeRead(string scenario, string take)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var loaded = store.Load(scenario, take);
            if (loaded.Value is null) return Fail(404, string.Join("; ", loaded.Problems));
            return Ok(new JsonObject
            {
                ["scenario"] = scenario, ["take"] = take, ["path"] = store.TakePath(scenario, take),
                ["json"] = TakeJson.WriteTake(loaded.Value),
                ["problems"] = new JsonArray(loaded.Problems.Select(p => (JsonNode?)p).ToArray()),
            });
        }

        private RouteResult TakeWrite(string scenario, string take, JsonNode? body)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            if (body is not JsonObject json) return Fail(400, "the body is the take (a JSON object: title, caption, anchors, shots, …)");
            var problems = new List<string>();
            var parsed = TakeJson.ParseTake(json, scenario, take, problems);
            if (problems.Count > 0) return Fail(400, "the take is not valid: " + string.Join("; ", problems));
            var path = store.Save(parsed);
            return Ok(new JsonObject { ["saved"] = path, ["shots"] = parsed.Shots.Count, ["anchors"] = parsed.Anchors.Count });
        }

        // ── ⭐ CE-4059 — the drafter ──────────────────────────────────────────────────────────────

        private RouteResult DirectorMoments()
        {
            if (_drafts is not { } drafts) return Fail(503, NoDirector);
            if (drafts.Index is null) return Fail(409, "no timeline index is shown yet (run the scenario: the live index fills as it plays)");
            var arr = new JsonArray();
            foreach (var m in drafts.Moments())
                arr.Add(new JsonObject
                {
                    ["lane"] = m.Lane, ["eventIndex"] = m.EventIndex, ["frame"] = m.Frame, ["sim"] = Math.Round(m.Sim, 3),
                    ["actor"] = m.Actor, ["actorName"] = drafts.NameOf(m.Actor),
                    ["subject"] = m.Subject, ["subjectName"] = m.Subject is { } s ? drafts.NameOf(s) : null, ["score"] = Math.Round(m.Score, 3),
                });
            // ⭐ §11r — a recording's index is read in the background after it opens: complete false = more moments may come (ask again)
            return Ok(new JsonObject { ["scenario"] = drafts.Scenario, ["complete"] = drafts.Index.IsComplete, ["count"] = arr.Count, ["moments"] = arr });
        }

        private RouteResult DirectorDraft(JsonNode? body)
        {
            if (_drafts is not { } drafts) return Fail(503, NoDirector);
            if (OpenRecording(body?["recording"]) is { } refused) return refused;   // ⭐ §11r — the recording opened first (the editor: refused)
            var lane = body?["lane"]?.ToString();
            if (string.IsNullOrWhiteSpace(lane) || body?["eventIndex"] is not JsonValue ev || !ev.TryGetValue<int>(out var eventIndex))
                return Fail(400, "name the event: {lane, eventIndex} (GET /director/moments lists them)");
            bool replay = body?["replay"] is JsonValue r && r.TryGetValue<bool>(out var rb) ? rb : true;
            if (drafts.Index is null) return Fail(409, "no timeline index is shown yet (run the scenario: the live index fills as it plays)");
            var (take, problems) = drafts.Draft(lane!, eventIndex, replay);
            if (take is null) return Fail(problems.Any(p => p.Contains("unsaved", StringComparison.Ordinal)) ? 409 : 400, string.Join("; ", problems));
            return Ok(new JsonObject
            {
                ["scenario"] = take.Scenario, ["take"] = take.Name, ["opened"] = true, ["saved"] = false,
                ["keys"] = take.Keys.Count, ["segments"] = take.Segments.Count,
                ["json"] = TakeJson.WriteTake(take),
            });
        }

        // ── ⭐ CE-1304 P4 (§11s) — promote ────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>POST /director/promote {take, scenario?, recording?, overwrite?}</c> — the take beside the open recording (or <c>recording=</c>, opened first) copied into
        /// its scenario's own takes. 404 no such take · 409 the scenario already has one of that name (unless <c>overwrite:true</c>) or it has unsaved edits ·
        /// 400 nothing to promote to (a host whose takes are the scenario's: the editor) or nothing named.
        /// </summary>
        private RouteResult DirectorPromote(JsonNode? body)
        {
            if (_promotion is not { } promotion) return Fail(503, NoDirector);
            var take = body?["take"]?.ToString();
            if (string.IsNullOrWhiteSpace(take)) return Fail(400, "name the take: {take, scenario?, recording?, overwrite?}");
            bool overwrite = body?["overwrite"] is JsonValue o && o.TryGetValue<bool>(out var ob) && ob;
            if (!promotion.Available)
                return Fail(400, "nothing to promote to: this host's takes are the scenario's own (promote copies a RECORDING's take: the Replay Browser, with the recording open or recording=)");
            var r = promotion.Promote(body?["scenario"]?.ToString(), take!, overwrite);
            if (!r.Written)
            {
                var why = string.Join("; ", r.Problems);
                int status = r.Conflict || why.Contains("unsaved", StringComparison.Ordinal) ? 409 : why.StartsWith("no take", StringComparison.Ordinal) ? 404 : 400;
                return Fail(status, why);
            }
            return Ok(new JsonObject
            {
                ["promoted"] = true, ["from"] = r.From, ["to"] = r.To, ["overwrote"] = r.Overwrote, ["bake"] = r.Bake,
                ["fixedKeys"] = new JsonArray(r.FixedKeys.Select(k => (JsonNode?)k).ToArray()),
            });
        }

        // ── ⭐ CE-4059 P3 (§11r) — stills and the contact sheet ─────────────────────────────────────

        /// <summary>How long <c>POST /director/stills</c> waits for its job before answering it as running (GET /director/stills reads it on).</summary>
        private const double StillsWaitSeconds = 300d;

        private async Task<RouteResult> DirectorStillsRoute(RequestContext ctx)
        {
            var started = await Main(() => DirectorStillsStart(ctx.Body)).ConfigureAwait(false);
            bool wait = ctx.Body?["wait"] is not JsonValue wv || !wv.TryGetValue<bool>(out var w) || w;
            if (started.Status != 200 || !wait || started.Data?["state"]?.GetValue<string>() != "running") return started;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < StillsWaitSeconds)
            {
                await Task.Delay(50).ConfigureAwait(false);
                var now = await Main(() => Ok(_stills!.Status())).ConfigureAwait(false);
                if (now.Data?["state"]?.GetValue<string>() != "running") return now;
            }
            return await Main(() => Ok(_stills!.Status())).ConfigureAwait(false);   // still running: the caller reads it on
        }

        private RouteResult DirectorStillsStart(JsonNode? body)
        {
            if (_director is not { } director || _takes is not { } store || _stills is not { } stills) return Fail(503, NoDirector);
            if (_renders is { Busy: true }) return Fail(409, RendersHold);   // ⭐ CE-1306 — a render holds the director
            if (OpenRecording(body?["recording"]) is { } refused) return refused;
            if (StillsRequestOf(body, out var fault) is not { } request) return fault!.Value;
            // ⭐ CE-1306 (§11u D3) — the plan, the take and the folder resolved in ONE place (the render queue's stills jobs plan through it too)
            var (prepared, code, error) = DirectorStills.Prepare(request, director, store, () => _drafts?.Scenario);
            if (prepared == null) return Fail(code, error!);
            var planned = new JsonObject
            {
                ["scenario"] = prepared.Scenario, ["take"] = prepared.Take, ["dir"] = prepared.Dir, ["width"] = prepared.Width, ["height"] = prepared.Height,
                ["planned"] = DirectorStills.PlanJson(prepared.Plan), ["problems"] = new JsonArray(prepared.Problems.Select(p => (JsonNode?)p).ToArray()),
                ["sheetGrid"] = ContactSheet.Layout(prepared.Plan.Count, prepared.Width, prepared.Height).ToJson(),
            };
            // ⭐ plan:true — what WOULD be taken (any host: the editor, a headless browser), nothing captured
            if (body?["plan"] is JsonValue pv && pv.TryGetValue<bool>(out var only) && only) { planned["state"] = "planned"; return Ok(planned); }
            var why = stills.Start(prepared);
            if (why.Count > 0)
            {
                int status = why[0] == DirectorStills.NoRenderer || why[0] == DirectorStills.CannotSeek || why[0].StartsWith("a still of", StringComparison.Ordinal) ? 400 : 409;
                return Fail(status, string.Join("; ", why));
            }
            var result = stills.Status();
            result["problems"] = new JsonArray(prepared.Problems.Select(p => (JsonNode?)p).ToArray());
            return Ok(result);
        }

        /// <summary>⭐ CE-1306 — the stills asked for in a body (<c>/director/stills</c>'s fields, and a <c>/director/renders</c> job of kind stills).</summary>
        private static StillsRequest? StillsRequestOf(JsonNode? body, out RouteResult? fault)
        {
            fault = null;
            List<double>? times = null;
            if (body?["times"] is JsonArray ta)
            {
                times = new List<double>();
                foreach (var t in ta)
                    if (TakeJson.Double(t) is { } d) times.Add(d);
                    else { fault = Fail(400, $"times: '{t?.ToJsonString()}' is not a film second"); return null; }
            }
            var takeName = body?["take"]?.ToString();
            return new StillsRequest(
                Scenario: body?["scenario"]?.ToString() is { Length: > 0 } sc ? sc : null,
                Take: string.IsNullOrWhiteSpace(takeName) ? "main" : takeName!,
                Times: times,
                EveryKey: body?["everyKey"] is JsonValue ek && ek.TryGetValue<bool>(out var e) && e,
                Width: (int)(TakeJson.Double(body?["width"]) ?? 1280d),
                Height: (int)(TakeJson.Double(body?["height"]) ?? 720d),
                Dir: body?["dir"]?.ToString() is { Length: > 0 } dir ? dir : null,
                Captions: body?["captions"] is not JsonValue cv || !cv.TryGetValue<bool>(out var cap) || cap);
        }

        // ── ⭐ CE-1306 R4 (§11u) — the render queue ────────────────────────────────────────────────────

        internal const string RendersHold = "a render holds the director (GET /director/renders) - it plays when the queue is idle, or cancel the render (DELETE /director/renders/{id})";

        /// <summary>
        /// <c>POST /director/renders</c> — queues ONE render (<c>{kind?, recording?, scenario?, take?, reel?, capture?, times?, everyKey?, width?, height?, dir?}</c>) or
        /// several (<c>{renders: [ … ]}</c>, each such an object). 400 when nothing could be queued (each refusal named), else the queue with the new ids.
        /// </summary>
        private RouteResult RendersEnqueue(JsonNode? body)
        {
            if (_renders is not { } renders) return Fail(503, NoDirector);
            var asked = body?["renders"] is JsonArray many ? many.ToList() : new List<JsonNode?> { body };
            if (asked.Count == 0) return Fail(400, "renders: [] queues nothing");
            var queued = new JsonArray();
            var refused = new JsonArray();
            for (int i = 0; i < asked.Count; i++)
            {
                string? why;
                if (RenderRequestOf(asked[i], out var error) is not { } request) why = error;
                else
                {
                    var (job, no) = renders.Enqueue(request);
                    if (job != null) { queued.Add(job.Id); continue; }
                    why = no;
                }
                refused.Add(new JsonObject { ["index"] = i, ["error"] = why });
            }
            if (queued.Count == 0)
                return Fail(400, asked.Count == 1 ? refused[0]!["error"]!.GetValue<string>() : "nothing queued: " + refused.ToJsonString());
            var status = renders.Status();
            status["queuedIds"] = queued;
            status["refused"] = refused;
            return Ok(status);
        }

        /// <summary>One render's request from its JSON; null with why not.</summary>
        private RenderRequest? RenderRequestOf(JsonNode? body, out string? error)
        {
            error = null;
            if (body is not JsonObject o) { error = "a render is an object: {take | reel, recording?, capture?} (kind:\"stills\": {take, times | everyKey})"; return null; }
            var kindText = o["kind"]?.ToString() ?? "movie";
            RenderKind kind;
            if (kindText.Equals("movie", StringComparison.OrdinalIgnoreCase)) kind = RenderKind.Movie;
            else if (kindText.Equals("stills", StringComparison.OrdinalIgnoreCase)) kind = RenderKind.Stills;
            else { error = $"kind '{kindText}': movie or stills"; return null; }
            var recording = o["recording"] is JsonValue rv && rv.TryGetValue<string>(out var rec) && !string.IsNullOrWhiteSpace(rec) ? rec : null;
            if (recording != null && _service is not { OpensRecordingFiles: true }) { error = RecordingRefused; return null; }
            if (kind == RenderKind.Stills)
            {
                if (StillsRequestOf(o, out var fault) is not { } s) { error = fault!.Value.Error; return null; }
                return new RenderRequest
                {
                    Kind = kind, Recording = recording, Scenario = s.Scenario, Take = s.Take, Times = s.Times, EveryKey = s.EveryKey,
                    Width = s.Width, Height = s.Height, Dir = s.Dir, Captions = s.Captions,
                };
            }
            var c = CaptureFieldsOf(o["capture"] as JsonObject, out var bad);
            if (bad != null) { error = bad; return null; }
            var takeName = o["take"]?.ToString();
            return new RenderRequest
            {
                Kind = kind, Recording = recording,
                Scenario = o["scenario"]?.ToString() is { Length: > 0 } sc ? sc : null,
                Take = string.IsNullOrWhiteSpace(takeName) ? "main" : takeName!,
                ReelName = o["reel"]?.ToString() is { Length: > 0 } reel ? reel : null,
                Dir = c.Dir, Fps = c.Fps, Width = c.Width, Height = c.Height, Supersample = c.Supersample, Captions = c.Captions, Movie = c.Movie, Encode = c.Encode,
            };
        }

        /// <summary><c>DELETE /director/renders/{id}</c> — 404 no such render, 409 it already ended, else the queue (the job cancelled).</summary>
        private RouteResult RendersCancel(string id)
        {
            if (_renders is not { } renders) return Fail(503, NoDirector);
            var (found, refused) = renders.Cancel(id);
            if (!found) return Fail(404, $"no render '{id}' (GET /director/renders lists them)");
            if (refused != null) return Fail(409, refused);
            return Ok(renders.Status());
        }

        /// <summary>
        /// ⭐ CE-1306 — the capture object (<c>{dir?, fps?, size?, supersample?, captions?, movie?, encode?}</c>) as <c>/director/play</c> and <c>/director/renders</c>
        /// read it — ONE reading. A null object: the defaults. <paramref name="error"/> names a malformed field.
        /// </summary>
        private static (string? Dir, int Fps, int Width, int Height, int Supersample, bool Captions, string? Movie, bool Encode) CaptureFieldsOf(JsonObject? c, out string? error)
        {
            error = null;
            int w = 1280, h = 720;
            if (c?["size"]?.ToString() is { } size)
            {
                var parts = size.ToLowerInvariant().Split('x');
                if (parts.Length != 2 || !int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out h)) error = $"size '{size}': WIDTHxHEIGHT";
            }
            int fps = (int)(TakeJson.Double(c?["fps"]) ?? 30d);
            if (error == null && fps is < 1 or > 60) error = "fps 1…60";
            return (c?["dir"]?.ToString() is { Length: > 0 } d ? d : null, fps, w, h, (int)(TakeJson.Double(c?["supersample"]) ?? 1d),
                    c?["captions"] is not JsonValue cv || !cv.TryGetValue<bool>(out var burn) || burn,
                    c?["movie"]?.ToString(),
                    c?["encode"] is not JsonValue ev || !ev.TryGetValue<bool>(out var enc) || enc);
        }

        // ── play / scout / transport ──────────────────────────────────────────────────────────

        private RouteResult DirectorPlay(JsonNode? body, bool scout)
        {
            if (_director is not { } director || _takes is not { } store) return Fail(503, NoDirector);
            if (_stills is { Busy: true }) return Fail(409, "a stills job holds the director (GET /director/stills) — play when it is done");   // §11r
            if (_renders is { Busy: true }) return Fail(409, RendersHold);   // ⭐ CE-1306 — a render holds the director
            Reel reel;
            Dictionary<string, Take>? inline = null;
            var scenario = body?["scenario"]?.ToString();
            // ⭐ §11r — on the Replay Browser, the scenario the OPEN recording names (its takes are beside it); the editor still names one
            if (string.IsNullOrWhiteSpace(scenario) && body?["reel"] is null && _service is { OpensRecordingFiles: true }) scenario = _drafts?.Scenario;
            var takeName = body?["take"] is JsonValue tv ? tv.ToString() : "main";
            if (body?["reel"]?.ToString() is { } reelName)
            {
                var loaded = store.LoadReel(reelName);
                if (loaded.Value is null) return Fail(404, string.Join("; ", loaded.Problems));
                if (loaded.Problems.Count > 0) return Fail(400, string.Join("; ", loaded.Problems));
                reel = loaded.Value;
            }
            else if (!string.IsNullOrWhiteSpace(scenario))
            {
                if (body?["take"] is JsonObject inlineTake)
                {
                    // ⭐ D-9 — a one-off take sent with the request (film.py's plan): played as is, never written
                    var problems = new List<string>();
                    var parsed = TakeJson.ParseTake(inlineTake, scenario!, "_inline", problems);
                    if (problems.Count > 0) return Fail(400, "the take is not valid: " + string.Join("; ", problems));
                    takeName = "_inline";
                    inline = new Dictionary<string, Take>(StringComparer.Ordinal) { [scenario + "/_inline"] = parsed };
                }
                reel = Reel.Of(scenario!, takeName);
            }
            else return Fail(400, "name what to play: {reel} or {scenario, take?} (take: a name, default main, or the take itself as an object)");

            CaptureOptions? capture = null;
            if (body?["capture"] is JsonObject c)
            {
                var f = CaptureFieldsOf(c, out var bad);   // ⭐ CE-1306 — the ONE reading of a capture object (the render queue's too)
                if (bad != null) return Fail(400, bad);
                var dir = f.Dir;
                // ⭐ §11r — a render of one take goes BESIDE it by default (takes/<take>.render/: the frames, the movie) — TE-1, the take lives by its recording
                if (string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(scenario) && body?["reel"] is null)
                    dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.TakePath(scenario!, takeName))!, takeName + ".render");
                if (string.IsNullOrWhiteSpace(dir)) return Fail(400, "capture needs dir (where the frames and the movie go)");
                capture = new CaptureOptions(System.IO.Path.GetFullPath(dir!), f.Fps, f.Width, f.Height, f.Supersample, f.Captions, f.Movie, f.Encode);
            }
            double speed = TakeJson.Double(body?["speed"]) ?? 1d;
            bool loop = body?["loop"] is JsonValue lv && lv.TryGetValue<bool>(out var l) && l;
            bool bake = body?["bake"] is not JsonValue bv || !bv.TryGetValue<bool>(out var b) || b;
            var refused = director.Play(new PlayRequest(reel, speed, loop, capture, scout, inline, bake));
            if (refused.Count > 0) return Fail(400, "not started: " + string.Join("; ", refused));
            return Ok(director.Status());
        }

        // ── verify (CE-4042) ──────────────────────────────────────────────────────────────────

        private RouteResult ChecksRead(string scenario)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var loaded = store.LoadScenarioJson(scenario, ChecksJson.FileName);
            if (loaded.Value is null) return Fail(404, string.Join("; ", loaded.Problems));
            var problems = new List<string>();
            ChecksJson.Parse(loaded.Value, scenario, problems);
            var golden = store.LoadScenarioJson(scenario, ScenarioGolden.FileName);
            return Ok(new JsonObject
            {
                ["scenario"] = scenario, ["path"] = store.ScenarioFilePath(scenario, ChecksJson.FileName), ["json"] = loaded.Value.DeepClone(),
                ["problems"] = new JsonArray(problems.Select(p => (JsonNode?)p).ToArray()),
                ["golden"] = golden.Value is { } g ? new JsonObject
                {
                    ["path"] = store.ScenarioFilePath(scenario, ScenarioGolden.FileName), ["steps"] = g["steps"]?.DeepClone(),
                    ["snapshots"] = (g["snapshots"] as JsonArray)?.Count, ["shots"] = (g["shots"] as JsonArray)?.Count,
                } : null,
            });
        }

        private RouteResult ChecksWrite(string scenario, JsonNode? body)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            if (body is not JsonObject json) return Fail(400, "the body is the checks (a JSON object: runFor, golden, anchors, do, expect)");
            var problems = new List<string>();
            var parsed = ChecksJson.Parse(json, scenario, problems);
            if (problems.Count > 0) return Fail(400, "the checks are not valid: " + string.Join("; ", problems));
            var written = store.SaveScenarioJson(scenario, ChecksJson.FileName, json);
            return Ok(new JsonObject
            {
                ["saved"] = new JsonArray(written.Select(p => (JsonNode?)p).ToArray()), ["expect"] = parsed.Expect.Count,
                ["anchors"] = parsed.Anchors.Count, ["do"] = parsed.Do.Count, ["golden"] = parsed.GoldenEvery is { } e ? e : null,
            });
        }

        private RouteResult DirectorVerify(JsonNode? body)
        {
            if (_director is not { } director || _takes is not { } store) return Fail(503, NoDirector);
            var names = new List<string>();
            if (body?["scenario"]?.ToString() is { Length: > 0 } one) names.Add(one);
            if (body?["scenarios"] is JsonArray many) names.AddRange(many.Select(x => x?.ToString()).OfType<string>().Where(x => x.Length > 0));
            if (body?["all"] is JsonValue av && av.TryGetValue<bool>(out var all) && all)
                names.AddRange(System.IO.Directory.Exists(store.ScenariosRoot)
                    ? System.IO.Directory.EnumerateDirectories(store.ScenariosRoot)
                        .Where(d => System.IO.File.Exists(System.IO.Path.Combine(d, ChecksJson.FileName)))
                        .Select(d => System.IO.Path.GetFileName(d)).OrderBy(n => n, StringComparer.Ordinal)
                    : Enumerable.Empty<string>());
            names = names.Distinct(StringComparer.Ordinal).ToList();
            if (names.Count == 0) return Fail(400, "name what to verify: {scenario} or {scenarios:[…]} or {all:true}");
            bool capture = body?["capture"] is JsonValue cv && cv.TryGetValue<bool>(out var c) && c;
            // ⭐ CE-4045 H1a (Scenario Regression §10-1) — replay: the run is LIVE and its own recording, played back, must hold it
            bool replay = body?["replay"] is JsonValue rv && rv.TryGetValue<bool>(out var rp) && rp;
            // ⭐ fresh: the regression verdict — each scenario a first load in a process of its own (the command line, started as a child)
            if (body?["fresh"] is JsonValue fv && fv.TryGetValue<bool>(out var fresh) && fresh)
                return Ok(_freshVerify.Start(names, capture, replay));
            var reel = new Reel { Name = "verify", Items = names.Select(n => new ReelItem { Scenario = n, Take = "_verify" }).ToList() };
            var refused = director.Play(new PlayRequest(reel, Plan: new VerifyOptions(capture, Replay: replay)));
            if (refused.Count > 0) return Fail(400, "not started: " + string.Join("; ", refused));
            var status = director.Status();
            // ⚠ R-4065: one process per scenario is the regression rule (a reload still leaks, CE-3294) — said, not refused
            if (names.Count > 1) status["note"] = "several scenarios in ONE process: each after the first is a reload (CE-3294 may leak state) — "
                                                 + "for the regression verdict run each in a fresh process (Hrot.ClusterRunner --mode editor --headless --verify a,b)";
            return Ok(status);
        }

        private RouteResult Transport(Action<DirectorRunner> act)
        {
            if (_director is not { } d) return Fail(503, NoDirector);
            act(d);
            return Ok(d.Status());
        }

        // ── layers (R-4060) ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// The editor's layer control, else the ACTIVE provider's map (⭐ CE-3327 RB-8): every map host's pack builds one over its gizmo buffer
        /// (<c>MapInteractionPack.LayerControlFor</c>), so the Replay Browser, CGF and IG answer <c>/map/layers</c> too. 🔴 Before, only the editor's
        /// director attach supplied it and every other host answered 503.
        /// </summary>
        private Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo? LayerControl()
            => _layerControl?.Invoke()
               ?? (_dispatcher?.GizmoBuffer is { } buffer ? Hrot.ScenarioEditor.Map.MapInteractionPack.LayerControlFor(buffer) : null);

        private RouteResult LayersRead(string? view)
        {
            if (LayerControl() is not { } lc) return Fail(503, "This host has no map layer control.");
            bool? only3D = view switch { "3d" or "3D" => true, "2d" or "2D" => false, _ => null };
            var o = new JsonObject { ["shown"] = lc.Shows3D ? "3d" : "2d", ["override"] = lc.Override != null };
            foreach (var is3D in new[] { false, true })
            {
                if (only3D is { } v && v != is3D) continue;
                var rows = new JsonArray();
                var set = lc.SetOf(is3D);
                foreach (var (name, display, doc) in Hrot.Common.Diagnostics.Gizmos.LayerControlSets.RowNames)
                {
                    Hrot.Common.Diagnostics.Gizmos.LayerControlSets.TryGet(set, name, out var on);
                    rows.Add(new JsonObject { ["name"] = name, ["display"] = display, ["on"] = on, ["doc"] = doc });
                }
                o[is3D ? "3d" : "2d"] = rows;
            }
            return Ok(o);
        }

        private RouteResult LayersWrite(JsonNode? body)
        {
            if (LayerControl() is not { } lc) return Fail(503, "This host has no map layer control.");
            var view = body?["view"]?.ToString();
            bool is3D = view switch { "3d" or "3D" => true, "2d" or "2D" => false, _ => lc.Shows3D };
            static List<string> Names(JsonNode? n) => n is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() : new();
            var unknown = lc.SetRows(is3D, Names(body?["on"]), Names(body?["off"]));
            if (unknown.Count > 0) return Fail(400, $"no such layer: {string.Join(", ", unknown)} — GET /map/layers lists them", DebugApiHints.Panel);
            return LayersRead(is3D ? "3d" : "2d");
        }

        // ── sound (R-4059) ────────────────────────────────────────────────────────────────────

        private static RouteResult AudioRead()
        {
            var s = Hrot.UI.Common.Audio.AudioPreferences.For(Hrot.UI.Common.Audio.AudioSettings.DefaultPath());
            return Ok(new JsonObject
            {
                ["sound"] = Hrot.UI.Common.Audio.AudioPreferences.CameraSound,
                ["takeSound"] = Hrot.UI.Common.Audio.AudioDirector.TakeSound,
                ["master"] = s.Master, ["muted"] = s.Muted,
                ["note"] = "sound = Settings ▸ Sound (the scene heard from the camera, 2-D and 3-D); FPS always sounds",
            });
        }

        private static RouteResult AudioWrite(JsonNode? body)
        {
            var path = Hrot.UI.Common.Audio.AudioSettings.DefaultPath();
            var s = Hrot.UI.Common.Audio.AudioPreferences.For(path);
            if (body?["sound"] is JsonValue sv && sv.TryGetValue<bool>(out var sound)) Hrot.UI.Common.Audio.AudioPreferences.CameraSound = sound;
            bool changed = false;
            if (TakeJson.Double(body?["master"]) is { } master) { s.Master = (float)Math.Clamp(master, 0d, 1d); changed = true; }
            if (body?["muted"] is JsonValue mv && mv.TryGetValue<bool>(out var muted)) { s.Muted = muted; changed = true; }
            if (changed) s.Save(path);
            return AudioRead();
        }
    }
}
