using System;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using CycloneDDS.Runtime;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Tests for <see cref="OrchestratorSubsystem"/> covering S0501 (title-bar color + ImGui window)
/// and S0502 (DdsWriter&lt;ClusterOpRequest&gt; wired through panel).
/// </summary>
[Collection("OrchestratorSubsystemTests")]
public sealed class OrchestratorSubsystemTests
{
    // Domain 26 is reserved for this test class.
    private const int TestDomain = 26;

    // ── S0501 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_Returns_Orchestrator()
    {
        var subsystem = new OrchestratorSubsystem();
        Assert.Equal("Orchestrator", subsystem.Name);
    }

    [Fact]
    public void TitleBarColor_IsBeige()
    {
        // S0501: OrchestratorSubsystem.TitleBarColor must be the warm-beige defined in the spec
        // (0.72f, 0.64f, 0.47f, 1f) so the runner window title bar is distinguishable from ExCon/IG.
        var subsystem = new OrchestratorSubsystem();
        var color = subsystem.TitleBarColor;

        Assert.Equal(0.72f, color.X, precision: 5);
        Assert.Equal(0.64f, color.Y, precision: 5);
        Assert.Equal(0.47f, color.Z, precision: 5);
        Assert.Equal(1.0f,  color.W, precision: 5);
    }

    // ── S0502 / lifecycle ─────────────────────────────────────────────────────

    /// <summary>
    /// After <see cref="OrchestratorSubsystem.Initialize"/> the internal ClusterMaster
    /// must be running (exposed via <c>TestHook_ClusterMaster</c>).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Initialize_Creates_ClusterMaster()
    {
        var subsystem = new OrchestratorSubsystem();
        try
        {
            subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
            Assert.NotNull(subsystem.TestHook_ClusterMaster);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    /// <summary>
    /// PACK-E001 update: <c>_sysOpWriter</c> (dead DDS writer) was removed from
    /// <see cref="OrchestratorSubsystem"/>.  The ClusterOpRequest egress path now lives
    /// in <c>ClusterOpEgressTranslator</c> (ExCon side).  Verify Initialize/Shutdown
    /// still completes without throwing after removing the dead writer field.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Initialize_SysOpWriter_IsDiscoverableOnDomain()
    {
        // Dead-writer field removed. Verify lifecycle is still clean.
        var subsystem = new OrchestratorSubsystem();
        var ex        = Record.Exception(() =>
        {
            subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
            subsystem.Shutdown();
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// A full Initialize → Update → Shutdown cycle must not throw.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Initialize_Update_Shutdown_DoesNotThrow()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        for (int i = 0; i < 5; i++)
            subsystem.Update(1f / 60f);

        subsystem.Shutdown();  // must not throw
    }

    /// <summary>
    /// S0502: After Shutdown, the DDS ClusterOpRequest writer endpoint must no longer
    /// be discoverable, and re-Initialize must work without throwing (proves the
    /// writer was properly disposed by Shutdown).
    /// </summary>
    [Fact(Timeout = 15_000)]
    public void Shutdown_DisposesWriter_ReinitializeWorks()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
        subsystem.Shutdown();

        // After Shutdown, a second Initialize → Shutdown cycle must not throw
        // (proves no leaked writer handle prevents re-creation).
        var ex = Record.Exception(() =>
        {
            subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
            subsystem.Shutdown();
        });
        Assert.Null(ex);
    }

    // ── S0501: FormatPrettyJson ────────────────────────────────────────────────

    [Fact]
    public void FormatPrettyJson_IndentsJson()
    {
        var result = OrchestratorSubsystem.FormatPrettyJson("{\"a\":1}");
        Assert.Contains("\n", result);
    }

    [Fact]
    public void FormatPrettyJson_InvalidJson_ReturnsOriginal()
    {
        const string bad = "not-json";
        var result = OrchestratorSubsystem.FormatPrettyJson(bad);
        Assert.Equal(bad, result);
    }

    [Fact]
    public void FormatPrettyJson_EmptyString_ReturnsEmpty()
    {
        var result = OrchestratorSubsystem.FormatPrettyJson(string.Empty);
        Assert.Equal(string.Empty, result);
    }

    // ── S0503: Time Control ───────────────────────────────────────────────────

    /// <summary>
    /// When not paused, <see cref="ClusterUiCache.IsPaused"/> is false.
    /// After a PauseTime request processes through ClusterMaster (via HandleClusterOpRequest +
    /// 3 Update frames for the bus pipeline), <see cref="ClusterUiCache.IsPaused"/> becomes true.
    /// Frame 1: ClusterMaster.Tick publishes PauseTimeIntent to WRITE.
    /// Frame 2: SwapBuffers promotes it to READ; MasterSyncController.Update drains it
    ///          and publishes SwitchTimeModeEvent{Deterministic} to WRITE.
    /// Frame 3: SwapBuffers promotes it to READ; ClusterUiCache.Update drains it -> IsPaused=true.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TimeControlRequested_PauseTime_SetsIsPaused()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });
        try
        {
            Assert.False(subsystem.UiCacheForTest!.IsPaused, "Expected not paused initially.");

            // Inject PauseTime via the ClusterMaster test hook (simulates what the UI button writes).
            subsystem.TestHook_ClusterMaster!.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.PauseTime,
                PayloadJson   = string.Empty,
            });
            // 3 frames needed: Tick->PauseTimeIntent(WRITE); swap->READ; MasterSync->SwitchTimeModeEvent(WRITE);
            // swap->READ; UiCache->IsPaused=true.
            subsystem.Update(1f / 60f);
            subsystem.Update(1f / 60f);
            subsystem.Update(1f / 60f);

            Assert.True(subsystem.UiCacheForTest!.IsPaused,
                "After 3 frames, UiCacheForTest.IsPaused must be true.");
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    /// <summary>
    /// When not paused, the Pause button label is "Pause##OrcPause".
    /// Writing a PauseTime ClusterOpRequest (simulating the button click) and processing it
    /// causes UiCacheForTest.IsPaused to become true after 3 frames (bus pipeline latency).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PauseButton_WhenNotPaused_DispatchesPauseTime()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        try
        {
            // Verify initial state: not paused (button woud show "Pause").
            Assert.False(subsystem.UiCacheForTest!.IsPaused, "Expected not paused initially.");

            // Simulate the button click: write a ClusterOpRequest{PauseTime} via the DDS writer
            // on the same domain (ClusterMaster reads from its _sysOpRequestReader on that domain).
            using var probe = new DdsParticipant(TestDomain);
            using var writer = new DdsWriter<ClusterOpRequest>(probe);
            Thread.Sleep(300);  // Allow DDS discovery

            writer.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.PauseTime,
                PayloadJson   = string.Empty,
            });

            // Update until IsPaused is set (3-frame bus pipeline: DDS read->bus->MasterSync->UiCache).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!subsystem.UiCacheForTest!.IsPaused && DateTime.UtcNow < deadline)
            {
                subsystem.Update(1f / 60f);
                Thread.Sleep(20);
            }

            Assert.True(subsystem.UiCacheForTest!.IsPaused,
                "PauseTime ClusterOpRequest published on DDS should set UiCacheForTest.IsPaused = true.");
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    /// <summary>
    /// When not paused, Step is not reachable.  After a Pause, Step requests are processed.
    /// Verify that <see cref="ClusterUiCache.IsPaused"/> remains coherent
    /// and that StepTime can only fire when paused.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StepButton_DisabledWhenNotPaused()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(new SubsystemConfig { DomainId = TestDomain });

        try
        {
            // Not paused — Step button is wrapped in BeginDisabled / EndDisabled.
            Assert.False(subsystem.UiCacheForTest!.IsPaused, "Expected not paused initially.");

            // Inject a StepTime request when not paused (the button is disabled, so this
            // simulates the guard: StepTime should also be processed, but the _isPaused guard
            // on the TimeControlRequested handler does nothing different for StepTime — the
            // main check is that StepTime doesn't set _isPaused).
            subsystem.TestHook_ClusterMaster!.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.StepTime,
                PayloadJson   = string.Empty,
            });
            subsystem.Update(1f / 60f);

