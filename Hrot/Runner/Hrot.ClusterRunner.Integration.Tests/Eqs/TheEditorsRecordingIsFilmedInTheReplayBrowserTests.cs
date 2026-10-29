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
    private Run RecordALiveRun(EditorSubsystem editor, bool recordOn, double liveSeconds = 8)
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
