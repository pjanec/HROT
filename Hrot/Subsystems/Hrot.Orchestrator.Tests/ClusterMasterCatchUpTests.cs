using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐⭐ <c>C3</c> + <c>C7</c> (Q91 §4-M) — the master's <c>InSync</c> set and a late joiner's catch-up: a node joining a cluster
/// at rest is in sync at once; a node joining a RUNNING cluster is caught up by node ops addressed to it alone — reset to
/// Idle, the universal load parts (<c>IsCatchUp</c>, no scenario), the operating state — and is in sync after its answers;
/// operator rounds target in-sync nodes only; one attempt per node per cluster commit (K7).
/// </summary>
public sealed class ClusterMasterCatchUpTests
{
    private static ClusterConfiguration Config() => new()
    {
        MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
        Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
    };

    private static void Heartbeat(FdpEventBus bus, ClusterMaster master, params int[] nodeIds)
    {
        foreach (var id in nodeIds)
            bus.PublishManaged(new NodeHeartbeatEvent
            {
                NodeId = id, SubsystemName = "TestNode",
                LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
                WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
            });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    /// <summary>A joiner whose capability advert (Brain + MuscleGround — the all-in-one case) lands in the same frame as its first heartbeat.</summary>
    private static void HeartbeatWithMuscle(FdpEventBus bus, ClusterMaster master, int nodeId)
    {
        bus.PublishManaged(new NodeCapabilitiesEvent { NodeId = nodeId, Capabilities = new[] { NodeRoleTokens.Brain, NodeRoleTokens.MuscleGround } });
        Heartbeat(bus, master, nodeId);
    }

    /// <summary>Drives the cluster to OperatingLive (no scenario ⇒ nothing parked), answering every prepare from every target.</summary>
    private static void GoLive(FdpEventBus bus, ClusterMaster master)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"OperatingLive\"}",
        });
        master.Tick();
        bus.SwapBuffers();
        AnswerAll(bus, master, bus.ReadManaged<ExecuteNodeOpIntent>().ToList());
        Assert.Equal(ClusterState.OperatingLive, master.CurrentClusterState);
    }

    private static void AnswerAll(FdpEventBus bus, ClusterMaster master, List<ExecuteNodeOpIntent> ops,
                                  OrchestrationStatusCode code = OrchestrationStatusCode.Success)
    {
        foreach (var op in ops.Where(o => o.Operation != FdpNodeOpType.CommitState))
            bus.PublishManaged(new NodeOpCompletedEvent
                { TransactionId = op.TransactionId, Operation = op.Operation, NodeId = op.TargetNodeId, StatusCode = code });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    [Fact(Timeout = 10_000)]
    public void A_node_joining_a_cluster_at_rest_is_in_sync_at_once()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1, 2);
        Assert.Equal(new[] { 1, 2 }, master.InSyncNodeIds.OrderBy(x => x));
        Assert.Empty(master.CatchingUpNodeIds);
    }

    /// <summary>
    /// ⭐ A node joining while a load is PARKED (waiting for its files) is admitted: the parked load has not chosen its targets
    /// yet. 📐 Measured live: it was left out and caught up afterwards — fatal had it been the scenario's Brain.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_node_joining_while_a_load_is_parked_is_in_sync_and_takes_part()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        HeartbeatWithMuscle(bus, master, 1);   // CE-4087 — a scenario load needs its roles present
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"OperatingLive\",\"ScenarioId\":\"s1\"}",
        });
        master.Tick();
        bus.SwapBuffers();
        Assert.NotNull(master.ParkedRequestId);

        Heartbeat(bus, master, 2);
        Assert.Contains(2, master.InSyncNodeIds);
    }

    /// <summary>
    /// ⭐⭐⭐ The whole catch-up: node 2 joins a RUNNING cluster ⇒ not in sync; it receives — alone — Idle, a load marked
    /// <c>IsCatchUp</c> naming no scenario, then the operating state; after its three answers it is in sync. ⛔ Node 1 (in sync)
    /// receives none of it.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_node_joining_a_running_cluster_is_caught_up_alone_then_is_in_sync()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1);
        GoLive(bus, master);

        Heartbeat(bus, master, 2);
        Assert.DoesNotContain(2, master.InSyncNodeIds);

        // the catch-up started in that same tick (no scenario known ⇒ no staging step)
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.Contains(2, master.CatchingUpNodeIds);
        Assert.All(ops, o => Assert.Equal(2, o.TargetNodeId));
        Assert.Equal(new[] { FdpNodeOpType.PrepareState, FdpNodeOpType.CommitState, FdpNodeOpType.PrepareLive,
                             FdpNodeOpType.CommitState, FdpNodeOpType.PrepareState, FdpNodeOpType.CommitState },
                     ops.Select(o => o.Operation).ToArray());
        var load = Assert.IsType<EditLoadHandlerPayload>(ops[2].DomainPayload);
        Assert.True(load.IsCatchUp);
        Assert.False(load.IsWorldBoundary);   // ⭐ measured live: a boundary clear here destroyed the current world the joiner already held
        Assert.Null(load.ScenarioId);   // ⭐ K5′ — a late joiner never reads the scenario

        AnswerAll(bus, master, ops);
        Assert.Contains(2, master.InSyncNodeIds);
        Assert.Empty(master.CatchingUpNodeIds);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4087</c> (R-4014 / Q91 §4-K K6 — "a node not in sync never participates", in the step barrier too) — a SimHost that
    /// registers while the cluster RUNS (or restarts mid-run) is not in the step set while it is still being caught up, and is in
    /// it — with the roster republished to the clock — the moment its catch-up completes. 📐 Before, the barrier admitted it on its
    /// advert alone, so a node still loading gated every step; and nothing pinned that it entered the set at all.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4087_A_node_joining_a_running_cluster_enters_the_step_set_when_it_is_in_sync_and_not_before()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        HeartbeatWithMuscle(bus, master, 1);                         // joins at rest ⇒ in sync at once
        GoLive(bus, master);
        Assert.Equal(new[] { 1 }, master.StepRoster().OrderBy(x => x));

        HeartbeatWithMuscle(bus, master, 2);                         // joins the RUNNING cluster: advert landed, catch-up in flight
        Assert.Contains(2, master.CatchingUpNodeIds);
        Assert.Equal(new[] { 1 }, master.StepRoster().OrderBy(x => x));   // ⭐ still being caught up ⇒ not in the barrier

        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().Where(o => o.TargetNodeId == 2).ToList();
        bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();        // drop what was published so far
        AnswerAll(bus, master, ops);                                 // the catch-up completes ⇒ in sync

        Assert.Contains(2, master.InSyncNodeIds);
        Assert.Equal(new[] { 1, 2 }, master.StepRoster().OrderBy(x => x));   // ⭐ in sync ⇒ in the barrier
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();         // and the clock is told (the roster is republished on change)
        Assert.Contains(bus.ReadManaged<SlaveNodeSetUpdatedEvent>(), e => e.SlaveNodeIds.SetEquals(new[] { 1, 2 }));
    }

    /// <summary>⭐ K7 — a failed catch-up is not retried within the same cluster commit; the node stays out of sync.</summary>
    [Fact(Timeout = 10_000)]
    public void A_failed_catch_up_is_not_retried_until_the_cluster_commits_again()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1);
        GoLive(bus, master);
        Heartbeat(bus, master, 2);
        AnswerAll(bus, master, bus.ReadManaged<ExecuteNodeOpIntent>().ToList(), OrchestrationStatusCode.Failure);

        for (int i = 0; i < 3; i++) { master.Tick(); bus.SwapBuffers(); }
        Assert.DoesNotContain(2, master.InSyncNodeIds);
        Assert.Empty(master.CatchingUpNodeIds);
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), o => o.TargetNodeId == 2);
    }

    /// <summary>⭐ J1 — a trajectory through Idle reaches the whole roster and leaves every node that answered it in sync.</summary>
    [Fact(Timeout = 10_000)]
    public void A_round_through_Idle_reaches_every_node_and_resyncs_them()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1);
        GoLive(bus, master);
        Heartbeat(bus, master, 2);
        AnswerAll(bus, master, bus.ReadManaged<ExecuteNodeOpIntent>().ToList(), OrchestrationStatusCode.Failure);   // stuck out of sync

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState, PayloadJson = "{\"TargetState\":\"Idle\"}",
        });
        master.Tick();
        bus.SwapBuffers();
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.Contains(ops, o => o.TargetNodeId == 2);          // ⭐ the out-of-sync node is reached too
        AnswerAll(bus, master, ops);
        Assert.Equal(new[] { 1, 2 }, master.InSyncNodeIds.OrderBy(x => x));
    }
}
