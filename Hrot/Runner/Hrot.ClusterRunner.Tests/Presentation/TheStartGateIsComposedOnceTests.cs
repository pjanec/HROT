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

    // ── D12: the armed default is ONE table (CE-1437: re-homed from the runner's HostArmedModes onto RunArmedDefaults) ──

    [Theory]
    [InlineData(RunHostKind.Editor, RunArmed.Preview)]
    [InlineData(RunHostKind.Cgf, RunArmed.Live)]
    [InlineData(RunHostKind.SimHost, RunArmed.Live)]
    [InlineData(RunHostKind.Other, RunArmed.Live)]
    public void TheArmedDefault_IsPreviewInTheEditor_LiveElsewhere(RunHostKind kind, RunArmed expected)
        => Assert.Equal(expected, RunArmedDefaults.For(kind));

    [Fact]
    public void TheArmedDefaultTable_HasNoOtherPreviewHost()
        => Assert.Equal(new[] { RunHostKind.Editor },
            System.Enum.GetValues<RunHostKind>().Where(k => RunArmedDefaults.For(k) == RunArmed.Preview).ToArray());

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

    /// <summary>A host that offers run controls over a REAL adapter + choice (built like the real hosts: <see cref="RunArmedDefaults"/> for its kind, a session only when it is an operator).</summary>
    private sealed class RunHost : MockSubsystem, IProvidesRunControls
    {
        public RunHostKind HostKind { get; }
        public ITimeTransportFacade? Transport { get; }
        public RunModeChoice? RunChoice { get; }
        public RunHost(string name, RunHostKind kind, bool operatorHost, bool offers = true) : base(name)
        {
            HostKind = kind;
            if (!offers) return;
            var adapter = new ClusterTimeTransportAdapter(new Fdp.Core.FdpEventBus(), () => null, () => 0.0);
            RunChoice = adapter.AttachRunChoice(RunArmedDefaults.For(kind), operatorHost ? new FakeControl() : null);
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
        Assert.Equal(RunArmed.Preview, ctrl.RunControlsOwner!.RunChoice!.HostDefault);
    }

    [Fact]
    public void TwoOperators_TheEditorsWins_WhateverTheOrder()
    {
        var editor = new RunHost("Editor", RunHostKind.Editor, operatorHost: true);
        var cgf = new RunHost("CGF", RunHostKind.Cgf, operatorHost: true);
        Assert.Same(editor, Composed(cgf, editor).Ctrl.RunControlsOwner);
        Assert.Same(editor, Composed(editor, cgf).Ctrl.RunControlsOwner);
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
