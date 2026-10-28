using System;
using System.Collections.Generic;
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
/// ⭐⭐⭐ <c>CE-4090</c> (AQ108 D9, D14, D15, D22, §5a) — <b>"Live does not imply recording" over HTTP</b>, on a REAL <see cref="DebugApiHost"/> on a loopback
/// port: when the run the cluster shows was not recorded (the master's broadcast, read from the host's own cluster read model) every route that needs a
/// recording answers <b>409</b> with <c>"this run was not recorded"</c> and publishes NOTHING to the master; a recorded run is untouched. Also the new routes:
/// <c>POST /run-settings</c>, <c>DELETE /exercises/{id}</c> (404 for an id the inventory does not list), <c>POST /review</c> needs its start (D9),
/// <c>POST /scenario/save</c> is 409 in Live and AAR, and <c>GET /status.recording</c> reports the run.
/// 🔴 Mutations (each compiles): <c>ResultOf</c> not mapping the refusal to 409 ⇒ the 409 cells red; <c>RefuseWhenNotRecorded</c> returning null ⇒ every "publishes
/// nothing" cell red; <c>EnterReview</c> without the start check ⇒ the D9 cell red; the host's live-save guard removed ⇒ the Live-save cell red.
/// </summary>
public sealed class TheRunRecordingGatesAnswer409Tests : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    public void Dispose() => _client.Dispose();

    private static readonly ExerciseInventoryItem Known = new(Guid.NewGuid(), new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(3), "a");

    private readonly RecordingMasterControl _master = new();
    private ClusterState _state = ClusterState.OperatingLive;
    private bool? _notRecorded = true;
    private bool? _settingOff = false;   // the broadcast's RecordLiveRunsOff

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task<(HttpStatusCode Status, JsonNode Body)> Send(MainThreadJobQueue queue, Task<HttpResponseMessage> request)
    {
        for (int i = 0; i < 2000 && !request.IsCompleted; i++) { queue.DrainAll(); await Task.Delay(5); }
        var response = await request;
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    [Fact(Timeout = 90_000)]
    public async Task WhenTheRunWasNotRecorded_EveryRecordingRouteAnswers409AndPublishesNothing_AndARecordedRunIsUntouched()
    {
        // the --mode all shape: two providers, so no single-provider fallback can hide a wrong route; the flag comes from the cluster read model
        var dispatcher = new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("IG", "IG"),
                new SubsystemDebugProvider("CGF", "CGF", master: () => _master, exercises: () => new[] { Known }, runNotRecorded: () => _notRecorded, recordLiveRunsOff: () => _settingOff),
            },
            currentPerspective: () => "CGF", acksPending: null);
        var service = new DebugApiService(dispatcher, clusterState: () => _state);

        int port = FreePort();
        var queue = new MainThreadJobQueue();
        using var host = new DebugApiHost(port, queue, () => { });
        host.AttachDispatcher(dispatcher);
        host.AttachService(service);
        host.Start();
        string url = $"http://localhost:{port}";
        StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");
        Task<HttpResponseMessage> Delete(string path) => _client.DeleteAsync(url + path);
        string Reason() => RunRecordingGates.NotRecordedReason;

        // ── the run was NOT recorded (live, loaded with "Record live runs" off) ──────────────────────────────────────────────────
        var (status, body) = await Send(queue, _client.PostAsync(url + "/checkpoint", Json("{}")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(Reason(), body["error"]!.GetValue<string>());

        (status, body) = await Send(queue, _client.PostAsync(url + "/checkpoint/restore", Json("""{"label":"cp"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(Reason(), body["error"]!.GetValue<string>());

        (status, body) = await Send(queue, _client.PostAsync(url + "/review", Json("""{"atSeconds":5}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(Reason(), body["error"]!.GetValue<string>());

        (status, body) = await Send(queue, _client.PostAsync(url + "/exercises/keep", Json("{}")));                  // "keep the last run"
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(Reason(), body["error"]!.GetValue<string>());

        (status, body) = await Send(queue, _client.PostAsync(url + "/replay", Json("{}")));                          // "replay the last run"
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(Reason(), body["error"]!.GetValue<string>());

        Assert.Empty(_master.Sent);                                                                                   // ⭐ nothing reached the master

        // a NAMED exercise is some other recorded run: not this gate's business
        (status, _) = await Send(queue, _client.PostAsync(url + "/exercises/keep", Json($"{{\"exerciseId\":\"{Known.ExerciseId}\"}}")));
        Assert.Equal(HttpStatusCode.OK, status);
        (status, _) = await Send(queue, _client.PostAsync(url + "/replay", Json($"{{\"exerciseId\":\"{Known.ExerciseId}\"}}")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, _master.Sent.Count);
        _master.Sent.Clear();

        // a review with no start is a 400 whatever the run (D9), before the recording question
        (status, body) = await Send(queue, _client.PostAsync(url + "/review", Json("{}")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("start", body["error"]!.GetValue<string>());

        // the status reports the run: not recorded
        (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body["data"]!["recording"]!.GetValue<bool>());

        // ── the same requests on a RECORDED live run: untouched ──────────────────────────────────────────────────────────────────
        _notRecorded = false;
        (status, _) = await Send(queue, _client.PostAsync(url + "/checkpoint", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        (status, _) = await Send(queue, _client.PostAsync(url + "/checkpoint/restore", Json("""{"label":"cp"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        (status, _) = await Send(queue, _client.PostAsync(url + "/review", Json("""{"atSimSeconds":3}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        (status, _) = await Send(queue, _client.PostAsync(url + "/exercises/keep", Json("{}")));
        Assert.Equal(HttpStatusCode.OK, status);
        (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.True(body["data"]!["recording"]!.GetValue<bool>());
        Assert.Equal(4, _master.Sent.Count);
        _master.Sent.Clear();

        // ── D22 — the scenario is not saved from a live run (recorded or not) nor from a recording, but is while editing ──────────
        (status, body) = await Send(queue, _client.PostAsync(url + "/scenario/save", Json("""{"name":"x"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("checkpoint", body["error"]!.GetValue<string>());
        _state = ClusterState.OperatingReplay;
        (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/save", Json("""{"name":"x"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);                                                                // AAR (the CE-3329 guard)
        Assert.Empty(_master.Sent);
        _state = ClusterState.OperatingEdit;
        (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/save", Json("""{"name":"x"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(_master.Sent);
        (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.False(body["data"]!["recording"]!.GetValue<bool>());                                                   // editing: no run, nothing recorded
        _master.Sent.Clear();

        // ── D15 — discard a recording ─────────────────────────────────────────────────────────────────────────────────────────────
        (status, _) = await Send(queue, Delete("/exercises/not-a-guid"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        (status, body) = await Send(queue, Delete($"/exercises/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.NotFound, status);                                                                // the inventory does not list it
        Assert.Empty(_master.Sent);
        (status, _) = await Send(queue, Delete($"/exercises/{Known.ExerciseId}"));
        Assert.Equal(HttpStatusCode.OK, status);
        var delete = Assert.IsType<ExecuteStorageOpIntent>(Assert.Single(_master.Sent));
        Assert.Equal(StorageOpType.Delete, delete.Operation);
        Assert.Equal(Known.ExerciseId, delete.ExerciseId);
        _master.Sent.Clear();

        // ── D14 — the setting ─────────────────────────────────────────────────────────────────────────────────────────────────────
        (status, _) = await Send(queue, _client.PostAsync(url + "/run-settings", Json("{}")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        (status, _) = await Send(queue, _client.PostAsync(url + "/run-settings", Json("""{"recordLiveRuns":"yes"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(_master.Sent);
        (status, body) = await Send(queue, _client.PostAsync(url + "/run-settings", Json("""{"recordLiveRuns":false}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(Assert.IsType<SetRunSettingsIntent>(Assert.Single(_master.Sent)).RecordLiveRuns);
        Assert.False(body["data"]!["recordLiveRuns"]!.GetValue<bool>());

        // GET reads the master's BROADCAST (one source on every host): ON until the broadcast says otherwise, then OFF; unknown ⇒ 503
        (status, body) = await Send(queue, _client.GetAsync(url + "/run-settings"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body["data"]!["recordLiveRuns"]!.GetValue<bool>());
        _settingOff = true;                                                   // the master has broadcast the POSTed setting
        (status, body) = await Send(queue, _client.GetAsync(url + "/run-settings"));
        Assert.False(body["data"]!["recordLiveRuns"]!.GetValue<bool>());
        _settingOff = null;
        (status, _) = await Send(queue, _client.GetAsync(url + "/run-settings"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }
}
