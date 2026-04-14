using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Time.Messages;
using Hrot.Orchestrator;
using Hrot.Orchestrator.Panels;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Verifies HEXAG2-S001: OrchestratorSubsystem uses a single unified FdpEventBus so that
/// SwitchTimeModeEvent published to the bus is visible to ClusterUiCache and correctly
/// updates IsPaused after one Update() call.
/// </summary>
public sealed class OrchestratorSubsystemBusTests
{
    private static SubsystemConfig HeadlessConfig() => new SubsystemConfig
    {
        Headless = true,
        DomainId = 224,
    };

    [Fact]
    public void OrchestratorSubsystem_PauseUpdatesIsPaused()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        try
        {
            var bus     = subsystem.TimeBusForTest!;
            var uiCache = subsystem.UiCacheForTest!;

            // Publish a time-mode switch (Deterministic = paused) to the bus write buffer.
            // Swap to promote to the read buffer via bus.SwapBuffers(), then call
            // uiCache.Update() directly to drain it.  This proves that TimeBusForTest and
            // UiCacheForTest reference the SAME unified bus (HEXAG2-S001 success condition).
            bus.Publish(new SwitchTimeModeEvent { TargetMode = TimeMode.Deterministic });
            bus.SwapBuffers();
            uiCache.Update();

            Assert.True(uiCache.IsPaused);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }

    [Fact]
    public void OrchestratorSubsystem_ResumeClears_IsPaused()
    {
        var subsystem = new OrchestratorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        try
        {
            var bus     = subsystem.TimeBusForTest!;
            var uiCache = subsystem.UiCacheForTest!;

            // Pause first.
            bus.Publish(new SwitchTimeModeEvent { TargetMode = TimeMode.Deterministic });
            bus.SwapBuffers();
            uiCache.Update();
            Assert.True(uiCache.IsPaused);

            // Resume: publish Continuous mode.
            bus.Publish(new SwitchTimeModeEvent { TargetMode = TimeMode.Continuous });
            bus.SwapBuffers();
            uiCache.Update();

            Assert.False(uiCache.IsPaused);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }
}
