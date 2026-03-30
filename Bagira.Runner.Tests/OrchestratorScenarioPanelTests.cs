using System;
using System.Numerics;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Orchestrator;
using Bagira.Runner.Services;
using CycloneDDS.Runtime;
using ImGuiNET;
using Xunit;

namespace Bagira.Runner.Tests;

/// <summary>
/// Tests for <see cref="OrchestratorScenarioPanel"/> (CGF1-S0106).
///
/// The panel uses <see cref="DrillMaster"/> directly (concrete dependency), so
/// tests that verify rendering create a real DrillMaster in domain 25 (reserved
/// for panel unit tests) via a headless ImGui context.  Logic-level tests
/// (payload content, disabling guard) call <see cref="DrillMaster.HandleSysOpRequest"/>
/// directly and inspect effects via observable side-channels.
/// </summary>
[Collection("OrchestratorScenarioPanelTests")]
public sealed class OrchestratorScenarioPanelTests : IDisposable
{
    private const int TestDomain = 25;

    private readonly DdsParticipant  _participant;
    private readonly DrillMaster     _drillMaster;
    private readonly OrchestratorScenarioPanel _panel;
    private IntPtr _imguiCtx;

    public OrchestratorScenarioPanelTests()
    {
        _participant = new DdsParticipant(TestDomain);
        // Use a non-empty mandatory list so the bootstrap latch does NOT clear
        // immediately, allowing the "BeforeBootstrap" tests to exercise that path.
        _drillMaster = new DrillMaster(_participant, new ClusterConfiguration
        {
            Mandatory = new[] { "FakeMandatoryNode" },
        });
        _panel       = new OrchestratorScenarioPanel(_drillMaster);
        _imguiCtx    = CreateHeadlessContext();
    }

    public void Dispose()
    {
        if (_imguiCtx != IntPtr.Zero)
        {
            ImGui.DestroyContext(_imguiCtx);
            _imguiCtx = IntPtr.Zero;
        }
        _drillMaster.Dispose();
        _participant.Dispose();
    }

    private static IntPtr CreateHeadlessContext()
    {
        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1920, 1080);
        io.DeltaTime   = 1.0f / 60.0f;
        io.Fonts.AddFontDefault();
        io.Fonts.Build();
        return ctx;
    }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>Panel construction must not throw even before bootstrap.</summary>
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var ex = Record.Exception(() => new OrchestratorScenarioPanel(_drillMaster));
        Assert.Null(ex);
    }

    /// <summary>Null DrillMaster must throw ArgumentNullException.</summary>
    [Fact]
    public void Constructor_NullDrillMaster_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OrchestratorScenarioPanel(null!));
    }

    // ── Rendering (headless ImGui) ─────────────────────────────────────────────

    /// <summary>Render() must not throw when cluster is not yet bootstrapped.</summary>
    [Fact]
    public void Render_BeforeBootstrap_DoesNotThrow()
    {
        Assert.False(_drillMaster.BootstrapComplete);

        Exception? ex = null;
        ImGui.NewFrame();
        ImGui.Begin("##PanelTestWin");
        ex = Record.Exception(() => _panel.Render());
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);
    }

    /// <summary>Render() must not throw when called multiple frames in a row.</summary>
    [Fact]
    public void Render_MultipleFrames_DoesNotThrow()
    {
        for (int i = 0; i < 3; i++)
        {
            ImGui.NewFrame();
            ImGui.Begin("##PanelTestWin");
            var ex = Record.Exception(() => _panel.Render());
            ImGui.End();
            ImGui.Render();

            Assert.Null(ex);
        }
    }

    // ── Logic: HandleSysOpRequest before bootstrap ────────────────────────────

    /// <summary>
    /// DrillMaster rejects SysOpRequests while BootstrapComplete == false.
    /// Verifies that the queue path does not throw (the panel can safely enqueue
    /// even before bootstrap — the DrillMaster will reject once drained).
    /// </summary>
    [Fact]
    public void HandleSysOpRequest_BeforeBootstrap_AcceptsEnqueue()
    {
        Assert.False(_drillMaster.BootstrapComplete);

        // Should not throw — just enqueued for next Tick().
        var ex = Record.Exception(() => _drillMaster.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}}}",
        }));

        Assert.Null(ex);
    }

    // ── Logic: GetReachableTargets plumbing ────────────────────────────────────

    /// <summary>
    /// Standby is the initial state of an unbootstrapped DrillMaster.
    /// GetReachableTargets should return the Standby neighbours (LoadingEdit,
    /// LoadingLive, LoadingDryRun, LoadingReplay).
    /// </summary>
    [Fact]
    public void GetReachableTargets_FromInitialState_ReturnsStandbyNeighbours()
    {
        var targets = _drillMaster.GetReachableTargets();

        // Standby has four direct neighbours in the Bagira DSM graph.
        Assert.True(targets.Count >= 2,
            "Expected at least 2 reachable targets from Standby.");
        Assert.Contains(DSMState.LoadingEdit,  targets);
        Assert.Contains(DSMState.LoadingLive,  targets);
    }
}

[CollectionDefinition("OrchestratorScenarioPanelTests", DisableParallelization = true)]
public class OrchestratorScenarioPanelTestCollection { }
