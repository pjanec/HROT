using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
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
        using var exercise = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());

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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
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
        // ⭐ CE-3344: while the latch is closed the master DOES announce its state — Idle, WITH what it is waiting for; a RELEASE would be an Idle with nothing missing.
        Assert.DoesNotContain(events, e => e.NewStateId == Fdp.Toolkit.Orchestration.ClusterState.Idle && string.IsNullOrEmpty(e.WaitingFor));
        Assert.Contains(events, e => e.WaitingFor == "simhost");
    }

    // ══ P1 (Q91 §4-B, B1) — a node that leaves is removed from every in-flight round ══════════════════════════════
    // 📐 Before: EjectNode returned early for a non-mandatory node and a tracker's Expected was fixed at fan-out, so a
    //    round whose node died waited FOREVER (ClusterMaster.cs:700, :1828). ⭐ No timer (R-4001): the heartbeat timeout
    //    that ejects the node is the only time limit; the round then completes on the nodes that remain.

    private static void Beat(FdpEventBus bus, int nodeId, string name) =>
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = nodeId, SubsystemName = name,
            LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });

    private static void Step(FdpEventBus bus, ClusterMaster master) { bus.SwapBuffers(); master.Tick(); bus.SwapBuffers(); }

    private static (ClusterMaster Master, Guid Request, Guid Tx) TwoNodeCheckpoint(FdpEventBus bus)
    {
        var master = new ClusterMaster(bus, new ClusterConfiguration { HeartbeatTimeoutSeconds = 600f, MandatoryRoles = NodeRole.None });
        Beat(bus, 1, "SimHost");
        Beat(bus, 100, "IG");
        Step(bus, master);
        var request = Guid.NewGuid();
        bus.PublishManaged(new TakeCheckpointIntent { RequestId = request });
        Step(bus, master);
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation == FdpNodeOpType.TakeSnapshot).ToList();
        Assert.Equal(2, ops.Count);
        return (master, request, ops[0].TransactionId);
    }

    private static void Ack(FdpEventBus bus, Guid tx, int nodeId) =>
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId = tx, Operation = FdpNodeOpType.TakeSnapshot, NodeId = nodeId,
            StatusCode = OrchestrationStatusCode.Success, IsParticipating = true,
        });

    [Fact]
    public void P1_A_round_completes_when_its_last_unanswered_node_is_ejected()
    {
        var bus = new FdpEventBus();
        var (master, request, tx) = TwoNodeCheckpoint(bus);
        using var _ = master;

        Ack(bus, tx, 1);
        Step(bus, master);
        Assert.DoesNotContain(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);   // still owed by 100

        master.EjectNode(100);
        bus.SwapBuffers();   // ⚠ a direct call publishes outside Tick: ONE swap makes it readable (production ejects inside Tick)

        var done = Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);
        Assert.Equal(OrchestrationStatusCode.Success, done.StatusCode);
    }

    [Fact]
    public void P1_Ejecting_a_node_that_already_answered_takes_nothing_off_the_round()
    {
        var bus = new FdpEventBus();
        var (master, request, tx) = TwoNodeCheckpoint(bus);
        using var _ = master;

        Ack(bus, tx, 1);
        Step(bus, master);
        master.EjectNode(1);      // ⛔ node 1 owes nothing — the round must still wait for node 100
        bus.SwapBuffers();
        Assert.DoesNotContain(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);

        Ack(bus, tx, 100);
        Step(bus, master);
        Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);
    }

    [Fact]
    public void P1_A_transition_drops_every_prepare_ack_the_ejected_node_still_owed()
    {
        // ⭐ A transition from Idle to OperatingLive has TWO prepare steps (LoadingLive, OperatingLive) under ONE
        //   transaction, so a node owes two ACKs there — the per-node count, not a flat "minus one", is what must go.
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration { HeartbeatTimeoutSeconds = 600f, MandatoryRoles = NodeRole.None });
        Beat(bus, 1, "SimHost");
        Beat(bus, 100, "IG");
        Step(bus, master);

        var request = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = request, TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
        });
        Step(bus, master);
        var prepares = bus.ReadManaged<ExecuteNodeOpIntent>()
                          .Where(i => i.TargetNodeId == 1 && i.Operation != FdpNodeOpType.CommitState).ToList();
        Assert.True(prepares.Count >= 2, $"expected two prepare steps for node 1, saw {prepares.Count}");

        foreach (var p in prepares)
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId = p.TransactionId, Operation = p.Operation, NodeId = 1,
                StatusCode = OrchestrationStatusCode.Success, IsParticipating = true,
            });
        Step(bus, master);
        Assert.DoesNotContain(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);

        master.EjectNode(100);
        bus.SwapBuffers();
        Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4103</c> (R-4001: no silent forever-wait) — a node whose FIRST prepare of a multi-step transition answers an error has dropped the
    /// rest of the transaction (its slave clears the pending steps), so the round must FAIL NOW with that code instead of waiting for the ACKs it will
    /// never get. 📐 Measured in the editor: a scenario naming a terrain nobody has — the node's PrepareEdit threw, the master sat at the target
    /// state with the request unanswered for good. Red-proof: drop the owed-ACK removal in <c>ClusterMaster.ProcessAcks</c> ⇒ no completion here.
    /// </summary>
    [Fact]
    public void CE4103_A_failed_prepare_fails_the_whole_transition_without_waiting_for_the_steps_the_node_will_never_answer()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration { HeartbeatTimeoutSeconds = 600f, MandatoryRoles = NodeRole.None });
        Beat(bus, 1, "SimHost");
        Step(bus, master);

        var request = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = request, TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
        });
        Step(bus, master);
        var prepares = bus.ReadManaged<ExecuteNodeOpIntent>()
                          .Where(i => i.TargetNodeId == 1 && i.Operation != FdpNodeOpType.CommitState).ToList();
        Assert.True(prepares.Count >= 2, $"the transition has several prepare steps for the node (saw {prepares.Count})");

        // the node answers the FIRST prepare with an error and, like ClusterSlave after a faulted prepare, never answers the others
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId = prepares[0].TransactionId, Operation = prepares[0].Operation, NodeId = 1,
            StatusCode = OrchestrationStatusCode.Failure, IsParticipating = true,
        });
        Step(bus, master);

        var done = Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == request);
        Assert.Equal(OrchestrationStatusCode.Failure, done.StatusCode);
    }

    // == CE-4087 - the mandatory set's ROLES half, and a load that WAITS on the latch ============================
    // User, 2026-10-28: "scenario should not be able to load at all without muscle role node loaded, as it can not be edited nor started."
    // The roles are the second half of the mandatory set (ClusterMaster.Bootstrap.cs); G1..G9 pin the wait/refuse, L1/L2 its tie to the name latch.

    private const int CgfNode = 400;
    private const int SimHostNode = 1;

    private static ClusterConfiguration GateConfig(double waitSeconds = 30.0) => new()
    {
        Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
        LoadWaitSeconds = waitSeconds,   // MandatoryRoles = the default: Brain | MuscleGround
    };

    /// <summary>A node's heartbeat joins the roster; its capability advert (the role token) lands in the same frame only when <paramref name="roleToken"/> is given.</summary>
    private static void GateJoin(FdpEventBus bus, ClusterMaster master, int nodeId, string? roleToken)
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = nodeId, SubsystemName = "Node", WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
            LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
        });
        if (roleToken != null)
            bus.PublishManaged(new NodeCapabilitiesEvent { NodeId = nodeId, Capabilities = new[] { roleToken } });
        Step(bus, master);
    }

    /// <summary>The advert of a node already in the roster arrives LATER than its heartbeat (R-4005, the join window).</summary>
    private static void AdvertLands(FdpEventBus bus, ClusterMaster master, int nodeId, string roleToken)
    {
        bus.PublishManaged(new NodeCapabilitiesEvent { NodeId = nodeId, Capabilities = new[] { roleToken } });
        Step(bus, master);
    }

    private static Guid RequestLoad(FdpEventBus bus, ClusterMaster master, string target = "OperatingLive", string scenario = "s1")
    {
        var id = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = id, OperationType = ClusterOpType.TransitionState,
            PayloadJson = $"{{\"TargetState\":\"{target}\",\"ScenarioId\":\"{scenario}\"}}",
        });
        Step(bus, master);
        return id;
    }

    private static List<ClusterOpCompletedEvent> StatusesOf(FdpEventBus bus, Guid requestId)
        => bus.ReadManaged<ClusterOpCompletedEvent>().Where(e => e.RequestId == requestId).ToList();

    // ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(Timeout = 10_000)]
    [InlineData("OperatingLive")]
    [InlineData("OperatingEdit")]
    public void G1_A_load_with_no_MuscleGround_node_waits_and_says_which_role(string target)
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig());
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);   // the CGF is there; the SimHost is not

        var id = RequestLoad(bus, master, target);

        Assert.Equal(id, master.HeldLoadRequestId);                                        // held, not refused, not started
        Assert.Null(master.ParkedRequestId);                                                // nothing was staged
        Assert.Equal(ClusterState.Idle, master.CurrentClusterState);                        // the cluster did not move
        Assert.Equal("waiting for: MuscleGround (SimHost)", master.LoadNotice);             // what the operator and HTTP read
        var status = Assert.Single(StatusesOf(bus, id));
        Assert.Equal(OrchestrationStatusCode.Pending, status.StatusCode);
        Assert.Equal("waiting for: MuscleGround (SimHost)", status.ResultPayload);          // and what the asking node's bus carries
    }

    [Fact(Timeout = 10_000)]
    public void G2_The_load_starts_the_moment_the_SimHosts_capability_advert_lands_and_not_on_its_heartbeat()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig());
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        var id = RequestLoad(bus, master);
        Assert.Equal(id, master.HeldLoadRequestId);

        // ⭐ trap ①: the SimHost's HEARTBEAT is in the roster but its advert has not landed ⇒ its mask reads None ⇒ it counts for nothing
        GateJoin(bus, master, SimHostNode, roleToken: null);
        Assert.Equal(id, master.HeldLoadRequestId);
        Assert.Equal("waiting for: MuscleGround (SimHost)", master.LoadNotice);

        AdvertLands(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);

        Assert.Null(master.HeldLoadRequestId);
        Assert.Null(master.LoadNotice);
        Assert.Equal(id, master.ParkedRequestId);   // the load began (a scenario load parks for its files, L8)
    }

    [Fact(Timeout = 10_000)]
    public void G3_A_load_is_refused_after_the_wait_and_the_reason_names_every_missing_role()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig(waitSeconds: 0.2));
        GateJoin(bus, master, 7, roleToken: null);   // someone is there, but plays no role

        var id = RequestLoad(bus, master);       // held …
        Assert.Equal(id, master.HeldLoadRequestId);
        System.Threading.Thread.Sleep(300);      // … nobody turns up for longer than the wait …
        Step(bus, master);                       // … and the next tick refuses it

        Assert.Null(master.HeldLoadRequestId);
        Assert.Null(master.ParkedRequestId);
        Assert.Equal(ClusterState.Idle, master.CurrentClusterState);
        Assert.Empty(bus.ReadManaged<ExecuteNodeOpIntent>());
        var status = Assert.Single(StatusesOf(bus, id));   // a FAILED load the operator sees — never a silent hang
        Assert.Equal(OrchestrationStatusCode.Failure, status.StatusCode);
        var reason = Assert.IsType<string>(status.ResultPayload);
        Assert.Contains("waiting for: Brain (CGF), MuscleGround (SimHost)", reason);
        Assert.Equal(reason, master.LoadNotice);           // the panel and GET /cluster/roster keep reading it
    }

    /// <summary>
    /// ⭐⭐ trap ②: the editor plays every simulation role IN-PROCESS and declares them to the roster only after its own
    /// heartbeat and advert loop back through the bus. Its own roles (<see cref="ClusterMaster.LocalRoles"/>) count at once, so
    /// the editor never refuses — or waits for — itself. ⭐ Anti-vacuity: the identical master WITHOUT them holds the same load.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void G4_The_hosts_own_in_process_roles_count_so_the_editor_never_refuses_itself()
    {
        var bus = new FdpEventBus();
        using var editor = new ClusterMaster(bus, GateConfig().ForOneNodeCluster())
        {
            LocalRoles = NodeRole.Brain | NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver,
        };
        var id = RequestLoad(bus, editor);
        Assert.Null(editor.HeldLoadRequestId);
        Assert.Equal(id, editor.ParkedRequestId);

        var bus2 = new FdpEventBus();
        using var bare = new ClusterMaster(bus2, GateConfig().ForOneNodeCluster());
        var id2 = RequestLoad(bus2, bare);
        Assert.Equal(id2, bare.HeldLoadRequestId);   // without LocalRoles it waits: the gate is real
    }

    /// <summary>⭐ The gate is about SCENARIO loads. A transition that names none (a replay, a review, a branch, a dry run) is none of its business.</summary>
    /// <summary>
    /// A transition naming no scenario is not a LOAD: it is never HELD by the load wait. ⭐ CE-3344 (R-3069): the latch now includes the roles, so on a cluster that has not
    /// reported its MuscleGround it is REJECTED (the cluster is not ready — ⛔ SUPERSEDED: it used to "go ahead"), and goes ahead the frame the muscle reports.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void G5_A_transition_naming_no_scenario_is_not_held_by_the_load_wait()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig());
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);   // no MuscleGround anywhere

        var id = Guid.NewGuid();
        var request = new ClusterOpRequest
        {
            RequestId = id, OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"OperatingLive\"}",
        };
        master.HandleClusterOpRequest(request);
        Step(bus, master);

        Assert.Null(master.HeldLoadRequestId);                                   // not a load: no wait
        Assert.Equal(ClusterState.Idle, master.CurrentClusterState);             // the cluster is not ready (no muscle): refused …
        Assert.Equal(OrchestrationStatusCode.Rejected, Assert.Single(StatusesOf(bus, id)).StatusCode);

        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);         // … and ready the frame the muscle reports
        master.HandleClusterOpRequest(request with { RequestId = Guid.NewGuid() });
        Step(bus, master);
        Assert.Equal(ClusterState.OperatingLive, master.CurrentClusterState);    // it went ahead
    }

    [Fact(Timeout = 10_000)]
    public void G6_A_newer_request_replaces_the_waiting_load_and_tells_its_asker()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig());
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        var first = RequestLoad(bus, master, scenario: "s1");
        Assert.Equal(first, master.HeldLoadRequestId);

        var second = RequestLoad(bus, master, scenario: "s2");

        Assert.Equal(second, master.HeldLoadRequestId);   // the newer one waits now
        Assert.Contains(StatusesOf(bus, first), e => e.StatusCode == OrchestrationStatusCode.Cancelled);
    }

    [Fact(Timeout = 10_000)]
    public void G7_The_operators_cancel_drops_a_waiting_load()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig());
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        var id = RequestLoad(bus, master);
        Assert.Equal(id, master.HeldLoadRequestId);

        bus.PublishManaged(new CancelOperationIntent { TargetRequestId = id });
        Step(bus, master);

        Assert.Null(master.HeldLoadRequestId);
        Assert.Null(master.LoadNotice);
        Assert.Contains(StatusesOf(bus, id), e => e.StatusCode == OrchestrationStatusCode.Cancelled);
        // and the SimHost arriving later starts nothing
        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);
        Assert.Null(master.ParkedRequestId);
    }

    /// <summary>A refusal is news only until the next request: the notice the panel and HTTP show must not outlive it forever.</summary>
    [Fact(Timeout = 10_000)]
    public void G9_A_refusal_is_cleared_by_the_next_request_of_any_kind()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, GateConfig(waitSeconds: 0.1));
        RequestLoad(bus, master);
        System.Threading.Thread.Sleep(200);
        Step(bus, master);
        Assert.Contains("refused", master.LoadNotice);

        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);                    // ⭐ CE-3344: the cluster must be READY for any non-load request to be admitted
        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"Idle\"}",
        });
        Step(bus, master);

        Assert.Null(master.LoadNotice);
    }

    /// <summary>The roles are configuration: a deployment that needs a Perception node says so, and one that needs none switches the gate off.</summary>
    [Fact(Timeout = 10_000)]
    public void G8_The_required_roles_are_configurable_and_None_switches_the_gate_off()
    {
        var bus = new FdpEventBus();
        using var off = new ClusterMaster(bus, new ClusterConfiguration
        {
            Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
            MandatoryRoles = NodeRole.None,
        });
        var id = RequestLoad(bus, off);
        Assert.Null(off.HeldLoadRequestId);
        Assert.Equal(id, off.ParkedRequestId);

        var bus2 = new FdpEventBus();
        using var perception = new ClusterMaster(bus2, new ClusterConfiguration
        {
            Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
            MandatoryRoles = NodeRole.Perception,
        });
        GateJoin(bus2, perception, SimHostNode, NodeRoleTokens.MuscleGround);   // a muscle, but no perception
        var id2 = RequestLoad(bus2, perception);
        Assert.Equal(id2, perception.HeldLoadRequestId);
        Assert.Equal("waiting for: Perception (SimHost)", perception.LoadNotice);
    }

    // -- L: the same wait on the latch's NAME half -----------------------------------------------------------

    /// <summary>
    /// CE-4087 - the roles are the second half of the mandatory set, the subsystem NAMES the first: a scenario load against a
    /// cluster whose NAME latch is still closed WAITS too (it used to be a bare Rejected), starts the moment the latch clears, and
    /// every other request is rejected as before.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void L1_A_load_against_a_closed_name_latch_waits_and_starts_when_the_latch_clears()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" }, HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
            MandatoryRoles = NodeRole.None,
        });

        var id = RequestLoad(bus, master);
        Assert.False(master.BootstrapComplete);
        Assert.Equal(id, master.HeldLoadRequestId);
        var status = Assert.Single(StatusesOf(bus, id));
        Assert.Equal(OrchestrationStatusCode.Pending, status.StatusCode);   // NOT Rejected
        Assert.Equal("waiting for: SimHost", status.ResultPayload);

        // a request that is not a load keeps today's rejection, and does not disturb the waiting load
        var other = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
            { RequestId = other, OperationType = ClusterOpType.TransitionState, PayloadJson = "{\"TargetState\":\"OperatingLive\"}" });
        Step(bus, master);
        Assert.Equal(OrchestrationStatusCode.Rejected, Assert.Single(StatusesOf(bus, other)).StatusCode);
        Assert.Equal(id, master.HeldLoadRequestId);

        Beat(bus, 1, "SimHost");
        Step(bus, master);

        Assert.True(master.BootstrapComplete);
        Assert.Null(master.HeldLoadRequestId);
        Assert.Equal(id, master.ParkedRequestId);   // the load began
    }

    /// <summary>One text for both halves: a configured name and the default roles are waited for together.</summary>
    [Fact(Timeout = 10_000)]
    public void L2_The_waiting_text_names_the_missing_subsystem_and_the_missing_roles_together()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" }, HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
        });
        RequestLoad(bus, master);
        Assert.Equal("waiting for: SimHost, Brain (CGF), MuscleGround (SimHost)", master.LoadNotice);
    }

    // -- R: CLUSTER READY (CE-3344, R-3069) — the latch over names AND roles, role loss, WaitingFor, ClusterView.CanOperate ------------

    private static ClusterConfiguration ReadyConfig(float timeout = 60f) => new()
    {
        Mandatory = new[] { "Node" }, HeartbeatTimeoutSeconds = timeout, TransactionHistoryCapacity = 10,   // GateJoin's subsystem name is "Node"; roles = the default Brain | MuscleGround
    };

    private static List<ClusterStateUpdateEvent> StateUpdates(FdpEventBus bus) => bus.ReadManaged<ClusterStateUpdateEvent>().ToList();

    /// <summary>
    /// CE-3344 success 1 - every mandatory NAME is present (a node named "Node" is in Idle) but the roles' capability adverts have not all landed: the latch stays closed,
    /// and releases the frame the LAST advert lands. 🔴 Mutation: <c>CheckBootstrapLatch</c> testing only <c>MissingMandatoryNames</c> => released at the first heartbeat.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void R1_The_latch_waits_for_the_roles_even_when_every_name_is_present_and_releases_the_frame_the_advert_lands()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, ReadyConfig());

        GateJoin(bus, master, CgfNode, roleToken: null);          // the name is there (Idle); no role advert yet
        Assert.False(master.BootstrapComplete);
        AdvertLands(bus, master, CgfNode, NodeRoleTokens.Brain);   // Brain present, MuscleGround still missing
        Assert.False(master.BootstrapComplete);
        GateJoin(bus, master, SimHostNode, roleToken: null);
        Assert.False(master.BootstrapComplete);                    // trap: a heartbeat without the advert counts for nothing

        AdvertLands(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);
        Assert.True(master.BootstrapComplete);                     // the same frame the last advert landed
        Assert.Contains(StateUpdates(bus), e => e.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Idle && string.IsNullOrEmpty(e.WaitingFor));   // released: Idle, nothing missing
    }

    /// <summary>
    /// CE-3344 success 2 - losing the only node that holds a mandatory ROLE (its name is NOT mandatory, so the old name-only path never degraded) =>
    /// Degraded, the latch re-engaged, the survivors told; the role's return releases it again through the same check.
    /// 🔴 Mutation: <c>EjectNode</c> without the <c>lostRole</c> arm => no Degraded, the latch stays released.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void R2_Losing_the_only_node_of_a_mandatory_role_degrades_the_cluster_and_its_return_releases_it()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration
            { Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 0.3f, TransactionHistoryCapacity = 10 });   // roles only: Brain | MuscleGround
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);
        Assert.True(master.BootstrapComplete);
        bus.ReadManaged<ClusterStateUpdateEvent>().ToList();

        // the SimHost goes silent; the CGF keeps beating
        Thread.Sleep(450);
        GateJoin(bus, master, CgfNode, roleToken: null);

        Assert.False(master.BootstrapComplete);                                  // the latch is re-engaged
        var degraded = Assert.Single(StateUpdates(bus), e => e.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Degraded);
        Assert.Contains("MuscleGround", degraded.WaitingFor);                    // and the broadcast says which role is gone
        Assert.DoesNotContain("Brain", degraded.WaitingFor);
        var told = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.TargetNodeId == CgfNode).Select(i => i.Operation).ToList();
        Assert.Contains(FdpNodeOpType.AbortTransaction, told);                   // the survivors were aborted + sent to Idle, as for a lost name
        Assert.Contains(FdpNodeOpType.PrepareState, told);

        GateJoin(bus, master, SimHostNode, roleToken: NodeRoleTokens.MuscleGround);   // the SimHost is back
        Assert.True(master.BootstrapComplete);
        Assert.Contains(StateUpdates(bus), e => e.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Idle && string.IsNullOrEmpty(e.WaitingFor));
    }

    /// <summary>
    /// CE-3344 success 3 - while the latch is engaged the master PUBLISHES its state with what is missing (it used to publish nothing), a node's view says it and cannot operate;
    /// after release the same view operates and says nothing. 🔴 Mutations: <c>PublishWaitingForIfChanged</c> not called => no WaitingFor before release; <c>ClusterView.CanOperate</c>
    /// ignoring <c>WaitingFor</c> => <c>CanOperate</c> true while waiting.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void R3_A_nodes_view_names_what_is_missing_while_the_latch_is_engaged_and_operates_after_release()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, ReadyConfig());
        var view = new ClusterView(bus, nodeId: 9);

        GateJoin(bus, master, 9, roleToken: null);          // this node's heartbeat (a role-less advert-less node)
        view.Update();
        Assert.True(view.HasFollowedState);                  // the master DID publish its state while not ready
        Assert.Contains("Brain", view.WaitingFor);
        Assert.Contains("MuscleGround", view.WaitingFor);
        Assert.False(view.CanOperate);
        Assert.Contains("Brain", view.NotOperableReason);

        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        view.Update();
        Assert.DoesNotContain("Brain", view.WaitingFor);     // the text follows: only the muscle is left
        Assert.Contains("MuscleGround", view.WaitingFor);
        Assert.False(view.CanOperate);

        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);
        view.Update();
        Assert.Equal(string.Empty, view.WaitingFor);
        Assert.True(view.CanOperate);
        Assert.Null(view.NotOperableReason);
    }

    /// <summary>
    /// CE-3344 success 4 - a node the master has NOT put in its in-sync set (R-4006) cannot operate even when the cluster is ready; one that is, can (anti-vacuity: same cluster).
    /// 🔴 Mutation: <c>ClusterView.IsInSync</c> returning true => the outsider's view operates.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void R4_A_node_outside_the_in_sync_set_cannot_operate_even_when_the_cluster_is_ready()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration
            { Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10 });
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);
        var insider  = new ClusterView(bus, nodeId: CgfNode);
        var outsider = new ClusterView(bus, nodeId: 77);   // never joined the roster, so never in sync
        insider.Update(); outsider.Update();

        Assert.True(master.BootstrapComplete);
        Assert.Contains(CgfNode, master.InSyncNodeIds);
        Assert.True(insider.CanOperate);
        Assert.False(outsider.CanOperate);
        Assert.Contains("in-sync", outsider.NotOperableReason);
        Assert.Equal(string.Empty, outsider.WaitingFor);   // not the cluster that is not ready: this node is not in it
    }

    /// <summary>
    /// CE-3344 success 5 - the editor alone (a one-node cluster whose host plays every role in-process, <c>LocalRoles</c>) is ready with no other node.
    /// 🔴 Mutation: <c>CheckBootstrapLatch</c> not counting <c>LocalRoles</c> (RolePresent without it) => stays closed, <c>CanOperate</c> false.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void R5_The_editor_alone_is_ready_through_its_own_roles()
    {
        var bus = new FdpEventBus();
        const int editorNode = 3;
        using var master = new ClusterMaster(bus, new ClusterConfiguration { HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10 }.ForOneNodeCluster())
        {
            LocalRoles = NodeRole.Brain | NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver,
        };
        var view = new ClusterView(bus, nodeId: editorNode);

        GateJoin(bus, master, editorNode, roleToken: null);   // the editor's own heartbeat — no other node anywhere
        view.Update();

        Assert.True(master.BootstrapComplete);
        Assert.True(view.CanOperate, view.NotOperableReason);
    }

    /// <summary>The wire (CE-3344): <c>WaitingFor</c> crosses the cluster-state topic and its one decoder / encoder pair unchanged. Pinned in the ingress/translator suites; here: the
    /// master's own Transitioned event carries it too (what the egress translator maps to the topic).</summary>
    [Fact(Timeout = 10_000)]
    public void R6_The_transitioned_event_the_egress_writes_to_the_wire_carries_the_waiting_text()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, ReadyConfig());
        GateJoin(bus, master, 9, roleToken: null);
        var transitioned = bus.ReadManaged<ClusterStateTransitionedEvent>().Last();
        Assert.Contains("Brain (CGF)", transitioned.WaitingFor);
    }

    // -- E: THE CLUSTER ERROR STATE (CE-3345 part A, R-3073) — a required node / role lost while a scenario is loaded or running ----------------------

    private static void SetField(ClusterMaster m, string field, object value)
        => typeof(ClusterMaster).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(m, value);

    /// <summary>
    /// A ready cluster (Brain on <see cref="CgfNode"/>, MuscleGround on <see cref="SimHostNode"/>, default roles mandatory) whose world is in <paramref name="state"/>, then the Brain's node goes
    /// silent past the heartbeat timeout while the SimHost keeps beating — the master ejects it on that tick. The bus is drained of everything before the loss.
    /// </summary>
    private static ClusterMaster ClusterThatLosesItsBrain(FdpEventBus bus, ClusterState state)
    {
        var master = new ClusterMaster(bus, new ClusterConfiguration
            { Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 0.3f, TransactionHistoryCapacity = 10 });   // roles only: Brain | MuscleGround
        master.SimSeconds = () => 751.0;   // 12:31
        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        GateJoin(bus, master, SimHostNode, NodeRoleTokens.MuscleGround);
        Assert.True(master.BootstrapComplete);
        SetField(master, "_currentDsmState", state);
        bus.ReadManaged<ClusterStateUpdateEvent>().ToList();
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Thread.Sleep(450);
        GateJoin(bus, master, SimHostNode, roleToken: null);   // the Brain's node is ejected on this tick
        return master;
    }

    /// <summary>
    /// CE-3345 success 1 - losing the only Brain node while OperatingLive => Degraded + an error reason naming the node and the role + the sim time; the survivors are NOT aborted and NOT sent to Idle (their world
    /// stays); the clock is paused (the ONE pause, <c>PauseTimeIntent</c>); the live recording is closed (<c>FinalizeLive</c>) and nothing else.
    /// 🔴 Mutation: <c>EjectNode</c>'s error arm skipped (<c>HasWorldLoaded</c> false) => the old teardown: AbortTransaction + PrepareState to the survivor, no reason, no pause.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void E1_Losing_the_only_Brain_while_operating_live_is_an_error_not_a_teardown()
    {
        var bus = new FdpEventBus();
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.OperatingLive);

        Assert.True(master.IsInError);
        Assert.False(master.BootstrapComplete);
        var degraded = Assert.Single(StateUpdates(bus), e => e.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Degraded);
        Assert.False(string.IsNullOrEmpty(degraded.ErrorReason));
        Assert.Contains("Brain", degraded.ErrorReason);
        Assert.Contains($"node {CgfNode}", degraded.ErrorReason);
        Assert.Contains("00:12:31", degraded.ErrorReason);
        Assert.Contains("Brain", degraded.WaitingFor);                           // WaitingFor still says what is missing (the error is beside it, not instead)

        var told = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.TargetNodeId == SimHostNode).Select(i => i.Operation).ToList();
        Assert.DoesNotContain(FdpNodeOpType.AbortTransaction, told);             // no abort ...
        Assert.DoesNotContain(FdpNodeOpType.PrepareState, told);                 // ... no Idle: the world stays on every node
        Assert.Contains(FdpNodeOpType.FinalizeLive, told);                       // the live recording is closed (and kept) through the one finalise
        Assert.Single(bus.ReadManaged<PauseTimeIntent>());                       // the clock is paused
    }

    /// <summary>
    /// CE-3345 success 2 - the same loss while IDLE is no error: only "not ready" (WaitingFor), the latch re-engaged, as CE-3344 built it (the survivors are still sent to Idle - there is nothing to keep).
    /// 🔴 Mutation: the error arm without the <c>HasWorldLoaded</c> test => an Idle cluster reports an error.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void E2_The_same_loss_while_idle_is_only_not_ready_and_no_error()
    {
        var bus = new FdpEventBus();
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.Idle);

        Assert.False(master.IsInError);
        Assert.Equal(string.Empty, master.ErrorReason);
        Assert.False(master.BootstrapComplete);
        var degraded = Assert.Single(StateUpdates(bus), e => e.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Degraded);
        Assert.True(string.IsNullOrEmpty(degraded.ErrorReason));
        Assert.Contains("Brain", degraded.WaitingFor);
        Assert.Empty(bus.ReadManaged<PauseTimeIntent>());
    }

    /// <summary>
    /// CE-3345 success 3 - the error is STICKY: the Brain's node coming back changes WaitingFor only, the cluster stays in error and refuses every request but the way back to Idle (Close);
    /// Close is accepted and, when its round completes, the error is cleared and the master says Idle - after which readiness behaves as CE-3344 built it.
    /// 🔴 Mutations: <c>CheckBootstrapLatch</c> without the <c>IsInError</c> return => the return releases the latch (a request is then ADMITTED during the error); <c>ClearClusterError</c> not called on
    /// the round's success => the error outlives Idle; <c>LatchRefuses</c> without the leave exemption => Close is Rejected.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void E3_The_error_is_sticky_a_returning_node_does_not_clear_it_reaching_Idle_does()
    {
        var bus = new FdpEventBus();
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.OperatingLive);
        Assert.True(master.IsInError);
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);                     // the Brain is back
        Assert.True(master.IsInError);                                           // ... and the error stays
        Assert.False(master.BootstrapComplete);
        Assert.DoesNotContain(StateUpdates(bus), e => e.CurrentState != Fdp.Toolkit.Orchestration.ClusterState.Degraded);   // nothing but Degraded is announced while in error
        Assert.Contains("Brain", master.ErrorReason);                           // the reason is the first loss, unchanged

        // every request but the way back to Idle is refused, with the error as the reason - the ONE not-ready gate
        var refused = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
            { RequestId = refused, OperationType = ClusterOpType.TransitionState, PayloadJson = "{\"TargetState\":\"OperatingEdit\",\"ScenarioId\":\"s1\"}" });
        Step(bus, master);
        Assert.Equal(OrchestrationStatusCode.Rejected, Assert.Single(StatusesOf(bus, refused)).StatusCode);

        // Close: a transition to Idle is admitted ...
        var close = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
            { RequestId = close, OperationType = ClusterOpType.TransitionState, PayloadJson = "{\"TargetState\":\"Idle\"}" });
        Step(bus, master);
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList();
        Assert.Contains(ops, i => i.Operation == FdpNodeOpType.FinalizeLive);   // the unload from the live run
        Assert.True(master.IsInError);                                           // ... but the error ends only when Idle is REACHED
        foreach (var op in ops.Where(o => o.TransactionId != Guid.Empty))
            bus.PublishManaged(new NodeOpCompletedEvent
                { TransactionId = op.TransactionId, Operation = op.Operation, NodeId = op.TargetNodeId, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
        Step(bus, master);

        Assert.False(master.IsInError);
        Assert.Equal(string.Empty, master.ErrorReason);
        var idle = StateUpdates(bus).Last();
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Idle, idle.CurrentState);
        Assert.True(string.IsNullOrEmpty(idle.ErrorReason));
    }

    /// <summary>
    /// CE-3345 success 3b - after Close the cluster is "not ready, waiting for: Brain" until the Brain is back (CE-3344's behaviour, no error); its return then releases the latch.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void E3b_After_Close_the_latch_reports_WaitingFor_as_before_until_the_node_returns()
    {
        var bus = new FdpEventBus();
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.OperatingLive);
        var close = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
            { RequestId = close, OperationType = ClusterOpType.TransitionState, PayloadJson = "{\"TargetState\":\"Idle\"}" });
        Step(bus, master);
        foreach (var op in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList())
            bus.PublishManaged(new NodeOpCompletedEvent
                { TransactionId = op.TransactionId, Operation = op.Operation, NodeId = op.TargetNodeId, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
        Step(bus, master);
        Assert.False(master.IsInError);
        Assert.False(master.BootstrapComplete);
        Assert.Contains("Brain", StateUpdates(bus).Last().WaitingFor);

        GateJoin(bus, master, CgfNode, NodeRoleTokens.Brain);
        Assert.True(master.BootstrapComplete);
        Assert.Contains(StateUpdates(bus), e => e.CurrentState == Fdp.Toolkit.Orchestration.ClusterState.Idle && string.IsNullOrEmpty(e.WaitingFor) && string.IsNullOrEmpty(e.ErrorReason));
    }

    /// <summary>
    /// CE-3345 success 4 - a node's ClusterView folds the error: <c>IsInError</c>, <c>CanOperate</c> false, <c>NotOperableReason</c> = the error (first, before the Degraded / waiting texts); cleared with Idle.
    /// 🔴 Mutation: <c>NotOperableReason</c> without the error line => the reason is the Degraded / waiting text.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void E4_The_view_cannot_operate_while_in_error_and_says_why()
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.OperatingLive);
        view.Update();

        Assert.True(view.IsInError);
        Assert.False(view.CanOperate);
        Assert.StartsWith("the cluster is in error", view.NotOperableReason);
        Assert.Contains("Brain", view.NotOperableReason);
        Assert.Equal(view.ErrorReason, master.ErrorReason);
    }

    /// <summary>
    /// CE-3345 - a scenario save is refused while in error (the world may be inconsistent), with the error as the reason; the control (no error, editing) saves.
    /// 🔴 Mutation: the <c>ProcessStorageOpIntent</c> error refusal removed => the save fans out.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void E5_A_scenario_save_is_refused_while_in_error()
    {
        var bus = new FdpEventBus();
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.OperatingEdit);
        Assert.True(master.IsInError);
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        var save = Guid.NewGuid();
        bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = save, Operation = StorageOpType.SaveScenario, ScenarioName = "x" });
        Step(bus, master);
        var refused = Assert.Single(StatusesOf(bus, save));
        Assert.Equal(OrchestrationStatusCode.Rejected, refused.StatusCode);
        Assert.Contains(master.ErrorReason, (string)refused.ResultPayload!);
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.SerializeLocal);
    }

    /// <summary>The error rides the wire: the master's own transitioned event carries it (what the egress translator writes to the topic).</summary>
    [Fact(Timeout = 10_000)]
    public void E6_The_transitioned_event_the_egress_writes_to_the_wire_carries_the_error()
    {
        var bus = new FdpEventBus();
        using var master = ClusterThatLosesItsBrain(bus, ClusterState.OperatingLive);
        var transitioned = bus.ReadManaged<ClusterStateTransitionedEvent>().Last();
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Degraded, transitioned.NewStateId);
        Assert.Equal(master.ErrorReason, transitioned.ErrorReason);
    }


    // -- P: THE READINESS PROTOCOL (CE-3345 part B, R-3070 / R-3072, AQ110 A-C) — each node reports its phase, the master combines the participants, a failed load ends in Idle ------------

    private static ClusterConfiguration NoRolesConfig() => new()
        { Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10, MandatoryRoles = NodeRole.None };

    /// <summary>A node's heartbeat carrying what it reports about ITSELF (phase / step / reason), and — once, when <paramref name="advert"/> is given — its capability advert (an empty array = a node with no role at all).</summary>
    private static void PhaseBeat(FdpEventBus bus, ClusterMaster master, int nodeId, NodePhase phase, string? step = null, string[]? advert = null, string name = "Node", string? nodeName = null)
    {
        if (advert != null) bus.PublishManaged(new NodeCapabilitiesEvent { NodeId = nodeId, Capabilities = advert });
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = nodeId, SubsystemName = name, NodeName = nodeName, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
            LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle, Phase = phase, Step = step,
        });
        Step(bus, master);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success 3 (R-3070, AQ110 B/C) — <b>cluster play-ready combines the PARTICIPANTS only</b> (in sync ∧ not an observer, K6): false in Idle, while any participant loads (with <c>LoadProgress</c> naming the node and its step), true when all report
    /// PlayReady; ⛔ a joiner outside the in-sync set that is still catching up does NOT hold it false, and neither does an observer that is still loading. What the master publishes is what every node's <c>ClusterView</c> reads.
    /// 🔴 Mutations (each compiles): <c>Readiness</c> without the <c>_inSync</c> test ⇒ the joiner cell reds; without <c>IsObserver</c> ⇒ the observer cell reds; <c>all</c> never cleared by a non-PlayReady phase ⇒ the "one is loading" cell reds;
    /// <c>PublishWaitingForIfChanged</c> not comparing the readiness ⇒ the first Loading cell reds (nothing is re-published).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void P1_ClusterPlayReady_CombinesTheParticipantsOnly_AJoinerOutsideTheInSyncSetAndAnObserverNeverHoldItFalse()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoRolesConfig());
        var view = new ClusterView(bus, nodeId: CgfNode);
        string[] brain = { NodeRoleTokens.Brain }, muscle = { NodeRoleTokens.MuscleGround };

        PhaseBeat(bus, master, CgfNode, NodePhase.Ready, advert: brain, name: "CGF");
        PhaseBeat(bus, master, SimHostNode, NodePhase.Ready, advert: muscle, name: "SimHost");
        // an OBSERVER (an advert with no simulation role) admitted at rest in Idle — IN SYNC, so only IsObserver keeps it out (below)
        PhaseBeat(bus, master, 600, NodePhase.Ready, advert: Array.Empty<string>(), name: "Observer");
        Assert.Contains(600, master.InSyncNodeIds);
        view.Update();
        Assert.False(view.ClusterPlayReady);                       // Idle: nobody has a world to play
        Assert.False(view.IsClusterLoading);

        PhaseBeat(bus, master, CgfNode, NodePhase.Loading, "Terrain", name: "CGF");
        view.Update();   // (the view folds the frame's broadcast: read it before the next Step swaps it away)
        PhaseBeat(bus, master, SimHostNode, NodePhase.PlayReady, name: "SimHost");
        view.Update();
        Assert.False(view.ClusterPlayReady);                       // one participant is still loading …
        Assert.Contains("CGF (node 400): Terrain", view.LoadProgress);   // … and the view says who and on what
        Assert.DoesNotContain("SimHost", view.LoadProgress);
        Assert.True(view.IsClusterLoading);

        PhaseBeat(bus, master, CgfNode, NodePhase.PlayReady, name: "CGF");
        view.Update();
        Assert.True(view.ClusterPlayReady);                        // all participants play
        Assert.Equal(string.Empty, view.LoadProgress);

        // a joiner that arrives while the cluster runs is NOT in the in-sync set (it is caught up first) — its loading holds nobody
        SetField(master, "_currentDsmState", ClusterState.OperatingLive);
        PhaseBeat(bus, master, 500, NodePhase.CatchingUp, "Terrain", advert: brain, name: "Joiner");
        Assert.DoesNotContain(500, master.InSyncNodeIds);
        view.Update();
        Assert.True(view.ClusterPlayReady);
        Assert.Equal(string.Empty, view.LoadProgress);

        // the in-sync OBSERVER that is still loading holds nobody either (⛔ CE-3345 O2: it used to join here, while live — outside the in-sync set, so
        //   the _inSync test excluded it and the IsObserver mutation survived)
        PhaseBeat(bus, master, 600, NodePhase.Loading, "KnowledgeBase", name: "Observer");
        Assert.Contains(600, master.InSyncNodeIds);
        view.Update();
        Assert.True(view.ClusterPlayReady);
        Assert.Equal(string.Empty, view.LoadProgress);

        // control (anti-vacuity): a PARTICIPANT's regression to loading does hold it
        PhaseBeat(bus, master, SimHostNode, NodePhase.Loading, "KnowledgeBase", name: "SimHost");
        view.Update();
        Assert.False(view.ClusterPlayReady);
        Assert.Contains("SimHost (node 1): KnowledgeBase", view.LoadProgress);
    }

    /// <summary>Two nodes, a load request with no scenario (nothing parked) fanned out; returns the request id and the transaction of the load round.</summary>
    private static (Guid Request, Guid Tx, List<ExecuteNodeOpIntent> Ops) StartLoadOnTwoNodes(FdpEventBus bus, ClusterMaster master)
    {
        PhaseBeat(bus, master, CgfNode, NodePhase.Ready, advert: new[] { NodeRoleTokens.Brain }, name: "CGF");
        PhaseBeat(bus, master, SimHostNode, NodePhase.Ready, advert: new[] { NodeRoleTokens.MuscleGround }, name: "SimHost");
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        var request = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent { TransactionId = request, TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive });
        Step(bus, master);
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        return (request, ops.First(i => i.Operation == FdpNodeOpType.PrepareLive).TransactionId, ops);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success 4 and 5 (R-3070; AQ91 §4-H) — a LOAD round that fails on ONE node — the user's cancel, or a missing terrain: the node's Failure ack carries a reason — fails AT ONCE (the other node's still-owed ACKs do not hold it), the
    /// request's status is Failure WITH that reason, every node is told to abort the transaction and to go to <b>Idle</b>, and when they answer, every node's <c>ClusterView</c> shows Idle and the reason on the request's outcome.
    /// 🔴 Mutations (each compiles): <c>CompleteRound</c> without <c>AbortAndReturnToIdle</c> ⇒ no abort / no Idle fan-out (the cluster stays at the target); <c>PublishOpStatus</c> without <c>FailureReason</c> ⇒ the reason cell reds;
    /// <c>tracker.Expected = tracker.Received</c> removed ⇒ the round waits for the other node (no Failure status yet); the master translator / <c>FailureReason ??=</c> capture removed ⇒ the reason cell reds.
    /// </summary>
    [Theory(Timeout = 10_000)]
    [InlineData("cancelled by the user on node 400")]
    [InlineData("terrain 'Alps' not found in the staged scenario")]
    public void P2_ALoadRoundThatFailsOnOneNode_FailsAtOnce_WithTheNodesReason_AndTheClusterGoesToIdle(string reason)
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoRolesConfig());
        var view = new ClusterView(bus, nodeId: SimHostNode);
        var (request, loadTx, ops) = StartLoadOnTwoNodes(bus, master);
        Assert.Equal(ClusterState.OperatingLive, master.CurrentClusterState);   // the optimistic advance: the master is already at the target

        // node 400 FAILS its prepare (carrying its reason); node 1 has not answered anything — it is still loading
        var failing = ops.First(i => i.TargetNodeId == CgfNode && i.Operation == FdpNodeOpType.PrepareLive);
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId = failing.TransactionId, Operation = failing.Operation, NodeId = CgfNode,
            StatusCode = OrchestrationStatusCode.Failure, IsParticipating = true, ResultPayload = reason,
        });
        Step(bus, master);
        view.Update();

        var status = Assert.Single(StatusesOf(bus, request));                    // the round failed NOW …
        Assert.Equal(OrchestrationStatusCode.Failure, status.StatusCode);
        Assert.Equal(reason, status.ResultPayload);                              // … with the node's own reason
        Assert.Equal(ClusterState.Idle, master.CurrentClusterState);             // and the master is no longer at the load's target

        var sent = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        foreach (var node in new[] { CgfNode, SimHostNode })
        {
            var abort = Assert.Single(sent, i => i.Operation == FdpNodeOpType.AbortTransaction && i.TargetNodeId == node);   // every node undoes the load …
            Assert.Equal(loadTx, Assert.IsType<AbortTransactionPayload>(abort.DomainPayload).TargetTransactionId);
            Assert.Contains(sent, i => i.Operation == FdpNodeOpType.PrepareState && i.TargetNodeId == node
                                    && i.DomainPayload is Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload { TargetState: Fdp.Toolkit.Orchestration.ClusterState.Idle });   // … and goes to Idle
        }

        // the nodes answer the Idle round: every node's view shows Idle, and the reason on the request's outcome
        foreach (var p in sent.Where(i => i.Operation == FdpNodeOpType.PrepareState))
            bus.PublishManaged(new NodeOpCompletedEvent { TransactionId = p.TransactionId, Operation = p.Operation, NodeId = p.TargetNodeId, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
        Step(bus, master);
        view.Update();
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Idle, view.State);
        Assert.Equal((OrchestrationStatusCode.Failure, reason), view.OutcomeOf(request)!.Value);
    }

    /// <summary>
    /// ⭐ <c>CE-3345</c> control for P2 — the return to Idle is for a failed LOAD only: a failing round to Idle itself ends there (no abort, no second Idle round — no loop), and the reason still reaches its status.
    /// 🔴 Mutation: <c>IsLoadRound</c> true for every transition ⇒ the failing Idle round fans out another abort + Idle.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void P3_AFailedRoundToIdle_IsNotLoopedBackToIdle()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoRolesConfig());
        PhaseBeat(bus, master, SimHostNode, NodePhase.Ready, advert: new[] { NodeRoleTokens.MuscleGround }, name: "SimHost");
        SetField(master, "_currentDsmState", ClusterState.OperatingLive);
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        var request = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent { TransactionId = request, TargetState = Fdp.Toolkit.Orchestration.ClusterState.Idle });
        Step(bus, master);
        var prepares = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList();
        var first = prepares.First();
        bus.PublishManaged(new NodeOpCompletedEvent
            { TransactionId = first.TransactionId, Operation = first.Operation, NodeId = SimHostNode, StatusCode = OrchestrationStatusCode.Failure, IsParticipating = true, ResultPayload = "boom" });
        Step(bus, master);

        var status = Assert.Single(StatusesOf(bus, request));
        Assert.Equal((OrchestrationStatusCode.Failure, "boom"), (status.StatusCode, (string?)status.ResultPayload));
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.AbortTransaction);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3354</c> ③ — the master holds the map-in-load set (case-insensitive, de-duplicated; null = no change, empty = none; a blank name or one with a comma is no node name), BROADCASTS it (every node's
    /// <c>ClusterView</c> reads it) and it survives a load; and a scenario load's PREPARE payload carries <c>MapInLoad = true</c> for the node named in the set ONLY — the other node gets the plain one, and CommitState is untouched.
    /// 🔴 Mutations (each compiles): <c>FanOutLoadPrepare</c> stamping every node / none ⇒ the payload cells red; the set matched case-sensitively ⇒ the "Film-Cam" cell reds; the de-duplication removed ⇒ the count cell reds;
    /// the set not on the broadcast ⇒ the view cells red; the set cleared by a load ⇒ the survives cell reds.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3354_MapInLoad_TheSetIsHeldAndBroadcast_AndOnlyTheNamedNodesLoadPayloadCarriesTheFlag()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoRolesConfig());
        var view = new ClusterView(bus, nodeId: CgfNode);
        PhaseBeat(bus, master, CgfNode, NodePhase.Ready, advert: new[] { NodeRoleTokens.Brain }, name: "CGF", nodeName: "film-cam");
        PhaseBeat(bus, master, SimHostNode, NodePhase.Ready, advert: new[] { NodeRoleTokens.MuscleGround }, name: "SimHost", nodeName: "sim-1");

        void Set(string[]? names) { bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), MapInLoadNodes = names }); Step(bus, master); view.Update(); }
        Set(new[] { "Film-Cam", "film-cam", " sim-9 ", "", "bad,name" });
        Assert.Equal(new[] { "Film-Cam", "sim-9" }, master.MapInLoadNodes);          // de-duplicated case-insensitively, junk dropped
        Assert.Equal(new[] { "Film-Cam", "sim-9" }, view.MapInLoadNodes);            // broadcast
        Set(null);
        Assert.Equal(2, master.MapInLoadNodes.Count);                                // null = no change

        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        bus.PublishManaged(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive });
        Step(bus, master);
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        bool Flag(int node) => Assert.IsType<Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload>(
            ops.Single(i => i.Operation == FdpNodeOpType.PrepareLive && i.TargetNodeId == node).DomainPayload).MapInLoad;
        Assert.True(Flag(CgfNode));                                                  // named (case-insensitively)
        Assert.False(Flag(SimHostNode));                                             // not named
        Assert.All(ops.Where(i => i.Operation == FdpNodeOpType.CommitState), i => Assert.IsType<CommitStatePayload>(i.DomainPayload));
        Assert.Equal(2, master.MapInLoadNodes.Count);                                // survives the load
        Set(Array.Empty<string>());
        Assert.Empty(view.MapInLoadNodes);                                           // empty = none
    }

    /// <summary>
    /// ⭐ <c>CE-3354</c> ② — the master keeps each node's NAME (an older sender's none ⇒ <c>&lt;subsystem&gt;-&lt;nodeId&gt;</c>), refuses a heartbeat whose name another LIVE node holds
    /// (the roster rows say it: the holder stays, the second is a conflict row, never a member), and a node re-heartbeating under its own name is fine.
    /// 🔴 Mutations (each compiles): the ingest not setting <c>NodeName</c> ⇒ the name cells red; <c>TryAdmit</c> replaced by <c>Upsert</c> ⇒ the second node is a member.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3354_TheMaster_KeepsTheName_AndRefusesASecondLiveNodeOfTheSameName()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoRolesConfig());
        void Beat(int id, string sub, string? name)
        {
            bus.PublishManaged(new NodeHeartbeatEvent { NodeId = id, SubsystemName = sub, NodeName = name, WallTicksUtc = DateTimeOffset.UtcNow.Ticks });
            Step(bus, master);
        }
        Beat(100, "IG", "film-cam");
        Beat(1, "SimHost", null);                  // an older sender: the default
        Beat(100, "IG", "film-cam");               // the holder again: fine
        Beat(101, "IG", "film-cam");               // a different node, the same name

        var rows = master.DescribeRoster();
        var members = rows.Where(r => r.NameConflict == null).ToDictionary(r => r.NodeId, r => r.Name);
        Assert.Equal(new[] { 1, 100 }, members.Keys.OrderBy(x => x).ToArray());
        Assert.Equal("film-cam", members[100]);
        Assert.Equal("SimHost-1", members[1]);
        var conflict = Assert.Single(rows, r => r.NameConflict != null);
        Assert.Equal((101, "film-cam"), (conflict.NodeId, conflict.Name));
        Assert.Contains("100", conflict.NameConflict);
    }
}
