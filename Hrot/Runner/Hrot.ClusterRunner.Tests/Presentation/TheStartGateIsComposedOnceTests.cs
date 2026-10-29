using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Presentation;
using Hrot.ClusterRunner.Tests.Mocks;
using Hrot.Presentation.DebugApi;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Presentation;

/// <summary>⭐ The tests that open / close a <c>LocalWindowController</c> share the user's layout folder (<c>fdp_windows.json</c>, <c>imgui.ini</c>); run
/// one at a time (found `2026-10-28`: parallel runs raced on <c>imgui.ini</c> and a sibling's Close persisted a perspective another test then read).</summary>
[Xunit.CollectionDefinition("UserLayoutFolder", DisableParallelization = true)]
public sealed class UserLayoutFolderCollection { }

/// <summary>
/// ⭐⭐⭐ <c>CE-4096</c> (AQ108 §2.6 / D16) — <b>the start gate is composed ONCE, in the one place every cluster-runner host builds its window
/// manager</b> (<c>LocalWindowController</c>), and fed ONE cluster-rest read taken from any node.
/// </summary>
[Xunit.Collection("UserLayoutFolder")]
public class TheStartGateIsComposedOnceTests
{
    private sealed class PerspectiveWindow : ManagedWindow
    {
        public PerspectiveWindow(string perspective) : base("w-" + perspective, perspective, perspective, WindowScope.PerspectiveBound) { IsOpen = true; }
        protected override void DrawClientArea() { }
    }

    private static LocalWindowController Controller(IReadOnlyList<ISubsystem> subsystems)
        => new(new GZH012_Tests.FakePresentationShell(), subsystems, new RunnerOptions(), null);

