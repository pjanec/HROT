using System;
using Fdp.Kernel;
using Hrot.NED.Descriptors.Orchestration;
using FDP.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for the <see cref="ClusterMaster.TimeControlRequested"/> event (CGF1-S0503-B).
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
    /// A <see cref="ClusterOpType.PauseTime"/> request must raise
    /// <see cref="ClusterMaster.TimeControlRequested"/> exactly once with
    /// <see cref="ClusterOpType.PauseTime"/> as the operation argument.
    /// </summary>
    [Fact]
    public void TimeControlRequested_FiresOnPauseTime()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        int fireCount = 0;
        ClusterOpType? capturedOp = null;

        exercise.TimeControlRequested += (op, _) =>
        {
            fireCount++;
            capturedOp = op;
        };

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.PauseTime,
            PayloadJson   = string.Empty,
        });
        exercise.Tick();  // drain injected requests

        Assert.Equal(1, fireCount);
        Assert.Equal(ClusterOpType.PauseTime, capturedOp);
    }

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
