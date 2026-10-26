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
        Assert.Equal(map.WallAt(1.5) - 4_000_000L, entry.TargetWallTicks);       // the entry, named in sim time

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
        var pm = new ReplayClockProcessManager(clock, cmds, () => replaying, () => axis, () => map, () => 4_000_000L);

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
        public GlobalTime Update() => GetCurrentState();
        public void SetTimeScale(float scale) { }
        public float GetTimeScale() => 1f;
        public TimeMode GetMode() => Mode;
        public GlobalTime GetCurrentState() => new() { TotalTime = TotalTime };
        public void SeedState(GlobalTime state) { }
        public void Dispose() { }
    }

    private sealed class RecordingTimeCommands : Fdp.Toolkit.Time.ITimeCommands
    {
        public List<double> Jumps { get; } = new();
        public void Pause() { }
        public void Resume() { }
        public void StepOneTick() { }
        public void StepBy(float seconds) { }
        public void SetTimeScale(float scale) { }
        public void SnapTo(GlobalTime position) => throw new InvalidOperationException("the sim axis must never snap (F14)");
        public void JumpTo(double totalTime) => Jumps.Add(totalTime);
    }

    /// <summary>Enters a replay on one registered node, answering its prepare round with <paramref name="prepared"/>.</summary>
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
