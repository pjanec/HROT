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

    /// <summary>
    /// ⭐ C9 P7 — entering a replay seeks every node to the replay's zero, so the playhead is seconds after it and the end IS
    /// the duration. 🔴 Before: "now + duration" with "now" the live clock's time at entry; and a seek back from the end
    /// never re-armed the pause.
    /// </summary>
    [Fact]
    public void C9_The_replay_end_is_the_duration_on_the_playhead_and_a_seek_rearms_it()
    {
        var bus  = new FdpEventBus();
        var time = new FixedTime { Now = 31.0 };   // the live clock's time when the replay is entered
        var pm   = new ReplayProcessManager(bus, time);

        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
            ResultPayload = new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 10f, 1, 2),
        });
        bus.SwapBuffers(); pm.Tick(); bus.SwapBuffers();
        Assert.Single(bus.ReadManaged<PauseTimeIntent>());   // past the end until the entry seek lands

        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
            ResultPayload = new ReplaySeekResult(new GlobalTime { TotalWallTicks = 1 }),
        });
        time.Now = 0.0;
        bus.SwapBuffers(); pm.Tick(); bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<PauseTimeIntent>());

        time.Now = 10.0;
        pm.Tick(); bus.SwapBuffers();
        Assert.Single(bus.ReadManaged<PauseTimeIntent>());
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
