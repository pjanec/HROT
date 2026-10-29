using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Editor;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// ⭐⭐⭐ <c>CE-4097</c> (AQ108 §2.8, D12) — the run choice is composed ONCE per host, on the CONSTRUCTED hosts (the silent-default rule: a caller that HAS
/// the dependency must pass it). The editor arms Preview, CGF arms Live, both over the scenario session their File menu uses (so ▶ is enabled), and both
/// hand the choice to the shared toolbar registrar.
/// 🔴 Red-proofs: the editor's / CGF's <c>AttachRunChoice</c> default flipped ⇒ the armed assertions; the CGF line passing <c>null</c> for the session ⇒
/// <c>PlayDisabledReason</c> is the "no scenario session" text; the registrar call dropping the choice ⇒ the toolbar entry still exists but the choice is unwired (covered by the
/// presentation rails — here only that the entry exists on both).
/// </summary>
[Collection("EqsIntegrationTests")]   // the same one-DDS-participant-at-a-time collection the other constructed-host rails use
public sealed class TheRunChoiceIsOnEveryHostTests
{
    private static WindowManager MakeWindowManager() => new(new IconAtlas(System.IntPtr.Zero, 512, 512));

    [Fact(Timeout = 60_000)]
    public void TheEditorArmsPreview_OverItsSession()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var windows = MakeWindowManager();
            editor.RegisterWindows(windows);

            var choice = editor.RunChoiceForTest;
            Assert.NotNull(choice);
            Assert.Equal(RunArmed.Preview, choice!.HostDefault);   // D12: the editor starts in Preview
            Assert.Equal(RunArmed.Preview, choice.Armed);
            Assert.Null(choice.PlayDisabledReason);                // a session is wired: ▶ is not disabled for want of one
            choice.Choose(RunArmed.Live);
            Assert.Null(choice.PlayDisabledReason);                // …and Live-armed ▶ neither (it needs the session's LoadForLive)
            // (the editor registers its toolbar only when windowed; the headless editor has none to assert — CGF's windowed build below does)
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>⭐ CGF's windows are composed only when it is NOT headless (<c>RegisterWindows</c> returns early otherwise), so this is the one rail that builds it windowed.</summary>
    [Fact(Timeout = 60_000)]
    public void CgfArmsLive_OverItsSession_AndTheToolbarCarriesTheTransport()
    {
        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.Brain);
        var cgf = new CgfSubsystem(factory);
        try
        {
            cgf.Initialize(new SubsystemConfig { DomainId = 230 + Interlocked.Increment(ref _domain), Headless = false });
            var windows = MakeWindowManager();
            cgf.RegisterWindows(windows);

            var choice = cgf.RunChoiceForTest;
            Assert.NotNull(choice);
            Assert.Equal(RunArmed.Live, choice!.HostDefault);      // D12: CGF / the all-in-one / the server start in Live
            Assert.Equal(RunArmed.Live, choice.Armed);
            Assert.Null(choice.PlayDisabledReason);                // the session is passed: Live-armed ▶ is enabled
            AssertTheHostOffersItsTransport_AndTheCompositionPutsItOnTheToolbar(windows, cgf, "Scenario");   // ⭐ CE-4083 + CE-1437
        }
        finally
        {
            cgf.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1438</c> (R-3066 audit #6) — <b>"a run is running" is guarded INSIDE the run choice, so every host that has one warns — by construction.</b> Measured before: the editor
    /// passed <c>runningRun</c> + <c>confirmLoad</c> to its File menu (<i>Stop &amp; Load</i>), CGF passed neither, so CGF's File › Open Scenario went straight over a live run. Asserted on the
    /// CONSTRUCTED hosts: the cluster is heard in <c>OperatingLive</c> through each node's own bus and view, and the host's own choice (the one its File menu's <c>openScenario</c> calls)
    /// raises the SAME confirmation — a <see cref="RunModeConfirmation"/> with <i>Stop &amp; Load</i> / <i>Cancel</i> — and Cancel loads nothing. (Confirming → stop, then load is the choice's
    /// own rail over a fake session: <c>TheOneTimeFacadeTests.CE1438_*</c>; here a confirm would load a real scenario.)
    /// 🔴 Red-proof: the guard removed from <c>RunModeChoice.OpenScenario</c> ⇒ both rows see no <c>Pending</c>.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE1438_BothConstructedHosts_AskStopAndLoad_WhenAScenarioIsOpenedOverALiveRun()
    {
        var editor = new EditorSubsystem();
        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.Brain);
        var cgf = new CgfSubsystem(factory);
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            editor.RegisterWindows(MakeWindowManager());
            cgf.Initialize(new SubsystemConfig { DomainId = 230 + Interlocked.Increment(ref _domain), Headless = false });
            cgf.RegisterWindows(MakeWindowManager());

            AssertAsksStopAndLoadOverALiveRun("editor", editor.RunChoiceForTest, editor.NodeForTest);
            AssertAsksStopAndLoadOverALiveRun("CGF", cgf.RunChoiceForTest, cgf.NodeForTest);
        }
        finally
        {
            cgf.Shutdown();
            editor.Shutdown();
        }
    }

