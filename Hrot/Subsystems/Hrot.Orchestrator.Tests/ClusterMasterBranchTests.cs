using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for ClusterMaster 2PC handling of the Live-from-Replay branch transition
/// after TASK-T001. SnapAndPause / RestoreTime are now owned by
/// <see cref="LiveBranchProcessManager"/>; see LiveBranchProcessManagerTests (SC2).
/// This file verifies ClusterMaster correctly tracks ACKs for the standard PrepareLive
/// fan-out.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterBranchTests
{
    private static ClusterConfiguration MandatorySimHostConfig() => new ClusterConfiguration
    {
        Mandatory                  = new[] { "SimHost" },
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    private static readonly Guid FinishedExercise = Guid.NewGuid();

    private static int BootstrapToOperatingReplay(FdpEventBus bus, ClusterMaster master)
    {
        const int nodeId = 1;
        // ⭐ R-3055 — a replay replays an EXERCISE: the request below names none, so the master resolves it (else it is rejected)
        master.NewestExercise ??= () => FinishedExercise;

        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        return nodeId;
    }

    // ── T21: standard ACK tracking for PrepareLive after TASK-T001 ───────────

    /// <summary>
    /// T21 (revised): After TASK-T001, ClusterMaster tracks ACKs for PrepareLive via
    /// the standard _pendingBusTransitionAcks path. When all ACKs arrive,
    /// ClusterOpCompletedEvent is published. SnapAndPause is now handled by
    /// LiveBranchProcessManager (see LiveBranchProcessManagerTests.SC2).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_StandardAckTracking_PublishesClusterOpCompletedOnAllAcks()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());

        int nodeId = BootstrapToOperatingReplay(bus, master);

        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(intents.Any(i => i.Operation == FdpNodeOpType.PrepareLive),
            "ClusterMaster must fan out a PrepareLive NodeOpIntent.");
        var txId = intents.First(i => i.Operation == FdpNodeOpType.PrepareLive).TransactionId;

        // ACK all prepare ops (CommitState is handled synchronously and does not produce an ACK).
        foreach (var intent in intents.Where(i => i.Operation != FdpNodeOpType.CommitState))
        {
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = intent.TransactionId,
                Operation       = intent.Operation,
                NodeId          = nodeId,
                StatusCode      = OrchestrationStatusCode.Success,
                IsParticipating = true,
                ResultPayload   = intent.Operation == FdpNodeOpType.PrepareLive
                    ? (object?)new LiveBranchResult(new Fdp.Core.GlobalTime
                    {
                        TotalWallTicks = 7777L,
                        TotalTime      = 3.0,
                    })
                    : null,
            });
        }
        _ = txId; // captured for reference
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // Verify ClusterOpCompletedEvent is published after all ACKs.
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(completed.Any(e => !e.StatusCode.IsError()),
            "ClusterMaster must publish ClusterOpCompletedEvent(Success) after all ACKs arrive.");
    }

    /// <summary>
    /// ⭐⭐ D18 ① (R-3047, CE-3299 B4) — a branch from a replay records a NEW exercise whose id the MASTER mints: the request's id
    /// (here the REPLAYED exercise's, the case that truncated a recording) never reaches the nodes, and the cluster reports the new one.
    /// </summary>
    /// <summary>⭐ CE-4065 — <c>LoadingLive</c> asked for from a replay is REFUSED (it is the branch's passage, not a destination: it parked
    /// the cluster there with the clock frozen); the branch is asked for as <c>OperatingLive</c>.</summary>
    [Fact(Timeout = 10_000)]
    public void CE4065_LoadingLive_asked_for_from_a_replay_is_refused_and_fans_nothing_out()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());
        BootstrapToOperatingReplay(bus, master);

        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = requestId, OperationType = ClusterOpType.TransitionState,
            PayloadJson = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.PrepareLive);
        Assert.Equal(ClusterState.OperatingReplay, master.CurrentClusterState);
    }

    [Fact(Timeout = 10_000)]
    public void D18_ABranchFromAReplay_RecordsAnExerciseTheMasterMints_NeverTheRequestedOne()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());
        BootstrapToOperatingReplay(bus, master);

        var replayed = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"OperatingLive\",\"ExerciseId\":\"{replayed}\"}}",
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var prepare = bus.ReadManaged<ExecuteNodeOpIntent>().Single(i => i.Operation == FdpNodeOpType.PrepareLive);
        var minted  = Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload>(prepare.DomainPayload).ExerciseId;
        Assert.NotEqual(Guid.Empty, minted);
        Assert.NotEqual(replayed, minted);
        Assert.Equal(minted, master.ActiveExerciseId);
    }

    /// <summary>
    /// ⭐⭐ CE-4090 (AQ108 D14, §5a) — "the replay → live branch follows the setting like any live run": with <i>Record live runs</i> OFF the branch's
    /// PrepareLive tells every node not to record (the exercise id is still minted — only the recording is not), and the master announces the run as
    /// not recorded; with it ON (the control) the branch records exactly as before.
    /// </summary>
    [Theory(Timeout = 10_000)]
    [InlineData(true)]
    [InlineData(false)]
    public void CE4090_ABranchFromAReplay_FollowsTheRecordLiveRunsSetting(bool recordLiveRuns)
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());
        BootstrapToOperatingReplay(bus, master);
        master.RecordLiveRuns = recordLiveRuns;

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"OperatingLive\"}",
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        var prepare = bus.ReadManaged<ExecuteNodeOpIntent>().Single(i => i.Operation == FdpNodeOpType.PrepareLive);
        var payload = Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload>(prepare.DomainPayload);
        Assert.Equal(!recordLiveRuns, payload.RecordingOff);
        Assert.NotEqual(Guid.Empty, payload.ExerciseId);              // the branch's id is minted either way
        Assert.Equal(!recordLiveRuns, master.RunNotRecorded);
    }

    /// <summary>
    /// ⭐⭐ CE-3310 (R-3049) — a live LOAD whose request names no exercise gets one the master mints (a node records nothing without
    /// one); a request that names its exercise keeps it.
    /// </summary>
    [Theory(Timeout = 10_000)]
    [InlineData(false)]
    [InlineData(true)]
    public void CE3310_ALiveLoadWithoutAnExerciseId_GetsOneTheMasterMints_ANamedOneIsKept(bool named)
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = 1, SubsystemName = "SimHost", LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        var requested = named ? Guid.NewGuid() : Guid.Empty;
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = named
                ? $"{{\"TargetState\":\"OperatingLive\",\"ExerciseId\":\"{requested}\"}}"
                : "{\"TargetState\":\"OperatingLive\"}",
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        var prepare = bus.ReadManaged<ExecuteNodeOpIntent>().Single(i => i.Operation == FdpNodeOpType.PrepareLive);
        var sent    = Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload>(prepare.DomainPayload).ExerciseId;
        Assert.NotEqual(Guid.Empty, sent);
        if (named) Assert.Equal(requested, sent);
        Assert.Equal(sent, master.ActiveExerciseId);
    }

    /// <summary>
    /// ⭐ CE-3299 D17 (R-3045) — the live load's recording mode reaches every node on the load it starts: the request's
    /// <c>CheckpointingSeconds</c> rides the PrepareLive fan-out (0 / omitted = FULL).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void D17_TheLiveLoadsRecordingMode_RidesThePrepareLiveFanOut()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = 1, SubsystemName = "SimHost", LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"OperatingLive\",\"CheckpointingSeconds\":15}",
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        var prepare = bus.ReadManaged<ExecuteNodeOpIntent>().Single(i => i.Operation == FdpNodeOpType.PrepareLive);
        Assert.Equal(15.0, Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload>(prepare.DomainPayload).CheckpointingSeconds);
    }
}
