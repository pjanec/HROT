using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <c>CE-4097</c> part two (AQ108 D22) — <b>the HTTP surface stays EXPLICIT, over a REAL <see cref="DebugApiHost"/></b> on a loopback port, in front of the host's REAL
/// <see cref="ClusterTimeTransportAdapter"/> with a run choice: <c>POST /sim/play</c> from editing is the dry run whatever is armed (never a live load); <c>POST /sim/stop</c> is the SAME
/// stop as the toolbar's ■ (one rail compares what each published); <c>POST /run-mode</c> arms a mode through the indicator's own choice (409 with the confirmation's text unless
/// <c>confirm:true</c>); <c>GET /status.armedMode</c> reads it; the menu-driven go-live and <c>POST /scenario/load/live</c> each cause ONE <c>CheckUnsaved</c> round.
/// 🔴 Mutations (each compiles): the adapter's <c>TogglePlayPause</c> following the armed mode ⇒ the play cell calls <c>LoadForLive</c>; <c>Stop()</c> in the service calling <c>StopRaw</c> ⇒ the
/// "same as ■" cell red; <c>SetRunMode</c> not resolving Cancel on the unconfirmed 409 ⇒ the "no standing question" cell red; <c>/status</c> dropping <c>armedMode</c> ⇒ its cell red.
/// </summary>
public sealed class TheRunModeRoutesTests : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };
    public void Dispose() => _client.Dispose();

    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private sealed class Control : IRunScenarioControl
    {
        public string? LoadedScenarioName => "hill-attack";
        public readonly List<string> Calls = new();
        public void LoadForLive(string scenarioName) => Calls.Add($"LoadForLive:{scenarioName}");
        public void OpenForEdit(string scenarioName) => Calls.Add($"OpenForEdit:{scenarioName}");
        public void OpenRecording(Guid exerciseId) => Calls.Add($"OpenRecording:{exerciseId}");
        public void SaveCurrent() => Calls.Add("SaveCurrent");
        public void TakeCheckpoint() => Calls.Add("TakeCheckpoint");
    }

    private sealed class Unchanged : IUnsavedChangesQuery
    {
        public void Ask(Action<UnsavedAnswer> reply) => reply(UnsavedAnswer.Unchanged);
    }

    /// <summary>A REAL adapter on a real bus with a run choice attached, heard in <paramref name="state"/> (exercise <see cref="RunId"/>).</summary>
    private static (ClusterTimeTransportAdapter F, FdpEventBus Bus, Control C) Adapter(ClusterState state, RunArmed armed, bool withChoice = true,
                                                                                       UnsavedCheckReads? reads = null)
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);   // CE-1436 - the node's one view, advanced by its owner
        var facade = new ClusterTimeTransportAdapter(bus, () => view, () => 0);
        var control = new Control();
        if (withChoice) facade.AttachRunChoice(control, reads is null ? new Unchanged() : null, reads);
        bus.PublishManaged(new AssetInventoryUpdateEvent { LocalExercises = new[] { new ExerciseInventoryItem(RunId, DateTime.UtcNow, TimeSpan.Zero, "hill-attack") } });
        Hear(facade, bus, state, view, Enum.Parse<ClusterArmedMode>(armed.ToString()));   // ⭐ CE-1443: the armed mode is the master's bootstrap one, as its first broadcast says it
        return (facade, bus, control);
    }

    private static void Hear(ClusterTimeTransportAdapter f, FdpEventBus bus, ClusterState state, ClusterView view, ClusterArmedMode? armed = null)
    {
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = RunId, ArmedMode = armed ?? view.ArmedMode });
        bus.SwapBuffers(); view.Update(); f.Update(); bus.SwapBuffers();
    }

    private static List<T> Sent<T>(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<T>().ToList(); }

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

    /// <summary>
    /// ⭐⭐ CE-1451 — <c>GET /status</c> shows a node that is LOADING: its heartbeat's phase and step (<c>load</c>), the run gate its controls read (<c>runGate</c>) and the text of
    /// its composed loading screen (<c>loadingScreen</c>); a host with none of them answers null for each. 🔴 Red-proof: drop <c>["load"]</c> from <c>GetStatus</c> ⇒ red.
    /// </summary>
    [Fact]
    public void CE1451_Status_CarriesTheLoadStep_TheRunGate_AndTheLoadingScreenText()
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var facade = new ClusterTimeTransportAdapter(bus, () => view, () => 0);
        facade.AttachRunChoice(new Control(), new Unchanged(), gatePolicy: HostGatePolicy.Player);
        var screen = new LoadingScreen(facade.RunChoice!, HostGatePolicy.Player);
        view.NoteLocalPhase(NodePhase.Loading, "3-D map: terrain", null);
        bus.SwapBuffers(); view.Update(); facade.Update(); bus.SwapBuffers();

        var dispatcher = new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("IG", "IG", drive: () => facade, clusterView: () => view) },
            currentPerspective: () => "IG", acksPending: null);
        var data = new DebugApiService(dispatcher, clusterState: () => ClusterState.LoadingLive).GetStatus();

        Assert.Equal("Loading", data["load"]!["localPhase"]!.GetValue<string>());
        Assert.Equal("3-D map: terrain", data["load"]!["localStep"]!.GetValue<string>());
        Assert.False(data["runGate"]!["open"]!.GetValue<bool>());
        Assert.True(data["runGate"]!["loading"]!.GetValue<bool>());
        Assert.Equal("loading: 3-D map: terrain", data["runGate"]!["reason"]!.GetValue<string>());
        Assert.Equal("3-D map: terrain", data["runGate"]!["progress"]!.GetValue<string>());
        Assert.True(data["loadingScreen"]!["visible"]!.GetValue<bool>());
        Assert.Contains("3-D map: terrain", data["loadingScreen"]!["text"]!.GetValue<string>());

        var bare = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("SimHost", "SimHost", drive: () => new ClusterTimeTransportAdapter(new FdpEventBus(), () => null, () => 0)) },
            currentPerspective: () => "SimHost", acksPending: null), clusterState: () => ClusterState.Idle).GetStatus();
        Assert.Null(bare["load"]); Assert.Null(bare["runGate"]); Assert.Null(bare["loadingScreen"]);   // a host with none reports none
    }

    /// <summary>
    /// ⭐⭐ CE-1451 — a Player host (the IG) offers NO time drive, and its <c>/status.runGate</c> / <c>loadingScreen</c> still report: the run choice comes from the provider's own
    /// <c>runChoice</c>, not through the drive. 🔴 Red-proof: <c>RunChoiceOrNull</c> ignoring the provider's choice ⇒ both read null here.
    /// </summary>
    [Fact]
    public void CE1451_ADriveLessPlayer_StillReportsItsRunGateAndLoadingScreen()
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var facade = new ClusterTimeTransportAdapter(bus, () => view, () => 0);
        facade.AttachRunChoice(new Control(), new Unchanged(), gatePolicy: HostGatePolicy.Player);
        var screen = new LoadingScreen(facade.RunChoice!, HostGatePolicy.Player);
        view.NoteLocalPhase(NodePhase.Loading, "3-D map: terrain", null);
        bus.SwapBuffers(); view.Update(); facade.Update(); bus.SwapBuffers();

        var dispatcher = new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("IG", "IG", clusterView: () => view, runChoice: () => facade.RunChoice) },   // ⛔ no drive: the IG issues no ticks
            currentPerspective: () => "IG", acksPending: null);
        var data = new DebugApiService(dispatcher, clusterState: () => ClusterState.LoadingLive).GetStatus();

        Assert.False(data["runGate"]!["open"]!.GetValue<bool>());
        Assert.Equal("loading: 3-D map: terrain", data["runGate"]!["reason"]!.GetValue<string>());
        Assert.Equal("Loading — 3-D map: terrain", data["loadingScreen"]!["text"]!.GetValue<string>());
    }

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    private (DebugApiHost Host, MainThreadJobQueue Queue, string Url) Start(ClusterTimeTransportAdapter facade, ClusterState state, IClusterMasterControl? master = null,
                                                                            Func<Guid, UnsavedCheckOutcome?>? unsavedCheck = null)
    {
        // the --mode all shape: two providers, the active one's Drive is the host's ONE facade
        var dispatcher = new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("IG", "IG"),
                new SubsystemDebugProvider("CGF", "CGF", drive: () => facade, master: master is null ? null : () => master, unsavedCheck: unsavedCheck),
            },
            currentPerspective: () => "CGF", acksPending: null);
        // the unload before a load is "reached" the moment the route asked for it (as in TheUnsavedCheckRoutesTests)
        var service = new DebugApiService(dispatcher, clusterState: () => master is RecordingMasterControl rm && rm.Transitions.Any(t => t.TargetState == ClusterState.Idle) ? ClusterState.Idle : state);
        int port = FreePort();
        var queue = new MainThreadJobQueue();
        var host = new DebugApiHost(port, queue, () => { });
        host.AttachDispatcher(dispatcher);
        host.AttachService(service);
        host.Start();
        return (host, queue, $"http://localhost:{port}");
    }

    /// <summary>
    /// ⭐ D22 — an HTTP <c>POST /sim/play</c> from editing with Live armed is the DRY RUN: one <c>OperatingPreview</c> transition on the host's bus and NOT a live load (the session's
    /// <c>LoadForLive</c> is never called). The route does what it names; only the USER's ▶ follows the armed mode.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task SimPlay_FromEdit_WithLiveArmed_IsTheDryRun_NeverALiveLoad()
    {
        var (facade, bus, control) = Adapter(ClusterState.OperatingEdit, RunArmed.Live);
        var (host, queue, url) = Start(facade, ClusterState.OperatingEdit);
        using var h = host;

        var (status, _) = await Send(queue, _client.PostAsync(url + "/sim/play", Json("{}")));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ClusterState.OperatingPreview, Sent<TransitionStateIntent>(bus).Single().TargetState);
        Assert.Empty(control.Calls);
    }

    /// <summary>
    /// ⭐ D22 — <c>POST /sim/stop</c> calls the SAME <c>ITimeTransportFacade.Stop</c> as ■: with the toolbar's ■ and the route on identical rigs in a live run, the control calls and the
    /// published intents are EQUAL (R2: the scenario reopens for editing, Live stays armed, nothing sent to plain Idle). From editing it answers 409 and publishes nothing.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task SimStop_IsTheSameStopAsTheToolbarsStop_AndIs409WhereThereIsNothingToStop()
    {
        var click = Adapter(ClusterState.OperatingLive, RunArmed.Live);
        new MainToolbarTimeControlSection(click.F, choice: click.F.RunChoice).OnStop();

        var request = Adapter(ClusterState.OperatingLive, RunArmed.Live);
        var (host, queue, url) = Start(request.F, ClusterState.OperatingLive);
        using var h = host;
        var (status, body) = await Send(queue, _client.PostAsync(url + "/sim/stop", Json("{}")));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Live", body["data"]!["stoppedFrom"]!.GetValue<string>());
        Assert.Equal("live", body["data"]!["armedMode"]!.GetValue<string>());
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, click.C.Calls);
        Assert.Equal(click.C.Calls, request.C.Calls);                        // the same call a click makes …
        Assert.Equal(Sent<TransitionStateIntent>(click.Bus).Select(t => t.TargetState),
                     Sent<TransitionStateIntent>(request.Bus).Select(t => t.TargetState));   // … and the same intents (none: never plain Idle)
        Assert.Empty(Sent<TransitionStateIntent>(request.Bus));

        var idle = Adapter(ClusterState.OperatingEdit, RunArmed.Live);
        var (host2, queue2, url2) = Start(idle.F, ClusterState.OperatingEdit);
        using var h2 = host2;
        (status, body) = await Send(queue2, _client.PostAsync(url2 + "/sim/stop", Json("{}")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Nothing to stop", body["error"]!.GetValue<string>());
        Assert.Empty(idle.C.Calls);
        Assert.Empty(Sent<TransitionStateIntent>(idle.Bus));
    }

    /// <summary>
    /// ⭐ D22 — <c>POST /run-mode</c> arms through the indicator's own <c>Choose</c>: from editing at once (<c>GET /status.armedMode</c> reads it); while a run goes the move would ASK, so
    /// the route answers 409 with the confirmation's text, does nothing and leaves NO question standing; <c>confirm:true</c> accepts it (the run ends, the scenario reopens); a bad word is 400.
    /// 🔴 Red-proof: dropping the Cancel resolve on the unconfirmed 409 ⇒ <c>Pending</c> stays set.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task RunMode_ArmsThroughTheIndicatorsChoice_Asks409WithTheText_AndConfirmAccepts()
    {
        var edit = Adapter(ClusterState.OperatingEdit, RunArmed.Preview);
        var (host, queue, url) = Start(edit.F, ClusterState.OperatingEdit);
        using var h = host;

        var (status, body) = await Send(queue, _client.PostAsync(url + "/run-mode", Json("""{"armed":"live"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("live", body["data"]!["armedMode"]!.GetValue<string>());
        Assert.Equal(RunArmed.Live, edit.F.RunChoice!.Armed);
        Assert.Equal(ClusterArmedMode.Live, Assert.Single(Sent<SetRunSettingsIntent>(edit.Bus)).Armed);   // ⭐ CE-3341 S7 — the route's arm IS the cluster op (one)
        (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal("live", body["data"]!["armedMode"]!.GetValue<string>());
        Assert.Empty(Sent<SetRunSettingsIntent>(edit.Bus));                  // reading status sends nothing
        (status, _) = await Send(queue, _client.PostAsync(url + "/run-mode", Json("""{"armed":"preview"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ClusterArmedMode.Preview, Assert.Single(Sent<SetRunSettingsIntent>(edit.Bus)).Armed);   // S7 — Preview, one op

        (status, body) = await Send(queue, _client.PostAsync(url + "/run-mode", Json("""{"armed":"nonsense"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(Sent<SetRunSettingsIntent>(edit.Bus));                  // a refused word arms nothing

        var live = Adapter(ClusterState.OperatingLive, RunArmed.Live);
        var (host2, queue2, url2) = Start(live.F, ClusterState.OperatingLive);
        using var h2 = host2;
        (status, body) = await Send(queue2, _client.PostAsync(url2 + "/run-mode", Json("""{"armed":"preview"}""")));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Ends the live session; you return to editing the scenario at its start", body["error"]!.GetValue<string>());
        Assert.Contains("confirm:true", body["error"]!.GetValue<string>());
        Assert.Empty(live.C.Calls);                                          // nothing was done …
        Assert.Null(live.F.RunChoice!.Pending);                              // … and no question is left standing for the next click
        Assert.Equal(RunArmed.Live, live.F.RunChoice.Armed);
        Assert.Empty(Sent<SetRunSettingsIntent>(live.Bus));                  // … and the unconfirmed 409 sent no armed mode

        (status, body) = await Send(queue2, _client.PostAsync(url2 + "/run-mode", Json("""{"armed":"preview","confirm":true}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, live.C.Calls);     // the confirmed move: the live run ended, the scenario reopens
        Assert.Equal(RunArmed.Preview, live.F.RunChoice.Armed);
        Assert.Equal(ClusterArmedMode.Preview, Assert.Single(Sent<SetRunSettingsIntent>(live.Bus)).Armed);   // the confirmed move arms through the same op
    }

    /// <summary>⭐ A host with no run choice on its facade answers <c>armedMode: null</c> in <c>/status</c> and 503 to <c>/run-mode</c> — absent and explained, never a made-up default.</summary>
    [Fact(Timeout = 90_000)]
    public async Task AHostWithNoRunChoice_ReportsNoArmedMode_And503()
    {
        var bare = Adapter(ClusterState.OperatingEdit, RunArmed.Live, withChoice: false);
        var (host, queue, url) = Start(bare.F, ClusterState.OperatingEdit);
        using var h = host;

        var (status, body) = await Send(queue, _client.GetAsync(url + "/status"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(body["data"]!["armedMode"]);
        (status, _) = await Send(queue, _client.PostAsync(url + "/run-mode", Json("""{"armed":"live"}""")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    /// <summary>
    /// ⭐ AQ108 D20 / the NOTE — "the menu's move and the HTTP gate must cause ONE <c>CheckUnsaved</c> per move": a menu-driven go-live (the toolbar's ▶ with Live armed, over the
    /// production <c>ClusterUnsavedChangesQuery</c>) publishes exactly ONE intent on the host's bus, and <c>POST /scenario/load/live</c> asks the master's round exactly ONCE — the same
    /// round on both. 🔴 Red-proof: <c>PlayLive</c> asking twice, or the gate asking twice, ⇒ a count of 2.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task TheMenuDrivenGoLive_AndPostScenarioLoadLive_EachCauseExactlyOneCheckUnsaved()
    {
        // ── the menu-driven go-live ────────────────────────────────────────────────────────────────────────────────────────────────
        var answered = new Dictionary<Guid, UnsavedCheckOutcome>();
        var menu = Adapter(ClusterState.OperatingEdit, RunArmed.Live, reads: new UnsavedCheckReads(() => true, id => answered.TryGetValue(id, out var o) ? o : null));
        new MainToolbarTimeControlSection(menu.F, choice: menu.F.RunChoice).OnPlayPause();
        var asked = Sent<CheckUnsavedIntent>(menu.Bus);
        Assert.Single(asked);
        answered[asked[0].RequestId] = new UnsavedCheckOutcome(UnsavedCheckResult.Unchanged, null);
        menu.Bus.SwapBuffers(); menu.F.Update(); menu.Bus.SwapBuffers();
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, menu.C.Calls);
        Assert.Empty(Sent<CheckUnsavedIntent>(menu.Bus));                    // no second round for the move

        // ── the HTTP gate ──────────────────────────────────────────────────────────────────────────────────────────────────────────────
        var master = new RecordingMasterControl();
        var http = Adapter(ClusterState.OperatingEdit, RunArmed.Live);
        var (host, queue, url) = Start(http.F, ClusterState.OperatingEdit, master,
            unsavedCheck: id => master.Sent.OfType<CheckUnsavedIntent>().Any(i => i.RequestId == id) ? new UnsavedCheckOutcome(UnsavedCheckResult.Unchanged, null) : null);
        using var h = host;
        var (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(master.Sent.OfType<CheckUnsavedIntent>());             // one round for the move, the same intent type the menu published
    }

    private static string Shape(TransitionStateIntent i)
        => string.Join("|", typeof(TransitionStateIntent).GetFields().Where(f => f.Name != nameof(TransitionStateIntent.TransactionId))
                                                           .Select(f => f.Name + "=" + f.GetValue(i)));

    private static DebugApiService ServiceOver(RecordingMasterControl master, ClusterState state)
        => new(new PerspectiveScopedDispatcher(
                   new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "CGF", master: () => master) },
                   currentPerspective: () => "CGF", acksPending: null),
               clusterState: () => state);

    /// <summary>
    /// ⭐⭐ CE-1440 (R-3066 audit #11) — <c>POST /preview/enter</c> publishes the SAME intent as the toolbar's ▶ (and Step) from Edit, running and start-paused, field by field
    /// (<c>DryRunIntents.Enter</c>). 🔴 Mutation: <c>EnterPreview</c> back to <c>TimeMode = startPaused ? "Deterministic" : null</c> ⇒ the running cell differs (Continuous vs null) ⇒ red.
    /// </summary>
    [Fact]
    public void CE1440_PostPreviewEnter_IsTheToolbarsDryRunIntent()
    {
        foreach (bool startPaused in new[] { false, true })
        {
            var toolbar = Adapter(ClusterState.OperatingEdit, RunArmed.Preview, withChoice: false);
            if (startPaused) toolbar.F.Step(); else toolbar.F.TogglePlayPause();
            var fromToolbar = Assert.Single(Sent<TransitionStateIntent>(toolbar.Bus));

            var master = new RecordingMasterControl();
            Assert.Null(ServiceOver(master, ClusterState.OperatingEdit).EnterPreview(startPaused).Error);
            var fromHttp = Assert.Single(master.Transitions);

            Assert.Equal(Shape(fromToolbar), Shape(fromHttp));
            Assert.Equal(startPaused ? "Deterministic" : "Continuous", fromHttp.TimeMode);
        }
    }

    /// <summary>
    /// ⭐ CE-3341 (AQ108 D22) — the HTTP / agent LOAD routes send NO armed mode: <c>POST /scenario/load/live</c> reaches the master with its unsaved-changes round and the load,
    /// but never a <c>SetRunSettingsIntent</c> (the operator UI's load path sends that, not the shared load sequence). 🔴 Red-proof: a load route publishing the intent ⇒ red.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task PostScenarioLoadLive_SendsNoArmedMode()
    {
        var master = new RecordingMasterControl();
        var http = Adapter(ClusterState.OperatingEdit, RunArmed.Live);
        var (host, queue, url) = Start(http.F, ClusterState.OperatingEdit, master,
            unsavedCheck: id => master.Sent.OfType<CheckUnsavedIntent>().Any(i => i.RequestId == id) ? new UnsavedCheckOutcome(UnsavedCheckResult.Unchanged, null) : null);
        using var h = host;
        var (status, _) = await Send(queue, _client.PostAsync(url + "/scenario/load/live", Json("""{"name":"hill-attack"}""")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEmpty(master.Sent);                                         // the route did reach the master (the check round) ...
        Assert.Empty(master.Sent.OfType<SetRunSettingsIntent>());             // ... and sent no armed mode with it
        Assert.Empty(Sent<SetRunSettingsIntent>(http.Bus));
    }
}