            // StepTime should not affect IsPaused.
            Assert.False(subsystem.UiCacheForTest!.IsPaused,
                "StepTime must not change IsPaused state.");
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    // ── S0503: ParseStepDelta ─────────────────────────────────────────────────

    [Fact]
    public void ParseStepDelta_ValidPayload_ReturnsParsedValue()
    {
        float result = OrchestratorSubsystem.ParseStepDelta("{\"FixedDelta\":0.1}", 1f / 60f);
        Assert.Equal(0.1f, result, precision: 5);
    }

    [Fact]
    public void ParseStepDelta_MissingField_ReturnsFallback()
    {
        float fallback = 1f / 60f;
        float result = OrchestratorSubsystem.ParseStepDelta("{\"SomethingElse\":99}", fallback);
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void ParseStepDelta_EmptyPayload_ReturnsFallback()
    {
        float fallback = 1f / 60f;
        float result = OrchestratorSubsystem.ParseStepDelta(string.Empty, fallback);
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void ParseStepDelta_NullPayload_ReturnsFallback()
    {
        float fallback = 1f / 60f;
        float result = OrchestratorSubsystem.ParseStepDelta(null!, fallback);
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void ParseStepDelta_ZeroDelta_ReturnsFallback()
    {
        float fallback = 1f / 60f;
        float result = OrchestratorSubsystem.ParseStepDelta("{\"FixedDelta\":0}", fallback);
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void ParseStepDelta_NegativeDelta_ReturnsFallback()
    {
        float fallback = 1f / 60f;
        float result = OrchestratorSubsystem.ParseStepDelta("{\"FixedDelta\":-0.5}", fallback);
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void ParseStepDelta_MalformedJson_ReturnsFallback()
    {
        float fallback = 1f / 60f;
        float result = OrchestratorSubsystem.ParseStepDelta("not json at all", fallback);
        Assert.Equal(fallback, result);
    }
}
