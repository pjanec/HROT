using System;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for ClusterMaster time-control request handling (CGF1-S0503-B).
/// Time-control ops bypass 2PC and publish typed intents to the bus (HEXAG2-S011).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterTimeControlTests
{

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// A <see cref="ClusterOpType.PauseTime"/> request must bypass the 2PC history:
    /// <see cref="ClusterMaster.TransactionHistory"/> must remain empty because the
    /// time-control early-return path skips transaction creation.
    /// </summary>
    [Fact]
    public void TimeControlRequested_BypassesTransactionHistory()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.PauseTime,
            PayloadJson   = string.Empty,
        });
        exercise.Tick();  // drain injected requests

        Assert.Equal(0, exercise.TransactionHistory.Count);
    }
}
