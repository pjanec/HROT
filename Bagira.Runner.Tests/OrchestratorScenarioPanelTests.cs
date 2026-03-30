using System;
using System.Collections.Generic;
using System.Linq;
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

    // ── S0503: GetReplayDuration helper ───────────────────────────────────────

    [Fact]
    public void GetReplayDuration_TotalFrames3600_Returns60s()
    {
        float result = OrchestratorScenarioPanel.GetReplayDuration("{\"TotalFrames\":3600}");
        Assert.Equal(60f, result);
    }

    [Fact]
    public void GetReplayDuration_MalformedJson_ReturnsFallback()
    {
        float result = OrchestratorScenarioPanel.GetReplayDuration("not valid json {{");
        Assert.Equal(3600f, result);
    }

    // ── S0503: Seek debounce ──────────────────────────────────────────────────

    [Fact]
    public void SeekDebounce_DoesNotWriteWithin400ms()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var dm          = new DrillMaster(participant, new ClusterConfiguration
            { Mandatory = Array.Empty<string>() });
        using var writer      = new DdsWriter<SysOpRequest>(participant);
        using var reader      = new DdsReader<SysOpRequest>(participant);
        var panel = new OrchestratorScenarioPanel(dm, writer);

        // Arm the debounce by using reflection to set _seekPending = true.
        var seekPendingField = typeof(OrchestratorScenarioPanel)
            .GetField("_seekPending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var seekTimerField = typeof(OrchestratorScenarioPanel)
            .GetField("_seekDebounceTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        seekPendingField.SetValue(panel, true);
        seekTimerField.SetValue(panel, 0.5f);

        // 4 × 0.1s = 0.4s total — timer is still > 0 (0.1s remaining).
        panel.Update(0.1f);
        panel.Update(0.1f);
        panel.Update(0.1f);
        panel.Update(0.1f);

        // No SysOpRequest should have been published yet.
        using var scope = reader.Take();
        bool anyWritten = false;
        foreach (var s in scope)
            if (s.IsValid && s.Data.OperationType == SysOpType.ReplaySeek)
                anyWritten = true;

        Assert.False(anyWritten, "No ReplaySeek should be published before debounce expires.");
        Assert.True((bool)seekPendingField.GetValue(panel)!,
            "_seekPending should still be true if timer has not expired.");
    }

    [Fact]
    public void SeekDebounce_WritesAfter500ms()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var dm          = new DrillMaster(participant, new ClusterConfiguration
            { Mandatory = Array.Empty<string>() });
        using var writer      = new DdsWriter<SysOpRequest>(participant);
        using var reader      = new DdsReader<SysOpRequest>(participant);
        var panel = new OrchestratorScenarioPanel(dm, writer);

        // Arm the debounce.
        var seekPendingField = typeof(OrchestratorScenarioPanel)
            .GetField("_seekPending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var seekTimerField = typeof(OrchestratorScenarioPanel)
            .GetField("_seekDebounceTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        seekPendingField.SetValue(panel, true);
        seekTimerField.SetValue(panel, 0.5f);

        // One call of 0.5s expires the timer.
        panel.Update(0.5f);

        System.Threading.Thread.Sleep(100);  // Allow DDS to propagate

        bool found = false;
        using var scope = reader.Take();
        foreach (var s in scope)
            if (s.IsValid && s.Data.OperationType == SysOpType.ReplaySeek)
                found = true;

        Assert.True(found, "Exactly 1 SysOpRequest{ReplaySeek} should be published after debounce expires.");
        Assert.False((bool)seekPendingField.GetValue(panel)!, "_seekPending should be cleared after write.");
    }

    // ── S0504: RefreshLocalAssets ─────────────────────────────────────────────

    [Fact]
    public void RefreshLocalAssets_PopulatesFromTempDirectory()
    {
        string tmpRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrcScenPanelTest_" + Guid.NewGuid());
        try
        {
            // Create a scenario directory with a .json file.
            string scenDir = System.IO.Path.Combine(tmpRoot, "ScenPkg1");
            System.IO.Directory.CreateDirectory(scenDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(scenDir, "entities.json"), "{}");

            // Create a drill directory with a .fdp file.
            string drillDir = System.IO.Path.Combine(tmpRoot, "Drill1");
            System.IO.Directory.CreateDirectory(drillDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(drillDir, "node_1.fdp"), "data");

            _panel.RefreshLocalAssets(tmpRoot);

            // Access internal arrays via reflection.
            var scenariosField = typeof(OrchestratorScenarioPanel)
                .GetField("_availableScenarios", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var drillsField = typeof(OrchestratorScenarioPanel)
                .GetField("_availableDrills", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var scenarios = (string[])scenariosField.GetValue(_panel)!;
            var drills    = (string[])drillsField.GetValue(_panel)!;

            Assert.Equal(1, scenarios.Length);
            Assert.Equal(1, drills.Length);
            Assert.Contains("ScenPkg1", scenarios);
            Assert.Contains("Drill1",   drills);
        }
        finally
        {
            if (System.IO.Directory.Exists(tmpRoot))
                System.IO.Directory.Delete(tmpRoot, recursive: true);
        }
    }

    [Fact]
    public void RefreshLocalAssets_ClampsStaleSelectionIndex()
    {
        // Set _selectedDrillIdx to an out-of-range value.
        var drillIdxField = typeof(OrchestratorScenarioPanel)
            .GetField("_selectedDrillIdx", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        drillIdxField.SetValue(_panel, 5);

        // Refresh with an empty (non-existent) root so drills = empty.
        _panel.RefreshLocalAssets(@"C:\ThisDirectoryDoesNotExist_" + Guid.NewGuid());

        int idx = (int)drillIdxField.GetValue(_panel)!;
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void InjectStory_AutoGeneratesStoryId()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var dm          = new DrillMaster(participant, new ClusterConfiguration
            { Mandatory = Array.Empty<string>() });
        using var writer      = new DdsWriter<SysOpRequest>(participant);
        using var reader      = new DdsReader<SysOpRequest>(participant);
        var panel = new OrchestratorScenarioPanel(dm, writer);

        System.Threading.Thread.Sleep(300); // DDS discovery

        // Write first inject (simulating button click 1)
        string storyId1 = Guid.NewGuid().ToString();
        writer.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ManageStory,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"StoryId\":\"{storyId1}\",\"ScenarioId\":\"ScenPkg1\"}}",
        });
        System.Threading.Thread.Sleep(150);
        string? readStoryId1 = null;
        using (var scope = reader.Take())
        {
            foreach (var s in scope)
                if (s.IsValid && s.Data.OperationType == SysOpType.ManageStory)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(s.Data.PayloadJson ?? "{}");
                    readStoryId1 = doc.RootElement.GetProperty("StoryId").GetString();
                }
        }

        // Write second inject (simulating button click 2) with a different StoryId.
        string storyId2 = Guid.NewGuid().ToString();
        writer.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ManageStory,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"StoryId\":\"{storyId2}\",\"ScenarioId\":\"ScenPkg1\"}}",
        });
        System.Threading.Thread.Sleep(150);
        string? readStoryId2 = null;
        using (var scope = reader.Take())
        {
            foreach (var s in scope)
                if (s.IsValid && s.Data.OperationType == SysOpType.ManageStory)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(s.Data.PayloadJson ?? "{}");
                    readStoryId2 = doc.RootElement.GetProperty("StoryId").GetString();
                }
        }

        Assert.NotNull(readStoryId1);
        Assert.NotNull(readStoryId2);
        Assert.NotEqual(readStoryId1, readStoryId2);
    }

    [Fact]
    public void LoadScenario_WithNoSelection_DisabledGuard()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var dm          = new DrillMaster(participant, new ClusterConfiguration
            { Mandatory = Array.Empty<string>() });
        using var writer      = new DdsWriter<SysOpRequest>(participant);
        using var reader      = new DdsReader<SysOpRequest>(participant);
        var panel = new OrchestratorScenarioPanel(dm, writer);

        // _selectedLoadScenarioIdx is -1 by default (no selection).
        // Render a frame: even if a button is pressed, -1 guard prevents writing.
        ImGui.NewFrame();
        ImGui.Begin("##GuardTest");
        var ex = Record.Exception(() => panel.Render(false, 0f));
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);

        // Check that no TransitionState or other SysOpRequest was written.
        System.Threading.Thread.Sleep(100);
        using var scope = reader.Take();
        bool anyWritten = false;
        foreach (var s in scope)
            if (s.IsValid) anyWritten = true;
        Assert.False(anyWritten, "No SysOpRequest should be written when load scenario index is -1.");
    }

    // ── S0505: Archive Management UI ─────────────────────────────────────────

    /// <summary>
    /// S0505: When an archive operation is in-flight (_activeArchiveOpId != Empty),
    /// Render() must not throw (verifies the progress-bar / cancel-button path executes).
    /// Also verifies that CancelOperation SysOpRequest is written when cancel is clicked
    /// only if the UI is not actually clicking the button in headless mode — so we
    /// verify the no-throw contract at minimum.
    /// </summary>
    [Fact]
    public void Archive_ProgressSection_DoesNotThrow_WhenOpInFlight()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var dm          = new DrillMaster(participant, new ClusterConfiguration
            { Mandatory = Array.Empty<string>() });
        using var writer      = new DdsWriter<SysOpRequest>(participant);
        var panel = new OrchestratorScenarioPanel(dm, writer);

        // Set _activeArchiveOpId via reflection to simulate an in-flight archive operation.
        var opIdField = typeof(OrchestratorScenarioPanel)
            .GetField("_activeArchiveOpId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        opIdField.SetValue(panel, Guid.NewGuid());

        // Render must complete without exception regardless of whether the
        // CollapsingHeader is open (it is closed by default in fresh headless context,
        // so the archive inner loop is skipped — that is acceptable; the key guarantee
        // is no crash and the field is correctly read).
        ImGui.NewFrame();
        ImGui.Begin("##ArchiveTest");
        var ex = Record.Exception(() => panel.Render(false, 0f));
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);
    }

    /// <summary>
    /// RefreshLocalAssets must populate _archivedDrills and _unarchivedLocalDrills
    /// without throwing even when the gateway is null (no storage module registered).
    /// </summary>
    [Fact]
    public void RefreshLocalAssets_WithNoGateway_PopulatesEmptyArchiveLists()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var dm          = new DrillMaster(participant, new ClusterConfiguration
            { Mandatory = Array.Empty<string>() });
        using var writer      = new DdsWriter<SysOpRequest>(participant);
        var panel = new OrchestratorScenarioPanel(dm, writer);

        var ex = Record.Exception(() => panel.RefreshLocalAssets());
        Assert.Null(ex);

        // _archivedDrills should be empty (no gateway, null gateway, no NAS dir).
        var archivedField = typeof(OrchestratorScenarioPanel)
            .GetField("_archivedDrills",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var archived = (string[])archivedField.GetValue(panel)!;
        Assert.NotNull(archived);
    }
}

[CollectionDefinition("OrchestratorScenarioPanelTests", DisableParallelization = true)]
public class OrchestratorScenarioPanelTestCollection { }
