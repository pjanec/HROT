using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
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
