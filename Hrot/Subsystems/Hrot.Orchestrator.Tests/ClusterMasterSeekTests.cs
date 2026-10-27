using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for RT-008 (ACK-tracked seek fan-out) and RT-009 (server-side pause precondition).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterSeekTests
{
    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Registers a single node via heartbeat so it appears in the roster.
    /// </summary>
    private static void RegisterNode(
        FdpEventBus bus, ClusterMaster master,
        int nodeId = 1, string subsystem = "SimHost")
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = subsystem,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    /// <summary>
    /// ⭐⭐ CE-3329 F8 — a seek moves the clock, so it is admitted only while the cluster shows a recording; and an exit from a review with a
    /// seek still in flight WAITS for it (the seek's completion snaps the clock — after the exit it would snap live back into history),
    /// refusing new seeks meanwhile. 📄 DESIGN_Replay_Time_Axis_And_Review §11b F8.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3329_F8_Seeks_only_inside_a_recording_and_an_exit_waits_for_the_seeks_in_flight()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplaySeekAggregator());
        RegisterNode(bus, master);   // in sync: it joined an idle cluster
        MasterShows.State(master, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive);

        var stray = Guid.NewGuid();
        bus.PublishManaged(new SeekReplayIntent { RequestId = stray, TargetWallTicks = 1 });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == stray && e.StatusCode == OrchestrationStatusCode.Rejected);
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.NodeReplaySeek);

        MasterShows.State(master, Fdp.Toolkit.Orchestration.ClusterState.OperatingReview);
        bus.PublishManaged(new SeekReplayIntent { RequestId = Guid.NewGuid(), TargetWallTicks = 50_000 });
        Guid seekTx = GetSeekTxId(bus, master);

        var exit = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent { TransactionId = exit, TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<ExecuteNodeOpIntent>());   // the exit fans out nothing while the seek is in flight
        Assert.Equal(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingReview, master.CurrentClusterState);

        var late = Guid.NewGuid();
        bus.PublishManaged(new SeekReplayIntent { RequestId = late, TargetWallTicks = 1 });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == late && e.StatusCode == OrchestrationStatusCode.Rejected);

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId = seekTx, Operation = FdpNodeOpType.NodeReplaySeek, NodeId = 1, StatusCode = OrchestrationStatusCode.Success,
            IsParticipating = true, ResultPayload = new ReplaySeekResult(new GlobalTime { TotalWallTicks = 9999L, TotalTime = 5.0 }),
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();   // the seek lands; its snap reaches the clock a tick later
        Assert.Empty(bus.ReadManaged<ExecuteNodeOpIntent>());  // ⛔ not yet: the snap has not landed

        bool exitFannedOut = false;
        for (int i = 0; i < 4 && !exitFannedOut; i++)
        {
            bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
            exitFannedOut = bus.ReadManaged<ExecuteNodeOpIntent>().Any();
        }
        Assert.True(exitFannedOut, "the held exit never proceeded after the seek landed");
        Assert.NotEqual(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingReview, master.CurrentClusterState);
    }

    // ── RT-008: ACK-tracked seek fan-out ─────────────────────────────────────

    /// <summary>
    /// T8a: When the cluster has one active node, a ReplaySeek request must NOT publish
    /// an immediate ClusterOpCompletedEvent.  Success must only arrive after the node ACKs
    /// the NodeReplaySeek fan-out.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_WithActiveNodes_RegistersAckTracker_AndDoesNotPublishImmediateSuccess()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        MasterShows.Recording(master);   // ⭐ CE-3329 F8 — a seek is admitted only while the cluster shows a recording

        var seekRequestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = seekRequestId,
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":99000}",
        });
        bus.SwapBuffers();
        master.Tick();

        // T8a: No ClusterOpCompletedEvent yet.
        bus.SwapBuffers();
        var earlyCompleted = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.Empty(earlyCompleted);

        // T8b: A NodeReplaySeek fan-out intent must have been published.
        // Read the intents that were written during Tick() (they are in the already-swapped read buffer).
        // We need to look at what was published in the write buffer during Tick — drain remaining
        // from the current read buffer.
        // Re-run: during the Tick the master writes ExecuteNodeOpIntent to the write buffer.
        // After the first SwapBuffers+Tick we did: bus.SwapBuffers() to swap again.
        // Let's send another Tick to flush intent reading opportunity.
        // Actually: bus.SwapBuffers (before Tick) puts the seek request in read; Tick runs,
        // publishes ExecuteNodeOpIntent to WRITE; then bus.SwapBuffers (after) promotes it to READ.
        var fanOutIntents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            fanOutIntents.Any(i => i.Operation == FdpNodeOpType.NodeReplaySeek),
            "ClusterMaster must fan out NodeReplaySeek to active nodes.");

        // T8c: ACK the node op — success must arrive after the next Tick.
        var fanOutIntent = fanOutIntents.First(i => i.Operation == FdpNodeOpType.NodeReplaySeek);
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = fanOutIntent.TransactionId,
            Operation       = fanOutIntent.Operation,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == seekRequestId && !e.StatusCode.IsError()),
            $"Expected ClusterOpCompletedEvent(Success) for requestId={seekRequestId} after node ACK. " +
            $"Events: [{string.Join(", ", completed.Select(e => $"{e.RequestId}:{e.StatusCode}"))}]");
    }

    // ── RT-009: precondition events now owned by ReplaySeekProcessManager (T002) ─────────

    /// <summary>
    /// T9a: After T002 ClusterMaster must NOT publish SlaveNodeSetUpdatedEvent or
    /// PauseTimeIntent during seek fan-out -- that responsibility moved to
    /// ReplaySeekProcessManager.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_DoesNotPublish_SlaveNodeSetUpdatedEvent_Or_PauseTimeIntent()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":5000}",
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var slaveUpdates = bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();
        var pauses       = bus.ReadManaged<PauseTimeIntent>().ToList();

        Assert.Empty(slaveUpdates);
        Assert.Empty(pauses);
    }

    /// <summary>
    /// T9b: ClusterMaster must NOT publish SlaveNodeSetUpdatedEvent at all after T002
    /// (behavior moved to ReplaySeekProcessManager).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_ClusterMaster_DoesNotPublish_SlaveNodeSetUpdatedEvent()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        RegisterNode(bus, master, nodeId: 2, subsystem: "Editor");

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":1000}",
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var slaveUpdates = bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();
        Assert.Empty(slaveUpdates);
    }

    // ── RT-015: SnapAndPause wired into ConsumeNodeOpStatuses ────────────────

    private static MasterSyncController CreateMasterSync(FdpEventBus bus, long initialTicks = 0)
    {
        long ticks = initialTicks;
        return new MasterSyncController(bus, new HashSet<int>(), tickSource: () => ticks);
    }

    /// <summary>
    /// Gets the txId of the NodeReplaySeek fan-out intent published during a seek.
    /// After calling Tick, swaps bus buffers and reads the intent.
    /// </summary>
    private static Guid GetSeekTxId(FdpEventBus bus, ClusterMaster master)
    {
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        return intents.First(i => i.Operation == FdpNodeOpType.NodeReplaySeek).TransactionId;
    }

    /// <summary>
    /// T15a/T15b -- After all nodes ACK with a non-default ReplaySeekResult,
    /// ReplaySeekProcessManager calls SnapAndPause: master clock snaps and enters
    /// Deterministic mode.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_OnAllNodesAck_WithSeekResult_CallsSnapAndPause()
    {
        var masterBus  = new FdpEventBus();
        var masterSync = CreateMasterSync(masterBus);
        // Drain the initial baseline published by the constructor.
        masterBus.SwapBuffers();
        masterBus.Read<Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent>();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplaySeekAggregator());
        var seekManager  = new ReplaySeekProcessManager(bus, new Fdp.Toolkit.Time.IntentTimeCommands(masterBus), master.StepRoster);

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        MasterShows.Recording(master);   // ⭐ CE-3329 F8 — a seek is admitted only while the cluster shows a recording

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":50000}",
        });

        Guid txId = GetSeekTxId(bus, master);

        // ACK from node 1 with a real ReplaySeekResult (typed object -- aggregator serializes it).
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = FdpNodeOpType.NodeReplaySeek,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new ReplaySeekResult(new GlobalTime
            {
                TotalWallTicks = 9999L,
                TotalTime      = 5.0,
            }),
        });
        bus.SwapBuffers();
        master.Tick();   // processes ACK, aggregates via ReplaySeekAggregator, publishes ClusterOpCompletedEvent
        bus.SwapBuffers();
        seekManager.Tick(); // reads ClusterOpCompletedEvent, calls SnapAndPause

        // T15a: master clock was snapped to the seek result wall ticks.
        masterBus.SwapBuffers(); masterSync.Update();   // Q86 §4-C: the snap is a request; the clock applies it on its next Update
        Assert.Equal(9999L, masterSync.GetCurrentState().TotalWallTicks);
        // T15b: master clock is now in Deterministic mode.
        Assert.Equal(TimeMode.Deterministic, masterSync.GetMode());
    }

    /// <summary>
    /// T15d -- When ACK carries default(GlobalTime) (TotalWallTicks == 0), SnapAndPause
    /// must NOT be called; master clock must remain unchanged.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_OnAllNodesAck_WithDefaultResult_DoesNotCallSnapAndPause()
    {
        var masterBus  = new FdpEventBus();
        var masterSync = CreateMasterSync(masterBus);
        masterBus.SwapBuffers();
        masterBus.Read<Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent>();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        var seekManager  = new ReplaySeekProcessManager(bus, new Fdp.Toolkit.Time.IntentTimeCommands(masterBus), master.StepRoster);

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        MasterShows.Recording(master);   // ⭐ CE-3329 F8 — a seek is admitted only while the cluster shows a recording

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":50000}",
        });

        Guid txId = GetSeekTxId(bus, master);

        long wallTicksBefore = masterSync.GetCurrentState().TotalWallTicks;

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = FdpNodeOpType.NodeReplaySeek,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new ReplaySeekResult(default(GlobalTime)),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        seekManager.Tick();

        // Master wall ticks must be unchanged (SnapAndPause not called).
        masterBus.SwapBuffers(); masterSync.Update();   // Q86 §4-C: the snap is a request; the clock applies it on its next Update
        Assert.Equal(wallTicksBefore, masterSync.GetCurrentState().TotalWallTicks);
    }

    /// <summary>
    /// T15c -- A non-seek transition (e.g. TakeCheckpoint) must not call SnapAndPause.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_NonSeekTransition_DoesNotCallSnapAndPause()
    {
        var masterBus  = new FdpEventBus();
        var masterSync = CreateMasterSync(masterBus);
        masterBus.SwapBuffers();
        masterBus.Read<Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent>();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        // Issue a TakeCheckpoint (not a seek).
        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.TakeCheckpoint,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(intents.Count > 0, "Expected TakeSnapshot fan-out intent.");
        var txId = intents[0].TransactionId;

        long wallTicksBefore = masterSync.GetCurrentState().TotalWallTicks;

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = intents[0].Operation,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = null,
        });
        bus.SwapBuffers();
        master.Tick();

        // Master wall ticks must be unchanged (SnapAndPause not called).
        masterBus.SwapBuffers(); masterSync.Update();   // Q86 §4-C: the snap is a request; the clock applies it on its next Update
        Assert.Equal(wallTicksBefore, masterSync.GetCurrentState().TotalWallTicks);
    }

    // ══ P3 (Q91 §4-D D1, §4-D′) — the step roster is chosen by ROLE, never by subsystem name ════════════════════════

    private static ClusterMaster MasterWith(FdpEventBus bus, int localNodeId, params (int Id, string Name, string? Role)[] nodes)
    {
        var master = new ClusterMaster(bus, NoMandatoryConfig()) { LocalNodeId = localNodeId };
        foreach (var (id, name, role) in nodes)
        {
            bus.PublishManaged(new NodeHeartbeatEvent
            {
                NodeId = id, SubsystemName = name, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
                LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            });
            if (role != null) bus.PublishManaged(new NodeCapabilitiesEvent { NodeId = id, Capabilities = new[] { role } });
        }
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        return master;
    }

    [Fact]
    public void P3_The_step_roster_holds_simulation_roles_only_and_never_the_masters_own_node()
    {
        var bus = new FdpEventBus();
        using var master = MasterWith(bus, localNodeId: 0,
            (0,   "Editor",  Fdp.Core.NodeRoleTokens.Brain),          // the master's own node — runs the MASTER clock
            (1,   "SimHost", Fdp.Core.NodeRoleTokens.MuscleGround),
            (100, "IG",      Fdp.Core.NodeRoleTokens.Map2D),          // a Player — an observer never gates a step
            (400, "CGF",     null));                                  // advert not landed yet

        Assert.Equal(new HashSet<int> { 1 }, master.StepRoster().ToHashSet());
    }

    [Fact]
    public void P3_A_Player_does_not_gate_a_replay_seek()
    {
        // ⭐ The SECOND site (ReplaySeekProcessManager) — T9a/T9b assert the master does NOT publish on seek, so they
        //   would never catch this half.
        var bus = new FdpEventBus();
        using var master = MasterWith(bus, localNodeId: -1,
            (1,   "SimHost", Fdp.Core.NodeRoleTokens.MuscleGround),
            (100, "IG",      Fdp.Core.NodeRoleTokens.Map2D));
        var seek = new ReplaySeekProcessManager(bus, new Fdp.Toolkit.Time.IntentTimeCommands(new FdpEventBus()), master.StepRoster);

        bus.PublishManaged(new SeekReplayIntent { TargetWallTicks = 1 });
        bus.SwapBuffers(); seek.Tick(); bus.SwapBuffers();

        var set = Assert.Single(bus.ReadManaged<SlaveNodeSetUpdatedEvent>());
        Assert.Equal(new HashSet<int> { 1 }, set.SlaveNodeIds.ToHashSet());
    }

    [Fact]
    public void P3_An_ejection_republishes_the_step_roster()
    {
        var bus = new FdpEventBus();
        using var master = MasterWith(bus, localNodeId: -1,
            (1, "SimHost", Fdp.Core.NodeRoleTokens.MuscleGround),
            (2, "SimHost", Fdp.Core.NodeRoleTokens.MuscleGround));
        bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();   // the join's own publication

        master.EjectNode(2);
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        var set = Assert.Single(bus.ReadManaged<SlaveNodeSetUpdatedEvent>());
        Assert.Equal(new HashSet<int> { 1 }, set.SlaveNodeIds.ToHashSet());
    }

    // ── C9 (Q91 §4-P, R-4012) — one replay timeline ────────────────────────────

    [Fact]
    public void C9_The_prepare_aggregate_is_the_earliest_first_frame_and_the_latest_last_frame()
    {
        var agg = new ReplayConsensusAggregator();
        var responses = new Dictionary<int, Dictionary<FdpNodeOpType, string>>
        {
            [1] = new() { [FdpNodeOpType.PrepareReplay] = JsonSerializer.Serialize(new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(5, 27f, 100_000_000, 370_000_000)) },
            [2] = new() { [FdpNodeOpType.PrepareReplay] = JsonSerializer.Serialize(new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(7, 13f, 240_000_000, 372_000_000)) },
            [3] = new() { [FdpNodeOpType.PrepareReplay] = JsonSerializer.Serialize(new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 0f, 0, 0)) },   // no recording
        };

        var r = Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(agg.Aggregate(responses));
        Assert.Equal(100_000_000L, r.FirstFrameWallTicks);
        Assert.Equal(372_000_000L, r.LastFrameWallTicks);
        Assert.Equal(27.2f, r.DurationSeconds, 3);
        Assert.Equal(7L, r.MaxNetworkId);
    }

    /// <summary>
    /// 📐 Measured live before C9: the planner's entry seek was fanned out with the transition, under a throw-away
    /// transaction, before any node had answered the prepare round — it never ran as a round and no clock snapped. Now the
    /// transition carries no seek; when its round completes the master publishes ONE seek intent, and that seek carries the
    /// replay's zero learnt from the prepare round.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void C9_Entering_a_replay_runs_one_seek_round_after_the_transition_and_it_carries_the_replay_zero()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplayConsensusAggregator());
        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId   = Guid.NewGuid(),
            TargetState     = Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay,
            ExerciseId      = Guid.NewGuid(),
            TargetWallTicks = 30_000_000L,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var burst = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.DoesNotContain(burst, i => i.Operation == FdpNodeOpType.NodeReplaySeek);
        var prepares = burst.Where(i => i.Operation != FdpNodeOpType.CommitState).ToList();
        Assert.Contains(prepares, i => i.Operation == FdpNodeOpType.PrepareReplay);

        foreach (var p in prepares)
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = p.TransactionId,
                Operation       = p.Operation,
                NodeId          = 1,
                StatusCode      = OrchestrationStatusCode.Success,
                IsParticipating = true,
                ResultPayload   = p.Operation == FdpNodeOpType.PrepareReplay
                    ? new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 20f, 4_000_000L, 204_000_000L)
                    : null,
            });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var seek = Assert.Single(bus.ReadManaged<SeekReplayIntent>());
        Assert.Equal(30_000_000L, seek.TargetWallTicks);
        Assert.Equal(4_000_000L, master.ReplayAnchorWallTicks);

        master.Tick();
        bus.SwapBuffers();
        var fanOut = Assert.Single(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.NodeReplaySeek);
        var payload = Assert.IsType<ReplaySeekPayload>(fanOut.DomainPayload);
        Assert.Equal(30_000_000L, payload.TargetWallTicks);
        Assert.Equal(4_000_000L, payload.AnchorWallTicks);
    }

    // ── CE-3329 P1 (R3, R11) — the cluster's sim axis: the nodes' maps intersected, sim targets resolved by the master ──

    private const long Tk = TimeSpan.TicksPerSecond / 10;   // one frame = 0.1 s of wall

    /// <summary>A node's map: <paramref name="runBefore"/> frames advancing 0.1 s of sim each, <paramref name="pause"/> paused, 10 more.</summary>
    private static Fdp.Core.FlightRecorder.ReplayTimeMap NodeMap(long firstWall, int runBefore, int pause)
    {
        int n = runBefore + pause + 10;
        var wall = new long[n]; var sim = new double[n]; var flags = new Fdp.Core.FlightRecorder.FrameFlags[n];
        double s = 0;
        for (int i = 0; i < n; i++)
        {
            bool halted = i >= runBefore && i < runBefore + pause;
            if (!halted) s += 0.1;
            wall[i] = firstWall + i * Tk; sim[i] = s;
            flags[i] = halted ? Fdp.Core.FlightRecorder.FrameFlags.Halted : Fdp.Core.FlightRecorder.FrameFlags.None;
        }
        return Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, sim, flags);
    }

    /// <summary>⭐ R3 — the prepare aggregate carries the CLUSTER's map: the maps cross the wire as JSON (as a node's result does) and
    /// come back intersected — a stretch only where every node paused, the earliest-starting node's sim times.</summary>
    [Fact]
    public void CE3329_The_prepare_aggregate_carries_the_clusters_time_map()
    {
        var early = NodeMap(firstWall: 100_000_000, runBefore: 10, pause: 20);   // paused frames 10–29
        var late  = NodeMap(firstWall: 100_000_000 + 5 * Tk, runBefore: 10, pause: 10);   // paused 15–24 on the early node's frames
        static string Wire(Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult r)
            => JsonSerializer.Serialize(r, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        // ⚠ node 2 is listed first on purpose: the anchor is the EARLIEST first frame, not the lowest node id
        var responses = new Dictionary<int, Dictionary<FdpNodeOpType, string>>
        {
            [2] = new() { [FdpNodeOpType.PrepareReplay] = Wire(new(7, 4f, 100_000_000 + 5 * Tk, 100_000_000 + 34 * Tk, late)) },
            [1] = new() { [FdpNodeOpType.PrepareReplay] = Wire(new(5, 4f, 100_000_000, 100_000_000 + 39 * Tk, early)) },
        };

        var r = Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(new ReplayConsensusAggregator().Aggregate(responses));
        var map = Assert.IsType<Fdp.Core.FlightRecorder.ReplayTimeMap>(r.TimeMap);
        Assert.True(map.HasSimTime);
        Assert.Equal(early.Points, map.Points);                                 // the anchor (earliest) node's sim times
        var s = Assert.Single(map.Stretches);
        Assert.Equal(100_000_000 + 14 * Tk, s.StartWallTicks);                  // where BOTH paused
        Assert.Equal(100_000_000 + 25 * Tk, s.EndWallTicks);
    }

    /// <summary>⭐ R11 — a seek in RECORDED sim time is turned into wall ticks by the MASTER (nodes unchanged): a paused instant lands at
    /// the end of the pause; a replay with no sim axis (v6) refuses it.</summary>
    [Fact(Timeout = 10_000)]
    public void CE3329_A_sim_seek_is_resolved_to_wall_ticks_by_the_master_and_refused_without_a_sim_axis()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplayConsensusAggregator());
        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        var map = NodeMap(firstWall: 4_000_000L, runBefore: 10, pause: 20);   // sim 1.0 paused from frame 9 to 30
        EnterReplay(bus, master, new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 4f, 4_000_000L, 4_000_000L + 39 * Tk, map),
                    targetSimSeconds: 1.5);
        var entry = Assert.Single(bus.ReadManaged<SeekReplayIntent>());
        Assert.Equal(map.SeekWallAt(1.5) - 4_000_000L, entry.TargetWallTicks);   // the entry, named in sim time (CE-4066: lands ON its frame)

        bus.PublishManaged(new SeekReplayIntent { RequestId = Guid.NewGuid(), TargetSimSeconds = 1.0 });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        var fanOut = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation == FdpNodeOpType.NodeReplaySeek).ToList();
        var payload = Assert.IsType<ReplaySeekPayload>(fanOut.Last().DomainPayload);
        Assert.Equal(29 * Tk, payload.TargetWallTicks);                          // the pause's END: its changes shown

        // a v6 replay: no map ⇒ a sim seek is refused, never sent as "0 ticks"
        var bus2 = new FdpEventBus();
        using var master2 = new ClusterMaster(bus2, NoMandatoryConfig());
        master2.RegisterAggregator(new ReplayConsensusAggregator());
        RegisterNode(bus2, master2, nodeId: 1, subsystem: "SimHost");
        EnterReplay(bus2, master2, new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 4f, 4_000_000L, 44_000_000L), targetSimSeconds: null);
        bus2.ReadManaged<SeekReplayIntent>().ToList();
        var refused = Guid.NewGuid();
        bus2.PublishManaged(new SeekReplayIntent { RequestId = refused, TargetSimSeconds = 1.0 });
        bus2.SwapBuffers(); master2.Tick(); bus2.SwapBuffers();
        Assert.Contains(bus2.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == refused && e.StatusCode == OrchestrationStatusCode.Rejected);
        Assert.DoesNotContain(bus2.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.NodeReplaySeek);
    }

    /// <summary>⭐ CE-3329 R-3058 — the axis is the SESSION's: any host's request switches it at the master, and the master's cluster-state
    /// broadcast carries it to every node.</summary>
    [Fact(Timeout = 10_000)]
    public void CE3329_The_axis_switch_is_broadcast_with_the_cluster_state()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        Assert.Equal(Fdp.Toolkit.Orchestration.ReplayAxis.Sim, master.ReplayAxis);   // R2: Sim by default

        var req = Guid.NewGuid();
        bus.PublishManaged(new SetReplayAxisIntent { RequestId = req, Axis = Fdp.Toolkit.Orchestration.ReplayAxis.Wall });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        Assert.Equal(Fdp.Toolkit.Orchestration.ReplayAxis.Wall, master.ReplayAxis);
        Assert.Contains(bus.ReadManaged<ClusterStateUpdateEvent>(), e => e.ReplayAxis == Fdp.Toolkit.Orchestration.ReplayAxis.Wall);
        Assert.Contains(bus.ReadManaged<ClusterStateTransitionedEvent>(), e => e.ReplayAxis == Fdp.Toolkit.Orchestration.ReplayAxis.Wall);
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == req && !e.StatusCode.IsError());
    }

    /// <summary>
    /// ⭐⭐ CE-3329 R4 — on the Sim axis a RUNNING replay clock jumps a paused stretch (non-pausing: <c>JumpTo</c>, never a snap), once
    /// per stretch; on the Wall axis, paused, or outside a replay, it never jumps.
    /// </summary>
    [Fact]
    public void CE3329_On_the_sim_axis_a_running_replay_jumps_each_paused_stretch_once()
    {
        var map   = NodeMap(firstWall: 4_000_000L, runBefore: 10, pause: 20);   // flat from 9T to 29T after the zero
        var clock = new FakeReplayClock { Mode = TimeMode.Continuous };
        var cmds  = new RecordingTimeCommands();
        var axis  = Fdp.Toolkit.Orchestration.ReplayAxis.Sim;
        bool replaying = true;
        var pm = new ReplayClockProcessManager(clock, cmds, () => replaying, () => axis, () => map, () => 4_000_000L, () => null);

        clock.TotalTime = 0.5;   pm.Tick();
        Assert.Empty(cmds.Jumps);                                           // before the pause: plays on
        clock.TotalTime = 1.0;   pm.Tick();
        Assert.Equal(new[] { 2.9 }, cmds.Jumps.Select(j => Math.Round(j, 6)));   // inside it: jumped to its end
        clock.TotalTime = 1.2;   pm.Tick();
        Assert.Single(cmds.Jumps);                                          // the jump in flight: not repeated
        clock.TotalTime = 3.2;   pm.Tick();
        Assert.Single(cmds.Jumps);                                          // after it: plays on

        clock.TotalTime = 1.5; axis = Fdp.Toolkit.Orchestration.ReplayAxis.Wall; pm.Tick();
        Assert.Single(cmds.Jumps);                                          // Wall: the pause plays out
        axis = Fdp.Toolkit.Orchestration.ReplayAxis.Sim; clock.Mode = TimeMode.Deterministic; pm.Tick();
        Assert.Single(cmds.Jumps);                                          // paused: never
        clock.Mode = TimeMode.Continuous; replaying = false; pm.Tick();
        Assert.Single(cmds.Jumps);                                          // live: never
    }

    private sealed class FakeReplayClock : ITimeController
    {
        public double TotalTime;
        public TimeMode Mode;
        public float Scale = 1f;   // ⭐ row 9 — the rate the clock runs at (a test applies what the manager asked for)
        public GlobalTime Update() => GetCurrentState();
        public void SetTimeScale(float scale) => Scale = scale;
        public float GetTimeScale() => Scale;
        public TimeMode GetMode() => Mode;
        public GlobalTime GetCurrentState() => new() { TotalTime = TotalTime };
        public void SeedState(GlobalTime state) { }
        public void Dispose() { }
    }

    private sealed class RecordingTimeCommands : Fdp.Toolkit.Time.ITimeCommands
    {
        public List<double> Jumps { get; } = new();
        public List<float> Rates { get; } = new();   // ⭐ row 9
        public int Pauses;
        public void Pause() => Pauses++;
        public void Resume() { }
        public void StepOneTick() { }
        public void StepBy(float seconds) { }
        public void SetTimeScale(float scale) => Rates.Add(scale);
        public void SnapTo(GlobalTime position) => throw new InvalidOperationException("the sim axis must never snap (F14)");
        public void JumpTo(double totalTime) => Jumps.Add(totalTime);
    }

    // ── CE-3329 §12n — the LOOP range: a session setting of the master, enforced by its replay clock ──────────────────────────

    /// <summary>A master in a replay with a sim axis: zero at 4_000_000, 3.9 s long, a pause from 0.9 s to 2.9 s (NodeMap).</summary>
    private static (FdpEventBus Bus, ClusterMaster Master, Fdp.Core.FlightRecorder.ReplayTimeMap Map) ReplayingMaster()
    {
        var bus = new FdpEventBus();
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplayConsensusAggregator());
        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        var map = NodeMap(firstWall: 4_000_000L, runBefore: 10, pause: 20);
        EnterReplay(bus, master, new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 4f, 4_000_000L, 4_000_000L + 39 * Tk, map), targetSimSeconds: null);
        bus.ReadManaged<SeekReplayIntent>().ToList();
        return (bus, master, map);
    }

    /// <summary>Sends the intent to the master and returns the status it answered with.</summary>
    private static OrchestrationStatusCode SetLoop(FdpEventBus bus, ClusterMaster master, SetReplayLoopIntent intent)
    {
        intent.RequestId = Guid.NewGuid();
        bus.PublishManaged(intent);
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        return Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == intent.RequestId).StatusCode;
    }

    /// <summary>
    /// ⭐⭐ CE-3329 §12n — a loop is the MASTER's: wall bounds are held; sim bounds are resolved through the cluster's time map to wall
    /// seconds after the zero; an inverted (or out-of-length) loop is refused and the previous one stays; clearing and leaving the
    /// replay both drop it.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3329_The_master_holds_resolves_validates_and_clears_the_loop()
    {
        var (bus, master, map) = ReplayingMaster();
        using var _ = master;
        Assert.Null(master.ReplayLoop);

        Assert.False(SetLoop(bus, master, new() { FromSeconds = 1.0, ToSeconds = 2.0 }).IsError());
        Assert.Equal((1.0, 2.0), master.ReplayLoop);                                  // wall bounds, held as given

        Assert.False(SetLoop(bus, master, new() { FromSimSeconds = 0.5, ToSimSeconds = 1.0 }).IsError());
        var (f, t) = master.ReplayLoop!.Value;                                        // sim bounds, through the map
        Assert.Equal((map.WallAt(0.5) - 4_000_000L) / (double)TimeSpan.TicksPerSecond, f, 6);
        Assert.Equal((map.WallAt(1.0) - 4_000_000L) / (double)TimeSpan.TicksPerSecond, t, 6);
        Assert.NotEqual(0.5, f);                                                      // (a wall value would have been 0.5 / 1.0)
        var held = master.ReplayLoop;

        Assert.Equal(OrchestrationStatusCode.Rejected, SetLoop(bus, master, new() { FromSeconds = 2.0, ToSeconds = 1.0 }));   // inverted
        Assert.Equal(OrchestrationStatusCode.Rejected, SetLoop(bus, master, new() { FromSeconds = 1.0, ToSeconds = 1.0 }));   // empty
        Assert.Equal(OrchestrationStatusCode.Rejected, SetLoop(bus, master, new() { FromSeconds = 1.0, ToSeconds = 99.0 }));  // past the length
        Assert.Equal(held, master.ReplayLoop);                                        // the previous loop stays

        Assert.False(SetLoop(bus, master, new()).IsError());                          // all null = clear
        Assert.Null(master.ReplayLoop);

        Assert.False(SetLoop(bus, master, new() { FromSeconds = 1.0, ToSeconds = 2.0 }).IsError());
        bus.PublishManaged(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = Fdp.Toolkit.Orchestration.ClusterState.Idle });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Equal(Hrot.NED.Descriptors.Orchestration.ClusterState.Idle, master.CurrentClusterState);
        Assert.Null(master.ReplayLoop);                                               // leaving the replay drops the bracket
    }

    /// <summary>
    /// ⭐⭐ CE-3329 §12n — the master's replay clock wraps the playhead: ONE <c>JumpTo(From)</c> when it reaches <c>To</c> while running,
    /// none while that jump has not landed (it goes BACKWARDS, so the Sim axis's "wait until now ≥ target" guard cannot hold it), a second
    /// only after the clock has read below <c>To</c> and reached it again; on either axis; never while paused; never a snap.
    /// </summary>
    [Theory]
    [InlineData(Fdp.Toolkit.Orchestration.ReplayAxis.Wall)]
    [InlineData(Fdp.Toolkit.Orchestration.ReplayAxis.Sim)]
    public void CE3329_The_replay_clock_wraps_the_playhead_once_per_crossing(Fdp.Toolkit.Orchestration.ReplayAxis axis)
    {
        var map   = NodeMap(firstWall: 4_000_000L, runBefore: 10, pause: 20);   // its paused stretch (0.9–2.9 s) lies BEFORE the loop
        var clock = new FakeReplayClock { Mode = TimeMode.Continuous };
        var cmds  = new RecordingTimeCommands();
        (double, double)? loop = (3.0, 3.8);
        bool replaying = true;
        var pm = new ReplayClockProcessManager(clock, cmds, () => replaying, () => axis, () => map, () => 4_000_000L, () => loop);

        clock.TotalTime = 3.7; pm.Tick();
        Assert.Empty(cmds.Jumps);                                    // inside the loop: plays on
        clock.TotalTime = 3.8; pm.Tick();
        Assert.Equal(new[] { 3.0 }, cmds.Jumps);                     // at To: back to From
        Assert.Equal(1, pm.LoopJumps);
        clock.TotalTime = 3.85; pm.Tick();
        clock.TotalTime = 3.9; pm.Tick();
        Assert.Single(cmds.Jumps);                                   // the jump has not landed: no second one
        clock.TotalTime = 3.1; pm.Tick();
        Assert.Single(cmds.Jumps);                                   // landed
        clock.TotalTime = 3.8; pm.Tick();
        Assert.Equal(new[] { 3.0, 3.0 }, cmds.Jumps);                // the next crossing wraps again
        Assert.Equal(2, pm.LoopJumps);

        clock.TotalTime = 3.2; pm.Tick();                            // landed again
        clock.Mode = TimeMode.Deterministic; clock.TotalTime = 3.9; pm.Tick();
        Assert.Equal(2, cmds.Jumps.Count);                           // paused: never
        clock.Mode = TimeMode.Continuous; loop = null; pm.Tick();
        Assert.Equal(2, cmds.Jumps.Count);                           // no loop: never
        loop = (3.0, 3.8); replaying = false; pm.Tick();
        Assert.Equal(2, cmds.Jumps.Count);                           // not replaying: never
    }

    /// <summary>⭐ CE-3329 §12n — the loop rides the session layer: a sample with the bounds after it is set, a NEW sample after it is cleared.</summary>
    [Fact(Timeout = 10_000)]
    public void CE3329_The_session_layer_carries_the_loop()
    {
        var (bus, master, _) = ReplayingMaster();
        using var _m = master;
        var review = new ReviewProcessManager(bus, new FakeReplayClock { Mode = TimeMode.Continuous }, new RecordingTimeCommands(),
                                              () => Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay);
        var session = new ReplaySessionProcessManager(bus, master, review);
        session.Tick();
        Assert.True(session.Current!.Active);
        Assert.Null(session.Current.LoopFromSeconds);

        SetLoop(bus, master, new() { FromSeconds = 1.0, ToSeconds = 2.0 });
        var before = session.Current;
        session.Tick();
        Assert.NotSame(before, session.Current);                      // a loop change is a new sample
        Assert.Equal(1.0, session.Current!.LoopFromSeconds);
        Assert.Equal(2.0, session.Current.LoopToSeconds);
        Assert.False(session.Current.SameAs(before));

        var withLoop = session.Current;
        session.Tick();
        Assert.Same(withLoop, session.Current);                       // unchanged: nothing republished

        SetLoop(bus, master, new());
        session.Tick();
        Assert.NotSame(withLoop, session.Current);                    // cleared: another new sample
        Assert.Null(session.Current!.LoopFromSeconds);
        Assert.Null(session.Current.LoopToSeconds);
    }

    /// <summary>Enters a replay on one registered node, answering its prepare round with <paramref name="prepared"/>.</summary>
    /// <summary>⭐ CE-3333 — "the end" (long.MaxValue, what scripts send) and anything past it reach the nodes as the replay's LAST moment
    /// (the cluster's span: 4 s here). 🔴 Before: sent as is, the node found it past its recording, listened and emptied its world.</summary>
    [Fact(Timeout = 10_000)]
    public void CE3333_A_seek_to_the_end_or_past_it_is_sent_as_the_replays_last_moment()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplayConsensusAggregator());
        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        EnterReplay(bus, master, new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 4f, 4_000_000L, 44_000_000L), targetSimSeconds: null);

        foreach (long target in new[] { long.MaxValue, 50_000_000L, 20_000_000L })
        {
            bus.PublishManaged(new SeekReplayIntent { RequestId = Guid.NewGuid(), TargetWallTicks = target });
            bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
            var payload = Assert.IsType<ReplaySeekPayload>(
                bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation == FdpNodeOpType.NodeReplaySeek).Last().DomainPayload);
            Assert.Equal(Math.Min(target, 40_000_000L), payload.TargetWallTicks);   // 4 s after the zero, or the target inside it
        }
    }

    private static void EnterReplay(FdpEventBus bus, ClusterMaster master, Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult prepared,
                                    double? targetSimSeconds)
    {
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId    = Guid.NewGuid(),
            TargetState      = Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay,
            ExerciseId       = Guid.NewGuid(),
            TargetSimSeconds = targetSimSeconds,
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        foreach (var p in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList())
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = p.TransactionId,
                Operation       = p.Operation,
                NodeId          = 1,
                StatusCode      = OrchestrationStatusCode.Success,
                IsParticipating = true,
                ResultPayload   = p.Operation == FdpNodeOpType.PrepareReplay ? prepared : null,
            });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    /// <summary>
    /// ⭐ C9 P7 (moved to the replay clock, CE-3329 row 9) — the playhead is seconds after the replay's zero, so the end IS the duration:
    /// a running clock at or past it pauses ONCE; a seek back re-arms it. 🔴 Before C9: "now + duration"; and a seek back never re-armed.
    /// </summary>
    [Fact]
    public void C9_The_replay_end_is_the_duration_on_the_playhead_and_a_seek_rearms_it()
    {
        var clock = new FakeReplayClock { Mode = TimeMode.Continuous, TotalTime = 31.0 };   // the live clock's time when the replay is entered
        var cmds  = new RecordingTimeCommands();
        var pm = new ReplayClockProcessManager(clock, cmds, () => true, () => Fdp.Toolkit.Orchestration.ReplayAxis.Wall,
                                               () => Fdp.Core.FlightRecorder.ReplayTimeMap.Empty, () => 0L, () => null, durationSeconds: () => 10.0);
        pm.Tick();
        Assert.Equal(1, cmds.Pauses);   // past the end until the entry seek lands
        pm.Tick();
        Assert.Equal(1, cmds.Pauses);   // once per arrival
        clock.TotalTime = 0.0; pm.Tick();
        Assert.Equal(1, cmds.Pauses);   // the seek landed at the zero: re-armed
        clock.TotalTime = 10.0; pm.Tick();
        Assert.Equal(2, cmds.Pauses);
        Assert.Equal(2, pm.EndPauses);
    }

    /// <summary>
    /// ⭐⭐ CE-3329 row 9 — the per-segment RATE: on the Sim axis the clock runs at the OPERATOR's rate divided by the recorded rate of the
    /// segment under the playhead (a run recorded at 2× plays its sim at the operator's speed); the operator's rate is kept apart (its own
    /// requests change it, the manager's echoes do not) and given back when the replay ends — ⛔ not when a review ends (F19).
    /// </summary>
    [Fact]
    public void CE3329_The_sim_axis_plays_each_segment_at_the_operators_rate()
    {
        // wall 0–2 s recorded at 1×, 2–4 s at 2× (sim 2 → 6)
        var wall = new long[41]; var sim = new double[41]; var flags = new Fdp.Core.FlightRecorder.FrameFlags[41];
        for (int i = 0; i <= 40; i++) { double w = i * 0.1; wall[i] = 1_000_000L + (long)Math.Round(w * TimeSpan.TicksPerSecond); sim[i] = w <= 2 ? w : 2 + 2 * (w - 2); }
        var map = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, sim, flags);
        var bus = new FdpEventBus();
        var clock = new FakeReplayClock { Mode = TimeMode.Continuous, TotalTime = 1.0 };
        var cmds = new RecordingTimeCommands();
        var axis = Fdp.Toolkit.Orchestration.ReplayAxis.Sim;
        bool replaying = true, reviewing = false;
        var pm = new ReplayClockProcessManager(clock, cmds, () => replaying, () => axis, () => map, () => 1_000_000L, () => null,
                                               reviewing: () => reviewing, bus: bus);
        void Tick() { bus.SwapBuffers(); pm.Tick(); if (cmds.Rates.Count > 0) clock.Scale = cmds.Rates[^1]; }

        Tick();
        Assert.Empty(cmds.Rates);                                       // 1× segment, operator 1×: nothing to change
        clock.TotalTime = 3.0; Tick();
        Assert.Equal(0.5f, cmds.Rates[^1], 3);                          // a 2× segment: the clock at ½ so the sim plays at 1×
        bus.PublishManaged(new SetTimeScaleIntent { TimeScale = 0.5f }); Tick();   // the manager's own request coming back
        Assert.Equal(1f, pm.OperatorRate);                              //   is not the operator's
        bus.PublishManaged(new SetTimeScaleIntent { TimeScale = 2f }); Tick();     // the operator asks for 2×
        Assert.Equal(2f, pm.OperatorRate);
        Tick();
        Assert.Equal(1f, cmds.Rates[^1], 3);                            // 2× of a 2× segment's sim: the clock at 1
        axis = Fdp.Toolkit.Orchestration.ReplayAxis.Wall; Tick();
        Assert.Equal(2f, cmds.Rates[^1], 3);                            // Wall: the operator's rate itself
        axis = Fdp.Toolkit.Orchestration.ReplayAxis.Sim; Tick();
        Assert.Equal(1f, cmds.Rates[^1], 3);
        replaying = false; Tick();
        Assert.Equal(2f, cmds.Rates[^1], 3);                            // the replay ended: the operator's rate given back

        replaying = true; reviewing = true; clock.Scale = 1f; Tick();  // a REVIEW at a 2× segment
        Assert.Equal(1f / 1f / 2f, cmds.Rates[^1], 3);
        int before = cmds.Rates.Count;
        replaying = false; Tick();
        Assert.Equal(before, cmds.Rates.Count);                         // ⛔ its end gives nothing back: the review's return restores the live rate
    }

    private sealed class FixedTime : ITimeController
    {
        public double Now;
        public GlobalTime Update() => GetCurrentState();
        public GlobalTime GetCurrentState() => new() { TotalTime = Now };
        public void SetTimeScale(float scale) { }
        public float GetTimeScale() => 1f;
        public TimeMode GetMode() => TimeMode.Continuous;
        public void SeedState(GlobalTime state) => Now = state.TotalTime;
        public void Dispose() { }
    }
}

/// <summary>⭐ CE-3329 F8 — a seek is admitted only while the cluster shows a recording: puts a test's master in a state (the node rounds
/// still go to the nodes that joined while it was idle).</summary>
internal static class MasterShows
{
    public static void State(ClusterMaster master, Fdp.Toolkit.Orchestration.ClusterState state)
    {
        var field = typeof(ClusterMaster).GetField("_currentDsmState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(master, Enum.ToObject(field.FieldType, (int)state));
    }

    public static void Recording(ClusterMaster master) => State(master, Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay);
}

