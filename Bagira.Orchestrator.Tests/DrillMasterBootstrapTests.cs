using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;

namespace Bagira.Orchestrator.Tests;

// Domain 15 is reserved for orchestrator unit tests to avoid interfering with
// domain-0 tests in Bagira.SimHost.Integration.Tests and other assemblies.
[CollectionDefinition("OrchestratorTests", DisableParallelization = true)]
public class OrchestratorTestCollection { }

[Collection("OrchestratorTests")]
public sealed class DrillMasterBootstrapTests
{
    private const int TestDomain = 15;

    // ── CGF1-S0102 (BATCH-02) ─────────────────────────────────────────────────

    [Fact]
    public void OrchestratorPublishesStandbyOnStartup()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var reader = new DdsReader<SystemStateTopic>(participant);
        var received = new List<SystemStateTopic>();
        var deadline = DateTime.UtcNow.AddSeconds(3);

        using (var drill = new DrillMaster(participant))
        {
            while (DateTime.UtcNow < deadline)
            {
                drill.Tick();
                using (var scope = reader.Take())
                {
                    foreach (var sample in scope)
                    {
                        if (!sample.IsValid) continue;
                        received.Add(sample.Data);
                    }
                }

                if (received.Count >= 1) break;
                Thread.Sleep(20);
            }
        }

