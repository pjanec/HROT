using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Hrot.Common.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;

namespace Hrot.SimHost.Integration.Tests;

/// <summary>
/// DDS integration tests for <see cref="DdsOrchestrationTransport"/> delivering commands
/// to the FDP toolkit <see cref="ClusterSlave"/> — CGF1-G0402 success condition 4.
/// </summary>
public sealed class DdsOrchestrationTransportTests
{
    // ── Stub handler ──────────────────────────────────────────────────────────

    private sealed class StubHandler : IClusterStateHandler
    {
        private readonly int _operationId;
        public int PrepareCallCount;
        public int CommitCallCount;

        public StubHandler(int operationId)
        {
            _operationId = operationId;
        }

        public bool CanHandle(int operationId) => operationId == _operationId;

        public System.Threading.Tasks.Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            PrepareCallCount++;
            return System.Threading.Tasks.Task.FromResult<string?>(null);
        }

        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            CommitCallCount++;
        }

        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }
    }

    // ── CGF1-G0402 success condition 4 ───────────────────────────────────────

    /// <summary>
    /// Fact: DdsOrchestrationTransport delivers commands to ClusterSlave.
    /// Sends a NodeOpCommand over DDS and verifies the toolkit ClusterSlave
    /// dispatches it to a registered handler within 2 s.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void DdsTransport_DeliversCommand_ToClusterSlave()
    {
        // Domain 17 reserved for this test.
        const int TestDomain = 17;
        const int nodeId     = 42;
        const int opId       = 5; // arbitrary non-CommitState

        using var participant      = new DdsParticipant(TestDomain);
        using var commandPublisher = new DdsWriter<NodeOpCommand>(participant);
        using var transport        = new DdsOrchestrationTransport(participant, nodeId);

        var handler = new StubHandler(opId);
        using var slave = new ClusterSlave(transport, nodeId, "TestSubsystem");
        slave.RegisterHandler(handler);

        Thread.Sleep(200); // DDS discovery

        commandPublisher.Write(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = nodeId,
            Operation     = (NodeOpType)opId,
            PayloadJson   = "{}",
        });

        // Poll until handler receives the command (up to 2 s).
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && handler.CommitCallCount == 0)
        {
            slave.Tick();
            Thread.Sleep(20);
        }

        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(1, handler.CommitCallCount);
    }
}
