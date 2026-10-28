using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Presentation;
using Hrot.ClusterRunner.Tests.Mocks;
using Hrot.Presentation.DebugApi;
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

    private static LocalWindowController Controller(IReadOnlyList<ISubsystem> subsystems, HostArmedMode mode = HostArmedMode.Live)
        => new(new GZH012_Tests.FakePresentationShell(), subsystems, new RunnerOptions(), null, mode);

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

    [Fact]
    public void StartIsNeverTheStartupPerspective_EvenWhenItIsTheOnlyOneClaimed()
    {
        var claimed = new[] { "Start", "Scenario" };
        Assert.Equal("Scenario", LocalWindowController.ResolveStartupPerspective(claimed, null, persisted: "Start"));
        Assert.Equal("Default", LocalWindowController.ResolveStartupPerspective(new[] { "Start" }, null, persisted: null));
    }

    // ── D12: the armed mode is one parameter ────────────────────────────────────

    [Theory]
    [InlineData("editor", true)]
    [InlineData("cgf", false)]
    [InlineData("all", false)]
    [InlineData("orchestrator", false)]
    public void TheArmedMode_IsPreviewInTheEditor_LiveElsewhere(string mode, bool preview)
    {
        var expected = preview ? HostArmedMode.Preview : HostArmedMode.Live;
        Assert.Equal(expected, HostArmedModes.For(new[] { mode }));
        Assert.Equal(expected, Controller(new ISubsystem[] { new MockSubsystem(mode) }, HostArmedModes.For(new[] { mode })).ArmedMode);
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
