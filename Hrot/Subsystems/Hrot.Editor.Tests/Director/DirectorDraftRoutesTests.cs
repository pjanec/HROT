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
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Editor.DebugApi;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Editor.Tests.Director;

/// <summary>
/// ⭐ <c>CE-4059</c> P3 (docs/DESIGN_Director_Timeline_Editor.md §11q D4) — the editor's <c>/director/moments</c> and <c>/director/draft</c>, over a REAL
/// <see cref="DebugApiHost"/> on a loopback port (the <c>DebugApiFoundationTests</c> pattern: the host binds, the main-thread queue is drained by the test). The
/// routes call the pack's <see cref="DirectorDrafts"/> — here one over a hand-built index (Alpha shoots Bravo at 10 s, a hit 0.8 s later) and a session of its own.
/// </summary>
public sealed class DirectorDraftRoutesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ce4059-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Host : IDirectorHost
    {
        public Host(TakeStore takes) => Takes = takes;
        public EntityRepository? World => null;
        public void LoadScenario(string scenario) { }
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview => false;
        public void EnterPreview(bool startPaused) { }
        public void ExitPreview() { }
        public bool Paused => true;
        public void Pause() { }
        public void StepBy(float seconds) { }
        public double SimTime => 0d;
        public double WallNow => 0d;
        public void Hold(double seconds) { }
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
    }

    private sealed class NoCinema : ICinema
    {
        public void Enter() { }
        public void Exit() { }
        public bool Active => false;
        public void Card(string? text) { }
        public void Caption(string? text) { }
    }

    private sealed class NoLayers : ILayerOverride
    {
        public void Apply(IReadOnlyList<string> on, IReadOnlyList<string> off) { }
        public void Clear() { }
    }

    private static TimelineIndex ShotIndex()
    {
        const int frames = 251;
        var lanes = new Dictionary<string, SharedList<TimelineEvent>>
        {
            ["combat.shot"] = new(new[] { new TimelineEvent(100, "combat.shot", 1, 2, 0f, 0f, 1f) }),
            ["combat.hit"] = new(new[] { new TimelineEvent(108, "combat.hit", 2, 0, 0f, 0f, 0f) }),
        };
        var positions = new PositionTrack(new Dictionary<long, PositionSample[]>
        {
            [1] = Enumerable.Range(0, frames / 5 + 1).Select(i => new PositionSample(i * 5, i, 0f, 0f, 0f)).ToArray(),
            [2] = Enumerable.Range(0, frames / 5 + 1).Select(i => new PositionSample(i * 5, 60f, i, 0f, MathF.PI / 2)).ToArray(),
        });
        return new TimelineIndex(lanes, new SharedList<long>(Enumerable.Range(0, frames).Select(f => (long)f * 1_000_000).ToArray()), frames, frames, null,
                                 new SharedList<double>(Enumerable.Range(0, frames).Select(f => f * 0.1).ToArray()), positions);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Sends the request and drains the main-thread queue until it is answered (the editor's frame does this).</summary>
    private static async Task<(HttpStatusCode Status, JsonNode Body)> Send(MainThreadJobQueue queue, Task<HttpResponseMessage> request)
    {
        for (int i = 0; i < 2000 && !request.IsCompleted; i++) { queue.DrainAll(); await Task.Delay(5); }
        var response = await request;
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> P3 (§11q D4, success condition 5): <c>GET /director/moments</c> lists the shown index's moments (the shot, the hit merged into it: one, named
    /// Alpha → Bravo); <c>POST /director/draft {lane, eventIndex}</c> OPENS the drafter's take in the session (a new unsaved take <c>draft</c> of the shown scenario,
    /// the replay's reverse angle a key of the replay) and answers its JSON; a second draft while it is unsaved is 409; <c>recording=</c> is refused (the Replay
    /// Browser's); no event named is 400. 🔴 Mutation: <c>DirectorDraft</c> answering without opening (the <c>drafts.Draft</c> call replaced by
    /// <c>Drafter.Draft</c>) ⇒ the session has no open take ⇒ red.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task CE4059_P3_TheEditorsDraftRoutes_ListTheMoments_AndOpenTheDraft()
    {
        var store = new TakeStore(() => _root, () => null);
        var runner = new DirectorRunner(new Host(store), new NoCinema(), new NoLayers());
        var session = new DirectorTakeSession(() => store, () => runner, () => null);
        var index = ShotIndex();
        var drafts = new DirectorDrafts(() => index, id => id switch { 1 => "Alpha", 2 => "Bravo", _ => null }, _ => SubjectKind.Vehicle, () => "s-a", session);

        int port = FreePort();
        var queue = new MainThreadJobQueue();
        using var host = new DebugApiHost(port, queue, () => { });
        host.AttachDirector(runner, store, () => null, drafts);
        // the "not ready" gate opens with a service attached (a world-less one: the director routes do not use it)
        host.AttachService(new DebugApiService(new Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher(
            Array.Empty<Hrot.Presentation.DebugApi.ISubsystemDebugProvider>(), () => "Scenario", null)));
        host.Start();
        string url = $"http://localhost:{port}";

        var (status, body) = await Send(queue, _client.GetAsync(url + "/director/moments"));
        Assert.Equal(HttpStatusCode.OK, status);
        var data = body["data"]!;
        Assert.Equal("s-a", data["scenario"]!.GetValue<string>());
        var moment = Assert.Single(data["moments"]!.AsArray())!;
        Assert.Equal(("combat.shot", 0, "Alpha", "Bravo"), (moment["lane"]!.GetValue<string>(), moment["eventIndex"]!.GetValue<int>(),
                                                            moment["actorName"]!.GetValue<string>(), moment["subjectName"]!.GetValue<string>()));

        StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");
        (status, body) = await Send(queue, _client.PostAsync(url + "/director/draft", Json("""{"lane":"combat.shot","eventIndex":0}""")));
        Assert.True(status == HttpStatusCode.OK, body.ToJsonString());
        data = body["data"]!;
        Assert.Equal(("s-a", "draft", true, false), (data["scenario"]!.GetValue<string>(), data["take"]!.GetValue<string>(),
                                                     data["opened"]!.GetValue<bool>(), data["saved"]!.GetValue<bool>()));
        var open = session.Editor!;
        Assert.NotNull(open);
        Assert.Equal(("s-a", "draft"), (open.Take.Scenario, open.Take.Name));
        Assert.True(open.Dirty);
        Assert.Equal(TakeJson.WriteTake(open.Take).ToJsonString(), data["json"]!.ToJsonString());
        Assert.Contains(open.Take.Keys, k => k.Segment == Drafter.ReplaySegmentId);
        Assert.False(File.Exists(store.TakePath("s-a", "draft")));

        (status, _) = await Send(queue, _client.PostAsync(url + "/director/draft", Json("""{"lane":"combat.hit","eventIndex":0}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);                                                  // the open draft is unsaved
        Assert.Same(open, session.Editor);
        (status, _) = await Send(queue, _client.PostAsync(url + "/director/draft", Json("""{"recording":"x.fdp","lane":"combat.shot","eventIndex":0}""")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        (status, _) = await Send(queue, _client.PostAsync(url + "/director/draft", Json("""{"lane":"combat.shot"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
