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

        var mgr = new LiveBranchProcessManager(bus, replayModule, new Fdp.Toolkit.Time.IntentTimeCommands(masterSyncBus));

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

        var mgr = new LiveBranchProcessManager(bus, replayModule, new Fdp.Toolkit.Time.IntentTimeCommands(masterSyncBus));

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
        masterSyncBus.SwapBuffers(); masterSync.Update();   // Q86 §4-C: the snap is a request; the clock applies it on its next Update
        Assert.Equal(42L, masterSync.GetCurrentState().TotalWallTicks);
    }

    /// <summary>
    /// ⭐⭐ CE-4065 (DAR §4) — a branch resumes on the SIM time of the frame on screen: the replay clock's playhead is WALL seconds after
    /// the zero (12 s here, after a 7 s pause), the cluster's map says that frame is sim 5 s, and the master's clock is snapped to 5.
    /// 🔴 Before: snapped to 12 (measured live: branched at sim 5 s the live run read 12.71 s) — every timer came due early.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4065_A_branch_resumes_on_the_frames_sim_time_not_the_wall_playhead()
    {
        var bus = new FdpEventBus();
        var (replayModule, _, _) = MakeTrackedModule();
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();
        long zero = TimeSpan.FromSeconds(100).Ticks, s = TimeSpan.TicksPerSecond;
        var map = new Fdp.Core.FlightRecorder.ReplayTimeMap(
            new[] { new Fdp.Core.FlightRecorder.ReplayTimeMap.Point(zero, 0), new(zero + 5 * s, 5), new(zero + 12 * s, 5), new(zero + 15 * s, 8) },
            new[] { new Fdp.Core.FlightRecorder.ReplayTimeMap.Stretch(zero + 5 * s, zero + 12 * s, 5, false) });
        var mgr = new LiveBranchProcessManager(bus, replayModule, new Fdp.Toolkit.Time.IntentTimeCommands(masterSyncBus),
                                               map: () => map, anchorWallTicks: () => zero);

        bus.PublishManaged(new ClusterStateTransitionedEvent { NewStateId = (FdpClusterState)(int)ClusterState.OperatingReplay, SubsystemName = "Cluster" });
        bus.SwapBuffers(); mgr.Tick(); bus.SwapBuffers();
        bus.PublishManaged(new TransitionStateIntent { TargetState = (FdpClusterState)(int)ClusterState.OperatingLive });
        bus.SwapBuffers(); mgr.Tick(); bus.SwapBuffers();   // the branch starts: the map is taken while the cluster replays

        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
            ResultPayload = new LiveBranchResult(new GlobalTime { TotalTime = 12.0, TotalWallTicks = zero + 12 * s }),
        });
        bus.SwapBuffers(); mgr.Tick();

        masterSyncBus.SwapBuffers(); masterSync.Update();
        Assert.Equal(5.0, masterSync.GetCurrentState().TotalTime, 6);
    }

    /// <summary>
    /// ⭐ CE-3299 B4 (D5) — a checkpoint restore's result snaps the master clock back to the checkpoint and leaves it PAUSED, and
    /// touches no replay freeze (there was none: the restore is from a live cluster).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3299_B4_ACheckpointRestore_SnapsTheClockToTheCheckpoint_WithoutAReplayFreeze()
    {
        var bus = new FdpEventBus();
        var (replayModule, freeze, restore) = MakeTrackedModule();
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();
        var mgr = new LiveBranchProcessManager(bus, replayModule, new Fdp.Toolkit.Time.IntentTimeCommands(masterSyncBus));

        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = Guid.NewGuid(),
            StatusCode    = OrchestrationStatusCode.Success,
            ResultPayload = new CheckpointRestoreResult(new Fdp.Core.GlobalTime { TotalWallTicks = 77L, TotalTime = 4.25, FrameNumber = 255 }, Guid.NewGuid()),
        });
        bus.SwapBuffers();
        mgr.Tick();

        Assert.Equal((0, 0), (freeze[0], restore[0]));
        masterSyncBus.SwapBuffers(); masterSync.Update();
        var now = masterSync.GetCurrentState();
        Assert.Equal((77L, 255L), (now.TotalWallTicks, now.FrameNumber));
        Assert.Equal(Fdp.ModuleHost.Time.TimeMode.Deterministic, masterSync.GetMode());   // paused
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

        var mgr = new LiveBranchProcessManager(bus, replayModule, new Fdp.Toolkit.Time.IntentTimeCommands(masterSyncBus));

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
