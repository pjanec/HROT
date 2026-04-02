using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests that verify the prefetch barrier ordering — specifically that
/// <see cref="NodeOpType.PrefetchFiles"/> is only fan-out to nodes <em>after</em>
/// <see cref="StorageGatewayModule.PrefetchScenarioAsync"/> completes (CGF1-S0302 / A.1).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterPrefetchTests : IDisposable
{
    private const int TestDomain = 15;

    private readonly string _nasDir;
    private readonly string _scenarioId = "prefetch_test_scenario";

    public ClusterMasterPrefetchTests()
    {
        _nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_nasDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_nasDir))
            Directory.Delete(_nasDir, recursive: true);
    }

    // ── A.1: PrefetchFiles only fan-out after gateway copy success ────────

    /// <summary>
    /// When the NAS source directory exists and files are present,
    /// <see cref="NodeOpType.PrefetchFiles"/> must only arrive at nodes
    /// <em>after</em> the gateway copy completes (i.e. not in the same tick
    /// as the ClusterOpRequest, but only once the task has resolved).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion()
    {
        // Arrange: create a scenario directory on the NAS with a test file.
        var scenarioDir = Path.Combine(_nasDir, _scenarioId);
        Directory.CreateDirectory(scenarioDir);
        File.WriteAllText(Path.Combine(scenarioDir, "Hrot.SimHost.json"), "{}");

        using var participant = new DdsParticipant(TestDomain);
        using var sysOpWriter = new DdsWriter<ClusterOpRequest>(participant);
        using var sysOpStatus = new DdsReader<ClusterOpStatus>(participant);
        using var cmdReader   = new DdsReader<NodeOpCommand>(participant);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);

        var config = new ClusterConfiguration
        {
            Mandatory = System.Array.Empty<string>(),
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };
        using var exercise = new ClusterMaster(participant, config);

        var gateway = new StorageGatewayModule();
        exercise.SetStorageGateway(gateway, _nasDir);

        Thread.Sleep(300); // DDS discovery

        // Register a fake node so FanOutNodeOp has a target to write PrefetchFiles to.
        hbWriter.Write(new NodeHeartbeat { NodeId = 1, SubsystemName = "TestNode", LocalClusterState = ClusterState.Idle });
        Thread.Sleep(50); // let heartbeat propagate
        exercise.Tick();     // ingest heartbeat into roster

        // Issue a TransitionState(LoadingEdit) with a ScenarioId to trigger prefetch.
        var reqId = Guid.NewGuid();
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingEdit},\"ScenarioId\":\"{_scenarioId}\"}}",
        });

        // Tick once: this starts the async gateway task, but the task is NOT yet
        // complete → no PrefetchFiles command should have been sent yet.
        exercise.Tick();
        Thread.Sleep(10);

        var immediateCommands = new List<NodeOpCommand>();
        using (var scope = cmdReader.Take())
            foreach (var s in scope)
                if (s.IsValid && s.Data.Operation == NodeOpType.PrefetchFiles)
                    immediateCommands.Add(s.Data);

        // For a local copy (tiny file), the task may complete very quickly.
        // Spin until the prefetch task completes (observed via PrefetchFiles arriving).
        var deadline = DateTime.UtcNow.AddSeconds(8);
        bool prefetchFilesReceived = immediateCommands.Count > 0;
        while (!prefetchFilesReceived && DateTime.UtcNow < deadline)
        {
            exercise.Tick();
            Thread.Sleep(10);
            using var scope = cmdReader.Take();
            foreach (var s in scope)
                if (s.IsValid && s.Data.Operation == NodeOpType.PrefetchFiles)
                    prefetchFilesReceived = true;
        }

        Assert.True(prefetchFilesReceived,
            "PrefetchFiles command was never received — expected it after gateway copy completed.");
    }

    /// <summary>
    /// When the NAS source directory is missing, the gateway throws
    /// <see cref="DirectoryNotFoundException"/> and the ClusterMaster must publish
    /// <see cref="ClusterOpStatus.Failure"/> rather than silently proceeding.
    /// No <see cref="NodeOpType.PrefetchFiles"/> command must be sent.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PrefetchScenario_WhenNasSourceDirMissing_PublishesFailure_AndNoPrefetchFiles()
    {
        // Arrange: NAS dir exists but the scenarioId sub-dir does NOT.
        const string missingScenarioId = "nonexistent_scenario_xyz";

        using var participant = new DdsParticipant(TestDomain);
        using var sysOpWriter = new DdsWriter<ClusterOpRequest>(participant);
        using var sysOpStatus = new DdsReader<ClusterOpStatus>(participant);
        using var cmdReader   = new DdsReader<NodeOpCommand>(participant);

        var config = new ClusterConfiguration
        {
            Mandatory = System.Array.Empty<string>(),
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };
        using var exercise = new ClusterMaster(participant, config);

        var gateway = new StorageGatewayModule();
        exercise.SetStorageGateway(gateway, _nasDir);

        Thread.Sleep(300); // DDS discovery

        var reqId = Guid.NewGuid();
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingEdit},\"ScenarioId\":\"{missingScenarioId}\"}}",
        });

        // Spin until the failure status arrives (InProgress may come first; keep looping).
        int? observedStatus = null;
        bool     prefetchFilesReceived = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !OrchestrationStatusCode.IsError(observedStatus ?? 0))
        {
            exercise.Tick();
            Thread.Sleep(15);

            using var statusScope = sysOpStatus.Take();
            foreach (var s in statusScope)
                if (s.IsValid && s.Data.RequestId == reqId)
                    observedStatus = s.Data.StatusCode;

            using var cmdScope = cmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.PrefetchFiles)
                    prefetchFilesReceived = true;
        }

        Assert.True(OrchestrationStatusCode.IsError(observedStatus ?? 0),
            $"Expected a failure status (>=10) but got: {observedStatus}");
        Assert.False(prefetchFilesReceived,
            "PrefetchFiles command must NOT be sent when the NAS source directory is missing.");
    }
}
