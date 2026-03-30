using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Bagira.Orchestrator.Tests;

/// <summary>
/// Integration tests for the ExportArchive / ImportArchive / CancelOperation
/// branches in <see cref="DrillMaster"/> (CGF1-S0505 success conditions).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class DrillMasterArchiveTests
{
    private const int TestDomain = 15;

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    // ── Reflection helper to read _activeCancellations ────────────────────────

    private static Dictionary<Guid, CancellationTokenSource> GetActiveCancellations(DrillMaster dm)
    {
        var field = typeof(DrillMaster).GetField(
            "_activeCancellations",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Dictionary<Guid, CancellationTokenSource>)field.GetValue(dm)!;
    }

    // ── CGF1-S0505 Success Condition 4: CancelOperation kills CTS ────────────

    /// <summary>
    /// Posting an <see cref="SysOpType.ExportArchive"/> request followed by a
    /// <see cref="SysOpType.CancelOperation"/> referencing the same RequestId must
    /// cancel the <see cref="CancellationTokenSource"/> that was registered for the
    /// export operation.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CancelOperation_CancelsActiveCts()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(300);  // DDS discovery

        // Register a fake node so FanOutSerializeLocal actually queues an ACK
        // (otherwise the 0-node path completes synchronously and removes the CTS).
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "TestNode",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(100);
        drill.Tick();  // ingest heartbeat into roster

        var exportRequestId = Guid.NewGuid();

        // 1. Post ExportArchive — creates CTS in _activeCancellations.
        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = exportRequestId,
            OperationType = SysOpType.ExportArchive,
            PayloadJson   = "{\"DrillId\":\"drill_cancel_test\"}",
        });
        drill.Tick();  // drain injected requests

        // 2. Capture the CTS before CancelOperation removes it.
        var activeCancellations = GetActiveCancellations(drill);
        Assert.True(activeCancellations.TryGetValue(exportRequestId, out var cts),
            "ExportArchive must register a CTS in _activeCancellations.");
        Assert.False(cts!.IsCancellationRequested,
            "CTS must NOT be cancelled before CancelOperation is sent.");

        // 3. Post CancelOperation referencing the export's RequestId.
        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.CancelOperation,
            PayloadJson   = exportRequestId.ToString(),
        });
        drill.Tick();  // drain injected requests

        // 4. The CTS must now be in the cancelled state.
        Assert.True(cts.IsCancellationRequested,
            "CancelOperation must cancel the CTS registered for the ExportArchive operation.");
    }

    /// <summary>
    /// An <see cref="SysOpType.ExportArchive"/> request with a missing DrillId must
    /// be rejected immediately (no CTS added to _activeCancellations).
    /// </summary>
    [Fact]
    public void ExportArchive_MissingDrillId_IsRejected()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        var reqId = Guid.NewGuid();
        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = reqId,
            OperationType = SysOpType.ExportArchive,
            PayloadJson   = "{\"WrongKey\":\"something\"}",
        });
        drill.Tick();

        // No CTS should be registered for a rejected request.
        var activeCancellations = GetActiveCancellations(drill);
        Assert.False(activeCancellations.ContainsKey(reqId),
            "A rejected ExportArchive must not register a CTS.");
    }

    /// <summary>
    /// An <see cref="SysOpType.ImportArchive"/> request with a missing DrillId must
    /// be rejected (no CTS added to _activeCancellations).
    /// </summary>
    [Fact]
    public void ImportArchive_MissingDrillId_IsRejected()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        var reqId = Guid.NewGuid();
        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = reqId,
            OperationType = SysOpType.ImportArchive,
            PayloadJson   = "{\"NoId\":true}",
        });
        drill.Tick();

        var activeCancellations = GetActiveCancellations(drill);
        Assert.False(activeCancellations.ContainsKey(reqId),
            "A rejected ImportArchive must not register a CTS.");
    }

    /// <summary>
    /// <see cref="SysOpType.CancelOperation"/> with an unknown target ID must not throw.
    /// </summary>
    [Fact]
    public void CancelOperation_UnknownTargetId_DoesNotThrow()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        var ex = Record.Exception(() =>
        {
            drill.HandleSysOpRequest(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.CancelOperation,
                PayloadJson   = Guid.NewGuid().ToString(),
            });
            drill.Tick();
        });

        Assert.Null(ex);
    }
}
