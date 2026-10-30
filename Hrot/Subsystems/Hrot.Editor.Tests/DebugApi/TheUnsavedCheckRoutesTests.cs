using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <c>CE-4093</c> (AQ108 D20, D22) — <b>the unsaved-changes CHECK over HTTP</b>, on a REAL <see cref="DebugApiHost"/> on a loopback port:
/// <c>POST /scenario/check-unsaved</c> asks the master for ONE round and returns <c>{changed, changedNodes}</c> (409 when the cluster is not editing);
/// <c>POST /scenario/load/live</c> (and a load of ANOTHER scenario for edit) runs that round at its TOP through ONE helper and answers <b>409</b>
/// <i>"the scenario has unsaved changes"</i> while it says changed — unless the body says <c>discardUnsaved: true</c>; <c>GET /status.scenarioDirty</c> reports
/// the LAST verdict and never runs a round. 🔴 Mutations (each compiles): the gate not called from <c>HandleScenarioLoad</c> ⇒ the 409 cells red; the gate
/// ignoring <c>discardUnsaved</c> ⇒ the discard cell red; the gate asking outside editing ⇒ the "asks nothing" cells red; <c>scenarioDirty</c> running a round
/// (publishing a <c>CheckUnsavedIntent</c>) ⇒ the "never runs one" cell red; <c>HandleCheckUnsaved</c> not mapping the refusal to 409 ⇒ the refusal cell red.
/// </summary>
public sealed class TheUnsavedCheckRoutesTests : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };
    public void Dispose() => _client.Dispose();

    private readonly RecordingMasterControl _master = new();
    private ClusterState _state = ClusterState.OperatingEdit;
    private UnsavedCheckResult? _verdict = UnsavedCheckResult.Unchanged;   // what the master answers to the next round
    private string? _refusal;                                              // …or this refusal
    private bool? _lastVerdictInReadModel;                                 // GET /status.scenarioDirty reads this

    private int Rounds => _master.Sent.OfType<CheckUnsavedIntent>().Count();

    /// <summary>The cluster read model: a round the master was asked for is answered as the test set it up (an answer to the id it was asked under).</summary>
    private readonly System.Collections.Generic.List<Guid> _readIds = new();

    private UnsavedCheckOutcome? Outcome(Guid id)
        => Record(id) && _master.Sent.OfType<CheckUnsavedIntent>().Any(i => i.RequestId == id) && Waited(id)
            ? (_refusal is not null ? new UnsavedCheckOutcome(null, _refusal) : new UnsavedCheckOutcome(_verdict, null))
            : null;

    private bool Record(Guid id) { lock (_readIds) _readIds.Add(id); return true; }

    /// <summary>
    /// ⭐⭐ CE-1440 (R-3066 audit #13) — HTTP asks through the ONE shared asker (<c>UnsavedCheckAsker</c>, the type the toolbar's move uses): the request id the master was asked under is the
    /// id every answer read carries. 🔴 Mutation: <c>DebugApiService.UnsavedAsker</c> reading under a fresh <c>Guid.NewGuid()</c> ⇒ the cell never gets its answer ⇒ red (504-style hang).
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task CE1440_TheCheckRound_FlowsOneRequestIdFromThePublishToTheAnswerRead()
    {
        var (host, queue, url) = Start();
        using var h = host;
        var (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        var asked = Assert.Single(_master.Sent.OfType<CheckUnsavedIntent>());
        Guid[] reads; lock (_readIds) reads = _readIds.ToArray();
        Assert.NotEmpty(reads);
        Assert.All(reads, id => Assert.Equal(asked.RequestId, id));
    }

    /// <summary>⭐ CE-3340 ⑤ — how long the master takes to end a round (a node dead mid-round: until its heartbeat times out); zero = at once.</summary>
    private TimeSpan _roundTakes = TimeSpan.Zero;
    private readonly System.Collections.Generic.Dictionary<Guid, System.Diagnostics.Stopwatch> _askedAt = new();

    private bool Waited(Guid id)
    {
        if (!_askedAt.TryGetValue(id, out var sw)) _askedAt[id] = sw = System.Diagnostics.Stopwatch.StartNew();
        return sw.Elapsed >= _roundTakes;
    }

    private (DebugApiHost Host, MainThreadJobQueue Queue, string Url) Start(bool observesChecks = true, ExerciseInventoryItem[]? exercises = null)
    {
        // the --mode all shape: two providers, so no single-provider fallback can hide a wrong route; the read model is the cgf-less one that keeps a cache
        var dispatcher = new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("IG", "IG"),
                new SubsystemDebugProvider("CGF", "CGF", master: () => _master, exercises: exercises is null ? null : () => exercises,
                    unsavedCheck: observesChecks ? Outcome : null, scenarioDirty: observesChecks ? () => _lastVerdictInReadModel : null),
            },
            currentPerspective: () => "CGF", acksPending: null);
        // the unload before a load is "reached" the moment the route asked for it
        var service = new DebugApiService(dispatcher, clusterState: () => _master.Transitions.Any(t => t.TargetState == ClusterState.Idle) ? ClusterState.Idle : _state);
        int port = DebugApiTestHost.FreePort();
        var queue = new MainThreadJobQueue();
        var host = new DebugApiHost(port, queue, () => { });
        host.AttachDispatcher(dispatcher);
        host.AttachService(service);
        host.Start();
        return (host, queue, $"http://localhost:{port}");
    }

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    [Fact(Timeout = 90_000)]
    public async Task CheckUnsaved_RunsOneRound_AndReturnsTheMastersAnswer_Or409OutsideEditing()
    {
        var (host, queue, url) = Start();
        using var h = host;

        // unchanged
        var (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body["data"]!["changed"]!.GetValue<bool>());
        Assert.Empty(body["data"]!["changedNodes"]!.AsArray());
        Assert.Equal(1, Rounds);

        // changed, with the nodes named
        _verdict = new UnsavedCheckResult(true, new[] { 2, 5 });
        (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body["data"]!["changed"]!.GetValue<bool>());
        Assert.Equal(new[] { 2, 5 }, body["data"]!["changedNodes"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray());
        Assert.Equal(2, Rounds);                                                  // a round PER call — never a cached answer

        // the master refuses outside editing: a state conflict
        _refusal = UnsavedCheckOutcome.NotEditingReason;
        (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("editing", body["error"]!.GetValue<string>());
    }

    /// <summary>
    /// ⭐⭐ CE-3340 ⑤ (R-4001) — the gate has NO deadline of its own: a round the master takes 17 s to end (a node died mid-round; its heartbeat timeout ejects it and
    /// the round completes on the rest — <c>ClusterMasterUnsavedCheckTests.ARoundWhoseNodeIsEjected_StillCompletes_OnTheNodesThatRemain</c>) is still answered, both
    /// by <c>POST /scenario/check-unsaved</c> and by the load's gate (409 "unsaved changes") — never the old fixed 15 s 504. 🔴 Mutation: a 15 s deadline back in
    /// <c>RunCheckRoundAsync</c> ⇒ both cells answer 504 ⇒ red.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task ARoundTheMasterTakesLongToEnd_IsStillAnswered_NeverA504()
    {
        var (host, queue, url) = Start();
        using var h = host;
        _roundTakes = TimeSpan.FromSeconds(17);
        _verdict = new UnsavedCheckResult(true, new[] { 4 });

        var check = _client.PostAsync(url + "/scenario/check-unsaved", Json("{}"));
        var load = _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}"""));
        for (int i = 0; i < 6000 && !(check.IsCompleted && load.IsCompleted); i++) { queue.DrainAll(); await Task.Delay(5); }
        var checkResponse = await check;
        var loadResponse = await load;

        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, loadResponse.StatusCode);
        Assert.Empty(_master.Transitions);                                        // the gate held the load, it did not time out into it
    }

    [Fact(Timeout = 90_000)]
    public async Task LoadLive_WhileTheScenarioIsChanged_Answers409_UnlessDiscardUnsaved_AndProceedsWhenUnchanged()
    {
        var (host, queue, url) = Start();
        using var h = host;

        // ── changed ⇒ 409, nothing sent to the master but the check itself ───────────────────────────────────────────────────────────
        _verdict = new UnsavedCheckResult(true, new[] { 3 });
        var (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("the scenario has unsaved changes", body["error"]!.GetValue<string>());
        Assert.Contains("discardUnsaved", body["error"]!.GetValue<string>());
        Assert.Equal(1, Rounds);
        Assert.Empty(_master.Transitions);                                        // ⭐ the load was NOT published

        // ── discardUnsaved ⇒ no round at all, the load goes out ────────────────────────────────────────────────────────────────────────
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack","discardUnsaved":true}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rounds);                                                  // the gate did not even ask
        Assert.Equal(ClusterState.OperatingLive, _master.Transitions.Last().TargetState);
        _master.Sent.Clear(); _master.Transitions.Clear();

        // ── unchanged ⇒ the load goes out after exactly one round ────────────────────────────────────────────────────────────────────
        _verdict = UnsavedCheckResult.Unchanged;
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rounds);
        Assert.Equal(ClusterState.OperatingLive, _master.Transitions.Last().TargetState);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> (AQ108 §2.11a, D22) — HTTP <c>POST /replay/stop</c> is the OUT door, asked with the REFUSING asker: while EDITING with unsaved changes it answers <b>409</b> with the loads' own words (nothing published);
    /// <c>discardUnsaved</c> asks the cluster nothing and stops; a clean scenario costs one round; a LIVE run is stopped with no round at all. Every stop publishes the ONE Idle intent (equal to
    /// <c>ScenarioLoadSequence.UnloadIntent</c> apart from the TransactionId).
    /// 🔴 Mutation: <c>StopReplay</c> publishing without the door ⇒ the 409 cell sees an Idle transition; <c>RefusingAsker</c> proceeding on <c>Unsaved</c> ⇒ the same cell is red.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task CE4101_ReplayStop_WhileEditing_IsTheOutDoor_409OnUnsaved_ThenTheOneIdleIntent()
    {
        var (host, queue, url) = Start();
        using var h = host;

        _verdict = new UnsavedCheckResult(true, new[] { 2, 5 });
        var (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/replay/stop", Json("{}")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        var error = body["error"]!.GetValue<string>();
        Assert.Contains("the scenario has unsaved changes (nodes: 2,5)", error);
        Assert.Contains("discardUnsaved", error);
        Assert.Equal(1, Rounds);
        Assert.Empty(_master.Transitions);                                        // ⭐ nothing was published

        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/replay/stop", Json("""{"discardUnsaved":true}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rounds);                                                  // the discard asked no round
        var idle = Assert.Single(_master.Transitions);
        var one = ScenarioLoadSequence.UnloadIntent();
        one.TransactionId = idle.TransactionId;
        Assert.Equal(one, idle);                                                  // the ONE Idle intent

        _master.Sent.Clear(); _master.Transitions.Clear();
        _verdict = UnsavedCheckResult.Unchanged;
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/replay/stop", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rounds);                                                  // a clean scenario: one round, then it goes
        Assert.Equal(ClusterState.Idle, Assert.Single(_master.Transitions).TargetState);

        _master.Sent.Clear(); _master.Transitions.Clear();
        _state = ClusterState.OperatingLive;
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/replay/stop", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, Rounds);                                                  // a run holds no scenario: nothing is asked
        Assert.Equal(ClusterState.Idle, Assert.Single(_master.Transitions).TargetState);
    }

    /// <summary>One helper for both loads: a load of ANOTHER scenario for editing is gated the same way (a host that has no editor driver cannot tell it is the same one).</summary>
    [Fact(Timeout = 90_000)]
    public async Task LoadEdit_OfAnotherScenario_IsGatedByTheSameCheck()
    {
        var (host, queue, url) = Start();
        using var h = host;

        _verdict = new UnsavedCheckResult(true, new[] { 3 });
        var (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json("""{"name":"other"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("the scenario has unsaved changes", body["error"]!.GetValue<string>());
        Assert.Empty(_master.Transitions);

        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json("""{"name":"other","discardUnsaved":true}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ClusterState.OperatingEdit, _master.Transitions.Last().TargetState);
    }

    /// <summary>
    /// ⭐⭐ CE-4091 (AQ108 D7, D22) — <c>POST /scenario/load/edit {recorded: &lt;exerciseId&gt;}</c> opens a recording's scenario copy as a NEW scenario: the
    /// cluster is asked to load for editing the name <c>opened/recorded/&lt;id&gt;</c> (the master's prefetch resolves it to the copy) — and the route writes
    /// nothing and sends no save/storage request. 404 for an id the inventory does not list, 400 for a malformed id, for name + recorded together and
    /// for recorded on a LIVE load. The same unsaved-changes gate as any other load applies. 🔴 Mutations: the route ignoring <c>recorded</c> ⇒ 400
    /// "name is required"; sending the original scenario's name ⇒ the name cell red; no gate for the derived name ⇒ the 409 cell red.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task LoadEdit_OfARecording_LoadsItsCopyUnderTheRecordedName_AndTouchesNothingElse()
    {
        var known = new ExerciseInventoryItem(Guid.NewGuid(), new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(3), "town-raid");
        var (host, queue, url) = Start(exercises: new[] { known });
        using var h = host;

        // the open scenario is unchanged ⇒ the load goes out, naming the recorded copy — and ONLY a transition (no save, no storage op, no delete)
        var (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json($"{{\"recorded\":\"{known.ExerciseId}\"}}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(RecordedScenarioNames.ForExercise(known.ExerciseId), body["data"]!["loading"]!.GetValue<string>());
        var transition = _master.Transitions.Last();                                // (the unload to Idle goes first when a scenario is open)
        Assert.Equal(ClusterState.OperatingEdit, transition.TargetState);
        Assert.Equal("opened/recorded/" + known.ExerciseId, transition.ScenarioId);
        Assert.DoesNotContain(_master.Sent, s => s is ExecuteStorageOpIntent);
        _master.Sent.Clear(); _master.Transitions.Clear();

        // refusals — nothing reaches the master
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json($"{{\"recorded\":\"{Guid.NewGuid()}\"}}")));
        Assert.Equal(HttpStatusCode.NotFound, status);
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json("""{"recorded":"not-a-guid"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json($"{{\"recorded\":\"{known.ExerciseId}\",\"name\":\"other\"}}")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/live", Json($"{{\"recorded\":\"{known.ExerciseId}\"}}")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(_master.Transitions);
        Assert.Empty(_master.Sent);

        // the unsaved-changes gate is the ordinary one: edits in the open scenario ⇒ 409 until discardUnsaved
        _verdict = new UnsavedCheckResult(true, new[] { 3 });
        (status, body) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json($"{{\"recorded\":\"{known.ExerciseId}\"}}")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(_master.Transitions);
        (status, _) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json($"{{\"recorded\":\"{known.ExerciseId}\",\"discardUnsaved\":true}}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("opened/recorded/" + known.ExerciseId, _master.Transitions.Last().ScenarioId);
    }

    /// <summary>Nothing to lose outside editing (Idle, a live run, a replay): no round is asked, the load goes out. A host that cannot SEE the answer does not wait for one.</summary>
    [Theory(Timeout = 90_000)]
    [InlineData(ClusterState.Idle, true)]
    [InlineData(ClusterState.OperatingLive, true)]
    [InlineData(ClusterState.OperatingReplay, true)]
    [InlineData(ClusterState.OperatingEdit, false)]   // editing, but this host keeps no read model that sees the answer
    public async Task ALoad_WhereThereIsNothingToLose_OrNoWayToSeeTheAnswer_AsksNoRound(ClusterState state, bool observes)
    {
        _state = state;
        _verdict = new UnsavedCheckResult(true, new[] { 3 });   // it would say changed — if anybody asked
        var (host, queue, url) = Start(observesChecks: observes);
        using var h = host;

        var (status, _x) = await DebugApiTestHost.Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, Rounds);
        Assert.Contains(_master.Transitions, t => t.TargetState == ClusterState.OperatingLive);
    }

    /// <summary>
    /// The master's answer reaches a REMOTE host as JSON on <c>ClusterOpStatus</c>; the observer must hand back the typed result. 🔴 Without the recognition the
    /// replay result — which deserialises "successfully" from ANY JSON object — swallows it, and the host reads a default <c>ReplayPrepareResult</c> instead of the verdict.
    /// </summary>
    [Fact]
    public void TheVerdict_SurvivesTheWire_AndIsNotMistakenForAReplayResult()
    {
        var sent = new UnsavedCheckResult(true, new[] { 4, 9 });
        string wire = System.Text.Json.JsonSerializer.Serialize(sent, sent.GetType(), Hrot.Network.Orchestration.OrchestrationJsonOptions.Default);

        var got = Assert.IsType<UnsavedCheckResult>(Hrot.Common.Orchestration.ClusterFactsIngress.DecodeResult(wire));
        Assert.True(got.Changed);
        Assert.Equal(new[] { 4, 9 }, got.ChangedNodes);

        string empty = System.Text.Json.JsonSerializer.Serialize(UnsavedCheckResult.Unchanged, Hrot.Network.Orchestration.OrchestrationJsonOptions.Default);
        Assert.False(Assert.IsType<UnsavedCheckResult>(Hrot.Common.Orchestration.ClusterFactsIngress.DecodeResult(empty)).Changed);

        // what it decoded before stays: a replay result is still a replay result; a refusal's plain text is still not JSON (the caller keeps the text)
        var replay = System.Text.Json.JsonSerializer.Serialize(new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 12f), Hrot.Network.Orchestration.OrchestrationJsonOptions.Default);
        Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(Hrot.Common.Orchestration.ClusterFactsIngress.DecodeResult(replay));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Hrot.Common.Orchestration.ClusterFactsIngress.DecodeResult(UnsavedCheckOutcome.NotEditingReason));
    }

    /// <summary><c>GET /status.scenarioDirty</c> is the LAST verdict — null when none, never a round of its own.</summary>
    [Fact(Timeout = 90_000)]
    public async Task Status_ReportsTheLastVerdict_AndNeverRunsARound()
    {
        var (host, queue, url) = Start();
        using var h = host;

        var (status, body) = await DebugApiTestHost.Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(body["data"]!["scenarioDirty"]);                               // none ran

        _lastVerdictInReadModel = true;
        (_, body) = await DebugApiTestHost.Send(queue, _client.GetAsync(url + "/status"));
        Assert.True(body["data"]!["scenarioDirty"]!.GetValue<bool>());
        _lastVerdictInReadModel = false;
        (_, body) = await DebugApiTestHost.Send(queue, _client.GetAsync(url + "/status"));
        Assert.False(body["data"]!["scenarioDirty"]!.GetValue<bool>());

        Assert.Equal(0, Rounds);                                                  // ⭐ reading it asked nobody
        Assert.Empty(_master.Sent);
    }
}
