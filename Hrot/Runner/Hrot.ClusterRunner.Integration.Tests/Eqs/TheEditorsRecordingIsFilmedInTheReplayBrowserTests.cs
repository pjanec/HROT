using Fdp.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Hrot.Editor.DebugApi;
using Hrot.ReplayBrowser;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;
using Xunit;
using static Hrot.ClusterRunner.Integration.Tests.Eqs.EditorPump;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// ⭐⭐⭐ <c>CE-4102</c> — <b>the armed-mode MVP, proven end to end through the REAL hosts, in one process.</b>
/// 📄 <c>docs/blueprints/batches/HANDOFF_Coordinator_To_Ui_Armed_Mode_MVP.md</c> (the chain), <c>Architect_Question_108_Run_Mode_Picker.md</c> (the run choice, R1–R3).
/// <code>
/// editor (headless EditorSubsystem): open hill-attack → Choose(Live) → ▶ → the live run records → ■ → the exercise is on disk
/// Replay Browser (its own ReplayBrowserSubsystem over the SAME staging root): lists it (with its scenario) → opens it → the director plays a take on it
/// </code>
/// Every step goes through the host's own surface: the editor's <see cref="RunModeChoice"/> (the toolbar's own object) and the Replay Browser's debug routes
/// (<c>GET /exercises</c>, <c>POST /replay</c>, <c>POST /director/play</c>) over a real <see cref="DebugApiHost"/> — the routes an agent or a person drives.
/// ⭐ Measured live by hand first (two processes sharing <c>FDP_STAGING_ROOT</c>); this rail automates exactly that.
/// 🔴 Red-proofs (each compiles): <c>Record live runs</c> OFF before ▶ ⇒ the exercise folder has no recording (S2 red — see
/// the last cells of the one test); the Replay Browser pointed at another root ⇒ S3's listing is empty
/// (the cell before it).
/// </summary>
[Collection("EqsIntegrationTests")]   // one DDS participant at a time, like the other constructed-host rails
public sealed class TheEditorsRecordingIsFilmedInTheReplayBrowserTests : IDisposable
{
    private const string Scenario = "hill-attack";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ce4102-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldRoot = Environment.GetEnvironmentVariable("FDP_STAGING_ROOT");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static int _domain;

    public TheEditorsRecordingIsFilmedInTheReplayBrowserTests()
    {
        // ⭐ ONE staging root for both hosts, as FDP_STAGING_ROOT gives two processes; restored in Dispose (the suite shares a process)
        Environment.SetEnvironmentVariable("FDP_STAGING_ROOT", _root);
        // ⭐ what the host start does for a dev checkout (assets, terrain, recipes into the shared root) — an unseeded root leaves the load's terrain / asset phase with nothing to stage
        ContentSeed.SeedIntoNas(OrchestrationConstants.GetSharedRoot(_root), TestRepo.Root);
        CopyDir(Path.Combine(TestRepo.Root, "scenarios", Scenario), Path.Combine(OrchestrationConstants.GetSharedScenariosRoot(_root), Scenario));
    }