    private static Fdp.Presentation.WindowManager.WindowManager BareManager()
        => new(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));

    private static int StartClaimants(Fdp.Presentation.WindowManager.WindowManager wm)
        => wm.RegisteredWindowIds.Count(id => wm.TryGetWindow(id, out var w) && w.OwningPerspective == Fdp.Presentation.WindowManager.WindowManager.StartPerspectiveName);

    // ── composition: one window claiming Start, one gate, per host family ───────

    public static IEnumerable<object[]> HostFamilies() => new[]
    {
        new object[] { "cgf" }, new object[] { "all" }, new object[] { "editor" }, new object[] { "simhost+ig" },
    };

    [Theory]
    [MemberData(nameof(HostFamilies))]
    public void EveryHostFamily_RegistersExactlyOneWindowClaimingStart_AndBuildsOneGate_AndTheExitItem(string family)
    {
        ISubsystem[] subsystems = family.Split('+').Select(n => (ISubsystem)new MockSubsystem(n)).ToArray();
        var ctrl = Controller(subsystems);
        var wm = BareManager();   // ⭐ the extracted composition step — ⛔ not OpenLocalWindow, which shares the user's layout folder with sibling tests

        ctrl.ComposeStartSurface(wm);

        Assert.Equal(1, StartClaimants(wm));
        Assert.NotNull(ctrl.StartGate);
        Assert.Same(ctrl.StartSurface, wm.TryGetWindow(StartSurfaceWindow.WindowId, out var w) ? w : null);
        var exit = wm.GlobalMenu.Root.Children["File"].Children["Exit"];
        Assert.True(exit.AllowedInIdle);                                  // offered on the Start surface
        Assert.False(ctrl.TakeExitRequest());
        exit.OnClick!();
        Assert.True(ctrl.TakeExitRequest());                              // the frame loop takes it like the window's [X]
        Assert.False(ctrl.TakeExitRequest());                             // …once
    }

    [Fact]
    public void OpeningTheLocalWindow_ComposesTheStartSurface()
    {
        // ResetLayoutOnRun=false: this test only READS the user's layout folder (siblings share it)
        var ctrl = new LocalWindowController(new GZH012_Tests.FakePresentationShell(), new ISubsystem[] { new MockSubsystem("CGF") },
            new RunnerOptions { ResetLayoutOnRun = false }, null);
        ctrl.OpenLocalWindow();
        Assert.NotNull(ctrl.StartGate);
        Assert.Equal(1, StartClaimants(ctrl.WindowManager!));
    }

    [Fact]
    public void AReplayBrowserOnlyProcess_IsExempt_ButAnyOtherMixComposesTheSurface()
    {
        var only = Controller(new ISubsystem[] { new Hrot.ReplayBrowser.ReplayBrowserSubsystem() });
        var wm1 = BareManager();
        only.ComposeStartSurface(wm1);
        Assert.Null(only.StartGate);
        Assert.Equal(0, StartClaimants(wm1));

        var mixed = Controller(new ISubsystem[] { new Hrot.ReplayBrowser.ReplayBrowserSubsystem(), new MockSubsystem("CGF") });
        var wm2 = BareManager();
        mixed.ComposeStartSurface(wm2);
        Assert.NotNull(mixed.StartGate);
        Assert.Equal(1, StartClaimants(wm2));
    }

    [Fact]
    public void TheControllerTicksTheGate_FromTheClusterRestRead_EnteringAfterTheSettleAndLeavingWhenTheClusterMoves()
    {
        var ctrl = Controller(new ISubsystem[] { new MockSubsystem("CGF") });
        var wm = BareManager();
        ctrl.ComposeStartSurface(wm);
        wm.RegisterWindow(new PerspectiveWindow("CGF"));
        wm.SwitchPerspective("CGF");

        var rest = new ClusterRest(ClusterState.Idle, false, true);
        ctrl.ClusterRestOverride = () => rest;

        ctrl.TickStartGate(100.0);
        Assert.False(wm.IsStartShowing);
        ctrl.TickStartGate(100.0 + StartGate.SettleSeconds);
        Assert.True(wm.IsStartShowing);
        Assert.Equal("CGF", wm.RememberedPerspective);

        rest = new ClusterRest(ClusterState.LoadingEdit, false, true);
        ctrl.TickStartGate(101.0);
        Assert.False(wm.IsStartShowing);
        Assert.Equal("CGF", wm.CurrentPerspective);
    }

    private sealed class CountingNode : MockSubsystem, IProvidesDebugSurface
    {
        public int Built;
        public CountingNode() : base("CGF") { }
        public ISubsystemDebugProvider? CreateDebugProvider()
        {
            Built++;
            return new SubsystemDebugProvider("CGF", "CGF", followedClusterState: () => ClusterState.Idle);
        }
    }

    [Fact]
    public void TheGate_BuildsEachSubsystemsDebugProviderOnce_NotEveryFrame()
    {
        var node = new CountingNode();
        var ctrl = Controller(new ISubsystem[] { node });
        var wm = BareManager();
        ctrl.ComposeStartSurface(wm);
        wm.RegisterWindow(new PerspectiveWindow("CGF"));
        wm.SwitchPerspective("CGF");

        for (int i = 0; i < 10; i++) ctrl.TickStartGate(100.0 + i * 0.1);

        Assert.Equal(1, node.Built);              // ⭐ a factory, called once (coordinator review)
        Assert.True(wm.IsStartShowing);           // …and the once-built provider still feeds the gate
    }

    [Fact]
    public void StartIsNeverTheStartupPerspective_EvenWhenItIsTheOnlyOneClaimed()
    {
        var claimed = new[] { "Start", "Scenario" };
        Assert.Equal("Scenario", LocalWindowController.ResolveStartupPerspective(claimed, null, persisted: "Start"));
        Assert.Equal("Default", LocalWindowController.ResolveStartupPerspective(new[] { "Start" }, null, persisted: null));
    }

    // ── ⭐ CE-1443 (R-1043): there is NO per-host armed default any more — the armed mode is the cluster's, set by the MASTER's bootstrap ──

    /// <summary>
    /// ⭐ <c>CE-1443</c> — the withdrawn D12 table (<c>RunArmedDefaults</c>: Preview in the editor, Live elsewhere) left no trace on a host: a choice built for EVERY host kind shows the cluster's armed mode, which (before any broadcast) is the master's own default,
    /// Live — the editor included (its single-node master is bootstrapped Preview, <c>EditorSubsystem</c> / <c>OrchestratorCoreOptions.InitialArmedMode</c>, and that arrives through the cluster view, not through a host table).
    /// 🔴 Red-proof: any host kind's choice seeding a per-kind armed mode again ⇒ that kind's row is Preview.
    /// </summary>
    [Theory]
    [InlineData(RunHostKind.Editor)]
    [InlineData(RunHostKind.Cgf)]
    [InlineData(RunHostKind.SimHost)]
    [InlineData(RunHostKind.Orchestrator)]
    [InlineData(RunHostKind.Other)]
    public void CE1443_NoHostKindCarriesAnArmedDefault(RunHostKind kind)
        => Assert.Equal(RunArmed.Live, new RunHost("h", kind, operatorHost: true).RunChoice!.Armed);

    // ── CE-3345 (R-3072, AQ110 D): the gate policy is ONE table too — Player for the IG, Operator for every other host kind ──

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success 6 (the table half) — what each host kind's controls wait on is read from the ONE table: the IG is a <b>Player</b> (its own PlayReady alone), every other kind an <b>Operator</b>; and a host's
    /// <c>IProvidesRunControls.GatePolicy</c> IS that table's value for its declared kind (no host overrides it).
    /// 🔴 Mutations (each compiles): <c>For</c> returning <c>Operator</c> for the IG ⇒ the IG row reds; the interface default returning a literal ⇒ the "host reads the table" cell reds.
    /// </summary>
    [Theory]
    [InlineData(RunHostKind.Editor, Fdp.Toolkit.Orchestration.HostGatePolicy.Operator)]
    [InlineData(RunHostKind.Cgf, Fdp.Toolkit.Orchestration.HostGatePolicy.Operator)]
    [InlineData(RunHostKind.Orchestrator, Fdp.Toolkit.Orchestration.HostGatePolicy.Operator)]
    [InlineData(RunHostKind.SimHost, Fdp.Toolkit.Orchestration.HostGatePolicy.Operator)]
    [InlineData(RunHostKind.Other, Fdp.Toolkit.Orchestration.HostGatePolicy.Operator)]
    [InlineData(RunHostKind.Ig, Fdp.Toolkit.Orchestration.HostGatePolicy.Player)]
    public void CE3345_TheGatePolicy_IsPlayerForTheIg_OperatorElsewhere_AndAHostReadsTheTable(RunHostKind kind, Fdp.Toolkit.Orchestration.HostGatePolicy expected)
    {
        Assert.Equal(expected, HostGatePolicies.For(kind));
        IProvidesRunControls host = new RunHost("h", kind, operatorHost: false, offers: false);
        Assert.Equal(expected, host.GatePolicy);
    }

    // ── CE-1437 (AQ108 §2.10): the run controls are composed ONCE per process, picked BY RULE ────────────────────────

    private sealed class FakeControl : IRunScenarioControl
    {
        public string? LoadedScenarioName => "s";
        public void LoadForLive(string scenarioName) { }
        public void OpenForEdit(string scenarioName) { }
        public void OpenRecording(System.Guid exerciseId) { }
        public void SaveCurrent() { }
        public void TakeCheckpoint() { }
    }

    /// <summary>A host that offers run controls over a REAL adapter + choice (built like the real hosts: a session only when it is an operator; ⭐ CE-1443 no armed default).</summary>
    private sealed class RunHost : MockSubsystem, IProvidesRunControls
    {
        public RunHostKind HostKind { get; }
        public ITimeTransportFacade? Transport { get; }
        public RunModeChoice? RunChoice { get; }
        /// <summary>⭐ CE-1439 — how many times the composer asked this host to register its scenario menu (it asks the PICKED owner only).</summary>
        public int ScenarioActionsComposed;
        public void ComposeScenarioActions(Fdp.Presentation.WindowManager.WindowManager windowManager) => ScenarioActionsComposed++;
        public RunHost(string name, RunHostKind kind, bool operatorHost, bool offers = true) : base(name)
        {
            HostKind = kind;
            if (!offers) return;
            var adapter = new ClusterTimeTransportAdapter(new Fdp.Core.FdpEventBus(), () => null, () => 0.0);
            RunChoice = adapter.AttachRunChoice(operatorHost ? new FakeControl() : null);
            Transport = adapter;
        }
    }

    private static (LocalWindowController Ctrl, Fdp.Presentation.WindowManager.WindowManager Wm) Composed(params ISubsystem[] subsystems)
    {
        var ctrl = Controller(subsystems);
        var wm = BareManager();
        ctrl.ComposeStartSurface(wm);
        ctrl.ComposeRunControls(wm);
        return (ctrl, wm);
    }

    private static int TimeControlEntries(Fdp.Presentation.WindowManager.WindowManager wm)
        => wm.MainToolbar.BuildViewModel(currentPerspective: "Scenario").Entries.Count(e => e.Id == ShellTimeControlToolbar.EntryId);

    [Fact]
    public void SimHostAndCgf_InEitherOrder_RegisterOneTimeControl_AndItsChoiceIsCgfs()
    {
        foreach (var cgfFirst in new[] { true, false })
        {
            var cgf = new RunHost("CGF", RunHostKind.Cgf, operatorHost: true);
            var sim = new RunHost("SimHost", RunHostKind.SimHost, operatorHost: false);
            var (ctrl, wm) = Composed(cgfFirst ? new ISubsystem[] { cgf, sim } : new ISubsystem[] { sim, cgf });

            Assert.Equal(1, TimeControlEntries(wm));
            Assert.Same(cgf, ctrl.RunControlsOwner);
            Assert.Same(cgf.RunChoice, ctrl.RunControlsOwner!.RunChoice);
        }
    }

    [Fact]
    public void EditorAndSimHost_TheEditorsWins()
    {
        var editor = new RunHost("Editor", RunHostKind.Editor, operatorHost: true);
        var sim = new RunHost("SimHost", RunHostKind.SimHost, operatorHost: false);
        var (ctrl, wm) = Composed(sim, editor);

        Assert.Equal(1, TimeControlEntries(wm));
        Assert.Same(editor, ctrl.RunControlsOwner);
    }

    [Fact]
    public void TwoOperators_TheEditorsWins_WhateverTheOrder()
    {
        var editor = new RunHost("Editor", RunHostKind.Editor, operatorHost: true);
        var cgf = new RunHost("CGF", RunHostKind.Cgf, operatorHost: true);
        Assert.Same(editor, Composed(cgf, editor).Ctrl.RunControlsOwner);
        Assert.Same(editor, Composed(editor, cgf).Ctrl.RunControlsOwner);
    }

    /// <summary>
    /// ⭐ CE-1442 (U1, S5) — the loading screen and the cluster-error section are composed EXACTLY ONCE per process, by <c>ComposeRunControls</c>, over the PICKED owner's run choice (the editor's among editor + CGF + SimHost,
    /// whatever the subsystem order); a process with no run-controls owner has neither. 🔴 Mutations: <c>ComposeRunControls</c> registering them per host ⇒ the choice is not the owner's;
    /// not composing them ⇒ the properties are null.
    /// </summary>
    [Fact]
    public void CE1442_TheLoadingScreenAndTheErrorSection_AreComposedOnce_OverTheOwnersChoice()
    {
        foreach (var editorFirst in new[] { true, false })
        {
            var editor = new RunHost("Editor", RunHostKind.Editor, operatorHost: true);
            var cgf = new RunHost("CGF", RunHostKind.Cgf, operatorHost: true);
            var sim = new RunHost("SimHost", RunHostKind.SimHost, operatorHost: false);
            var (ctrl, wm) = Composed(editorFirst ? new ISubsystem[] { editor, cgf, sim } : new ISubsystem[] { sim, cgf, editor });
            Assert.Same(editor.RunChoice, ctrl.LoadingScreen!.Choice);
            Assert.Same(editor.RunChoice, ctrl.ErrorSection!.Choice);
            Assert.Equal(1, wm.StatusBar.SectionIds.Count(id => id == Hrot.UI.Common.Panels.LoadingScreen.SectionId));
            Assert.Equal(1, wm.StatusBar.SectionIds.Count(id => id == Hrot.UI.Common.Panels.ClusterErrorStatusBarSection.SectionId));
        }

        var (none, bare) = Composed(new MockSubsystem("IG"));
        Assert.Null(none.LoadingScreen);
        Assert.Null(none.ErrorSection);
        Assert.DoesNotContain(Hrot.UI.Common.Panels.LoadingScreen.SectionId, bare.StatusBar.SectionIds);
    }

    [Fact]
    public void SimHostAlone_KeepsItsOwn()
    {
        var sim = new RunHost("SimHost", RunHostKind.SimHost, operatorHost: false);
        var (ctrl, wm) = Composed(sim);
        Assert.Equal(1, TimeControlEntries(wm));
        Assert.Same(sim, ctrl.RunControlsOwner);
    }

    [Fact]
    public void NoRunControls_NoTimeControl_AndAHostThatOffersNothingIsSkipped()
    {
        var (ctrl, wm) = Composed(new MockSubsystem("IG"), new RunHost("CGF", RunHostKind.Cgf, operatorHost: true, offers: false));
        Assert.Equal(0, TimeControlEntries(wm));
        Assert.Null(ctrl.RunControlsOwner);
    }

    /// <summary>The toolbar time control comes from the composition and nothing else: a host that holds its transport has put nothing on the toolbar.</summary>
    [Fact]
    public void TheToolbarTimeControl_IsRegisteredOnlyByTheComposition()
    {
        var host = new RunHost("CGF", RunHostKind.Cgf, operatorHost: true);
        var ctrl = Controller(new ISubsystem[] { host });
        var wm = BareManager();
        Assert.Equal(0, TimeControlEntries(wm));
        ctrl.ComposeRunControls(wm);
        Assert.Equal(1, TimeControlEntries(wm));
    }

    // ── CE-1439 (AQ108 §2.12): the scenario menu — so the Start surface's Open / New / Recording — belongs to the host that owns ▶ ────────────

    /// <summary>
    /// ⭐ The composer asks ONLY the picked owner to register the scenario menu, and the pick among operators is a RANK (editor, CGF, then the world-less Orchestrator) — never the subsystem order:
    /// in the all-in-one (Orchestrator + CGF) CGF keeps its real items, an Orchestrator-only process gets its own.
    /// 🔴 Mutation: <c>PickRunControls</c> returning <c>operators[0]</c> ⇒ (Orchestrator, CGF) picks the Orchestrator; <c>ComposeRunControls</c> asking every host ⇒ the counts fail.
    /// </summary>
    [Fact]
    public void TheScenarioMenu_IsComposedOnlyOnThePickedOwner_EditorThenCgfThenOrchestrator_WhateverTheOrder()
    {
        RunHost Make(RunHostKind k) => new(k.ToString(), k, operatorHost: k != RunHostKind.SimHost);

        void Check(RunHostKind expectedOwner, params RunHostKind[] kinds)
        {
            var hosts = kinds.Select(Make).ToArray();
            var (ctrl, _) = Composed(hosts.Cast<ISubsystem>().ToArray());
            var owner = Assert.IsType<RunHost>(ctrl.RunControlsOwner);
            Assert.Equal(expectedOwner, owner.HostKind);
            Assert.Equal(1, owner.ScenarioActionsComposed);
            Assert.All(hosts.Where(h => !ReferenceEquals(h, owner)), h => Assert.Equal(0, h.ScenarioActionsComposed));
        }

        Check(RunHostKind.Cgf, RunHostKind.Orchestrator, RunHostKind.Cgf);
        Check(RunHostKind.Cgf, RunHostKind.Cgf, RunHostKind.Orchestrator);
        Check(RunHostKind.Editor, RunHostKind.Orchestrator, RunHostKind.Cgf, RunHostKind.Editor);
        Check(RunHostKind.Orchestrator, RunHostKind.Orchestrator);
        Check(RunHostKind.Orchestrator, RunHostKind.SimHost, RunHostKind.Orchestrator);   // an operator beats a host with no session (CE-1437)
    }

    /// <summary>
    /// ⭐⭐⭐ <b>#9 — an Orchestrator-only window at Idle offers Open Scenario, New Scenario and Open Recording, each through the MENU'S OWN binding.</b> The REAL <c>OrchestratorSubsystem</c>, composed by the real
    /// <c>LocalWindowController</c>: the Start surface's buttons are the allowed-in-Idle menu items (the surface owns no action). <i>New Scenario</i> reaches the session's <c>ClearWorld</c>; <i>Open Scenario</i> opens the
    /// scenario picker over the MASTER'S inventory and a pick opens for edit through the run choice (the request goes out on the bus); <i>Open Recording</i> opens the recording picker over the same inventory and a pick
    /// publishes the one replay intent. Before CE-1439 the Orchestrator registered none of them and Start showed Help + Exit only.
    /// 🔴 Mutations: <c>ComposeRunControls</c> not calling <c>ComposeScenarioActions</c> ⇒ the three buttons are missing; <c>OrchestratorSubsystem.ComposeScenarioActions</c> passing <c>openRecordingPicker: null</c> ⇒ Open Recording
    /// is disabled; <c>_scenarioCatalog.Refresh()</c> dropped from <c>Update</c> ⇒ the picker's list stays empty and the <c>Single</c> over "harbour" fails.
    /// </summary>
    [Fact]
    public void AnOrchestratorOnlyProcess_StartOffersOpenNewAndRecording_EachThroughTheMenusOwnBinding()
    {
        var orch = new Hrot.Orchestrator.OrchestratorSubsystem();
        orch.Initialize(new SubsystemConfig { Headless = true, DomainId = 224 });
        try
        {
            var ctrl = Controller(new ISubsystem[] { orch });
            var wm = BareManager();
            orch.RegisterWindows(wm);
            ctrl.ComposeStartSurface(wm);
            ctrl.ComposeRunControls(wm);
            Assert.Same(orch, ctrl.RunControlsOwner);

            var opened = new List<NodeEditor.UI.Picker.PickerRequest>();
            var callbacks = new List<Action<NodeEditor.UI.Picker.PickerResult>>();
            orch.PickerOpenerOverride = (request, onChosen) => { opened.Add(request); callbacks.Add(onChosen); };

            // the master's inventory reaches the host's cache (and so the pickers' lists) the way it does in a run: on the host's bus, folded by its frame
            var exercise = new ExerciseInventoryItem(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), DateTime.UtcNow, TimeSpan.FromMinutes(3), "harbour");
            orch.TimeBusForTest!.PublishManaged(new AssetInventoryUpdateEvent { LocalScenarios = new[] { "harbour" }, LocalExercises = new[] { exercise } });
            orch.TimeBusForTest!.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle });   // ⭐ CE-1442 — the run gate is closed until the master's state has reached the host (its Start actions read it)
            orch.Update(0.016f);

            ctrl.ClusterRestOverride = () => new ClusterRest(ClusterState.Idle, false, true);
            ctrl.TickStartGate(100.0);
            ctrl.TickStartGate(100.0 + StartGate.SettleSeconds);
            var model = ctrl.StartSurface!.Model();
            Assert.Equal(StartVariant.Actions, model.Variant);
            var byLabel = model.Buttons.ToDictionary(b => b.Label);
            foreach (var label in new[] { "Load Scenario…", "New Scenario…", "Open Exercise Recording…", "Close" })   // ⭐ CE-1443: the first four + Close (Exit is the controller's)
            {
                Assert.True(byLabel.ContainsKey(label), $"the Start surface of an Orchestrator-only host must offer '{label}'; it offers: {string.Join(", ", model.Buttons.Select(b => b.Label))}");
                Assert.True(byLabel[label].Enabled, label);
            }

            // New Scenario → the session's ClearWorld (the menu's binding)
            var session = orch.ScenarioSessionForTest!;
            session.OpenForEdit("earlier");
            session.Update();
            Assert.Equal("earlier", session.LoadedScenarioName);
            byLabel["New Scenario…"].Invoke();
            Assert.Null(session.LoadedScenarioName);

            // Load Scenario… → the scenario picker over the master's inventory; a pick LOADS into the cluster's armed mode (R-1043: Live, this cluster's master default) through the door (the request goes out)
            byLabel["Load Scenario…"].Invoke();
            var scenarioPick = Assert.Single(opened);
            var harbour = scenarioPick.ItemsProvider().Single(e => e.Tag is Hrot.Editor.AiShared.IEditableAsset a && a.Name == "harbour");
            callbacks[0](new NodeEditor.UI.Picker.PickerResult(new[] { harbour }));
            Assert.Equal("harbour", session.LoadedScenarioName);

            // Open Exercise Recording… → the recording picker over the same inventory; a pick publishes the ONE replay intent
            byLabel["Open Exercise Recording…"].Invoke();
            Assert.Equal(2, opened.Count);
            var recording = opened[1].ItemsProvider().Single(e => e.Tag is ExerciseInventoryItem);
            callbacks[1](new NodeEditor.UI.Picker.PickerResult(new[] { recording }));
            var bus = orch.TimeBusForTest!;
            bus.SwapBuffers();
            Assert.Contains(bus.ReadManaged<TransitionStateIntent>(), t => t.TargetState == ClusterState.OperatingReplay && t.ExerciseId == exercise.ExerciseId);
        }
        finally
        {
            orch.Shutdown();
        }
    }

    // ── CE-4101 follow-up (AQ108 §2.11a ④): the exit goes through the ONE scenario door, composed once ────────────────────────────────────

    /// <summary>A scenario session with its OWN door (like the real <c>EditorScenarioSession</c>): the raw verbs and the leave are recorded in <see cref="Calls"/>.</summary>
    private sealed class DoorControl : IRunScenarioControl, IHasScenarioDoor
    {
        public readonly List<string> Calls = new();
        public string? LoadedScenarioName => "s";
        public ScenarioDoor Door { get; }
        public DoorControl() { Door = new ScenarioDoor(this, () => TransportMode.Idle, reason => Calls.Add($"Leave:{reason}")); }
        public void LoadForLive(string scenarioName) => Calls.Add("LoadForLive");
        public void OpenForEdit(string scenarioName) => Calls.Add("OpenForEdit");
        public void OpenRecording(System.Guid exerciseId) => Calls.Add("OpenRecording");
        public void SaveCurrent() => Calls.Add("SaveCurrent");
        public void TakeCheckpoint() => Calls.Add("TakeCheckpoint");
    }

    private sealed class AnswerUnsaved : IUnsavedChangesQuery
    {
        public UnsavedAnswer Answer = UnsavedAnswer.Unchanged;
        public void Ask(System.Action<UnsavedAnswer> reply) => reply(Answer);
    }

    /// <summary>An operator host over a REAL adapter + choice + view, on a real bus; the cluster is "heard" in the state <see cref="Hear"/> publishes.</summary>
    private sealed class DoorHost : MockSubsystem, IProvidesRunControls
    {
        private readonly Fdp.Core.FdpEventBus _bus = new();
        private readonly ClusterView _view;
        private readonly ClusterTimeTransportAdapter _adapter;
        public readonly DoorControl Control = new();
        public readonly AnswerUnsaved Unsaved = new();
        public RunHostKind HostKind { get; }
        public ITimeTransportFacade? Transport => _adapter;
        public RunModeChoice? RunChoice { get; }
        public DoorHost(RunHostKind kind) : base(kind.ToString())
        {
            HostKind = kind;
            _view = new ClusterView(_bus);
            _adapter = new ClusterTimeTransportAdapter(_bus, () => _view, () => 0.0);
            RunChoice = _adapter.AttachRunChoice(Control, Unsaved);
        }
        public DoorHost Hear(ClusterState state)
        {
            _bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state });
            _bus.SwapBuffers();
            _view.Update();
            _adapter.Update();
            _bus.SwapBuffers();
            return this;
        }
    }

    /// <summary>The editor's DOCUMENT guard, scripted (what <c>EditorSubsystem.OnExitRequested</c> is to the chain): a dirty one defers and opens its own question.</summary>
    private sealed class DocGuard : MockSubsystem, IAppExitGuard
    {
        public bool Dirty = true;
        public bool Approved;
        public int Asked;
        public DocGuard() : base("Docs") { }
        public ExitDisposition OnExitRequested() { Asked++; Approved = false; return Dirty ? ExitDisposition.Deferred : ExitDisposition.CanExit; }
        public bool ExitApproved => Approved;
    }

    private static void Press(RunModeConfirmation? ask, string label)
    {
        Assert.NotNull(ask);
        ask!.Resolve(ask.Buttons.Single(b => b.Label == label));
    }

    private static readonly RunHostKind[] OperatorKinds = { RunHostKind.Editor, RunHostKind.Cgf, RunHostKind.Orchestrator };

    /// <summary>
    /// ⭐⭐⭐ <b>Exit over unsaved scenario edits asks <i>Save / Discard / Cancel</i> through the door — on the editor, CGF and the Orchestrator alike</b>, each composed by the REAL <c>LocalWindowController</c>
    /// (the frame loop's own decision, <c>ExitDue</c>): Cancel keeps the exit from being taken (and nothing is stopped or saved), <i>Discard</i> exits (the door's leave runs), <i>Save</i> saves then exits.
    /// 🔴 Mutations: the composition not building <c>ScenarioExit</c> ⇒ the first <c>ExitDue(true)</c> is true (exits unasked); <c>ScenarioExitGuard</c> approving without waiting for the door's answer ⇒ Cancel's
    /// <c>ExitDue(false)</c> is true.
    /// </summary>
    [Fact]
    public void AnExit_OverUnsavedScenarioEdits_AsksSaveDiscardCancel_OnEveryOperatorHost_AndCancelDoesNotExit()
    {
        foreach (var kind in OperatorKinds)
        {
            var host = new DoorHost(kind).Hear(ClusterState.OperatingEdit);
            host.Unsaved.Answer = UnsavedAnswer.Changed;
            var (ctrl, _) = Composed(host);

            Assert.False(ctrl.ExitDue(true), $"{kind}: exit over unsaved edits must ask first");
            Assert.Equal(new[] { "Save", "Discard", "Cancel" }, host.RunChoice!.Pending!.Buttons.Select(b => b.Label));
            host.RunChoice.Pending.Cancel();
            Assert.False(ctrl.ExitDue(false), $"{kind}: Cancel must not take the exit");
            Assert.Empty(host.Control.Calls);                                   // nothing stopped, nothing saved

            Assert.False(ctrl.ExitDue(true));                                   // asked again on the next request
            Press(host.RunChoice.Pending, "Discard");
            Assert.True(ctrl.ExitDue(false), $"{kind}: Discard exits");
            Assert.Equal(new[] { "Leave:Exit" }, host.Control.Calls);           // the door's decision is what ran

            host.Control.Calls.Clear();
            Assert.False(ctrl.ExitDue(true));
            Press(host.RunChoice.Pending, "Save");
            Assert.True(ctrl.ExitDue(false), $"{kind}: Save exits");
            Assert.Equal(new[] { "SaveCurrent", "Leave:Exit" }, host.Control.Calls);
        }
    }

    [Fact]
    public void AnExit_OverAScenarioNeverSaved_AsksDiscardOrCancel_AndWithNoEditsOrOverARunOrAtRest_DoesNotAsk_ButTheDoorsLeaveRuns()
    {
        foreach (var kind in OperatorKinds)
        {
            // never saved ⇒ Discard / Cancel
            var never = new DoorHost(kind).Hear(ClusterState.OperatingEdit);
            never.Unsaved.Answer = UnsavedAnswer.NeverSaved;
            var (c1, _) = Composed(never);
            Assert.False(c1.ExitDue(true));
            Assert.Equal(new[] { "Discard", "Cancel" }, never.RunChoice!.Pending!.Buttons.Select(b => b.Label));
            never.RunChoice.Pending.Cancel();
            Assert.False(c1.ExitDue(false));

            // editing with nothing unsaved, a live run (the door stops it — ■ never asks), a cluster at rest ⇒ no question
            foreach (var state in new[] { ClusterState.OperatingEdit, ClusterState.OperatingLive, ClusterState.Idle })
            {
                var host = new DoorHost(kind).Hear(state);   // Unsaved = Unchanged
                var (ctrl, _) = Composed(host);
                Assert.True(ctrl.ExitDue(true), $"{kind}/{state}: nothing to ask ⇒ the exit is taken");
                Assert.Null(host.RunChoice!.Pending);
                Assert.Equal(new[] { "Leave:Exit" }, host.Control.Calls);       // the door's own leave ran (the session's raw leave stops a run; at rest it sends nothing)
            }
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>ONE scenario exit guard per process — the composition's — whatever the host combination</b>, over the PICKED owner's door (the same rule as ▶ and the scenario menu); none when no host has a scenario session
    /// (SimHost alone). 🔴 Mutation: <c>ComposeExitGuard</c> adding a guard per offering host ⇒ the count is 2 on the multi-host rows.
    /// </summary>
    [Fact]
    public void ThereIsExactlyOneScenarioExitGuard_PerProcess_OverThePickedOwnersDoor_InEveryHostCombination()
    {
        ISubsystem Make(RunHostKind k) => k == RunHostKind.SimHost ? new RunHost("SimHost", k, operatorHost: false) : new DoorHost(k);

        void Check(params RunHostKind[] kinds)
        {
            var (ctrl, _) = Composed(kinds.Select(Make).ToArray());
            var chain = Assert.IsType<ExitGuardChain>(ctrl.ExitGuard);
            var guard = Assert.Single(chain.Guards.OfType<ScenarioExitGuard>());
            Assert.Same(ctrl.ScenarioExit, guard);
            Assert.Same(ctrl.RunControlsOwner!.RunChoice!.Door, guard.Door);   // the picked owner's door, not another host's
            Assert.IsType<DoorHost>(ctrl.RunControlsOwner);
        }

        Check(RunHostKind.Editor);
        Check(RunHostKind.Cgf);
        Check(RunHostKind.Orchestrator);
        Check(RunHostKind.Cgf, RunHostKind.SimHost);
        Check(RunHostKind.SimHost, RunHostKind.Cgf);
        Check(RunHostKind.Orchestrator, RunHostKind.Cgf);
        Check(RunHostKind.Cgf, RunHostKind.Orchestrator);
        Check(RunHostKind.Editor, RunHostKind.SimHost);
        Check(RunHostKind.Orchestrator, RunHostKind.Cgf, RunHostKind.Editor);

        // no host with a scenario session ⇒ nothing of a scenario to lose, no scenario guard
        var (simOnly, _) = Composed(new RunHost("SimHost", RunHostKind.SimHost, operatorHost: false));
        Assert.Null(simOnly.ScenarioExit);
        Assert.Empty(((ExitGuardChain)simOnly.ExitGuard).Guards.OfType<ScenarioExitGuard>());
    }

    /// <summary>
    /// ⭐⭐ <b>The order of the two questions</b> (the editor's unsaved DOCUMENTS, then the scenario door): both can appear, EITHER Cancel stops the exit, and neither's approval takes the exit while the other is
    /// still open (the loop used to exit as soon as ANY guard approved). Documents first because the door's approval is the destructive step (it stops the run / unloads the scenario) — it runs only once nothing asked
    /// before it can still cancel; a Cancel at the document question never reaches the scenario question.
    /// 🔴 Mutations: the scenario guard placed FIRST ⇒ the scenario question is open before the documents are asked; the chain approving when any guard has ⇒ the document approval alone exits.
    /// </summary>
    [Fact]
    public void TheDocumentQuestionComesFirst_TheScenarioQuestionSecond_AndEitherCancelStopsTheExit()
    {
        var docs = new DocGuard();
        var host = new DoorHost(RunHostKind.Editor).Hear(ClusterState.OperatingEdit);
        host.Unsaved.Answer = UnsavedAnswer.Changed;
        var (ctrl, _) = Composed(docs, host);
        Assert.Equal(new IAppExitGuard[] { docs, ctrl.ScenarioExit! }, ((ExitGuardChain)ctrl.ExitGuard).Guards);

        // the documents are asked first; the scenario is not asked yet, and nothing exits while they are open
        Assert.False(ctrl.ExitDue(true));
        Assert.Equal(1, docs.Asked);
        Assert.Null(host.RunChoice!.Pending);
        Assert.False(ctrl.ExitDue(false));

        // the documents are answered in favour ⇒ NOW the scenario question appears, and the exit is still not taken
        docs.Approved = true;
        Assert.False(ctrl.ExitDue(false));
        Assert.NotNull(host.RunChoice.Pending);
        Assert.Empty(host.Control.Calls);

        // Cancel at the scenario question stops the exit; nothing was stopped
        host.RunChoice.Pending!.Cancel();
        Assert.False(ctrl.ExitDue(false));
        Assert.Empty(host.Control.Calls);

        // the next request starts over: documents again, then the scenario; Discard there takes the exit
        Assert.False(ctrl.ExitDue(true));
        Assert.Equal(2, docs.Asked);
        docs.Approved = true;
        Assert.False(ctrl.ExitDue(false));
        Press(host.RunChoice.Pending, "Discard");
        Assert.True(ctrl.ExitDue(false));
        Assert.Equal(new[] { "Leave:Exit" }, host.Control.Calls);

        // a Cancel at the DOCUMENT question never reaches the scenario question
        var docs2 = new DocGuard();
        var host2 = new DoorHost(RunHostKind.Editor).Hear(ClusterState.OperatingEdit);
        host2.Unsaved.Answer = UnsavedAnswer.Changed;
        var (ctrl2, _) = Composed(docs2, host2);
        Assert.False(ctrl2.ExitDue(true));
        for (int i = 0; i < 3; i++) Assert.False(ctrl2.ExitDue(false));     // the prompt is open / was cancelled: the exit is never taken
        Assert.Null(host2.RunChoice!.Pending);
        Assert.Empty(host2.Control.Calls);
    }

    // ── the one rest read, from any node ────────────────────────────────────────

    private static SubsystemDebugProvider Node(ClusterState? followed, bool gone = false, bool master = false)
        => new("n", "n", followedClusterState: () => followed, masterGone: () => gone,
               master: master ? () => new OrchestrationBusMasterControl(() => null) : null);

    [Fact]
    public void TheClusterRest_IsOneFact_TheFirstNonIdleStateAnyNodeReports_MasterGoneIfAnySays_CanOperateIfAnyHasTheMaster()
    {
        var none = Array.Empty<ClusterRest>();
        Assert.Equal(new ClusterRest(null, false, false), ClusterRestReader.From(new[] { Node(null), Node(null) }, none));          // no master seen
        Assert.Equal(ClusterState.Idle, ClusterRestReader.From(new[] { Node(null), Node(ClusterState.Idle) }, none).State);
        Assert.Equal(ClusterState.OperatingLive, ClusterRestReader.From(new[] { Node(ClusterState.Idle), Node(ClusterState.OperatingLive) }, none).State);  // a lagging Idle never holds Start up
        Assert.True(ClusterRestReader.From(new[] { Node(ClusterState.Idle), Node(null, gone: true) }, none).MasterGone);
        Assert.True(ClusterRestReader.From(new[] { Node(null), Node(null, master: true) }, none).CanOperate);
        Assert.False(ClusterRestReader.From(new[] { Node(null) }, none).CanOperate);

        // a subsystem that answers for itself (the editor) folds in the same way
        var editor = new[] { new ClusterRest(ClusterState.OperatingEdit, false, true) };
        Assert.Equal(new ClusterRest(ClusterState.OperatingEdit, false, true), ClusterRestReader.From(new[] { Node(ClusterState.Idle) }, editor));
    }

    [Fact]
    public void AProviderWithNoFollowedStateOfItsOwn_FallsBackToItsClusterState()
    {
        // the Orchestrator / ExCon / Replay Browser answer the cluster-wide fact in `ClusterState` already — the gate reads it unchanged
        var p = new SubsystemDebugProvider("Orchestrator", "Orchestrator", clusterState: () => ClusterState.OperatingPreview);
        Assert.Equal(ClusterState.OperatingPreview, p.FollowedClusterState);
        Assert.False(p.MasterGone);
    }
}
