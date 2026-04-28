// LiveBranchProcessManagerTests.cs
// Tests for LiveBranchProcessManager (TASK-T001).

using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Controllers;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for LiveBranchProcessManager (CGF1-S0305 / TASK-T001).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class LiveBranchProcessManagerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (ReplayMasterModule module, int[] freeze, int[] restore) MakeTrackedModule()
    {
        float  scale   = 1.0f;
        var    freeze  = new int[1];
        var    restore = new int[1];
        var module = new ReplayMasterModule(
            s => { if (s == 0.0f) freeze[0]++; else restore[0]++; scale = s; },
            () => scale);
        return (module, freeze, restore);
    }

    private static MasterSyncController MakeMasterSync(FdpEventBus bus)
    {
        return new MasterSyncController(bus, new HashSet<int>(), tickSource: () => 0L);
    }

    // ── SC1: FreezeTime called on branch transition from OperatingReplay ───

    /// <summary>
    /// SC1: When a TransitionStateIntent targeting LoadingLive (or OperatingLive) is
    /// published while _lastKnownDsmState == OperatingReplay, FreezeTime must be called.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_OnTransitionFromReplay_ToLive_CallsFreezeTime()
    {
        var bus = new FdpEventBus();
        var (replayModule, freeze, _) = MakeTrackedModule();
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();

        var mgr = new LiveBranchProcessManager(bus, replayModule, masterSync);

        // Simulate state: cluster is in OperatingReplay.
        bus.PublishManaged(new ClusterStateTransitionedEvent
        {
            NewStateId    = (FdpClusterState)(int)ClusterState.OperatingReplay,
            SubsystemName = "Cluster",
        });
        bus.SwapBuffers();
        mgr.Tick(); // reads ClusterStateTransitionedEvent → _lastKnownDsmState = OperatingReplay
        bus.SwapBuffers();

        // Now publish a branch intent: OperatingReplay → LoadingLive.
        bus.PublishManaged(new TransitionStateIntent
        {
            TargetState = (FdpClusterState)(int)ClusterState.LoadingLive,
        });
        bus.SwapBuffers();
        mgr.Tick(); // detects branch condition, calls FreezeTime

        Assert.Equal(1, freeze[0]);
    }

    // ── SC2: RestoreTime + SnapAndPause after LiveBranchResult ACK ────────

    /// <summary>
    /// SC2: When ClusterOpCompletedEvent carries a LiveBranchResult with non-zero
    /// TotalWallTicks, RestoreTime and SnapAndPause are called.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_OnCompletedEventWithResult_RestoresAndSnaps()
    {
        var bus = new FdpEventBus();
        var (replayModule, _, restore) = MakeTrackedModule();
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();

        var mgr = new LiveBranchProcessManager(bus, replayModule, masterSync);

        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = Guid.NewGuid(),
            StatusCode    = OrchestrationStatusCode.Success,
            ResultPayload = new LiveBranchResult(new Fdp.Core.GlobalTime
            {
                TotalWallTicks = 42L,
                TotalTime      = 1.5,
            }),
        });
        bus.SwapBuffers();
        mgr.Tick();

        Assert.Equal(1, restore[0]);
        // SnapAndPause snaps the master clock to 42 ticks.
        Assert.Equal(42L, masterSync.GetCurrentState().TotalWallTicks);
    }

    // ── SC3: No FreezeTime for non-Replay branch ─────────────────────────

    /// <summary>
    /// SC3: When _lastKnownDsmState is OperatingLive (not OperatingReplay), a
    /// TransitionStateIntent targeting LoadingLive must NOT trigger FreezeTime.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_OnTransitionFromLive_DoesNotFreeze()
    {
        var bus = new FdpEventBus();
        var (replayModule, freeze, _) = MakeTrackedModule();
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();

        var mgr = new LiveBranchProcessManager(bus, replayModule, masterSync);

        // Simulate state: cluster is in OperatingLive (NOT OperatingReplay).
        bus.PublishManaged(new ClusterStateTransitionedEvent
        {
            NewStateId    = (FdpClusterState)(int)ClusterState.OperatingLive,
            SubsystemName = "Cluster",
        });
        bus.SwapBuffers();
        mgr.Tick(); // reads event → _lastKnownDsmState = OperatingLive
        bus.SwapBuffers();

        // Publish LoadingLive intent: NOT from replay, so FreezeTime must NOT be called.
        bus.PublishManaged(new TransitionStateIntent
        {
            TargetState = (FdpClusterState)(int)ClusterState.LoadingLive,
        });
        bus.SwapBuffers();
        mgr.Tick();

        Assert.Equal(0, freeze[0]);
    }

    // ── SC4: Build verification ───────────────────────────────────────────

    // SC4: Verified by successful build.
    // ClusterMaster must not contain _replayMasterModule, _pendingBranchTasks,
    // BranchTransitionTask, or SetReplayMasterModule after TASK-T001.
}