        Assert.True(received.Count > 0, "No SystemStateTopic sample within 3 s.");
        Assert.Equal(1, received.Count);
        Assert.Equal(DSMState.Standby, received[0].CurrentState);
        Assert.Equal(0, received[0].TransactionEpoch);
    }

    // ── CGF1-S0105 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that DrillMaster rejects SysOpRequests while mandatory nodes are not yet
    /// in Standby, then accepts them once the mandatory node publishes a Standby heartbeat.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void RejectsCommands_UntilMandatoryNodesReady()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,   // disable auto-eviction during this test
            TransactionHistoryCapacity = 10,
        };

        using var orchParticipant = new DdsParticipant(TestDomain);
        using var sysOpWriter     = new DdsWriter<SysOpRequest>(orchParticipant);
        using var sysOpReader     = new DdsReader<SysOpStatus>(orchParticipant);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(orchParticipant);

        using var drill = new DrillMaster(orchParticipant, config);

        // Allow DDS endpoint discovery to settle.
        Thread.Sleep(400);

        // ── Phase 1: Send request before SimHost heartbeat — expect Rejected ──
        var reqId1 = Guid.NewGuid();
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = reqId1,
            OperationType = SysOpType.PauseTime,
            PayloadJson   = string.Empty,
        });

        OpStatus? phase1Status = null;
        var deadline1 = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline1)
        {
            drill.Tick();
            using var scope = sysOpReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                if (s.Data.RequestId == reqId1)
                {
                    phase1Status = s.Data.Status;
                    break;
                }
            }
            if (phase1Status.HasValue) break;
            Thread.Sleep(20);
        }

        Assert.True(phase1Status.HasValue, "DrillMaster did not respond to SysOpRequest before bootstrap.");
        Assert.Equal(OpStatus.Rejected, phase1Status!.Value);
        Assert.False(drill.BootstrapComplete, "Bootstrap latch must not be set before mandatory heartbeat.");

        // ── Phase 2: Deliver SimHost heartbeat (Standby) → latch clears ──────
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });

        var latchDeadline = DateTime.UtcNow.AddSeconds(3);
        while (!drill.BootstrapComplete && DateTime.UtcNow < latchDeadline)
        {
            drill.Tick();
            Thread.Sleep(20);
        }

        Assert.True(drill.BootstrapComplete, "Bootstrap latch not cleared after mandatory node reached Standby.");

        // ── Phase 3: Next SysOpRequest should be accepted (InProgress/Success) ─
        var reqId2 = Guid.NewGuid();
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = reqId2,
            OperationType = SysOpType.PauseTime,
            PayloadJson   = string.Empty,
        });

        OpStatus? phase3Status = null;
        var deadline3 = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline3)
        {
            drill.Tick();
            using var scope = sysOpReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                if (s.Data.RequestId == reqId2)
                {
                    phase3Status = s.Data.Status;
                    break;
                }
            }
            if (phase3Status.HasValue) break;
            Thread.Sleep(20);
        }

        Assert.True(phase3Status.HasValue, "DrillMaster did not respond to accepted SysOpRequest.");
        Assert.NotEqual(OpStatus.Rejected, phase3Status!.Value);
    }

    /// <summary>
    /// Verifies that when a mandatory node's heartbeat times out, DrillMaster publishes
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

        using var orchParticipant = new DdsParticipant(TestDomain);
        using var stateReader     = new DdsReader<SystemStateTopic>(orchParticipant);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(orchParticipant);

        using var drill = new DrillMaster(orchParticipant, config);
        Thread.Sleep(400);

        // Bootstrap: publish SimHost heartbeat once, let latch clear.
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });

        var latchDeadline = DateTime.UtcNow.AddSeconds(3);
        while (!drill.BootstrapComplete && DateTime.UtcNow < latchDeadline)
        {
            drill.Tick();
            Thread.Sleep(20);
        }
        Assert.True(drill.BootstrapComplete, "Bootstrap should have cleared after SimHost Standby heartbeat.");

        // Drain any previously-published Standby sample.
        DrainStateReader(stateReader);

        // Now stop heartbeats: wait long enough for timeout (0.1 s) then tick.
        Thread.Sleep(200);

        DSMState? degradedState = null;
        var ejectionDeadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < ejectionDeadline)
        {
            drill.Tick();
            using var scope = stateReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                if (s.Data.CurrentState == DSMState.Degraded)
                {
                    degradedState = s.Data.CurrentState;
                    break;
                }
            }
            if (degradedState.HasValue) break;
            Thread.Sleep(20);
        }

        Assert.True(degradedState.HasValue,
            "DrillMaster did not publish Degraded after mandatory node timed out.");
        Assert.Equal(DSMState.Degraded, degradedState!.Value);
        Assert.False(drill.BootstrapComplete,
            "Bootstrap latch should re-engage after mandatory node ejection.");
    }

    /// <summary>
    /// Verifies that after a mandatory node is ejected, all surviving nodes receive
    /// a <c>PrepareState(Standby)</c> command, and the ejected node is removed from the roster.
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

        using var orchParticipant = new DdsParticipant(TestDomain);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(orchParticipant);
        using var cmdReader       = new DdsReader<NodeOpCommand>(orchParticipant);

        using var drill = new DrillMaster(orchParticipant, config);
        Thread.Sleep(400);

        // Bootstrap: publish both SimHost and CGF as Standby.
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 400,
            SubsystemName = "CGF",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });

        var latchDeadline = DateTime.UtcNow.AddSeconds(3);
        while (!drill.BootstrapComplete && DateTime.UtcNow < latchDeadline)
        {
            drill.Tick();
            Thread.Sleep(20);
        }
        Assert.True(drill.BootstrapComplete, "Both nodes bootstrapped.");
        Assert.Equal(2, drill.NodeRoster.ActiveNodes.Count);

        // Drain stale command samples before ejection.
        DrainCmdReader(cmdReader);

        // Stop SimHost heartbeats; wait for timeout, then trigger ejection via Tick.
        Thread.Sleep(200);

        var cmds = new List<NodeOpCommand>();
        var ejectionDeadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < ejectionDeadline)
        {
            drill.Tick();
            using var scope = cmdReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                cmds.Add(s.Data);
            }
            if (!drill.NodeRoster.ActiveNodes.ContainsKey(1)) break;
            Thread.Sleep(20);
        }

        // Give any in-flight broadcast samples a moment to arrive, then do a final drain.
        Thread.Sleep(50);
        using (var finalScope = cmdReader.Take())
        {
            foreach (var s in finalScope)
            {
                if (!s.IsValid) continue;
                cmds.Add(s.Data);
            }
        }

        // SimHost (nodeId 1) should be removed from the roster.
        Assert.False(drill.NodeRoster.ActiveNodes.ContainsKey(1),
            "SimHost must be removed from roster after ejection.");
        Assert.True(drill.NodeRoster.ActiveNodes.ContainsKey(400),
            "CGF must remain in roster as a surviving node.");

        // The broadcast should include AbortTransaction and PrepareState(Standby).
        Assert.Contains(cmds, c => c.Operation == NodeOpType.AbortTransaction);
        Assert.Contains(cmds, c => c.Operation == NodeOpType.PrepareState);
    }

    /// <summary>
    /// Verifies that a completed (accepted) SysOpRequest is recorded in the
    /// <see cref="DrillMaster.TransactionHistory"/> ring buffer with <c>IsAborted == false</c>.
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

        using var orchParticipant = new DdsParticipant(TestDomain);
        using var sysOpWriter     = new DdsWriter<SysOpRequest>(orchParticipant);

        using var drill = new DrillMaster(orchParticipant, config);
        Thread.Sleep(400);

        Assert.True(drill.BootstrapComplete, "With empty mandatory list the latch should clear immediately.");

        var reqId = Guid.NewGuid();
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = reqId,
            OperationType = SysOpType.PauseTime,
            PayloadJson   = string.Empty,
        });

        // Tick until the request is processed and history contains the entry.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            drill.Tick();
            if (drill.TransactionHistory.Count > 0) break;
            Thread.Sleep(20);
        }

        var history = drill.TransactionHistory;
        Assert.True(history.Count >= 1, "Expected at least one transaction in history.");

        var tx = history[history.Count - 1];
        Assert.False(tx.IsAborted,
            "Transaction from accepted SysOpRequest must not be marked as aborted.");
        Assert.Equal(reqId, tx.OriginRequestId);
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    private static void DrainStateReader(DdsReader<SystemStateTopic> reader)
    {
        Thread.Sleep(50);
        using var scope = reader.Take();
        // intentionally discard
    }

    private static void DrainCmdReader(DdsReader<NodeOpCommand> reader)
    {
        Thread.Sleep(50);
        using var scope = reader.Take();
        // intentionally discard
    }
}

