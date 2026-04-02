using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Integration tests for the ExportArchive / ImportArchive / CancelOperation
/// branches in <see cref="ClusterMaster"/> (CGF1-S0505 success conditions).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterArchiveTests
{
    private const int TestDomain = 15;

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    // ── Reflection helper to read _activeCancellations ────────────────────────

    private static Dictionary<Guid, CancellationTokenSource> GetActiveCancellations(ClusterMaster dm)
    {
        var field = typeof(ClusterMaster).GetField(
            "_activeCancellations",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Dictionary<Guid, CancellationTokenSource>)field.GetValue(dm)!;
    }

    // ── CGF1-S0505 Success Condition 4: CancelOperation kills CTS ────────────

    /// <summary>
    /// Posting an <see cref="ClusterOpType.ExportArchive"/> request followed by a
    /// <see cref="ClusterOpType.CancelOperation"/> referencing the same RequestId must
    /// cancel the <see cref="CancellationTokenSource"/> that was registered for the
    /// export operation.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CancelOperation_CancelsActiveCts()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(300);  // DDS discovery

        // Register a fake node so FanOutSerializeLocal actually queues an ACK
        // (otherwise the 0-node path completes synchronously and removes the CTS).
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "TestNode",
            LocalClusterState = ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(100);
        exercise.Tick();  // ingest heartbeat into roster

        var exportRequestId = Guid.NewGuid();

        // 1. Post ExportArchive — creates CTS in _activeCancellations.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = exportRequestId,
            OperationType = ClusterOpType.ExportArchive,
            PayloadJson   = "{\"ExerciseId\":\"exercise_cancel_test\"}",
        });
        exercise.Tick();  // drain injected requests

        // 2. Capture the CTS before CancelOperation removes it.
        var activeCancellations = GetActiveCancellations(exercise);
        Assert.True(activeCancellations.TryGetValue(exportRequestId, out var cts),
            "ExportArchive must register a CTS in _activeCancellations.");
        Assert.False(cts!.IsCancellationRequested,
            "CTS must NOT be cancelled before CancelOperation is sent.");

        // 3. Post CancelOperation referencing the export's RequestId.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.CancelOperation,
            PayloadJson   = exportRequestId.ToString(),
        });
        exercise.Tick();  // drain injected requests

        // 4. The CTS must now be in the cancelled state.
        Assert.True(cts.IsCancellationRequested,
            "CancelOperation must cancel the CTS registered for the ExportArchive operation.");
    }

    /// <summary>
    /// An <see cref="ClusterOpType.ExportArchive"/> request with a missing ExerciseId must
    /// be rejected immediately (no CTS added to _activeCancellations).
    /// </summary>
    [Fact]
    public void ExportArchive_MissingExerciseId_IsRejected()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.ExportArchive,
            PayloadJson   = "{\"WrongKey\":\"something\"}",
        });
        exercise.Tick();

        // No CTS should be registered for a rejected request.
        var activeCancellations = GetActiveCancellations(exercise);
        Assert.False(activeCancellations.ContainsKey(reqId),
            "A rejected ExportArchive must not register a CTS.");
    }

    /// <summary>
    /// An <see cref="ClusterOpType.ImportArchive"/> request with a missing ExerciseId must
    /// be rejected (no CTS added to _activeCancellations).
    /// </summary>
    [Fact]
    public void ImportArchive_MissingExerciseId_IsRejected()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.ImportArchive,
            PayloadJson   = "{\"NoId\":true}",
        });
        exercise.Tick();

        var activeCancellations = GetActiveCancellations(exercise);
        Assert.False(activeCancellations.ContainsKey(reqId),
            "A rejected ImportArchive must not register a CTS.");
    }

    /// <summary>
    /// <see cref="ClusterOpType.CancelOperation"/> with an unknown target ID must not throw.
    /// </summary>
    [Fact]
    public void CancelOperation_UnknownTargetId_DoesNotThrow()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(200);

        var ex = Record.Exception(() =>
        {
            exercise.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.CancelOperation,
                PayloadJson   = Guid.NewGuid().ToString(),
            });
            exercise.Tick();
        });

        Assert.Null(ex);
    }
}