    public void Dispose()
    {
        _http.Dispose();
        Environment.SetEnvironmentVariable("FDP_STAGING_ROOT", _oldRoot);
        try { Directory.Delete(_root, recursive: true); } catch { /* a recorder may still hold a file: the temp folder is the OS's */ }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // ── the editor's half ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The recording files of every exercise under the staging root: <c>{root}/nodes/node-N/exercises/&lt;id&gt;/node_N.fdp</c> (where the editor's own node records).</summary>
    private string[] RecordingsOnDisk()
        => Directory.Exists(_root) ? Directory.GetFiles(_root, "node_*.fdp", SearchOption.AllDirectories) : Array.Empty<string>();

    private sealed record Run(Guid Exercise, RunModeChoice Choice, ClusterTimeTransportAdapter Transport);

    /// <summary>
    /// The editor's half of the chain, up to ■: opens <see cref="Scenario"/> (the operator's File › Open), arms Live through the choice, ▶, lets the run record for
    /// <paramref name="liveSeconds"/> of wall time, ■. Asserts S1 (the run is live, Live armed, recording) and the first half of S2 (back in Edit, Live still armed) on the way.
    /// </summary>
    private Run RecordALiveRun(EditorSubsystem editor, bool recordOn, double liveSeconds = 8, bool stop = true)
    {
        var choice = editor.RunChoiceForTest ?? throw new InvalidOperationException("the editor has no run choice");
        var transport = editor.TimeTransportForTest!;

        if (transport.Mode != TransportMode.Edit)   // a second run on the same editor starts where ■ left it: editing the scenario (R2)
        {
            Pump(editor, () => choice.Gate.Open, 60, "the run gate open (the cluster is ready: CE-1442)");   // ⭐ CE-1442 — an open before the master's state has reached the node is refused by the door
            choice.OpenScenario(Scenario);                                               // the operator's open: loads it for editing, sends the host's armed mode (Preview)
            Pump(editor, () => transport.Mode == TransportMode.Edit, 60, "the scenario open for editing (OperatingEdit)");
            Assert.Equal(RunArmed.Preview, choice.Armed);                                // the editor's default, sent by the open (CE-3341)
        }

        if (!recordOn)   // the red-proof: the cluster's "Record live runs" setting OFF before ▶ (the same op POST /run-settings sends)
        {
            editor.OrchestrationBusForTest!.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), RecordLiveRuns = false });
            Pump(editor, () => !transport.RecordsLiveRuns, 10, "Record live runs OFF heard by the editor");
        }

        choice.Choose(RunArmed.Live);                                                    // the indicator's menu: R1, the ONLY place Armed changes
        Pump(editor, () => ((IRunTransport)transport).ClusterArmed == RunArmed.Live, 10, "the cluster's armed mode = Live");
        Assert.Equal(RunArmed.Live, choice.Armed);

        choice.Play();                                                                   // ▶ from Edit with Live armed: the unsaved check, then the live load
        Pump(editor, () => transport.Mode == TransportMode.Live, 90, "the cluster in OperatingLive");
        // ── S1 ──
        Assert.Equal(RunArmed.Live, choice.Armed);
        Assert.Equal(recordOn, transport.RecordsLiveRuns);                               // the transport says whether this run records
        var exercise = transport.ActiveExercise;
        Assert.NotEqual(Guid.Empty, exercise);

        PumpFor(editor, liveSeconds);                                                    // the run advances; the recorder captures frames
        if (!stop) return new Run(exercise, choice, transport);                          // CE-4104: the caller leaves the live run itself (→ AAR)

        choice.Stop();                                                                   // ■ per R2: back to editing the scenario, the armed mode stays
        Pump(editor, () => transport.Mode == TransportMode.Edit, 60, "back in OperatingEdit after ■ (R2)");
        PumpFor(editor, 1);                                                              // the recorder's close lands in the frames after the leave
        Assert.Equal(RunArmed.Live, choice.Armed);                                       // ── S2 (editor side): Live stays armed
        Assert.Equal(exercise, choice.LastExercise);
        return new Run(exercise, choice, transport);
    }

    // ── the Replay Browser's half: a real DebugApiHost in front of a real ReplayBrowserSubsystem ──────────────────

    private sealed class BrowserRig : IDisposable
    {
        public ReplayBrowserSubsystem Browser { get; }
        public DebugApiHost Host { get; }
        public MainThreadJobQueue Queue { get; } = new();
        public string Url { get; }
        private readonly HttpClient _http;

        public BrowserRig(string recordingsRoot, HttpClient http, int domain)
        {
            _http = http;
            Browser = new ReplayBrowserSubsystem();
            Browser.Initialize(new SubsystemConfig { DomainId = domain, Headless = true, OwnWindow = false, NodeId = 0, SubsystemName = "ReplayBrowser" });
            Browser.RecordingsRoot = recordingsRoot;
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
            Host = new DebugApiHost(port, Queue, () => { });
            var provider = ((Hrot.Presentation.DebugApi.IProvidesDebugSurface)Browser).CreateDebugProvider()!;
            Host.AttachService(new DebugApiService(
                new Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher(new[] { provider }, () => "ReplayBrowser", acksPending: null)));
            Host.AttachDirector(((Hrot.UI.Common.Map3D.Director.IProvidesDirector)Browser).Director!);   // as the runner attaches the browser's pack
            Host.Start();
            Url = $"http://localhost:{port}";
        }

        /// <summary>One request, answered while the browser's frames run (routes run on the "main thread": this one).</summary>
        public async Task<(HttpStatusCode Status, JsonNode Body)> Call(HttpMethod method, string route, string? json = null)
        {
            var request = new HttpRequestMessage(method, Url + route);
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var call = _http.SendAsync(request);
            for (int i = 0; i < 6000 && !call.IsCompleted; i++) { Queue.DrainAll(); Browser.Update(0.016f); await Task.Delay(5); }
            var response = await call;
            return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
        }

        public void Dispose() { Host.Dispose(); Browser.Shutdown(); }
    }

    // ── S1 · S2 · S3 · S4 (+ the two red-proof cells) ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ ONE test, ONE editor: the red-proof cells ride on this editor / this root instead of each building their own (cheaper — each editor boots a DDS participant).
    /// ⚠ The earlier "a second editor in the process never leaves Idle" was <c>CE-4103</c>: <c>ClusterConfiguration.Default</c> froze the FIRST root of the process
    /// (see <see cref="ASecondEditorInTheSameProcessOpensItsScenario"/>).
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task TheEditorsLiveRunIsRecorded_ListedByTheReplayBrowser_OpenedAndFilmedByTheDirector()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true, DomainId = 240 + Interlocked.Increment(ref _domain) });
        try
        {
            var run = RecordALiveRun(editor, recordOn: true);                            // S1 + S2 (editor side) asserted inside

            // ── S2 — exactly one exercise on disk: a node_*.fdp and its sidecar naming the run's exercise ──
            var fdp = Assert.Single(RecordingsOnDisk());
            Assert.True(new FileInfo(fdp).Length > 0, "the recording is empty");
            Assert.Equal(run.Exercise.ToString(), Path.GetFileName(Path.GetDirectoryName(fdp)));   // .../exercises/<the run's exercise>/node_N.fdp
            var sidecar = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(fdp)!, "*.meta.json"));
            var meta = JsonNode.Parse(File.ReadAllText(sidecar))!;
            Assert.Equal(run.Exercise, Guid.Parse(meta["ExerciseId"]!.ToString()));
            Assert.Equal(Scenario, meta["ScenarioName"]!.ToString());

            using (var rb = new BrowserRig(_root, _http, 240 + Interlocked.Increment(ref _domain)))
            {
                // ── S3 — the Replay Browser over the same root lists it (id + the scenario opened), and opens it ──
                var (status, body) = await rb.Call(HttpMethod.Get, "/exercises");
                Assert.Equal(HttpStatusCode.OK, status);
                var listed = body["data"]!["exercises"]!.AsArray().Select(e => e!).Single(e => Guid.Parse(e["exerciseId"]!.ToString()) == run.Exercise);
                Assert.Equal(Scenario, listed["scenario"]!.ToString());

                (status, body) = await rb.Call(HttpMethod.Post, "/replay", $$"""{"exerciseId":"{{run.Exercise}}"}""");
                Assert.True(status == HttpStatusCode.OK, body.ToJsonString());
                Assert.True(rb.Browser.Manager is { Contexts.Count: > 0 }, "no recording group is open on the Replay Browser");
                Assert.NotNull(rb.Browser.ActiveRepo);
                Assert.True(rb.Browser.ActiveRepo!.EntityCount > 0, "the opened recording holds no entities");
                Assert.True(rb.Browser.ReplayEndTicks > rb.Browser.ReplayZeroTicks, "the opened recording has no span");

                // ── S4 — the director plays an inline take of two time-anchored shots on it, to the end ──
                var take = """
                {"scenario":"hill-attack","recording":"%ID%","speed":4,"take":{"shots":[
                  {"at":1,"seconds":2,"lookAt":[668,522,0],"distance":150,"yaw":45,"pitch":-35},
                  {"at":3,"seconds":2,"lookAt":[668,522,0],"distance":150,"yaw":45,"pitch":-35}]}}
                """.Replace("%ID%", run.Exercise.ToString());
                (status, body) = await rb.Call(HttpMethod.Post, "/director/play", take);
                Assert.True(status == HttpStatusCode.OK, body.ToJsonString());

                JsonNode? last = null;
                for (int i = 0; i < 600; i++)   // up to ~60 s of wall time; the browser's frames run inside Call
                {
                    (status, body) = await rb.Call(HttpMethod.Get, "/director/status");
                    Assert.Equal(HttpStatusCode.OK, status);
                    last = body["data"];
                    if (last!["phase"]!.ToString() is "Done" or "Stopped" or "Idle") break;
                    await Task.Delay(100);
                }
                Assert.Equal("Done", last!["phase"]!.ToString());
                Assert.True(string.IsNullOrEmpty(last["error"]?.ToString()), "director error: " + last["error"]);
                var report = Assert.Single(last["reports"]!.AsArray())!;
                Assert.True(report["status"]!.ToString() is "ok" or "done", "report status: " + report["status"]);
                Assert.Empty(report["problems"]!.AsArray());
                Assert.Equal(2, report["cuts"]!.AsArray().Count);                       // a cut per shot
            }

            // ── RED-PROOF of S3: a Replay Browser pointed at ANOTHER root lists nothing of a run that WAS recorded ──
            var elsewhere = Path.Combine(_root, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            using (var other = new BrowserRig(elsewhere, _http, 240 + Interlocked.Increment(ref _domain)))
            {
                var (_, body) = await other.Call(HttpMethod.Get, "/exercises");
                Assert.Empty(body["data"]!["exercises"]!.AsArray());
            }

            // ── RED-PROOF of S2: Record live runs OFF before play - the run happens (S1's live state) and NOTHING new is on disk ──
            RecordALiveRun(editor, recordOn: false, liveSeconds: 3);
            Assert.Single(RecordingsOnDisk());                                           // still only the first run's
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4104</c> switch 1 (AQ108 §3 / D21, handoff <c>HANDOFF_Coordinator_To_Ui_Mode_Switches.md</c> row 1) — <b>Live → AAR</b>, through the real headless editor, ONE editor (a second in the process is the CE-4103 trap),
    /// both entries in sequence: A — the indicator (<c>Choose(AAR)</c> asks, confirming opens THAT run's recording); B — the timeline's <i>AAR from here</i> (no ask, at the clicked moment). Then ■ in AAR ⇒ the recording's start, still AAR (R2),
    /// and the recording is listed + opened by the Replay Browser. 🔴 Red-proof: <c>AarFromHere</c> seeking to 0 instead of <c>atSimSeconds</c> ⇒ the position row; the confirm text row breaks on any wording change.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE4104_LiveToAar_BothEntries_OpenTheRunsRecording_StopGoesToItsStart_AndTheReplayBrowserOpensIt()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true, DomainId = 240 + Interlocked.Increment(ref _domain) });
        try
        {
            // ── Entry A — the indicator ──
            var run = RecordALiveRun(editor, recordOn: true, liveSeconds: 6, stop: false);
            var choice = run.Choice; var transport = run.Transport;
            Assert.Equal(TransportMode.Live, transport.Mode);
            choice.Choose(RunArmed.AAR);
            Assert.NotNull(choice.Pending);
            Assert.Equal("Ends the live session; its recording opens for AAR", choice.Pending!.Text);
            Assert.Equal(TransportMode.Live, transport.Mode);                            // asking changes nothing
            choice.Pending.Resolve(choice.Pending.Buttons[0]);                           // "End live, open AAR"
            Pump(editor, () => transport.Mode == TransportMode.Replay, 60, "OperatingReplay after confirming Live → AAR");
            Assert.Equal(run.Exercise, transport.ActiveExercise);                        // THAT run's exercise
            Assert.Equal(RunArmed.AAR, choice.Armed);
            Assert.Equal(run.Exercise, choice.LastExercise);

            // back to editing, then a second live run for Entry B
            choice.Choose(RunArmed.Preview);
            if (choice.Pending is { } toEdit) toEdit.Resolve(toEdit.Buttons[0]);
            Pump(editor, () => transport.Mode == TransportMode.Edit, 60, "back to OperatingEdit from the AAR");

            // ── Entry B — AAR from here, at 3 s of a >= 6 s run, no confirmation ──
            var run2 = RecordALiveRun(editor, recordOn: true, liveSeconds: 8, stop: false);
            Assert.NotEqual(run.Exercise, run2.Exercise);
            Assert.True(transport.TotalTime >= 6, $"the run is only {transport.TotalTime:0.0} s long");
            Assert.Null(choice.AarFromHereDisabledReason);
            choice.AarFromHere(3.0);
            Assert.Null(choice.Pending);                                                 // the user picked the moment: no ask
            Pump(editor, () => transport.Mode == TransportMode.Replay, 60, "OperatingReplay after AAR from here");
            Assert.Equal(run2.Exercise, transport.ActiveExercise);
            Assert.Equal(RunArmed.AAR, choice.Armed);
            PumpFor(editor, 2);                                                          // the seek lands
            Assert.InRange(transport.TotalTime, 3.0 - 0.15, 3.0 + 0.15);                 // positioned at the clicked moment (a frame or two)

            // ── ■ in AAR — the recording's start, still AAR (R2) ──
            choice.Stop();
            Pump(editor, () => transport.TotalTime < 0.15, 30, "the replay back at its start after ■");
            Assert.Equal(TransportMode.Replay, transport.Mode);
            Assert.Equal(RunArmed.AAR, choice.Armed);
            Assert.Equal(run2.Exercise, transport.ActiveExercise);

            // ── the Replay Browser lists and opens the same recording ──
            using var rb = new BrowserRig(_root, _http, 240 + Interlocked.Increment(ref _domain));
            var (status, body) = await rb.Call(HttpMethod.Get, "/exercises");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(body["data"]!["exercises"]!.AsArray(), e => Guid.Parse(e!["exerciseId"]!.ToString()) == run2.Exercise);
            (status, body) = await rb.Call(HttpMethod.Post, "/replay", $$"""{"exerciseId":"{{run2.Exercise}}"}""");
            Assert.True(status == HttpStatusCode.OK, body.ToJsonString());
            Assert.True(rb.Browser.ActiveRepo is { EntityCount: > 0 }, "the Replay Browser opened an empty recording");
            Console.WriteLine($"CE4104 exercises: A={run.Exercise} B={run2.Exercise}");
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>The parent's wall ticks at <paramref name="t"/> on the facade's axis (the inverse of the cursor's conversion): what a branch taken there should record as its branch point.</summary>
    private static long WallAt(ClusterTimeTransportAdapter transport, double t)
    {
        var s = transport.Session!;
        return transport.Axis == ReplayAxis.Sim && s.TimeMap is { HasSimTime: true } map
            ? map.WallAt(t)
            : s.ZeroWallTicks + (long)Math.Round(t * TimeSpan.TicksPerSecond);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4104</c> switch 2 (AQ108 §3 / D18 / D21 / D5, handoff row 2) — <b>AAR → Live (the branch)</b>, through the real headless editor, ONE editor, both entries in sequence:
    /// A — the indicator (<c>Choose(Live)</c> asks, names the time, the button branches); ■ in the branched run ⇒ the PARENT's AAR at the branch point, Live still armed (D5);
    /// B — the timeline's <i>Go live from here</i> (no ask, branched at the seeked 3 s). The parent's recording stays byte-identical; a branch's recording opens in the Replay Browser.
    /// 🔴 Red-proofs: <c>Plan</c>'s Replay→Live not calling <c>BranchLive</c> ⇒ the new-exercise row; <c>GoLiveFromHere</c> without its seek ⇒ the branch-point row; StopLive ignoring the branch ⇒ the parent-AAR row.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE4104_AarToLive_BothEntries_BranchANewExercise_StopReturnsToTheParent_ParentUntouched()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true, DomainId = 240 + Interlocked.Increment(ref _domain) });
        try
        {
            // ── the parent: a >= 6 s recorded live run, opened as an AAR (switch 1's path) ──
            var parentRun = RecordALiveRun(editor, recordOn: true, liveSeconds: 7, stop: false);
            var choice = parentRun.Choice; var transport = parentRun.Transport; var parent = parentRun.Exercise;
            choice.Choose(RunArmed.AAR);
            choice.Pending!.Resolve(choice.Pending.Buttons[0]);
            Pump(editor, () => transport.Mode == TransportMode.Replay && transport.Session is { Active: true }, 60, "OperatingReplay on the parent");
            Assert.Equal(parent, transport.ActiveExercise);
            PumpFor(editor, 2);                                                          // the parent's recorder has closed
            var parentFile = Assert.Single(RecordingsOnDisk());
            var parentSize = new FileInfo(parentFile).Length;
            Assert.True(parentSize > 0);

            // ── Entry A — the indicator: Choose(Live) asks and names the time ──
            transport.Seek(4.0);
            PumpFor(editor, 2);                                                          // the seek lands (on a frame boundary: 4.27 s measured for a 4 s seek)
            Pump(editor, () => Math.Abs(transport.TotalTime - 4.0) < 0.5, 30, "the AAR at ~4 s");
            var atA = transport.TotalTime;
            var expectedWallA = WallAt(transport, 4.0);                                  // the seek target: the master branches where IT stands (the display leads it, see the report)
            choice.Choose(RunArmed.Live);
            Assert.NotNull(choice.Pending);
            Assert.Equal($"A new exercise starts from {Hrot.UI.Common.Panels.MainToolbarTimeControlSection.FormatSpan(atA)}; this recording stays unchanged", choice.Pending!.Text);
            Assert.Equal(TransportMode.Replay, transport.Mode);                          // asking changes nothing
            choice.Pending.Resolve(choice.Pending.Buttons[0]);                           // "Go live from here"
            Pump(editor, () => transport.Mode == TransportMode.Live && transport.ActiveExercise != parent, 90, "OperatingLive on a NEW exercise");
            var branchA = transport.ActiveExercise;
            Assert.NotEqual(parent, branchA);
            Assert.Equal(RunArmed.Live, choice.Armed);
            Pump(editor, () => transport.ActiveExerciseBranch is not null, 30, "the inventory names the branch's parent");
            var (pA, wallA) = transport.ActiveExerciseBranch!.Value;
            Assert.Equal(parent, pA);
            Assert.InRange(Math.Abs(wallA - expectedWallA), 0, TimeSpan.TicksPerSecond / 5);   // the branch point = the parent's moment (a few frames)
            PumpFor(editor, 4);                                                          // the branch records

            // ── ■ in the branched run ⇒ the PARENT's AAR at the branch point (D5), Live still armed ──
            choice.Stop();
            Pump(editor, () => transport.Mode == TransportMode.Replay && transport.ActiveExercise == parent && transport.Session is { Active: true }, 60, "the parent's AAR after ■ (D5)");
            Assert.Equal(RunArmed.Live, choice.Armed);
            Pump(editor, () => Math.Abs(transport.TotalTime - atA) < 0.3, 30, "the parent's AAR at the branch point");
            Assert.Equal(branchA, choice.LastExercise);

            // ── Entry B — the timeline's Go live from here, seek to ~3 s, no confirmation ──
            PumpFor(editor, 1);
            var expectedWallB = WallAt(transport, 3.0);
            Assert.Null(choice.GoLiveFromHereDisabledReason);
            choice.GoLiveFromHere(() => transport.Seek(3.0));
            Assert.Null(choice.Pending);
            Pump(editor, () => transport.Mode == TransportMode.Live && transport.ActiveExercise != parent && transport.ActiveExercise != branchA, 90, "OperatingLive on the second branch");
            var branchB = transport.ActiveExercise;
            Assert.Equal(RunArmed.Live, choice.Armed);
            Pump(editor, () => transport.ActiveExerciseBranch is not null, 30, "the inventory names the second branch's parent");
            var (pB, wallB) = transport.ActiveExerciseBranch!.Value;
            Assert.Equal(parent, pB);
            Assert.InRange(Math.Abs(wallB - expectedWallB), 0, TimeSpan.TicksPerSecond / 5);   // branched at the seeked 3 s, not where the replay stood (≈ 4 s)
            Assert.True(Math.Abs(wallB - expectedWallA) > TimeSpan.TicksPerSecond / 2, "branch B is not at A's point");
            PumpFor(editor, 3);
            choice.Stop();
            Pump(editor, () => transport.Mode == TransportMode.Replay && transport.ActiveExercise == parent, 60, "the parent's AAR after ■ of branch B");
            PumpFor(editor, 2);

            // ── the parent's recording is untouched; each branch has its own ──
            Assert.Equal(parentSize, new FileInfo(parentFile).Length);
            var files = RecordingsOnDisk();
            Assert.Equal(3, files.Length);
            Assert.Contains(files, f => Path.GetFileName(Path.GetDirectoryName(f)) == branchA.ToString());
            Assert.Contains(files, f => Path.GetFileName(Path.GetDirectoryName(f)) == branchB.ToString());

            // ── a branch's recording is listed (with its parent) and opens in the Replay Browser ──
            using var rb = new BrowserRig(_root, _http, 240 + Interlocked.Increment(ref _domain));
            var (status, body) = await rb.Call(HttpMethod.Get, "/exercises");
            Assert.Equal(HttpStatusCode.OK, status);
            var listed = body["data"]!["exercises"]!.AsArray().Select(e => e!).Single(e => Guid.Parse(e["exerciseId"]!.ToString()) == branchA);
            Assert.Equal(parent.ToString(), listed["parentExerciseId"]?.ToString());
            (status, body) = await rb.Call(HttpMethod.Post, "/replay", $$"""{"exerciseId":"{{branchA}}"}""");
            Assert.True(status == HttpStatusCode.OK, body.ToJsonString());
            Assert.True(rb.Browser.ActiveRepo is { EntityCount: > 0 }, "the Replay Browser opened an empty branch recording");
            Console.WriteLine($"CE4104-2 exercises: parent={parent} A={branchA} B={branchB}");
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4104</c> switches 3 + 4 (AQ108 §3 "Live → DAR", D9, R-3060; handoff rows 3 and 4) — through the real headless editor, a ≥ 7 s live run, ONE entry per editor
    /// (⚠ a SECOND DAR in the same run reports a bogus live point — see <see cref="LiveToDarAndBack"/>'s note — so each entry gets its own run):
    /// <b>Live → DAR</b> by the timeline's DAR-at-a-time (<see cref="Hrot.UI.Common.Adapters.ClusterTimelineCursor.EnterReview"/>, built over the editor's own node timeline as the editor builds it)
    /// and, in the sibling Fact, by the indicator's window item (the menu model's own action); each lands in the review at the ASKED moment (never the live end), the exercise unchanged, the live point frozen,
    /// <i>Go live from here</i> not offered. <b>DAR → Live</b> by the menu's ⏏ (here) / ■ (sibling): live is back at the live point, paused (the design: "live, paused"), ▶ runs it on. Then ■ the run: ONE recording, no branch, no gap.
    /// 🔴 Red-proofs: <c>EnterReviewAtSim</c> sending the live end ⇒ the position rows; ■ in a review stopping instead of exiting the review ⇒ the back-in-Live row.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public Task CE4104_LiveToDar_ByTheTimeline_AndBackToLiveByEject_OneContinuousRecording() => LiveToDarAndBack(viaTimeline: true);

    /// <summary>Switch 3's other entry (the indicator's <i>DAR — the last 30 s</i>) and switch 4's ■ in a review. See <see cref="CE4104_LiveToDar_ByTheTimeline_AndBackToLiveByEject_OneContinuousRecording"/>.</summary>
    [Fdp.Testing.FlakyFact("CE-1447: the master reports the DAR's own position as the live point when this editor is not the first in the process (passes alone, fails 3 of 3 after the timeline Fact; ReviewProcessManager.cs:78-81 captures the live point only once the pause lands)", Timeout = 600_000), Trait("Category", "Flaky")]
    public Task CE4104_LiveToDar_ByTheIndicatorsWindow_AndBackToLiveByStop_OneContinuousRecording() => LiveToDarAndBack(viaTimeline: false);

    /// <remarks>
    /// ⛔ <b>Production finding (reported, not fixed here — backend's master):</b> a SECOND DAR in the same live run (entered after a first DAR's return) reports <c>LivePointSimSeconds</c> = the DAR's own position (0.3 s) instead of the live end (8.1 s),
    /// and ■/⏏ then leaves live at that 0.3 s (<c>ReplaySessionProcessManager.cs:66-69</c> reads <c>_review.LivePoint</c>). Measured: the first DAR's live point is right in both orders.
    /// </remarks>
    private async Task LiveToDarAndBack(bool viaTimeline)
{
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true, DomainId = 240 + Interlocked.Increment(ref _domain) });
        try
        {
            var run = RecordALiveRun(editor, recordOn: true, liveSeconds: 7, stop: false);
            var choice = run.Choice; var transport = run.Transport; var exercise = run.Exercise;
            var section = new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(transport, choice: choice);
            var cursor = new Hrot.UI.Common.Adapters.ClusterTimelineCursor(() => transport, () => editor.NodeForTest?.ShownTimeline);
            Assert.True(transport.TotalTime >= 6, $"the run is only {transport.TotalTime:0.0} s long");

            // the shared checks of a DAR just entered: review mode, same exercise, live frozen, no "Go live from here"
            double EnteredDar(double asked, string what)
            {
                Pump(editor, () => transport.Mode == TransportMode.Review && transport.LivePoint is not null, 60, $"the DAR ({what}) with its live point");
                Assert.Equal(exercise, transport.ActiveExercise);
                var live = transport.LivePoint!.Value;
                Pump(editor, () => Math.Abs(transport.TotalTime - asked) < 0.6, 20, $"the DAR at the asked {asked:0.0} s (the entry pause lands a frame or two after the mode flips; the display leads ≈ 0.3 s)");
                Assert.InRange(transport.TotalTime, asked - 0.6, asked + 0.6);           // at the asked moment — ⛔ not the live end
                Assert.True(transport.TotalTime < live - 2.5, $"the DAR sits at the live end ({transport.TotalTime:0.0} vs live {live:0.0})");
                PumpFor(editor, 1.5);
                Assert.Equal(live, transport.LivePoint!.Value, 2);                       // live is paused: its point does not advance
                Assert.NotNull(choice.GoLiveFromHereDisabledReason);                     // handoff row 5: never offered in a DAR
                Assert.DoesNotContain(cursor.ModeMenuItems(transport.Session!.ZeroWallTicks, "0:03"), i => i.Label.StartsWith("Go live"));
                return live;
            }
            void BackInLive(double live, string how, int framesBefore)
            {
                Pump(editor, () => transport.Mode == TransportMode.Live, 60, $"back in OperatingLive by {how}");
                Assert.Equal(exercise, transport.ActiveExercise);                        // the same run: no branch, no new exercise
                Pump(editor, () => Math.Abs(transport.TotalTime - live) < 1.0, 20, $"the sim time back at the live point {live:0.0} s (the clock snaps a few frames after the mode flips)");
                Assert.InRange(transport.TotalTime, live - 1.0, live + 1.0);             // resumed at the live point, not the DAR's position
                PumpFor(editor, 1.5);
                Assert.InRange(transport.TotalTime, live - 1.0, live + 1.0);             // DESIGN_Replay_Time_Axis_And_Review §5 / row 19: "live, paused" — the return does not run the clock
                Assert.True(transport.IsPaused, $"live is running after {how}: the design says it returns paused");
                transport.TogglePlayPause();                                             // the user's ▶
                Pump(editor, () => transport.TotalTime > live + 1, 20, $"live running on from the live point after ▶ (from {how})");
                // ⭐ CE-1448 — the live timeline index is NOT restarted by the return: it holds at least the frames it held before the DAR (the node held its step until the world caught up)
                Assert.True(editor.NodeForTest!.ShownTimeline!.Index.FrameSim.Count >= framesBefore,
                    $"the live index restarted after {how}: {editor.NodeForTest.ShownTimeline.Index.FrameSim.Count} frames, {framesBefore} before the DAR");
            }

            if (viaTimeline)
            {
            // ── the timeline's DAR at ~3 s, then ⏏ (the cursor reads the editor's own live index) ──
            Assert.True(cursor.CanReview);
            var index = editor.NodeForTest!.ShownTimeline!.Index;
            int frame = 0; while (frame < index.FrameSim.Count - 1 && index.FrameSim[frame] < 3.0) frame++;
            var asked = index.FrameSim[frame];
            var liveB = transport.TotalTime;
            int framesBeforeB = index.FrameSim.Count;
            cursor.EnterReview(index.FrameTicks[frame]);
            var pausedAtB = EnteredDar(asked, "the timeline at 3 s");
            Assert.InRange(pausedAtB, liveB - 0.5, liveB + 1.5);
            var eject = section.ModeMenu()!.Items.First(i => i.Label.StartsWith("⏏"));
            eject.Invoke!();
            BackInLive(pausedAtB, "⏏", framesBeforeB);
            }
            else
            {
            // ── the indicator's window item, then ■ ──
            var liveA = transport.TotalTime;
            int framesBeforeA = editor.NodeForTest!.ShownTimeline!.Index.FrameSim.Count;
            var window = section.ModeMenu()!.Items.First(i => i.Label.StartsWith("DAR — the last 30 s"));
            Assert.True(window.Enabled);
            window.Invoke!();
            var pausedAtA = EnteredDar(Math.Max(0, liveA - 30), "the last 30 s");
            Assert.InRange(pausedAtA, liveA - 0.5, liveA + 1.5);                         // the live point = where live was when the DAR began
            choice.Stop();                                                               // R-3060: ■ in a review = back to live
            BackInLive(pausedAtA, "■", framesBeforeA);
            }

            // ── ■ the run: ONE recording, covering the live time, no gap, no branch ──
            var totalLive = transport.TotalTime;
            choice.Stop();
            Pump(editor, () => transport.Mode == TransportMode.Edit, 60, "back in OperatingEdit after ■");
            PumpFor(editor, 2);
            var fdp = Assert.Single(RecordingsOnDisk());
            Assert.Equal(exercise.ToString(), Path.GetFileName(Path.GetDirectoryName(fdp)));
            choice.OpenRecording(exercise);
            Pump(editor, () => transport.Mode == TransportMode.Replay && transport.Session is { Active: true } && transport.ActiveExercise == exercise, 60, "the run's recording open as an AAR");
            var s = transport.Session!;
            var lastSim = s.TimeMap is { HasSimTime: true } map ? map.SimAt(s.ZeroWallTicks + (long)Math.Round(s.LengthSeconds * TimeSpan.TicksPerSecond)) : s.LengthSeconds;
            Console.WriteLine($"CE4104-3/4 exercise={exercise} totalLive={totalLive:0.00} recordingLastSim={lastSim:0.00} lengthSeconds={s.LengthSeconds:0.00}");
            Assert.InRange(lastSim, totalLive - 0.5, totalLive + 0.5);                   // the DAR time is not added as a gap

            using var rb = new BrowserRig(_root, _http, 240 + Interlocked.Increment(ref _domain));
            var (status, body) = await rb.Call(HttpMethod.Get, "/exercises");
            Assert.Equal(HttpStatusCode.OK, status);
            var listed = body["data"]!["exercises"]!.AsArray().Select(e => e!).Single(e => Guid.Parse(e["exerciseId"]!.ToString()) == exercise);
            Assert.True(listed["parentExerciseId"] is null || string.IsNullOrEmpty(listed["parentExerciseId"]!.ToString()), "the run has a parent: " + listed["parentExerciseId"]);
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4103</c> — a SECOND <see cref="EditorSubsystem"/> in one process (this class's tests each build their own staging root, so the second one's root is NOT
    /// the first's) opens its scenario and reaches <c>OperatingEdit</c>. 📐 It used to prepare its load against the FIRST root's NAS (<c>ClusterConfiguration.Default</c>
    /// cached the environment once per process): <c>NAS source directory … does not exist</c>, the load abandoned, the view at Idle. Run with the other Fact in one process.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void ASecondEditorInTheSameProcessOpensItsScenario()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true, DomainId = 240 + Interlocked.Increment(ref _domain) });
        try
        {
            var choice = editor.RunChoiceForTest ?? throw new InvalidOperationException("the editor has no run choice");
            var transport = editor.TimeTransportForTest!;
            // ⭐ CE-1442 — was: *no warm-up pump* (an open before the node's first heartbeat reached OperatingEdit too, CE-4103 ①). The run gate now REFUSES that early open at the door (by design: the controls are closed until the cluster is ready), so the rail waits on the gate.
            Pump(editor, () => choice.Gate.Open, 60, "the run gate open (the cluster is ready: CE-1442)");
            choice.OpenScenario(Scenario);
            Pump(editor, () => transport.Mode == TransportMode.Edit, 60, "the scenario open for editing (OperatingEdit)");
        }
        finally { editor.Shutdown(); }
    }
}
