using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Time.Messages;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.Orchestrator.Panels;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using FdpNodeOpType   = Fdp.Toolkit.Orchestration.NodeOpType;
using ClusterState    = Hrot.NED.Descriptors.Orchestration.ClusterState;

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

    // ── RT-016: ClusterUiCache default SourceDsmState/TargetDsmState ──────────

    /// <summary>
    /// T16a: When a NodeReplaySeek ExecuteNodeOpIntent arrives while the cluster is in
    /// OperatingReplay, the resulting DistributedTransaction must have both
    /// SourceDsmState and TargetDsmState set to OperatingReplay (not Idle).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ReplaySeekOp_HasSourceAndTargetEqualToCurrentState()
    {
        var bus     = new FdpEventBus();
        var uiCache = new ClusterUiCache(bus);

        // Set CurrentState to OperatingReplay via ClusterStateUpdateEvent.
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = FdpClusterState.OperatingReplay });
        bus.SwapBuffers();
        uiCache.Update();

        Assert.Equal(ClusterState.OperatingReplay, uiCache.CurrentState);

        // Publish a NodeReplaySeek ExecuteNodeOpIntent (no typed payload that overrides state).
        var txId = Guid.NewGuid();
        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = FdpNodeOpType.NodeReplaySeek,
            DomainPayload = new ReplaySeekPayload(12345L),
        });
        bus.SwapBuffers();
        uiCache.Update();

        Assert.Equal(1, uiCache.TxHistory.Count);
        var tx = uiCache.TxHistory[0];
        Assert.Equal(ClusterState.OperatingReplay, tx.SourceDsmState);
        Assert.Equal(ClusterState.OperatingReplay, tx.TargetDsmState);
        uiCache.Dispose();
    }
}
