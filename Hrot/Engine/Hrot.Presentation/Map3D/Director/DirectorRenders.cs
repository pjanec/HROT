using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>What a render makes: ONE movie of a take or a reel (the runner's capture), or a take's STILLS and contact sheet (<see cref="DirectorStills"/>).</summary>
    public enum RenderKind { Movie, Stills }

    /// <summary>A render's life: queued → (opening its recording) → running → done · failed · cancelled.</summary>
    public enum RenderState { Queued, Opening, Running, Done, Failed, Cancelled }

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (docs/DESIGN_Director_Timeline_Editor.md §11u D3–D5) — ONE render asked for: what (a take, a saved reel by name, or a reel as it stands —
    /// the composer's), from where (<see cref="Recording"/>: opened at the job's turn; null = the source the host shows), and how (the capture options of
    /// <see cref="CaptureOptions"/>, or the stills of <see cref="StillsRequest"/>). Defaults are resolved when the job STARTS, not when it is queued
    /// (the scenario the open recording names; the folder beside the take): a job's recording moves the takes root.
    /// </summary>
    public sealed record RenderRequest
    {
        public RenderKind Kind { get; init; } = RenderKind.Movie;
        /// <summary>A recording (a <c>.fdp</c> path, its folder, or an exercise id) opened at the job's turn, through the host's one opener (<c>POST /replay</c>'s).</summary>
        public string? Recording { get; init; }
        /// <summary>The take's scenario (null: the one the open recording / the loaded scenario names).</summary>
        public string? Scenario { get; init; }
        public string Take { get; init; } = "main";
        /// <summary>A saved reel (<c>films/&lt;name&gt;.reel.json</c>), read at the job's turn. A movie only.</summary>
        public string? ReelName { get; init; }
        /// <summary>A reel as it stands (the composer's, unsaved edits included). A movie only. Wins over <see cref="ReelName"/>.</summary>
        public Reel? Reel { get; init; }
        /// <summary>Where the frames and the movie / the stills go (null: a folder of this job's own beside the take, or under the scenarios root for a reel).</summary>
        public string? Dir { get; init; }
        public int Fps { get; init; } = 30;
        public int Width { get; init; } = 1280;
        public int Height { get; init; } = 720;
        public int Supersample { get; init; } = 1;
        public bool Captions { get; init; } = true;
        /// <summary>The movie's path (null: <c>&lt;dir&gt;/&lt;reel&gt;.mp4</c>).</summary>
        public string? Movie { get; init; }
        /// <summary>False: the frames only, no movie (no ffmpeg).</summary>
        public bool Encode { get; init; } = true;
        /// <summary>Stills: film seconds (else every key with <see cref="EveryKey"/>, else the take's still keys).</summary>
        public IReadOnlyList<double>? Times { get; init; }
        public bool EveryKey { get; init; }

        /// <summary>A take, a reel, or "the reel being composed": what the job is of, for its row.</summary>
        public string What => Reel is { } r ? "reel " + (string.IsNullOrEmpty(r.Name) ? "(composed)" : r.Name)
                            : ReelName is { } n ? "reel " + n
                            : (Scenario ?? "(open recording)") + "/" + Take;
    }

    /// <summary>One render in the queue: its request, state, progress and results (the movie's path, or the stills and their sheet).</summary>
    public sealed class RenderJob
    {
        internal RenderJob(string id, RenderRequest request) { Id = id; Request = request; Enqueued = DateTime.UtcNow; }

        public string Id { get; }
        public RenderRequest Request { get; }
        public RenderState State { get; internal set; } = RenderState.Queued;
        /// <summary>The folder it writes into (known once it starts).</summary>
        public string? Dir { get; internal set; }
        /// <summary>The result: the movie (or the frames' folder when not encoded), or the contact sheet of a stills job.</summary>
        public string? Output { get; internal set; }
        /// <summary>Every file it made worth naming (the stills' PNGs).</summary>
        public List<string> Outputs { get; } = new();
        public List<string> Problems { get; } = new();
        /// <summary>Why a queued job has not started yet (null: it is next, or it ran).</summary>
        public string? Waiting { get; internal set; }
        /// <summary>Frames captured so far (a movie), or stills taken (a stills job).</summary>
        public int Frames { get; internal set; }
        /// <summary>The reel item playing (0-based; −1 when none) and the reel's length.</summary>
        public int Item { get; internal set; } = -1;
        public int Items { get; internal set; }
        public DateTime Enqueued { get; }
        public DateTime? Started { get; internal set; }
        public DateTime? Finished { get; internal set; }
        public bool Ended => State is RenderState.Done or RenderState.Failed or RenderState.Cancelled;

        /// <summary>The capture this job handed the runner (its identity on the runner — another play replaces it).</summary>
        internal CaptureOptions? Capture { get; set; }
        internal int Settle { get; set; }

        public JsonObject ToJson() => new()
        {
            ["id"] = Id,
            ["state"] = State.ToString().ToLowerInvariant(),
            ["kind"] = Request.Kind.ToString().ToLowerInvariant(),
            ["what"] = Request.What,
            ["recording"] = Request.Recording,
            ["scenario"] = Request.Scenario,
            ["take"] = Request.Reel == null && Request.ReelName == null ? Request.Take : null,
            ["reel"] = Request.Reel?.Name ?? Request.ReelName,
            ["size"] = $"{Request.Width}x{Request.Height}",
            ["fps"] = Request.Kind == RenderKind.Movie ? Request.Fps : null,
            ["dir"] = Dir,
            ["output"] = Output,
            ["outputs"] = new JsonArray(Outputs.Select(o => (JsonNode?)o).ToArray()),
            ["frames"] = Frames,
            ["item"] = Item < 0 ? null : Item + 1,
            ["items"] = Items == 0 ? null : Items,
            ["waiting"] = Waiting,
            ["problems"] = new JsonArray(Problems.Select(p => (JsonNode?)p).ToArray()),
            ["enqueuedAt"] = Enqueued.ToString("O"),
            ["startedAt"] = Started?.ToString("O"),
            ["finishedAt"] = Finished?.ToString("O"),
            ["wallSeconds"] = Started is { } s ? Math.Round(((Finished ?? DateTime.UtcNow) - s).TotalSeconds, 1) : null,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (docs/DESIGN_Director_Timeline_Editor.md §11u) — the RENDER QUEUE: renders asked for (the Director window's <i>Queue render</i>,
    /// <c>POST /director/renders</c>, a night run's many) run ONE AFTER ANOTHER through the pack's ONE runner and ONE stills job — ⛔ never two at once (the
    /// runner plays one thing), ⛔ no capture path of its own. Each job, at its turn: its recording opened (<see cref="OpenRecording"/> — the host's one opener),
    /// then a movie through <see cref="DirectorRunner.Play"/> with its capture (a recording SEEKS; the editor runs the scenario live, paced by the take), or stills
    /// through <see cref="DirectorStills"/>. A job that fails (an unknown take, a recording that does not open) is reported and the queue goes on; a cancel takes a
    /// queued job off the queue, or stops the running one.
    /// Main thread only: <see cref="Tick"/> is called by <see cref="DirectorPack.Tick"/>, after the runner's and the stills job's.
    /// </summary>
    public sealed class DirectorRenders
    {
        public const string NoRenderer = "renders need a renderer: this host is headless (no map is drawn, so there is no frame to capture) - run the Replay Browser (or the editor) with a window (on a server: Xvfb, DISPLAY=:99)";
        public const string NoOpener = "this host opens no recordings (recording= is the Replay Browser's) - leave it out to render what this host shows";
        public const string TakenOver = "another play took the director while this render ran (Play, /director/play): the render stopped";
        public const string StoppedByUser = "stopped by the director's Stop (or Esc) before it finished";
        /// <summary>Frames a job waits after its recording opened before it plays (the open's source settles into the host's frame).</summary>
        public const int OpenSettleFrames = 2;
        /// <summary>Ended jobs kept for <see cref="Jobs"/> (the oldest go first).</summary>
        public const int MaxEnded = 200;

        private readonly DirectorRunner _runner;
        private readonly DirectorStills _stills;
        private readonly IFrameSink? _sink;
        private readonly Func<bool> _canSeek;
        private readonly Func<TakeStore?> _store;
        private readonly Func<string?> _openScenario;
        private readonly List<RenderJob> _jobs = new();
        private RenderJob? _running;
        private int _next = 1;

        /// <param name="sink">The pack's frame sink (null: a headless host — every render refused).</param>
        /// <param name="canSeek">The host's source seeks (a recording): stills need it.</param>
        /// <param name="openScenario">The scenario the host shows (the open recording's, the editor's loaded one) — a job naming none renders its take.</param>
        public DirectorRenders(DirectorRunner runner, DirectorStills stills, IFrameSink? sink, Func<bool> canSeek, Func<TakeStore?> store, Func<string?> openScenario)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _stills = stills ?? throw new ArgumentNullException(nameof(stills));
            _sink = sink;
            _canSeek = canSeek ?? throw new ArgumentNullException(nameof(canSeek));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _openScenario = openScenario ?? (() => null);
        }

        /// <summary>
        /// ⭐ §11u D4 — opens a job's recording (a path, a folder, an exercise id); returns why not (null: opened). Handed in by the process's debug HTTP host
        /// (<c>DebugApiHost.AttachDirector</c>): the very call <c>POST /replay</c> makes — ⛔ no second opener. Null: this host opens none (a job naming one fails).
        /// </summary>
        public Func<string, string?>? OpenRecording { get; set; }

        /// <summary>Null when this host can render, else why not (a headless host has no frame to capture).</summary>
        public string? Refusal => _sink == null ? NoRenderer : null;

        /// <summary>Every job: the queued in order, the running one, the ended ones (the last <see cref="MaxEnded"/>).</summary>
        public IReadOnlyList<RenderJob> Jobs => _jobs;

        /// <summary>The job holding the director now (null: none).</summary>
        public RenderJob? Running => _running;

        /// <summary>A render holds the director (the window's Play and <c>/director/play</c>, <c>/director/stills</c> wait for it).</summary>
        public bool Busy => _running != null;

        public int Queued => _jobs.Count(j => j.State == RenderState.Queued);

        public RenderJob? Find(string id) => _jobs.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.Ordinal));

        /// <summary>Queues <paramref name="request"/>; null with why it cannot be rendered here (checked now: the renderer, the sizes, stills on a host that cannot seek).</summary>
        public (RenderJob? Job, string? Refused) Enqueue(RenderRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (Refusal is { } why) return (null, why);
            if (request.Width < 16 || request.Height < 16 || request.Width > 4096 || request.Height > 4096)
                return (null, $"a render of {request.Width}x{request.Height}: each side 16...4096 px");
            if (request.Kind == RenderKind.Movie && request.Fps is < 1 or > 60) return (null, "fps 1...60");
            if (request.Kind == RenderKind.Stills && (request.Reel != null || request.ReelName != null)) return (null, "stills are of ONE take (name take, not reel)");
            if (request.Kind == RenderKind.Stills && request.Recording == null && !_canSeek()) return (null, DirectorStills.CannotSeek);
            if (request.Reel is { Items.Count: 0 }) return (null, "the reel has no items");
            var job = new RenderJob("r" + _next++, request);
            _jobs.Add(job);
            Trim();
            return (job, null);
        }

        /// <summary>
        /// Cancels job <paramref name="id"/>: a queued one never runs; the running one stops the runner (or the stills job). Found false: no such job;
        /// a refusal when it had already ended.
        /// </summary>
        public (bool Found, string? Refused) Cancel(string id)
        {
            if (Find(id) is not { } job) return (false, null);
            if (job.Ended) return (true, $"render {id} already ended ({job.State.ToString().ToLowerInvariant()})");
            bool wasRunning = ReferenceEquals(job, _running);
            job.State = RenderState.Cancelled;   // ⭐ first: the runner's stop below must not read as the user's
            job.Problems.Add("cancelled");
            job.Finished = DateTime.UtcNow;
            job.Waiting = null;
            if (wasRunning)
            {
                _running = null;
                if (job.Request.Kind == RenderKind.Stills) _stills.Cancel();
                else if (job.Capture != null && ReferenceEquals(_runner.Request?.Capture, job.Capture) && _runner.Busy) _runner.Stop();
            }
            return (true, null);
        }

        /// <summary>One frame of the queue (after the runner's and the stills job's ticks): watch the running job, else start the next.</summary>
        public void Tick()
        {
            if (_running is { } job) { Watch(job); return; }
            var next = _jobs.FirstOrDefault(j => j.State == RenderState.Queued);
            if (next == null) return;
            if (_runner.Busy || _stills.Busy)
            {
                next.Waiting = _stills.Busy ? "a stills job holds the director" : "the director is playing (not a render): it starts when the director is idle";
                return;
            }
            next.Waiting = null;
            Begin(next);
        }

        private void Begin(RenderJob job)
        {
            job.Started = DateTime.UtcNow;
            _running = job;
            if (job.Request.Recording is { } recording)
            {
                if (OpenRecording == null) { End(job, RenderState.Failed, NoOpener); return; }
                string? error;
                try { error = OpenRecording(recording); }
                catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }
                if (error != null) { End(job, RenderState.Failed, error); return; }
                job.State = RenderState.Opening;
                job.Settle = OpenSettleFrames;
                return;
            }
            Launch(job);
        }

        private void Launch(RenderJob job)
        {
            var r = job.Request;
            if (_store() is not { } store) { End(job, RenderState.Failed, "no take store on this host"); return; }
            var scenario = string.IsNullOrWhiteSpace(r.Scenario) ? _openScenario() : r.Scenario;
            if (r.Kind == RenderKind.Stills)
            {
                if (string.IsNullOrWhiteSpace(scenario)) { End(job, RenderState.Failed, "name the scenario (or a recording that names one)"); return; }
                var dir = r.Dir ?? Path.Combine(Path.GetDirectoryName(store.TakePath(scenario!, r.Take))!, r.Take + ".stills", job.Id);
                var (prepared, _, error) = DirectorStills.Prepare(new StillsRequest(scenario, r.Take, r.Times, r.EveryKey, r.Width, r.Height, dir, r.Captions),
                                                                  _runner, store, _openScenario);
                if (prepared == null) { End(job, RenderState.Failed, error!); return; }
                job.Problems.AddRange(prepared.Problems);
                job.Dir = prepared.Dir;
                job.Items = 1;
                var why = _stills.Start(prepared);
                if (why.Count > 0) { End(job, RenderState.Failed, string.Join("; ", why)); return; }
                job.State = RenderState.Running;
                return;
            }

            Reel reel;
            bool oneTake = false;
            if (r.Reel != null) reel = r.Reel;
            else if (r.ReelName != null)
            {
                var loaded = store.LoadReel(r.ReelName);
                if (loaded.Value is null || loaded.Problems.Count > 0) { End(job, RenderState.Failed, string.Join("; ", loaded.Problems)); return; }
                reel = loaded.Value;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(scenario)) { End(job, RenderState.Failed, "name the scenario (or a recording that names one)"); return; }
                reel = Reel.Of(scenario!, r.Take);
                oneTake = true;
            }
            // ⭐ D5 — a folder of THIS job's own (two renders of one take never share a frames folder): beside the take, else under the scenarios root
            var folder = r.Dir ?? (oneTake
                ? Path.Combine(Path.GetDirectoryName(store.TakePath(scenario!, r.Take))!, r.Take + ".render", job.Id)
                : Path.Combine(store.ScenariosRoot, ".renders", (string.IsNullOrEmpty(reel.Name) ? "reel" : reel.Name).Replace('/', '-'), job.Id));
            var capture = new CaptureOptions(Path.GetFullPath(folder), r.Fps, r.Width, r.Height, r.Supersample, r.Captions, r.Movie, r.Encode);
            job.Dir = capture.Dir;
            job.Capture = capture;
            job.Items = reel.Items.Count;
            var refused = _runner.Play(new PlayRequest(reel, Capture: capture));
            if (refused.Count > 0) { End(job, RenderState.Failed, "not started: " + string.Join("; ", refused)); return; }
            job.State = RenderState.Running;
        }

        private void Watch(RenderJob job)
        {
            if (job.State == RenderState.Opening)
            {
                if (--job.Settle > 0) return;
                Launch(job);
                return;
            }
            if (job.Request.Kind == RenderKind.Stills)
            {
                var st = _stills.Status();
                job.Frames = (st["stills"] as JsonArray)?.Count ?? 0;
                if (_stills.Busy) return;
                job.Outputs.Clear();
                foreach (var s in st["stills"] as JsonArray ?? new JsonArray()) if (s?["path"]?.GetValue<string>() is { } p) job.Outputs.Add(p);
                job.Output = st["sheet"]?.GetValue<string>();
                foreach (var p in st["problems"] as JsonArray ?? new JsonArray()) if (p?.GetValue<string>() is { } t && !job.Problems.Contains(t)) job.Problems.Add(t);
                End(job, st["state"]?.GetValue<string>() == "done" ? RenderState.Done : RenderState.Failed, null);
                return;
            }
            if (!ReferenceEquals(_runner.Request?.Capture, job.Capture)) { End(job, RenderState.Failed, TakenOver); return; }
            job.Frames = _runner.Status()["frames"]?.GetValue<int>() ?? job.Frames;
            if (_runner.Busy) { job.Item = _runner.CurrentItem; return; }
            job.Item = -1;
            foreach (var rep in _runner.Reports)
                foreach (var p in rep.Problems.Concat(rep.Missing.Select(m => "beat missing: " + m)))
                    job.Problems.Add($"{rep.Scenario}/{rep.Take}: {p}");
            if (_runner.Phase == DirectorPhase.Stopped && _runner.Error == null) { End(job, RenderState.Cancelled, StoppedByUser); return; }
            bool failed = _runner.Phase != DirectorPhase.Done || _runner.Error != null || _runner.Reports.Any(x => x.Status is "failed");
            job.Output = _runner.MoviePath ?? Path.Combine(job.Capture!.Dir, "frames");
            End(job, failed ? RenderState.Failed : RenderState.Done, _runner.Error);
        }

        private void End(RenderJob job, RenderState state, string? problem)
        {
            if (problem != null) job.Problems.Add(problem);
            job.State = state;
            job.Finished = DateTime.UtcNow;
            if (ReferenceEquals(_running, job)) _running = null;
            Trim();
        }

        private void Trim()
        {
            int ended = _jobs.Count(j => j.Ended);
            for (int i = 0; i < _jobs.Count && ended > MaxEnded; i++)
                if (_jobs[i].Ended) { _jobs.RemoveAt(i--); ended--; }
        }

        /// <summary>The queue: <c>{running, queued, refusal, jobs[]}</c> (each job's <see cref="RenderJob.ToJson"/>).</summary>
        public JsonObject Status() => new()
        {
            ["running"] = _running?.Id,
            ["queued"] = Queued,
            ["refusal"] = Refusal,
            ["jobs"] = new JsonArray(_jobs.Select(j => (JsonNode?)j.ToJson()).ToArray()),
        };
    }
}
