using System;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Bagira.Orchestrator.Tests;

/// <summary>
/// Tests for the <see cref="DrillMaster.TimeControlRequested"/> event (CGF1-S0503-B).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class DrillMasterTimeControlTests
{
    private const int TestDomain = 15;

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// A <see cref="SysOpType.PauseTime"/> request must raise
    /// <see cref="DrillMaster.TimeControlRequested"/> exactly once with
    /// <see cref="SysOpType.PauseTime"/> as the operation argument.
    /// </summary>
    [Fact]
    public void TimeControlRequested_FiresOnPauseTime()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        int fireCount = 0;
        SysOpType? capturedOp = null;

        drill.TimeControlRequested += (op, _) =>
        {
            fireCount++;
            capturedOp = op;
        };

        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.PauseTime,
            PayloadJson   = string.Empty,
        });
        drill.Tick();  // drain injected requests

        Assert.Equal(1, fireCount);
        Assert.Equal(SysOpType.PauseTime, capturedOp);
    }

    /// <summary>
    /// A <see cref="SysOpType.PauseTime"/> request must bypass the 2PC history:
    /// <see cref="DrillMaster.TransactionHistory"/> must remain empty because the
    /// time-control early-return path skips transaction creation.
    /// </summary>
    [Fact]
    public void TimeControlRequested_BypassesTransactionHistory()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.PauseTime,
            PayloadJson   = string.Empty,
        });
        drill.Tick();  // drain injected requests

        Assert.Equal(0, drill.TransactionHistory.Count);
    }
}
