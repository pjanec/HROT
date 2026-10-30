using System.Collections.Generic;
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
using static Hrot.ClusterRunner.Integration.Tests.Eqs.EditorPump;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// ⭐⭐⭐ <c>CE-4097</c> (AQ108 §2.8, D12) — the run choice is composed ONCE per host, on the CONSTRUCTED hosts (the silent-default rule: a caller that HAS
/// the dependency must pass it). The editor arms Preview, CGF arms Live, both over the scenario session their File menu uses (so ▶ is enabled), and both
/// hand the choice to the shared toolbar registrar.
/// 🔴 Red-proofs: the editor's master bootstrapped Live (the <c>InitialArmedMode</c> line removed) ⇒ the Preview assertions; the CGF line passing <c>null</c> for the session ⇒
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
            Assert.Equal(RunArmed.Live, choice!.Armed);   // before ANY broadcast has reached the view: the master's own default (⭐ CE-1443 — the editor's Preview is the master's bootstrap, asserted below)
            Pump(editor, () => choice.Gate.Open, 60, "the run gate open (the cluster is ready: CE-1442)");   // a fresh host is closed until its view has heard the cluster
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

    /// <summary>
    /// ⭐⭐ <c>CE-1443</c> S5 (R-1043), on the CONSTRUCTED editor: its single-node cluster is BOOTSTRAPPED in Preview — the ONE difference from every other cluster. The in-process master holds it, its first broadcast carries it, and once the node's
    /// view has folded that broadcast the run choice's armed mode IS Preview (no per-host default anywhere). 🔴 Red-proof: the <c>InitialArmedMode</c> line removed from <c>EditorSubsystem</c> ⇒ the master row reads Live.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE1443_S5_TheEditorsMasterIsBootstrappedInPreview_AndTheChoiceReadsItOffTheView()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            editor.RegisterWindows(MakeWindowManager());
            Assert.Equal(Fdp.Toolkit.Orchestration.ClusterArmedMode.Preview, editor.ClusterMasterForTest!.ArmedMode);
            editor.ClusterMasterForTest.Tick();                               // the master's frame: its broadcast carries the armed mode …
            editor.NodeForTest!.EventBus.SwapBuffers();                       // … and the node's frame folds it into the cluster view
            editor.NodeForTest.ClusterSlave.View.Update();
            Assert.Equal(Fdp.Toolkit.Orchestration.ClusterArmedMode.Preview, editor.NodeForTest.ClusterSlave.View.ArmedMode);
            Assert.Equal(RunArmed.Preview, editor.RunChoiceForTest!.Armed);
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
            cgf.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = false });
            var windows = MakeWindowManager();
            cgf.RegisterWindows(windows);

            var choice = cgf.RunChoiceForTest;
            Assert.NotNull(choice);
            Assert.Equal(RunArmed.Live, choice!.Armed);            // CGF / the all-in-one / the server: the master's default (R-1043: no host default)
            HearTheCluster(cgf.NodeForTest!);                      // CE-1442: a fresh host is closed until its view has heard the cluster
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
            cgf.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = false });
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

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> (AQ108 §2.11a) — on the CONSTRUCTED hosts, EVERY entry to the scenario a caller can reach — the session's public open / live load / open recording / New Exercise, and (the editor) the logic the pickers,
    /// New-from-a-recipe and the director's load call — goes through the ONE door: over a live run each asks <i>Stop &amp; Load</i> in the host's own pending slot and publishes NOTHING until the operator confirms; Cancel leaves the
    /// run alone. (The unsaved asks, the OUT door and HTTP are the door's own rails: <c>TheOneTimeFacadeTests.CE4101_*</c>, <c>EditorScenarioSessionLoadTests.CE4101_*</c>, <c>TheUnsavedCheckRoutesTests.CE4101_*</c>.)
    /// 🔴 Red-proof: a host passing its session's RAW verbs to a picker (or a public raw verb on the session) ⇒ the row sees no <c>Pending</c> and an Idle intent on the node's bus.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE4101_BothConstructedHosts_EveryEntryToTheScenario_GoesThroughTheDoor_OverALiveRun()
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
            cgf.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = false });
            cgf.RegisterWindows(MakeWindowManager());

            var editorLogic = editor.EditorLogic;
            AssertEveryEntryAsks("editor", editor.RunChoiceForTest, editor.NodeForTest, editor.ScenarioSessionForTest,
                ("EditorLogic.LoadScenarioByName", () => editorLogic.LoadScenarioByName("any-scenario")));
            AssertEveryEntryAsks("CGF", cgf.RunChoiceForTest, cgf.NodeForTest, cgf.ScenarioSessionForTest);
        }
        finally
        {
            cgf.Shutdown();
            editor.Shutdown();
        }
    }

    /// <summary>A node's frame hearing a cluster state (CE-1442's gate opens on it) — for a host with no in-process master to pump.</summary>
    private static void HearTheCluster(HrotNodeContext node, ClusterState state = ClusterState.OperatingEdit)
    {
        node.EventBus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state });
        if (node.ClusterSlave.View.NodeId is { } id)   // CE-1442 / CE-3344: the gate also wants this node in the master's in-sync set
            node.EventBus.PublishManaged(new InSyncSetChangedEvent { NodeIds = new[] { id } });
        node.EventBus.SwapBuffers();
        node.ClusterSlave.View.Update();
    }

    private static void AssertEveryEntryAsks(string host, RunModeChoice? choice, HrotNodeContext? node, Hrot.Editor.AiShared.Scenarios.IScenarioSession? session,
                                             params (string Name, System.Action Call)[] extra)
    {
        Assert.NotNull(choice);
        Assert.NotNull(node);
        Assert.NotNull(session);
        HearTheCluster(node!, ClusterState.OperatingLive);

        var entries = new (string Name, System.Action Call)[]
        {
            ("session.OpenForEdit",    () => session!.OpenForEdit("any-scenario")),
            ("session.LoadForLive",    () => session!.LoadForLive("any-scenario")),
            ("session.OpenRecording",  () => session!.OpenRecording(System.Guid.NewGuid())),
        }.Concat(extra).ToArray();
        foreach (var entry in entries)
        {
            entry.Call();
            var ask = choice!.Pending;
            Assert.True(ask is not null, $"{host}: {entry.Name} over a live run must ask first");
            Assert.Equal(new[] { "Stop & Load", "Cancel" }, ask!.Buttons.Select(b => b.Label));
            node.EventBus.SwapBuffers();
            Assert.Empty(node.EventBus.ReadManaged<TransitionStateIntent>());      // asked, nothing published
            ask.Cancel();
            Assert.Null(choice.Pending);
        }
    }

    private static void AssertAsksStopAndLoadOverALiveRun(string host, RunModeChoice? choice, HrotNodeContext? node)
    {
        Assert.NotNull(choice);
        Assert.NotNull(node);
        Assert.Null(choice!.Pending);

        // the master's broadcast, as this node's ONE cluster view folds it (the adapter's Mode reads that view)
        HearTheCluster(node!, ClusterState.OperatingLive);

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
    /// Live armed (the master's initial armed mode, R-1043), no scenario session; the host offers them and the composition puts the toolbar group up. SimHost's windows need a real
    /// graphics visualization, so the factory is the constructed piece a rail can reach.
    /// 🔴 Red-proof: the factory seeding a per-host armed mode ⇒ the Live assertion reddens.
    /// </summary>
    [Fact]
    public void SimHost_BuildsItsChoiceFromTheTable_AndTheCompositionPutsItsTransportOnTheToolbar()
    {
        var adapter = Hrot.SimHost.SimHostSubsystem.CreateTimeTransport(new FdpEventBus(), () => null, () => 0.0);
        var choice = adapter.RunChoice;
        Assert.NotNull(choice);
        Assert.Equal(RunArmed.Live, choice!.Armed);
        Assert.False(choice.HasScenarioControl);                // SimHost has no scenario session ⇒ Live-armed ▶ is disabled with its reason
        Assert.NotNull(choice.PlayDisabledReason);

        var windows = MakeWindowManager();
        var offer = new Offer(RunHostKind.SimHost, adapter);
        var controller = new Hrot.ClusterRunner.Presentation.LocalWindowController(null!, new Fdp.Toolkit.Runner.ISubsystem[] { offer }, new Fdp.Toolkit.Runner.RunnerOptions(), null);
        controller.ComposeRunControls(windows);
        Assert.Same(offer, controller.RunControlsOwner);
        AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(windows, "SimHost");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1445</c> (R-1044, DESIGN_Cluster_Load_Phase §11.8 ①②) S1 — on the CONSTRUCTED IG (headless, no DDS: <c>new IgSubsystem()</c>, <c>Initialize</c> as <c>IgSubsystemTests</c> does): it OFFERS run controls
    /// (before this item it offered none, so a player-only process drew no waiting screen), built by the shared factory with the PLAYER policy, no scenario session (not an operator), and the role's EXPLICIT suppression of the
    /// toolbar time control. Composed by the runner's <c>LocalWindowController</c> alone in its process: the loading / waiting screen and the error section are drawn over its choice (Player: no Cancel), and the toolbar carries no time control.
    /// 🔴 Mutation: the IG not offering run controls (<c>Transport</c> null) ⇒ the offer cells red; <c>SuppressTimeControl</c> false ⇒ the toolbar cell reds.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE1445_TheConstructedIg_OffersPlayerRunControls_AndTheWaitingScreenIsComposedOverThem()
    {
        var ig = new Hrot.IG.IgSubsystem();
        try
        {
            ig.Initialize(new SubsystemConfig { Headless = true, DomainId = DomainBase + Interlocked.Increment(ref _domain) });
            var offer = Assert.IsAssignableFrom<IProvidesRunControls>(ig);
            Assert.Equal(RunHostKind.Ig, offer.HostKind);
            Assert.NotNull(offer.Transport);
            var choice = Assert.IsType<RunModeChoice>(offer.RunChoice);
            Assert.Equal(Fdp.Toolkit.Orchestration.HostGatePolicy.Player, offer.GatePolicy);
            Assert.False(choice.HasScenarioControl);                 // a player is not an operator
            Assert.True(offer.SuppressTimeControl);                  // the role's explicit suppression
            choice.CancelLoad("x");                                  // a player owns no load: nothing to cancel, nothing throws

            var windows = MakeWindowManager();
            var controller = ComposeLikeTheRunner(windows, ig);
            Assert.Same(ig, controller.RunControlsOwner);
            Assert.Same(choice, controller.LoadingScreen!.Choice);
            Assert.True(controller.LoadingScreen.IsPlayer);
            Assert.False(controller.LoadingScreen.CanCancel);
            Assert.Same(choice, controller.ErrorSection!.Choice);
            Assert.Empty(windows.MainToolbar.BuildViewModel(currentPerspective: "IG").Entries.Where(e => e.Id == ShellTimeControlToolbar.EntryId));
        }
        finally
        {
            ig.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>R-1046</c> (CE-4078, DESIGN_Cluster_Load_Phase §11.9 ②) S4 — on the CONSTRUCTED IG: its profile puts the map in the load (<c>MapInLoad</c>) and ITS map is built <c>Eager</c> BECAUSE of that flag — one statement, the profile's;
    /// the map's build is reachable from the node as <c>IgSubsystem.MapBuildStatus</c> (the load step's poll). Headless there is no window to upload into, so the status reads built (nothing to wait for).
    /// 🔴 Mutation: the IG's attach passing no policy (⇒ <c>OnFirst3DRequest</c>) ⇒ the policy cell reds.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void R1046_TheConstructedIgs_MapIsEager_BecauseItsProfilePutsTheMapInTheLoad()
    {
        var ig = new Hrot.IG.IgSubsystem();
        try
        {
            ig.Initialize(new SubsystemConfig { Headless = true, DomainId = DomainBase + Interlocked.Increment(ref _domain) });
            Assert.True(Hrot.IG.IgApplication.RunProfile.MapInLoad);
            var status = ig.MapBuildStatus;
            var build = Assert.IsType<Fdp.Toolkit.Vis3D.Map3DBuild>(status);
            Assert.Equal(Fdp.Toolkit.Vis3D.Map3DBuildPolicy.Eager, build.Policy);
            Assert.Equal(Hrot.IG.IgApplication.RunProfile.MapBuildPolicy, build.Policy);
            Assert.True(status!.IsBuilt);                              // headless: no window, nothing to build
            Assert.Null(status.Failure);
            Assert.Equal(string.Empty, status.Step);
        }
        finally
        {
            ig.Shutdown();
        }
        Assert.Null(ig.MapBuildStatus);                                // after Shutdown the node holds none
    }

    /// <summary>The scenario menu items the ONE registrar owns (AQ108 §3a / CE-4097 part two) — pinned by path so the three hosts are compared on the same leaves.</summary>
    private static readonly string[] ScenarioMenuPaths =
    {
        "File/Load Scenario…", "File/New Scenario…", "File/Load Checkpoint…", "File/Open Exercise Recording…", "File/Save Checkpoint",
        "File/Save Scenario", "File/Save Scenario As", "File/Migration History", "File/Save Curated Scenarios to Git", "File/Close",   // ⭐ CE-1443 (AQ108 §2.15): one flat menu, no submenus
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
            Assert.All(ScenarioMenuShape(editorWindows), line => Assert.Contains("MISSING", line));   // ⭐ CE-4101 follow-up: RegisterWindows registers NO scenario item any more

            cgf.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = false });
            var cgfWindows = MakeWindowManager();
            cgf.RegisterWindows(cgfWindows);
            Assert.All(ScenarioMenuShape(cgfWindows), line => Assert.Contains("MISSING", line));

            orchestrator.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = true });
            var orchestratorWindows = MakeWindowManager();

            // ⭐ the ONE registration: each host alone in its process, composed by the runner's composition — it asks the picked owner to register
            ComposeLikeTheRunner(editorWindows, editor);
            ComposeLikeTheRunner(cgfWindows, cgf);
            ComposeLikeTheRunner(orchestratorWindows, orchestrator);

            var editorShape = ScenarioMenuShape(editorWindows);
            Assert.DoesNotContain(editorShape, line => line.Contains("MISSING"));
            Assert.Equal(editorShape, ScenarioMenuShape(cgfWindows));
            Assert.Equal(editorShape, ScenarioMenuShape(orchestratorWindows));
            Assert.Contains("File/Load Scenario…: bound=True idle=True", editorShape);
            Assert.Contains("File/Open Exercise Recording…: bound=True idle=True", editorShape);
            Assert.Contains("File/New Scenario…: bound=True idle=True", editorShape);
            Assert.Contains("File/Load Checkpoint…: bound=True idle=True", editorShape);
            Assert.Contains("File/Close: bound=True idle=True", editorShape);
            Assert.Contains("File/Save Checkpoint: bound=True idle=False", editorShape);                 // live runs only, never on Start
        }
        finally
        {
            orchestrator.Shutdown();
            cgf.Shutdown();
            editor.Shutdown();
        }
    }

    private static Hrot.ClusterRunner.Presentation.LocalWindowController ComposeLikeTheRunner(WindowManager windows, params ISubsystem[] hosts)
    {
        var controller = new Hrot.ClusterRunner.Presentation.LocalWindowController(null!, hosts, new RunnerOptions(), null);   // the shell is not used by the composition step
        controller.ComposeRunControls(windows);
        return controller;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> follow-up (AQ108 §2.11a ④) — on the CONSTRUCTED hosts, composed by the runner's <c>LocalWindowController</c>: (1) in every host combination the process has ONE scenario exit guard, over the
    /// picked owner's REAL session door; (2) an exit over a never-saved scenario (the editor and CGF, heard in Edit through their own bus and view) asks <i>Discard / Cancel</i> — Cancel does not take the exit, Discard
    /// does; at rest (every host, the Orchestrator included) the exit is taken at once. (The menu's single registration is <c>CE1439_*</c> above.)
    /// 🔴 Mutation: the composition not building <c>ScenarioExit</c> ⇒ the first exit row is taken unasked and the single-guard row finds none.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE4101_OnTheConstructedHosts_TheExitGoesThroughTheOwnersDoor_OneGuardPerProcess() => ExitThroughTheOwnersDoor(withCgf: true);

    /// <summary>⭐ The same rail without CGF (its DDS participant is what fails to create where DDS is unavailable): the editor and the Orchestrator alone.</summary>
    [Fact(Timeout = 60_000)]
    public void CE4101_OnTheConstructedEditorAndOrchestrator_TheExitGoesThroughTheOwnersDoor_OneGuardPerProcess() => ExitThroughTheOwnersDoor(withCgf: false);

    private static void ExitThroughTheOwnersDoor(bool withCgf)
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
            editor.Initialize(new SubsystemConfig { Headless = true });
            editor.RegisterWindows(MakeWindowManager());
            if (withCgf)
            {
                cgf.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = false });
                cgf.RegisterWindows(MakeWindowManager());
            }
            orchestrator.Initialize(new SubsystemConfig { DomainId = DomainBase + Interlocked.Increment(ref _domain), Headless = true });

            // (1) every combination: ONE scenario exit guard, over the owner's session door
            var sessions = new Dictionary<ISubsystem, Hrot.Editor.AiShared.Scenarios.IScenarioSession>
            {
                [editor] = editor.ScenarioSessionForTest!, [orchestrator] = orchestrator.ScenarioSessionForTest!,
            };
            if (withCgf) sessions[cgf] = cgf.ScenarioSessionForTest!;
            var combos = new List<ISubsystem[]> { new ISubsystem[] { editor }, new ISubsystem[] { orchestrator }, new ISubsystem[] { editor, orchestrator } };
            if (withCgf)
                combos.AddRange(new[] { new ISubsystem[] { cgf }, new ISubsystem[] { orchestrator, cgf }, new ISubsystem[] { cgf, editor }, new ISubsystem[] { orchestrator, cgf, editor } });
            foreach (var combo in combos)
            {
                var controller = ComposeLikeTheRunner(MakeWindowManager(), combo);
                var owner = (ISubsystem)Assert.IsAssignableFrom<IProvidesRunControls>(controller.RunControlsOwner);
                var guard = Assert.Single(((Hrot.ClusterRunner.Presentation.ExitGuardChain)controller.ExitGuard).Guards.OfType<Hrot.ClusterRunner.Presentation.ScenarioExitGuard>());
                Assert.Same(((Hrot.UI.Common.RunMode.IHasScenarioDoor)sessions[owner]).Door, guard.Door);
            }

            // (2) exit over a never-saved scenario, heard in Edit, on the editor and CGF (each alone)
            var edits = new List<(ISubsystem, HrotNodeContext?, RunModeChoice?)> { (editor, editor.NodeForTest, editor.RunChoiceForTest) };
            if (withCgf) edits.Add((cgf, cgf.NodeForTest, cgf.RunChoiceForTest));
            foreach (var (host, node, choice) in edits)
            {
                Assert.NotNull(node);
                var controller = ComposeLikeTheRunner(MakeWindowManager(), host);
                Assert.True(controller.ExitDue(true), $"{host.Name}: a cluster at rest — the exit is taken at once");

                node!.EventBus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit });
                node.EventBus.SwapBuffers();
                node.ClusterSlave.View.Update();

                Assert.False(controller.ExitDue(true), $"{host.Name}: exit over a never-saved scenario must ask");
                Assert.Equal(new[] { "Discard", "Cancel" }, choice!.Pending!.Buttons.Select(b => b.Label));
                choice.Pending.Cancel();
                Assert.False(controller.ExitDue(false), $"{host.Name}: Cancel must not take the exit");

                Assert.False(controller.ExitDue(true));
                var ask = choice.Pending!;
                ask.Resolve(ask.Buttons.Single(b => b.Label == "Discard"));
                Assert.True(controller.ExitDue(false), $"{host.Name}: Discard exits");
            }

            // the Orchestrator alone, at rest: the exit is taken at once (its door has nothing to ask)
            Assert.True(ComposeLikeTheRunner(MakeWindowManager(), orchestrator).ExitDue(true));
        }
        finally
        {
            orchestrator.Shutdown();
            if (withCgf) cgf.Shutdown();
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

    /// <summary>
    /// ⭐ The DDS domain block of this class: <c>11 … 10 + (the class's participant count)</c>, a block no other integration class uses (their bases: 39, 52–59, 70–71, 81, 91, 95, 100+; ids 1–38 are free).
    /// 📐 It was <c>230</c>: the third host landed on 233, past CycloneDDS's default-port maximum of 232 — <c>CreateParticipant</c> fails there, which was the four "environment" reds (and the IG rails were put on 90 to dodge it).
    /// </summary>
    private const int DomainBase = 10;

    private static int _domain;
}
