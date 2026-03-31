using System;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.ClusterRunner.Services;
using CycloneDDS.Runtime;
using ImGuiNET;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Tests for <see cref="ClusterScenarioPanel"/> (CGF1-S0506).
///
/// The panel now takes a <see cref="ClusterUiCache"/> instead of <see cref="ClusterMaster"/>.
/// Tests create a real DdsParticipant and a <see cref="ClusterUiCache"/> stubbed by writing
/// DDS samples from within the same process.
/// </summary>
[Collection("ClusterScenarioPanelTests")]
public sealed class ClusterScenarioPanelTests : IDisposable
{
    private const int TestDomain = 28;

    private readonly DdsParticipant             _participant;
    private readonly ClusterUiCache             _uiCache;
    private readonly DdsWriter<ClusterOpRequest>    _sysOpWriter;
    private readonly ClusterScenarioPanel       _panel;
    private IntPtr _imguiCtx;

    public ClusterScenarioPanelTests()
    {
        _participant  = new DdsParticipant(TestDomain);
        _uiCache      = new ClusterUiCache(_participant);
        _sysOpWriter  = new DdsWriter<ClusterOpRequest>(_participant);
        _panel        = new ClusterScenarioPanel(_sysOpWriter, _uiCache);
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
        _uiCache.Dispose();
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

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var ex = Record.Exception(() => new ClusterScenarioPanel(_sysOpWriter, _uiCache));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_NullSysOpWriter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ClusterScenarioPanel(null!, _uiCache));
    }

    [Fact]
    public void Constructor_NullUiCache_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ClusterScenarioPanel(_sysOpWriter, null!));
    }

    // ── Rendering (headless ImGui) ─────────────────────────────────────────────

    [Fact]
    public void Render_BeforeBootstrap_DoesNotThrow()
    {
        // uiCache is not bootstrapped (no DDS samples written)
        Exception? ex = null;
        ImGui.NewFrame();
        ImGui.Begin("##PanelTestWin");
        ex = Record.Exception(() => _panel.Render(_uiCache, disableAll: true));
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);
    }

    [Fact]
    public void Render_MultipleFrames_DoesNotThrow()
    {
        for (int i = 0; i < 3; i++)
        {
            ImGui.NewFrame();
            ImGui.Begin("##PanelTestWin");
            var ex = Record.Exception(() => _panel.Render(_uiCache, disableAll: false));
            ImGui.End();
            ImGui.Render();

            Assert.Null(ex);
        }
    }

    // ── GetReplayDuration (static helper) ──────────────────────────────────────

    [Fact]
    public void GetReplayDuration_TotalFrames3600_Returns60s()
    {
        float result = ClusterScenarioPanel.GetReplayDuration("{\"TotalFrames\":3600}");
        Assert.Equal(60f, result);
    }

    [Fact]
    public void GetReplayDuration_MalformedJson_ReturnsFallback()
    {
        float result = ClusterScenarioPanel.GetReplayDuration("not valid json {{");
        Assert.Equal(3600f, result);
    }

    // ── Seek debounce ──────────────────────────────────────────────────────────

    [Fact]
    public void SeekDebounce_DoesNotWriteWithin400ms()
    {
        using var reader = new DdsReader<ClusterOpRequest>(_participant);

        var seekPendingField = typeof(ClusterScenarioPanel)
            .GetField("_seekPending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var seekTimerField = typeof(ClusterScenarioPanel)
            .GetField("_seekDebounceTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        seekPendingField.SetValue(_panel, true);
        seekTimerField.SetValue(_panel, 0.5f);

        // 4 × 0.1s = 0.4s total — timer still > 0
        _panel.Update(0.1f);
        _panel.Update(0.1f);
        _panel.Update(0.1f);
        _panel.Update(0.1f);

        using var scope = reader.Take();
        bool anyWritten = false;
        foreach (var s in scope)
            if (s.IsValid && s.Data.OperationType == ClusterOpType.ReplaySeek)
                anyWritten = true;

        Assert.False(anyWritten, "No ReplaySeek should be published before debounce expires.");
        Assert.True((bool)seekPendingField.GetValue(_panel)!,
            "_seekPending should still be true if timer has not expired.");
    }

    [Fact]
    public void SeekDebounce_WritesAfter500ms()
    {
        using var reader = new DdsReader<ClusterOpRequest>(_participant);

        var seekPendingField = typeof(ClusterScenarioPanel)
            .GetField("_seekPending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var seekTimerField = typeof(ClusterScenarioPanel)
            .GetField("_seekDebounceTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        seekPendingField.SetValue(_panel, true);
        seekTimerField.SetValue(_panel, 0.5f);

        _panel.Update(0.5f);

        Thread.Sleep(100);

        bool found = false;
        using var scope = reader.Take();
        foreach (var s in scope)
            if (s.IsValid && s.Data.OperationType == ClusterOpType.ReplaySeek)
                found = true;

        Assert.True(found, "1 ClusterOpRequest{ReplaySeek} should be published after debounce expires.");
        Assert.False((bool)seekPendingField.GetValue(_panel)!, "_seekPending should be cleared after write.");
    }

    // ── Archive progress section ───────────────────────────────────────────────

    [Fact]
    public void Archive_ProgressSection_DoesNotThrow_WhenOpInFlight()
    {
        var opIdField = typeof(ClusterScenarioPanel)
            .GetField("_activeArchiveOpId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        opIdField.SetValue(_panel, Guid.NewGuid());

        ImGui.NewFrame();
        ImGui.Begin("##ArchiveTest");
        var ex = Record.Exception(() => _panel.Render(_uiCache, disableAll: false));
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);
    }

    // ── LoadScenario guard ─────────────────────────────────────────────────────

    [Fact]
    public void LoadScenario_WithNoSelection_NoWriteOccurs()
    {
        using var reader = new DdsReader<ClusterOpRequest>(_participant);

        ImGui.NewFrame();
        ImGui.Begin("##GuardTest");
        var ex = Record.Exception(() => _panel.Render(_uiCache, disableAll: false));
        ImGui.End();
        ImGui.Render();

        Assert.Null(ex);

        Thread.Sleep(100);
        using var scope = reader.Take();
        bool anyWritten = false;
        foreach (var s in scope)
            if (s.IsValid) anyWritten = true;
        Assert.False(anyWritten, "No ClusterOpRequest should be written when selection index is -1.");
    }
}

[CollectionDefinition("ClusterScenarioPanelTests", DisableParallelization = true)]
public class ClusterScenarioPanelTestCollection { }
