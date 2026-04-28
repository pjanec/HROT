using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
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

        // Arrange: staging dir for AssetPrefetchProcessManager.
        var stagingDir = Path.Combine(Path.GetTempPath(), "fdp_pf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var bus = new FdpEventBus();
        var config = new ClusterConfiguration
        {
            Mandatory = System.Array.Empty<string>(),
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };
        using var exercise = new ClusterMaster(bus, config);

        var gateway = new StorageGatewayModule();
        var assetPrefetchPM = new AssetPrefetchProcessManager(bus, gateway, _nasDir, stagingDir);

        // Register a fake node so FanOutNodeOp has a target to write PrefetchFiles to.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = 1, SubsystemName = "TestNode",
            LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();     // ingest heartbeat into roster
        bus.SwapBuffers();

        // Issue a TransitionState(LoadingEdit) with a ScenarioId to trigger prefetch.
        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.LoadingEdit}\",\"ScenarioId\":\"{_scenarioId}\"}}",
        });

        // First tick: exercise publishes ExecutePrefetchIntent to WRITE buffer.
        assetPrefetchPM.Tick();
        exercise.Tick();
        bus.SwapBuffers();

        // Second tick: assetPrefetchPM reads ExecutePrefetchIntent, starts gateway task.
        assetPrefetchPM.Tick();
        exercise.Tick();
        bus.SwapBuffers();

        // Spin until PrefetchFiles fan-out arrives (gateway completes async).
        bool prefetchFilesReceived = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!prefetchFilesReceived && DateTime.UtcNow < deadline)
        {
            assetPrefetchPM.Tick();
            exercise.Tick();
            bus.SwapBuffers();
            prefetchFilesReceived = bus.ReadManaged<ExecuteNodeOpIntent>()
                .Any(i => i.Operation == FdpNodeOpType.PrefetchFiles);
            Thread.Sleep(10);
        }

        try { Directory.Delete(stagingDir, recursive: true); } catch { }

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

        // Arrange: staging dir for AssetPrefetchProcessManager.
        var stagingDir = Path.Combine(Path.GetTempPath(), "fdp_pf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var bus = new FdpEventBus();
        var config = new ClusterConfiguration
        {
            Mandatory = System.Array.Empty<string>(),
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };
        using var exercise = new ClusterMaster(bus, config);

        var gateway = new StorageGatewayModule();
        var assetPrefetchPM = new AssetPrefetchProcessManager(bus, gateway, _nasDir, stagingDir);

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.LoadingEdit}\",\"ScenarioId\":\"{missingScenarioId}\"}}",
        });

        // Spin until the failure status arrives.
        bool observedFailure = false;
        bool prefetchFilesReceived = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !observedFailure)
        {
            assetPrefetchPM.Tick();
            exercise.Tick();
            bus.SwapBuffers();
            Thread.Sleep(15);

            foreach (var ev in bus.ReadManaged<ClusterOpCompletedEvent>())
            {
                if (ev.RequestId == reqId && ev.StatusCode.IsError())
                    observedFailure = true;
            }

            foreach (var intent in bus.ReadManaged<ExecuteNodeOpIntent>())
            {
                if (intent.Operation == FdpNodeOpType.PrefetchFiles)
                    prefetchFilesReceived = true;
            }
        }

        try { Directory.Delete(stagingDir, recursive: true); } catch { }

        Assert.True(observedFailure,
            $"Expected a failure ClusterOpCompletedEvent but none arrived.");
        Assert.False(prefetchFilesReceived,
            "PrefetchFiles command must NOT be sent when the NAS source directory is missing.");
    }
}
