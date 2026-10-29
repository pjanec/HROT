using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Replication.Services;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <c>CE-4094</c> (AQ108 M7 / D22) — while the <c>Start</c> perspective shows, the runner's HTTP surface KEEPS ANSWERING: <c>/entities</c> is read from the
/// REMEMBERED perspective's world, <c>GET /status</c> says <c>perspective: Start</c> + <c>routedTo</c>, and <c>POST /perspective</c> answers 409 with the reason
/// (the window manager's own refusal, never a second rule). Over a REAL <see cref="DebugApiHost"/> on a loopback port with a REAL <see cref="WM"/> and the
/// production switcher — the <c>--mode all</c> shape (two providers, so no single-provider fallback can hide a wrong route).
/// 🔴 Mutations: <c>Active()</c> resolving <c>CurrentPerspective</c> ⇒ <c>/entities</c> answers 501 ⇒ red; the host mapping the refusal prefix to 400 ⇒ red;
/// <c>/status</c> reading <c>_dispatcher.CurrentPerspective</c> only for <c>routedTo</c> ⇒ red.
/// </summary>
public sealed class TheStartPerspectiveKeepsHttpWorkingTests : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    public void Dispose() => _client.Dispose();

    private sealed class Win : ManagedWindow
    {
        public Win(string id, string perspective) : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

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
        for (int i = 0; i < 2000 && !request.IsCompleted; i++) { queue.DrainAll(); await Task.Delay(5); }
        var response = await request;
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    [Fact(Timeout = 60_000)]
    public async Task WhileStartShows_EntitiesAnswer200FromTheRememberedPerspective_StatusSaysStart_AndSwitchingIs409()
    {
        using var world = new EntityRepository();
        var map = new NetworkEntityMap();

        var wm = new WM(new IconAtlas(IntPtr.Zero, 16f, 16f));
        wm.RegisterWindow(new Win("w-ig", "IG"));
        wm.RegisterWindow(new Win("w-scenario", "Scenario"));
        wm.RegisterWindow(new Win("w-start", WM.StartPerspectiveName));
        wm.SwitchPerspective("Scenario");

        var dispatcher = new PerspectiveScopedDispatcher(
            new[]
            {
                new SubsystemDebugProvider("IG", "IG"),
                new SubsystemDebugProvider("CGF", "Scenario", world: () => world, entityMap: () => map),
            },
            currentPerspective: () => wm.CurrentPerspective, acksPending: null,
            rememberedPerspective: () => wm.RememberedPerspective);
        var service = new DebugApiService(dispatcher);
        service.AttachPerspectives(new WindowManagerPerspectiveSwitcher(wm));

        int port = FreePort();
        var queue = new MainThreadJobQueue();
        using var host = new DebugApiHost(port, queue, () => { });
        host.AttachDispatcher(dispatcher);
        host.AttachService(service);
        host.Start();
        string url = $"http://localhost:{port}";
        StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

        // before Start: the ordinary state — routedTo == perspective, a switch answers 200
        var (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Scenario", body["data"]!["perspective"]!.GetValue<string>());
        Assert.Equal("Scenario", body["data"]!["routedTo"]!.GetValue<string>());

        wm.SwitchPerspective(WM.StartPerspectiveName);

        // Start shows: the status is honest about it …
        (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Start", body["data"]!["perspective"]!.GetValue<string>());
        Assert.Equal("Scenario", body["data"]!["routedTo"]!.GetValue<string>());

        // … and the world reads still answer (they would be 501 on a multi-provider host without the alias)
        (status, _) = await Send(queue, _client.GetAsync(url + "/entities"));
        Assert.Equal(HttpStatusCode.OK, status);

        // a perspective switch is refused with 409 and the reason; nothing moved
        (status, body) = await Send(queue, _client.PostAsync(url + "/perspective", Json("""{"name":"IG"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Start", body["error"]!.GetValue<string>());
        Assert.Equal("Start", wm.CurrentPerspective);

        // a name no window claims is also 409 while Start shows (the state explains it, not the name)
        (status, _) = await Send(queue, _client.PostAsync(url + "/perspective", Json("""{"name":"Nobody"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);

        // the gate leaves Start: the switch works again, and a bad name is the old 400
        Assert.True(wm.LeaveStartPerspective(out _));
        (status, _) = await Send(queue, _client.PostAsync(url + "/perspective", Json("""{"name":"IG"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("IG", wm.CurrentPerspective);
        (status, _) = await Send(queue, _client.PostAsync(url + "/perspective", Json("""{"name":"Nobody"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
