using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Time.Messages;
using Hrot.ExCon;
using Hrot.Orchestrator.Panels;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Verifies HEXAG2-S001b: SwitchTimeModeEvent published to the observer bus (where
/// DDS time-mode ingress translators write) is visible to ClusterUiCache and correctly
/// updates IsPaused after one Update() call.
/// </summary>
public sealed class ExConSubsystemBusTests
{
    private static SubsystemConfig HeadlessConfig() => new SubsystemConfig
    {
        Headless = true,
        DomainId = 222,
    };

    [Fact]
    public void ExConSubsystem_ClusterUiCache_UpdatesIsPaused_AfterSwitchTimeModeEvent()
    {
        var subsystem = new ExConSubsystem();
        subsystem.Initialize(HeadlessConfig());
        try
        {
            var bus     = subsystem.ObserverBusForTest!;
            var uiCache = subsystem.UiCacheForTest!;

            // Publish a time-mode switch (Deterministic = paused) to the bus write buffer.
            // Update() swaps it into the read buffer so ClusterUiCache.Update() can consume it.
            bus.Publish(new SwitchTimeModeEvent { TargetMode = TimeMode.Deterministic });
            subsystem.Update(0f);

            Assert.True(uiCache.IsPaused);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }
}
