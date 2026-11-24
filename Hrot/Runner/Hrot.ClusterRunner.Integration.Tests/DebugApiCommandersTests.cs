using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Hrot.CGF.Commander;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-4679</c> ② ③ (docs/designs/llm-commander/DESIGN_LLM_Commander.md §3 "the HTTP surface", L-2) — the commander seat over a REAL
/// <see cref="DebugApiHost"/> on a loopback port, in the editor's networkless world (its brain, <c>CgfLogicPack</c>, opens the seat): the seat list, the
/// briefing byte for byte, the tools, the feed (long-poll), the answer — an illegal one refused with its reason, a malformed one refused whole — the
/// mind switch, and the server-sent stream. Every world access runs on ONE thread (the frame loop below also drains the main-thread queue), as in the editor.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiCommandersTests
{
    private const int CommanderId = 3000;

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>The editor's world with a platoon commander whose seat is External, its frames and the main-thread queue on one background thread.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        public readonly EditorHarness H = new(productionBehaviors: true);
        public readonly MainThreadJobQueue Queue = new();
        public readonly DebugApiHost Host;
        public readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
        public readonly string Url;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _frames;
        public string? RecordingPath;

        public Rig(CommanderMind mind = CommanderMind.External)
        {
            var svc = H.BuildDebugApiService();
            var e = H.Repo.CreateEntity();
            H.Repo.AddComponent(e, new EntityInfo { Name = new FixedString64("1st Platoon"), ForceId = ForceId.Friend });
            H.Repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            H.Repo.AddComponent(e, new CommanderSeat { Mind = mind, Mode = CommanderMode.ThinkPause });
            H.Repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = CommanderId });   // the seat's id on the HTTP surface
            H.EntityMap.Register(CommanderId, e);
            CommanderSeats.For(H.Repo).RecordingPath = () => RecordingPath;
            int port = FreePort();
            Host = new DebugApiHost(port, Queue, () => { });
            Host.AttachService(svc);
            Host.Start();
            Url = $"http://localhost:{port}";
            _frames = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    Queue.DrainAll();
                    H.PumpFrames(1);
                    await Task.Delay(5).ConfigureAwait(false);
                }
            });
        }

        public async Task<(HttpStatusCode Status, JsonNode? Body)> Get(string path)
        {
            using var r = await Client.GetAsync(Url + path).ConfigureAwait(false);
            return (r.StatusCode, JsonNode.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false)));
        }

        public async Task<(HttpStatusCode Status, JsonNode? Body)> Post(string path, string json)
        {
            using var r = await Client.PostAsync(Url + path, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            return (r.StatusCode, JsonNode.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false)));
        }

        /// <summary>The route's payload (the host wraps it in <c>data</c>).</summary>
        public static JsonNode D(JsonNode? body) => body?["data"] ?? body!;

        public SeatRuntime Seat => CommanderSeats.Peek(H.Repo)?.ByNetworkId(CommanderId) ?? throw new InvalidOperationException("no seat");

        public async Task Until(Func<bool> condition, string what, int ms = 10_000)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > ms) throw new TimeoutException(what);
                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _frames.ConfigureAwait(false); } catch (OperationCanceledException) { }
            CommanderSeats.Peek(H.Repo)?.Dispose();
            Host.Dispose();
            Client.Dispose();
            H.Dispose();
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4679 ② ③ — the whole surface, in the order an outside mind uses it: <c>GET /commanders</c> lists the seat (External); <c>/tools</c> is the
    /// JSON-schema list; <c>/briefing</c> is the text the mind is given BYTE FOR BYTE and says it waits; <c>/feed</c> starts with the snapshot; an
    /// action list that is not a list is a 400 and changes nothing; an ILLEGAL order (a squad he does not have) is taken over HTTP and REFUSED in the feed
    /// with its reason; a second answer with nothing waiting is a 409; a long-poll with nothing new waits its time; <c>/mind</c> switches to Scripted and
    /// then <c>/act</c> is a 409.
    /// 🔴 Mutations: the parse problems ignored ⇒ the 400 cell red; the briefing built from the prefix only ⇒ the byte-for-byte cell red; the long-poll's
    /// wait skipped ⇒ the timing cell red.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task CE4679_TheCommanderRoutes_AnOutsideMindSeesWhatTheModelSees_AndAnIllegalActionIsRefusedOverHttp()
    {
        await using var rig = new Rig();
        await rig.Until(() => CommanderSeats.Peek(rig.H.Repo)?.ByNetworkId(CommanderId) is { } s && s.Runner?.Mind is ExternalMind { Pending: not null },
                        "the seat opened and its mind waits on the opening snapshot");

        var (status, body) = await rig.Get("/commanders");
        Assert.Equal(HttpStatusCode.OK, status);
        var seat = Assert.Single(Rig.D(body)["commanders"]!.AsArray())!;
        Assert.Equal(CommanderId, seat["id"]!.GetValue<int>());
        Assert.Equal("External", seat["mind"]!.GetValue<string>());
        Assert.Equal("1st Platoon", seat["name"]!.GetValue<string>());

        (status, body) = await rig.Get($"/commanders/{CommanderId}/tools");
        Assert.Equal(HttpStatusCode.OK, status);
        var tools = Rig.D(body)["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "run_play", "order", "hold", "ask_sitrep", "stop_play", "note" }, tools);   // ⭐ CE-4680 B-1 — the plays; ⭐ CE-4683 ③ — run_play FIRST

        (status, body) = await rig.Get($"/commanders/{CommanderId}/briefing");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(Rig.D(body)["waitingForAnswer"]!.GetValue<bool>());
        var pending = ((ExternalMind)rig.Seat.Runner!.Mind).Pending!;
        Assert.Equal(pending.Text(), Rig.D(body)["text"]!.GetValue<string>());   // ⭐ byte for byte what the mind is given
        Assert.Contains("SNAPSHOT", pending.Text());

        (status, body) = await rig.Get($"/commanders/{CommanderId}/feed?after=0");
        Assert.Equal(HttpStatusCode.OK, status);
        var first = Rig.D(body)["items"]!.AsArray().First()!;
        Assert.Equal("snapshot", first["kind"]!.GetValue<string>());
        long latest = Rig.D(body)["latestSeq"]!.GetValue<long>();

        (status, body) = await rig.Post($"/commanders/{CommanderId}/act", """{"actions":"attack everything"}""");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("nothing changed", body!.ToJsonString());

        (status, body) = await rig.Post($"/commanders/{CommanderId}/act",
            """{"seq":1,"actions":[{"tool":"order","squad":"Zulu","kind":"Move","where":"start"}]}""");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Rig.D(body)["queued"]!.GetValue<int>());

        // the refusal comes back as an order-state item — a long-poll returns on it
        (status, body) = await rig.Get($"/commanders/{CommanderId}/feed?after={latest}&wait=10");
        Assert.Equal(HttpStatusCode.OK, status);
        var refused = Rig.D(body)["items"]!.AsArray().Single(i => i!["kind"]!.GetValue<string>() == "order-state")!;
        Assert.Equal("refused", refused["data"]!["state"]!.GetValue<string>());
        Assert.Contains("no squad named 'Zulu'", refused["radio"]!.GetValue<string>());

        // a refusal is a wake: the mind is asked again (it may correct itself) — it answers with a note, and then nothing waits
        await rig.Until(() => ((ExternalMind)rig.Seat.Runner!.Mind).Pending != null, "the refusal woke the mind");
        (status, _) = await rig.Post($"/commanders/{CommanderId}/act", """{"actions":[{"tool":"note","text":"Zulu is not mine"}]}""");
        Assert.Equal(HttpStatusCode.OK, status);
        (status, body) = await rig.Post($"/commanders/{CommanderId}/act", """{"actions":[{"tool":"note","text":"again"}]}""");
        Assert.Equal(HttpStatusCode.Conflict, status);   // nothing waits for an answer now
        Assert.Contains("no question is waiting", body!.ToJsonString());

        // a long-poll with nothing new waits its time, then answers empty
        // ⭐ CE-4683 ① — the note is his running estimate (in the briefing), no longer echoed into the feed: only the refusal is an item here
        await rig.Until(() => rig.Seat.Channel.ReadAfter(latest, new System.Collections.Generic.List<FeedItem>()) >= 1, "the refusal was said");
        var since = new System.Collections.Generic.List<FeedItem>();
        rig.Seat.Channel.ReadAfter(latest, since);
        Assert.DoesNotContain(since, i => i.Radio.Contains("NOTE:"));
        latest = rig.Seat.Channel.LatestSeq;
        var sw = Stopwatch.StartNew();
        (status, body) = await rig.Get($"/commanders/{CommanderId}/feed?after={latest}&wait=1.5");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(sw.ElapsedMilliseconds >= 1300, $"the long-poll waited {sw.ElapsedMilliseconds} ms");
        Assert.Empty(Rig.D(body)["items"]!.AsArray());

        (status, body) = await rig.Post($"/commanders/{CommanderId}/mind", """{"kind":"Scripted"}""");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Scripted", Rig.D(body)["mind"]!.GetValue<string>());
        (status, _) = await rig.Post($"/commanders/{CommanderId}/act", """{"actions":[]}""");
        Assert.Equal(HttpStatusCode.Conflict, status);

        (status, _) = await rig.Get("/commanders/424242/briefing");
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    /// <summary>
    /// ⭐ CE-4679 ② — <c>GET /commanders/{id}/stream</c> is server-sent events: <c>text/event-stream</c>, one event per feed item (<c>id:</c> = seq,
    /// <c>data:</c> = the item), starting after the seq asked for.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task CE4679_TheStream_IsServerSentEvents_OnePerFeedItem()
    {
        await using var rig = new Rig();
        await rig.Until(() => CommanderSeats.Peek(rig.H.Repo)?.ByNetworkId(CommanderId) is { } s && s.Channel.LatestSeq > 0, "the seat said its snapshot");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await rig.Client.GetAsync(rig.Url + $"/commanders/{CommanderId}/stream?after=0", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        Assert.Equal("id: 1", await reader.ReadLineAsync(cts.Token));
        var data = await reader.ReadLineAsync(cts.Token);
        Assert.StartsWith("data: ", data);
        var item = JsonNode.Parse(data!["data: ".Length..])!;
        Assert.Equal(1, item["seq"]!.GetValue<long>());
        Assert.Equal("snapshot", item["kind"]!.GetValue<string>());
    }

    /// <summary>
    /// ⭐ CE-4679 ④ — the model log beside the recording: <c>&lt;recording&gt;.commander-&lt;id&gt;.jsonl</c> exists, holds the call (the radio lines the mind
    /// was given, the answer it gave, its raw text) and the verdict on each action, naming the order the simulation took.
    /// 🔴 Mutation: the verdict line not written ⇒ the "names the order" cell red.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task CE4679_TheModelLog_SitsBesideTheRecording_AndNamesEachOrderAndVerdict()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ce4679-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await using var rig = new Rig();
            rig.RecordingPath = Path.Combine(dir, "run.fdp");
            await rig.Until(() => CommanderSeats.Peek(rig.H.Repo)?.ByNetworkId(CommanderId) is { } s && s.Runner?.Mind is ExternalMind { Pending: not null }, "the mind waits");
            var (status, _) = await rig.Post($"/commanders/{CommanderId}/act",
                """{"actions":[{"tool":"order","squad":"Zulu","kind":"Attack","where":"start"},{"tool":"note","text":"testing the log"}]}""");
            Assert.Equal(HttpStatusCode.OK, status);
            string log = Path.Combine(dir, $"run.fdp.commander-{CommanderId}.jsonl");
            await rig.Until(() => File.Exists(log) && File.ReadAllText(log).Contains("\"verdict\""), "the log has the verdicts");
            string text;
            using (var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) text = new StreamReader(f).ReadToEnd();
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!).ToArray();
            var call = lines.First(l => l["kind"]!.GetValue<string>() == "call");
            Assert.Contains("Attack", call["actions"]!.ToJsonString());
            Assert.Contains("testing the log", call["raw"]!.GetValue<string>());
            var verdict = lines.First(l => l["kind"]!.GetValue<string>() == "verdict" && l["action"]!.ToJsonString().Contains("Attack"));
            Assert.Equal("refused", verdict["state"]!.GetValue<string>());
            Assert.Contains("Zulu", verdict["reason"]!.GetValue<string>());
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
