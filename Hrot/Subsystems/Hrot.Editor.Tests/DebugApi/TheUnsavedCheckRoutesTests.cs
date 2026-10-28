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

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<(HttpStatusCode Status, JsonNode Body)> Send(MainThreadJobQueue queue, Task<HttpResponseMessage> request)
    {
        for (int i = 0; i < 4000 && !request.IsCompleted; i++) { queue.DrainAll(); await Task.Delay(5); }
        var response = await request;
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    private int Rounds => _master.Sent.OfType<CheckUnsavedIntent>().Count();

    /// <summary>The cluster read model: a round the master was asked for is answered as the test set it up (an answer to the id it was asked under).</summary>
    private UnsavedCheckOutcome? Outcome(Guid id)
        => _master.Sent.OfType<CheckUnsavedIntent>().Any(i => i.RequestId == id)
            ? (_refusal is not null ? new UnsavedCheckOutcome(null, _refusal) : new UnsavedCheckOutcome(_verdict, null))
            : null;

    private (DebugApiHost Host, MainThreadJobQueue Queue, string Url) Start(bool observesChecks = true)
    {
        // the --mode all shape: two providers, so no single-provider fallback can hide a wrong route; the read model is the cgf-less one that keeps a cache
        var dispatcher = new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("IG", "IG"),
                new SubsystemDebugProvider("CGF", "CGF", master: () => _master,
                    unsavedCheck: observesChecks ? Outcome : null, scenarioDirty: observesChecks ? () => _lastVerdictInReadModel : null),
            },
            currentPerspective: () => "CGF", acksPending: null);
        // the unload before a load is "reached" the moment the route asked for it
        var service = new DebugApiService(dispatcher, clusterState: () => _master.Transitions.Any(t => t.TargetState == ClusterState.Idle) ? ClusterState.Idle : _state);
        int port = FreePort();
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
        var (status, body) = await Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body["data"]!["changed"]!.GetValue<bool>());
        Assert.Empty(body["data"]!["changedNodes"]!.AsArray());
        Assert.Equal(1, Rounds);

        // changed, with the nodes named
        _verdict = new UnsavedCheckResult(true, new[] { 2, 5 });
        (status, body) = await Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body["data"]!["changed"]!.GetValue<bool>());
        Assert.Equal(new[] { 2, 5 }, body["data"]!["changedNodes"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray());
        Assert.Equal(2, Rounds);                                                  // a round PER call — never a cached answer

        // the master refuses outside editing: a state conflict
        _refusal = UnsavedCheckOutcome.NotEditingReason;
        (status, body) = await Send(queue, _client.PostAsync(url + "/scenario/check-unsaved", Json("{}")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("editing", body["error"]!.GetValue<string>());
    }

    [Fact(Timeout = 90_000)]
    public async Task LoadLive_WhileTheScenarioIsChanged_Answers409_UnlessDiscardUnsaved_AndProceedsWhenUnchanged()
    {
        var (host, queue, url) = Start();
        using var h = host;

        // ── changed ⇒ 409, nothing sent to the master but the check itself ───────────────────────────────────────────────────────────
        _verdict = new UnsavedCheckResult(true, new[] { 3 });
        var (status, body) = await Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("the scenario has unsaved changes", body["error"]!.GetValue<string>());
        Assert.Contains("discardUnsaved", body["error"]!.GetValue<string>());
        Assert.Equal(1, Rounds);
        Assert.Empty(_master.Transitions);                                        // ⭐ the load was NOT published

        // ── discardUnsaved ⇒ no round at all, the load goes out ────────────────────────────────────────────────────────────────────────
        (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack","discardUnsaved":true}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rounds);                                                  // the gate did not even ask
        Assert.Equal(ClusterState.OperatingLive, _master.Transitions.Last().TargetState);
        _master.Sent.Clear(); _master.Transitions.Clear();

        // ── unchanged ⇒ the load goes out after exactly one round ────────────────────────────────────────────────────────────────────
        _verdict = UnsavedCheckResult.Unchanged;
        (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rounds);
        Assert.Equal(ClusterState.OperatingLive, _master.Transitions.Last().TargetState);
    }

    /// <summary>One helper for both loads: a load of ANOTHER scenario for editing is gated the same way (a host that has no editor driver cannot tell it is the same one).</summary>
    [Fact(Timeout = 90_000)]
    public async Task LoadEdit_OfAnotherScenario_IsGatedByTheSameCheck()
    {
        var (host, queue, url) = Start();
        using var h = host;

        _verdict = new UnsavedCheckResult(true, new[] { 3 });
        var (status, body) = await Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json("""{"name":"other"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("the scenario has unsaved changes", body["error"]!.GetValue<string>());
        Assert.Empty(_master.Transitions);

        (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/load/edit", Json("""{"name":"other","discardUnsaved":true}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ClusterState.OperatingEdit, _master.Transitions.Last().TargetState);
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

        var (status, _x) = await Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));

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

        var got = Assert.IsType<UnsavedCheckResult>(Hrot.Common.Orchestration.OrchestrationObserverTranslator.DecodeResult(wire));
        Assert.True(got.Changed);
        Assert.Equal(new[] { 4, 9 }, got.ChangedNodes);

        string empty = System.Text.Json.JsonSerializer.Serialize(UnsavedCheckResult.Unchanged, Hrot.Network.Orchestration.OrchestrationJsonOptions.Default);
        Assert.False(Assert.IsType<UnsavedCheckResult>(Hrot.Common.Orchestration.OrchestrationObserverTranslator.DecodeResult(empty)).Changed);

        // what it decoded before stays: a replay result is still a replay result; a refusal's plain text is still not JSON (the caller keeps the text)
        var replay = System.Text.Json.JsonSerializer.Serialize(new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 12f), Hrot.Network.Orchestration.OrchestrationJsonOptions.Default);
        Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(Hrot.Common.Orchestration.OrchestrationObserverTranslator.DecodeResult(replay));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Hrot.Common.Orchestration.OrchestrationObserverTranslator.DecodeResult(UnsavedCheckOutcome.NotEditingReason));
    }

    /// <summary><c>GET /status.scenarioDirty</c> is the LAST verdict — null when none, never a round of its own.</summary>
    [Fact(Timeout = 90_000)]
    public async Task Status_ReportsTheLastVerdict_AndNeverRunsARound()
    {
        var (host, queue, url) = Start();
        using var h = host;

        var (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(body["data"]!["scenarioDirty"]);                               // none ran

        _lastVerdictInReadModel = true;
        (_, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.True(body["data"]!["scenarioDirty"]!.GetValue<bool>());
        _lastVerdictInReadModel = false;
        (_, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.False(body["data"]!["scenarioDirty"]!.GetValue<bool>());

        Assert.Equal(0, Rounds);                                                  // ⭐ reading it asked nobody
        Assert.Empty(_master.Sent);
    }
}