    private static void AssertAsksStopAndLoadOverALiveRun(string host, RunModeChoice? choice, HrotNodeContext? node)
    {
        Assert.NotNull(choice);
        Assert.NotNull(node);
        Assert.Null(choice!.Pending);

        // the master's broadcast, as this node's ONE cluster view folds it (the adapter's Mode reads that view)
        node!.EventBus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive });
        node.EventBus.SwapBuffers();
        node.ClusterSlave.View.Update();

        choice.OpenScenario("any-scenario");

        var ask = choice.Pending;
        Assert.True(ask is not null, $"{host}: File > Open Scenario over a live run must ask first");
        Assert.Equal(new[] { "Stop & Load", "Cancel" }, ask!.Buttons.Select(b => b.Label));
        Assert.Contains("stops the live run", ask.Text);
        ask.Cancel();
        Assert.Null(choice.Pending);   // Cancel: nothing was loaded
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4083</c> (AQ108 D13: "the picker exists once") — the WINDOWED editor: its toolbar carries the transport group and its status bar carries NO time control.
    /// 🔴 Red-proof: re-register a <c>"editor_time_controls"</c> status-bar section in <c>EditorSubsystem.RegisterWindows</c> ⇒ the status-bar assertion reddens.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void TheWindowedEditor_HasTheTransportOnTheToolbar_AndNoTimeControlInTheStatusBar()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = false });
            var windows = MakeWindowManager();
            editor.RegisterWindows(windows);
            AssertTheHostOffersItsTransport_AndTheCompositionPutsItOnTheToolbar(windows, editor, "Scenario");
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4083</c> / <c>CE-1437</c> — SimHost's adapter + run choice come from its ONE factory (<c>CreateTimeTransport</c>, the host's single <c>AttachRunChoice</c> site):
    /// Live armed (<see cref="RunArmedDefaults"/>), no scenario session; the host offers them and the composition puts the toolbar group up. SimHost's windows need a real
    /// graphics visualization, so the factory is the constructed piece a rail can reach.
    /// 🔴 Red-proof: the factory passing a literal other than the table's default ⇒ the Live assertion reddens.
    /// </summary>
    [Fact]
    public void SimHost_BuildsItsChoiceFromTheTable_AndTheCompositionPutsItsTransportOnTheToolbar()
    {
        var adapter = Hrot.SimHost.SimHostSubsystem.CreateTimeTransport(new FdpEventBus(), () => null, () => 0.0);
        var choice = adapter.RunChoice;
        Assert.NotNull(choice);
        Assert.Equal(RunArmedDefaults.For(RunHostKind.SimHost), choice!.HostDefault);
        Assert.Equal(RunArmed.Live, choice.Armed);
        Assert.False(choice.HasScenarioControl);                // SimHost has no scenario session ⇒ Live-armed ▶ is disabled with its reason
        Assert.NotNull(choice.PlayDisabledReason);

        var windows = MakeWindowManager();
        var offer = new Offer(RunHostKind.SimHost, adapter);
        var controller = new Hrot.ClusterRunner.Presentation.LocalWindowController(null!, new Fdp.Toolkit.Runner.ISubsystem[] { offer }, new Fdp.Toolkit.Runner.RunnerOptions(), null);
        controller.ComposeRunControls(windows);
        Assert.Same(offer, controller.RunControlsOwner);
        AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(windows, "SimHost");
    }

    /// <summary>The scenario menu items the ONE registrar owns (AQ108 §3a / CE-4097 part two) — pinned by path so the three hosts are compared on the same leaves.</summary>
    private static readonly string[] ScenarioMenuPaths =
    {
        "File/Checkpoint/Take Checkpoint", "File/Edit/Migration History", "File/Edit/Save Curated Scenarios to Git", "File/Edit/Save Scenario",
        "File/Edit/Save Scenario As", "File/Live/New Exercise", "File/New Scenario", "File/Open Recording", "File/Open Scenario",
    };

    /// <summary>Each scenario item's (path, registered, allowed-in-Idle) as this window manager's global menu holds it.</summary>
    private static string[] ScenarioMenuShape(WindowManager windows) => ScenarioMenuPaths.Select(path =>
    {
        var node = windows.GlobalMenu.Root;
        foreach (var segment in path.Split('/'))
        {
            if (!node.Children.TryGetValue(segment, out var next)) return $"{path}: MISSING";
            node = next;
        }
        return $"{path}: bound={node.Bindings.Count > 0} idle={node.AllowedInIdle}";
    }).ToArray();

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1439</c> (AQ108 §2.12; R-3066 audit #9) — <b>the scenario menu is the SAME on the three operator hosts</b>, on the CONSTRUCTED hosts: the windowed editor and CGF register it in their <c>RegisterWindows</c>,
    /// the Orchestrator through the composer's <c>ComposeScenarioActions</c> — every item present, bound, and the same allowed-in-Idle flags (the ones its Start surface draws). Before this item the Orchestrator's shape was
    /// nine MISSING lines.
    /// 🔴 Mutation: the Orchestrator's <c>ComposeScenarioActions</c> registering nothing (or the registrar dropping <c>allowedInIdle</c> for one item on one host) ⇒ the shapes differ.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE1439_TheScenarioMenu_IsTheSame_OnTheEditor_Cgf_AndTheOrchestrator()
    {
        var editor = new EditorSubsystem();
        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.Brain);
        var cgf = new CgfSubsystem(factory);
        var orchestrator = new Hrot.Orchestrator.OrchestratorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = false });
            var editorWindows = MakeWindowManager();
            editor.RegisterWindows(editorWindows);

            cgf.Initialize(new SubsystemConfig { DomainId = 230 + Interlocked.Increment(ref _domain), Headless = false });
            var cgfWindows = MakeWindowManager();
            cgf.RegisterWindows(cgfWindows);

            orchestrator.Initialize(new SubsystemConfig { DomainId = 230 + Interlocked.Increment(ref _domain), Headless = true });
            var orchestratorWindows = MakeWindowManager();
            orchestrator.ComposeScenarioActions(orchestratorWindows);

            var editorShape = ScenarioMenuShape(editorWindows);
            Assert.DoesNotContain(editorShape, line => line.Contains("MISSING"));
            Assert.Equal(editorShape, ScenarioMenuShape(cgfWindows));
            Assert.Equal(editorShape, ScenarioMenuShape(orchestratorWindows));
            Assert.Contains("File/Open Scenario: bound=True idle=True", editorShape);
            Assert.Contains("File/Open Recording: bound=True idle=True", editorShape);
            Assert.Contains("File/New Scenario: bound=True idle=True", editorShape);
        }
        finally
        {
            orchestrator.Shutdown();
            cgf.Shutdown();
            editor.Shutdown();
        }
    }

    /// <summary>A stand-in for SimHost's offer over the REAL adapter its factory builds (the real subsystem needs a graphics window).</summary>
    private sealed class Offer : Fdp.Toolkit.Runner.ISubsystem, IProvidesRunControls
    {
        public Offer(RunHostKind kind, Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter adapter) { HostKind = kind; Transport = adapter; RunChoice = adapter.RunChoice; }
        public RunHostKind HostKind { get; }
        public Hrot.UI.Common.Facades.ITimeTransportFacade? Transport { get; }
        public RunModeChoice? RunChoice { get; }
        public string Name => "Offer";
        public System.Numerics.Vector4 TitleBarColor => default;
        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1437</c> success condition 2 — on a REAL constructed host: its <c>RegisterWindows</c> puts NO time control on the toolbar (the host no longer registers it),
    /// it OFFERS its transport + choice, and the composition (<c>LocalWindowController.ComposeRunControls</c>, with this host the only subsystem) picks it and registers the one group.
    /// 🔴 Red-proof: a host calling <c>ShellTimeControlToolbar.Register</c> from <c>RegisterWindows</c> ⇒ the "none yet" assertion reddens.
    /// </summary>
    private static void AssertTheHostOffersItsTransport_AndTheCompositionPutsItOnTheToolbar(WindowManager windows, Fdp.Toolkit.Runner.ISubsystem host, string perspective)
    {
        Assert.Empty(windows.MainToolbar.BuildViewModel(currentPerspective: perspective).Entries.Where(e => e.Id == ShellTimeControlToolbar.EntryId));   // the host did not register it

        var offer = Assert.IsAssignableFrom<IProvidesRunControls>(host);
        Assert.NotNull(offer.Transport);
        Assert.NotNull(offer.RunChoice);

        var controller = new Hrot.ClusterRunner.Presentation.LocalWindowController(null!, new[] { host }, new Fdp.Toolkit.Runner.RunnerOptions(), null);   // the shell is not used by the composition step
        controller.ComposeRunControls(windows);
        Assert.Same(host, controller.RunControlsOwner);
        AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(windows, perspective);
    }

    /// <summary>
    /// ⭐⭐⭐ The two CE-4083 conditions on ONE composed window manager: (1) no status-bar section is a time control, (2) the host's toolbar carries <c>TimeControlGroup</c>.
    /// ⚠ A time control is recognised by id (<c>*time*</c> / <c>*transport*</c>) — the bar has no richer seam than <c>SectionIds</c>.
    /// </summary>
    private static void AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(WindowManager windows, string perspective)
    {
        Assert.Contains(windows.MainToolbar.BuildViewModel(currentPerspective: perspective).Entries, e => e.Id == ShellTimeControlToolbar.EntryId);

        var ids = windows.StatusBar.SectionIds;
        Assert.DoesNotContain(ids, id => id.Contains("time", System.StringComparison.OrdinalIgnoreCase) || id.Contains("transport", System.StringComparison.OrdinalIgnoreCase));
    }

    private static int _domain;
}
