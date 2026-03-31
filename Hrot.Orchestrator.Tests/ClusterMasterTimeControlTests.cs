using System;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for the <see cref="ClusterMaster.TimeControlRequested"/> event (CGF1-S0503-B).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterTimeControlTests
{
    private const int TestDomain = 15;

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
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

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
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

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
