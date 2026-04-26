using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

// Domain 15 is reserved for orchestrator unit tests to avoid interfering with
// domain-0 tests in Hrot.SimHost.Integration.Tests and other assemblies.
[CollectionDefinition("OrchestratorTests", DisableParallelization = true)]
public class OrchestratorTestCollection { }

[Collection("OrchestratorTests")]
public sealed class ClusterMasterBootstrapTests
{

    // ── CGF1-S0102 (BATCH-02) ─────────────────────────────────────────────────

    [Fact]
    public void OrchestratorPublishesIdleOnStartup()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus);

        // Constructor calls PublishStandby() (empty mandatory) → ClusterStateUpdateEvent in write buffer.
        bus.SwapBuffers();
        var received = bus.ReadManaged<ClusterStateUpdateEvent>().ToList();

        Assert.True(received.Count > 0, "No ClusterStateUpdateEvent published at startup.");
        Assert.Single(received);
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Idle, received[0].CurrentState);
    }

    // ── CGF1-S0105 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that ClusterMaster rejects ClusterOpRequests while mandatory nodes are not yet
    /// in Standby, then accepts them once the mandatory node publishes a Standby heartbeat.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void RejectsCommands_UntilMandatoryNodesReady()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };

        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, config);

        // ── Phase 1: Send request before SimHost heartbeat — expect Rejected ──
        var reqId1 = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId1,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var phase1Events = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(phase1Events.Any(e => e.RequestId == reqId1),
            "ClusterMaster did not respond to ClusterOpRequest before bootstrap.");
        Assert.True(phase1Events.Any(e => e.RequestId == reqId1 && e.StatusCode == OrchestrationStatusCode.Rejected),
            $"Expected Rejected, got: {string.Join(", ", phase1Events.Select(e => e.StatusCode))}");
        Assert.False(exercise.BootstrapComplete, "Bootstrap latch must not be set before mandatory heartbeat.");

        // ── Phase 2: Deliver SimHost heartbeat (Standby) → latch clears ──────
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        Assert.True(exercise.BootstrapComplete, "Bootstrap latch not cleared after mandatory node reached Standby.");

        // ── Phase 3: Next ClusterOpRequest should be accepted ─────────────────
        var reqId2 = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId2,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // A TransitionState with nodes fans out ExecuteNodeOpIntents (waits for ACKs),
        // so ClusterOpCompletedEvent is not published immediately.
        // Acceptance is verified by the presence of fan-out intents.
        var phase3Intents    = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        var phase3Completed  = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(phase3Intents.Any(),
            "ClusterMaster did not fan out node op intents — request was not accepted after bootstrap.");
        Assert.False(phase3Completed.Any(e => e.RequestId == reqId2 && e.StatusCode == OrchestrationStatusCode.Rejected),
            "Second request must not be rejected after bootstrap.");
    }

    /// <summary>
    /// Verifies that when a mandatory node's heartbeat times out, ClusterMaster publishes
    /// <c>Degraded</c> and re-engages the bootstrap latch.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void EjectsMandatoryNode_EntersDegraded()
    {
        // Use a very short timeout so we don't wait 5+ seconds.
        var config = new ClusterConfiguration
        {
            Mandatory               = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 0.1f,
        };

        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, config);

        // Bootstrap: publish SimHost heartbeat once, let latch clear.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        Assert.True(exercise.BootstrapComplete, "Bootstrap should have cleared after SimHost Standby heartbeat.");

        // Drain any already-published events (e.g. bootstrap Standby publish).
        bus.ReadManaged<ClusterStateUpdateEvent>().ToList();

        // Now stop heartbeats: wait long enough for timeout (0.1 s) then tick.
        Thread.Sleep(200);

        Fdp.Toolkit.Orchestration.ClusterState? degradedState = null;
        var ejectionDeadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < ejectionDeadline)
        {
            exercise.Tick();
            bus.SwapBuffers();
            foreach (var ev in bus.ReadManaged<ClusterStateUpdateEvent>())
            {
                if (ev.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Degraded)
                {
                    degradedState = ev.CurrentState;
                    break;
                }
            }
            if (degradedState.HasValue) break;
            Thread.Sleep(20);
        }

        Assert.True(degradedState.HasValue,
            "ClusterMaster did not publish Degraded after mandatory node timed out.");
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Degraded, degradedState!.Value);
        Assert.False(exercise.BootstrapComplete,
            "Bootstrap latch should re-engage after mandatory node ejection.");
    }

    /// <summary>
    /// Verifies that after a mandatory node is ejected, surviving nodes receive
    /// <c>PrepareState(Standby)</c> commands and the ejected node is removed from the roster.
    /// <para>
    /// <b>Keyed per-node delivery (CGF-1-BATCH-09 §B):</b> <c>NodeOpCommand</c> has
    /// <c>[DdsKey] TargetNodeId</c>.  <c>ClusterMaster.FanOutNodeOp</c> writes one sample per
    /// surviving roster entry; the per-node writer for the ejected node is disposed before the
    /// fan-out, so no more samples reach its instance key.  Each reader applies a client-side
    /// filter (<c>cmd.TargetNodeId == ownNodeId</c>) to drop any stale cross-key samples.
    /// </para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SurvivingNodes_CommandedToStandby_AfterEjection()
    {
        var config = new ClusterConfiguration
        {
            Mandatory               = new[] { "SimHost" },
            Optional                = new[] { "CGF" },
            HeartbeatTimeoutSeconds = 0.1f,
        };

        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, config);

        // Bootstrap: publish both SimHost (nodeId=1) and CGF (nodeId=400) as Standby.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 400,
            SubsystemName = "CGF",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        Assert.True(exercise.BootstrapComplete, "Both nodes bootstrapped.");
        Assert.Equal(2, exercise.NodeRoster.ActiveNodes.Count);

        // Drain any pre-ejection events.
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        // Stop SimHost heartbeats; wait for timeout then trigger ejection via Tick.
        Thread.Sleep(200);

        var allIntents = new List<ExecuteNodeOpIntent>();
        var ejectionDeadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < ejectionDeadline)
        {
            exercise.Tick();
            bus.SwapBuffers();
            allIntents.AddRange(bus.ReadManaged<ExecuteNodeOpIntent>());
            if (!exercise.NodeRoster.ActiveNodes.ContainsKey(1)) break;
            Thread.Sleep(20);
        }

        // SimHost (nodeId 1) must be removed from the roster.
        Assert.False(exercise.NodeRoster.ActiveNodes.ContainsKey(1),
            "SimHost must be removed from roster after ejection.");
        Assert.True(exercise.NodeRoster.ActiveNodes.ContainsKey(400),
            "CGF must remain in roster as a surviving node.");

        // CGF should receive AbortTransaction and PrepareState intents.
        var cgfIntents = allIntents.Where(i => i.TargetNodeId == 400).ToList();
        Assert.Contains(cgfIntents, i => i.Operation == FdpNodeOpType.AbortTransaction);
        Assert.Contains(cgfIntents, i => i.Operation == FdpNodeOpType.PrepareState);

        // SimHost (ejected) should receive no intents after ejection.
        var simHostIntents = allIntents.Where(i => i.TargetNodeId == 1).ToList();
        Assert.Empty(simHostIntents);
    }

    /// <summary>
    /// Verifies that a completed (accepted) ClusterOpRequest is recorded in the
    /// <see cref="ClusterMaster.TransactionHistory"/> ring buffer with <c>IsAborted == false</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransactionHistory_RecordsCompletedTransaction()
    {
        // No mandatory nodes → bootstrap immediately.
        var config = new ClusterConfiguration
        {
            Mandatory                  = Array.Empty<string>(),
            TransactionHistoryCapacity = 10,
        };

        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, config);

        Assert.True(exercise.BootstrapComplete, "With empty mandatory list the latch should clear immediately.");

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var history = exercise.TransactionHistory;
        Assert.True(history.Count >= 1, "Expected at least one transaction in history.");

        var tx = history[history.Count - 1];
        Assert.False(tx.IsAborted,
            "Transaction from accepted ClusterOpRequest must not be marked as aborted.");
        Assert.Equal(reqId, tx.OriginRequestId);
    }

    // ── A.3 (CGF-1-BATCH-05): Optimistic _currentDsmState advance ────────────

    /// <summary>
    /// After an accepted <c>TransitionState</c> request advances the cluster to
    /// <c>LoadingLive</c>, a subsequent request is planned from <c>LoadingLive</c>
    /// (not the initial <c>Standby</c>). Verifies optimistic <c>_currentDsmState</c> advance.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CurrentDsmState_AdvancesOptimistically_AfterAcceptedTransition()
    {
        // No mandatory nodes → bootstrap immediately.
        var config = new ClusterConfiguration
        {
            Mandatory                  = Array.Empty<string>(),
            TransactionHistoryCapacity = 10,
        };

        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, config);

        Assert.True(exercise.BootstrapComplete, "Empty mandatory list: bootstrap should be immediate.");

        // ── First request: Standby → LoadingLive ──────────────────────────────
        var req1Id = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = req1Id,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var phase1Events = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        bool req1Accepted = !phase1Events.Any(e => e.RequestId == req1Id && e.StatusCode == OrchestrationStatusCode.Rejected);
        Assert.True(req1Accepted, "First TransitionState request should be accepted.");

        // ── Second request: from (now-optimistically) LoadingLive → RunningLive ─
        var req2Id = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = req2Id,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var phase2Events = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        bool req2Accepted = !phase2Events.Any(e => e.RequestId == req2Id && e.StatusCode == OrchestrationStatusCode.Rejected);
        Assert.True(req2Accepted, "Second TransitionState request should be accepted.");

        // The second transaction's TotalSteps should be 1 (LoadingLive → RunningLive directly).
        var history = exercise.TransactionHistory;
        Assert.True(history.Count >= 2, "Expected at least two transactions in history.");
        var tx2 = history[history.Count - 1];
        Assert.Equal(req2Id, tx2.OriginRequestId);
        Assert.Equal(1, tx2.TotalSteps);
    }

    // ── TASK-D06: Bootstrap latch case-insensitive fix ─────────────────────

    /// <summary>
    /// Verifies bootstrap latch releases when subsystem name differs only in casing.
    /// Regression test for the case-sensitive comparison bug (TASK-D06).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void BootstrapLatch_ReleasesWithCaseInsensitiveSubsystemName()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                  = new[] { "simhost" },  // lowercase in config
            HeartbeatTimeoutSeconds    = 60f,
            TransactionHistoryCapacity = 10,
        };

        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, config);

        // Feed a heartbeat with mixed-case name "SimHost" (not "simhost")
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "SimHost",  // different case from config
        });
        bus.SwapBuffers();
        master.Tick();

        // Bootstrap should now be complete.
        Assert.True(master.BootstrapComplete,
            "Bootstrap latch must release when subsystem name matches case-insensitively.");

        // A ClusterStateTransitionedEvent for Idle (Standby) should have been published.
        bus.SwapBuffers();
        var events = bus.ReadManaged<ClusterStateTransitionedEvent>().ToList();
        Assert.True(events.Any(e => e.NewStateId == Fdp.Toolkit.Orchestration.ClusterState.Idle),
            "Expected ClusterStateTransitionedEvent(Idle) when bootstrap latch releases.");
    }

    /// <summary>
    /// Verifies the bootstrap latch does NOT release for a completely wrong subsystem name.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void BootstrapLatch_DoesNotReleaseForWrongSubsystemName()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                  = new[] { "simhost" },
            HeartbeatTimeoutSeconds    = 60f,
            TransactionHistoryCapacity = 10,
        };

        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, config);

        // Feed a heartbeat with completely different name
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "IG",
        });
        bus.SwapBuffers();
        master.Tick();

        // Should not complete bootstrap — latch unreleased.
        Assert.False(master.BootstrapComplete,
            "Bootstrap latch must NOT release for a non-matching subsystem name.");

        bus.SwapBuffers();
        var events = bus.ReadManaged<ClusterStateTransitionedEvent>().ToList();
        Assert.DoesNotContain(events, e => e.NewStateId == Fdp.Toolkit.Orchestration.ClusterState.Idle);
    }
}

