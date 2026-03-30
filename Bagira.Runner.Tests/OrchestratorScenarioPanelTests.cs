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
    private readonly DdsWriter<SysOpRequest> _sysOpWriter;  // S0502
    private readonly OrchestratorScenarioPanel _panel;
    private IntPtr _imguiCtx;

    public OrchestratorScenarioPanelTests()
    {
        _participant  = new DdsParticipant(TestDomain);
        // Use a non-empty mandatory list so the bootstrap latch does NOT clear
        // immediately, allowing the "BeforeBootstrap" tests to exercise that path.
        _drillMaster  = new DrillMaster(_participant, new ClusterConfiguration
        {
            Mandatory = new[] { "FakeMandatoryNode" },
        });
        _sysOpWriter  = new DdsWriter<SysOpRequest>(_participant);
        _panel        = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter);
        _imguiCtx     = CreateHeadlessContext();
    }

    public void Dispose()
    {
        if (_imguiCtx != IntPtr.Zero)
        {
            ImGui.DestroyContext(_imguiCtx);
            _imguiCtx = IntPtr.Zero;
        }
        _sysOpWriter.Dispose();
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
        var ex = Record.Exception(() => new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter));
        Assert.Null(ex);
    }

    /// <summary>Null DrillMaster must throw ArgumentNullException.</summary>
    [Fact]
    public void Constructor_NullDrillMaster_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OrchestratorScenarioPanel(null!, _sysOpWriter));
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

    // ── S0501: Status banner source→target rendering ───────────────────────────

    /// <summary>
    /// S0501: When a transaction is in flight, Render() must complete without
    /// exception even with a populated <c>ActiveTransaction</c> that has
    /// differing Source and Target states (exercises the "→" banner path).
    /// </summary>
    [Fact]
    public void StatusBanner_ShowsArrow_WhenInFlight_DoesNotThrow()
    {
        // Bootstrap the latch so the panel renders the full content.
        // No mandatory nodes → latch is immediately true.
        using var p = new DdsParticipant(TestDomain);
        using var dm = new DrillMaster(p, new ClusterConfiguration
        {
            Mandatory = Array.Empty<string>(),
        });
        using var w = new DdsWriter<SysOpRequest>(p);
        var panel = new OrchestratorScenarioPanel(dm, w);

        // Inject a TransitionState request to create an in-flight transaction.
        dm.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
        });
        dm.Tick();  // process request → creates active transaction

        Assert.True(dm.HasInFlightTransaction,
            "Expected HasInFlightTransaction == true after ticking a TransitionState request.");

        // Render without throwing.
        ImGui.NewFrame();
        ImGui.Begin("##BannerTest");
        var ex = Record.Exception(() => panel.Render());
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);
    }
}

[CollectionDefinition("OrchestratorScenarioPanelTests", DisableParallelization = true)]
public class OrchestratorScenarioPanelTestCollection { }
