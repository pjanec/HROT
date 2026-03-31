using System;
using System.IO;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for the ClusterMaster local <see cref="GlobalContextClusterOpHandler"/> invocation.
/// Verifies that <see cref="GlobalContextClusterOpHandler.OnContextLoaded"/> fires when
/// a trajectory passes through <see cref="ClusterState.LoadingLive"/> or
/// <see cref="ClusterState.LoadingEdit"/>, and that it does NOT fire for other transitions.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterContextHandlerTests : IDisposable
{
    private const int TestDomain = 15;

    private readonly string _tempDir;

    public ClusterMasterContextHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fdp_cm_ctx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    private static void RegisterNode(DdsWriter<NodeHeartbeat> hbWriter, ClusterMaster exercise, int nodeId = 1)
    {
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId            = nodeId,
            SubsystemName     = "SimHost",
            LocalClusterState = ClusterState.Idle,
            WallTicksUtc      = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick();
    }

    /// <summary>
    /// Creates temp dirs: {_tempDir}/{scenarioId}/Orchestrator.json for the handler and
    /// {_tempDir}/nas/{scenarioId}/placeholder.json for the gateway NAS source.
    /// </summary>
    private void SetupScenarioFiles(string scenarioId, long wallTicks = 12_345L, double simTime = 5.0)
    {
        var localDir = Path.Combine(_tempDir, scenarioId);
        Directory.CreateDirectory(localDir);
        var ctxDto = new GlobalContextDto
        {
            StartWallTicks      = wallTicks,
            SceneId             = "scene_" + scenarioId,
            ScenarioId          = scenarioId,
            ScenarioTimeSeconds = simTime,
            SchemaVersion       = 2,
        };
        File.WriteAllText(
            Path.Combine(localDir, "Orchestrator.json"),
            System.Text.Json.JsonSerializer.Serialize(ctxDto));

        // Minimal NAS source so PrefetchScenarioAsync does not throw.
        var nasDir = Path.Combine(_tempDir, "nas", scenarioId);
        Directory.CreateDirectory(nasDir);
        File.WriteAllText(Path.Combine(nasDir, "placeholder.json"), "{}");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a TransitionState(LoadingLive) request with a ScenarioId is processed,
    /// ClusterMaster must invoke the local handler whose OnContextLoaded fires
    /// with the saved scenario timeline.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_LoadingLive_InvokesLocalContextHandler()
    {
        const string scenarioId = "ctx_ll_01";
        SetupScenarioFiles(scenarioId, wallTicks: 99_000L);

        using var participant = new DdsParticipant(TestDomain);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);
        using var exercise    = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        var gateway = new StorageGatewayModule();
        exercise.SetStorageGateway(gateway, Path.Combine(_tempDir, "nas"));

        var handler = new GlobalContextClusterOpHandler(participant, string.Empty);
        handler.LocalTempRoot = _tempDir;

        bool eventFired    = false;
        long capturedTicks = 0;
        handler.OnContextLoaded += (ticks, _) => { capturedTicks = ticks; eventFired = true; };
        exercise.SetGlobalContextHandler(handler);

        RegisterNode(hbWriter, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingLive},\"ScenarioId\":\"{scenarioId}\"}}",
        });
        exercise.Tick();

        Assert.True(eventFired,
            "ClusterMaster must invoke the local GlobalContextClusterOpHandler " +
            "during LoadingLive CommitState, firing OnContextLoaded.");
        Assert.Equal(99_000L, capturedTicks);
    }

    /// <summary>
    /// Requesting OperatingLive as the final target (the corrected UI behaviour)
    /// must still fire OnContextLoaded for the intermediate LoadingLive step.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_OperatingLive_InvokesLocalContextHandlerForLoadingLiveStep()
    {
        const string scenarioId = "ctx_ol_02";
        SetupScenarioFiles(scenarioId, wallTicks: 77_000L);

        using var participant = new DdsParticipant(TestDomain);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);
        using var exercise    = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        var gateway = new StorageGatewayModule();
        exercise.SetStorageGateway(gateway, Path.Combine(_tempDir, "nas"));

        var handler = new GlobalContextClusterOpHandler(participant, string.Empty);
        handler.LocalTempRoot = _tempDir;

        bool eventFired = false;
        handler.OnContextLoaded += (_, _) => eventFired = true;
        exercise.SetGlobalContextHandler(handler);

        RegisterNode(hbWriter, exercise);

        // Request FINAL state OperatingLive — planner: PrefetchScenario -> LoadingLive -> OperatingLive.
        // The handler must still be invoked for the LoadingLive step.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.OperatingLive},\"ScenarioId\":\"{scenarioId}\"}}",
        });
        exercise.Tick();

        Assert.True(eventFired,
            "ClusterMaster must invoke the local handler during the LoadingLive step even " +
            "when OperatingLive is the requested final target.");
    }

    /// <summary>
    /// When no GlobalContextClusterOpHandler is registered, a TransitionState(LoadingLive)
    /// must complete without throwing.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_LoadingLive_WithoutContextHandler_DoesNotThrow()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);
        using var exercise    = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, exercise);

        var ex = Record.Exception(() =>
        {
            exercise.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
            });
            exercise.Tick();
        });

        Assert.Null(ex);
    }

    /// <summary>
    /// OnContextLoaded must NOT fire for transitions that do not pass through
    /// LoadingLive/LoadingEdit, such as UnloadingLive.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_UnloadingLive_DoesNotFireContextLoadedEvent()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var hbWriter    = new DdsWriter<NodeHeartbeat>(participant);
        using var exercise    = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        var handler = new GlobalContextClusterOpHandler(participant, string.Empty);
        handler.LocalTempRoot = _tempDir;

        bool eventFired = false;
        handler.OnContextLoaded += (_, _) => eventFired = true;
        exercise.SetGlobalContextHandler(handler);

        RegisterNode(hbWriter, exercise);

        // LoadingLive with no ScenarioId — CommitLoad exits early, event NOT fired.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        exercise.Tick();
        Assert.False(eventFired, "Sanity: event should not fire without ScenarioId.");

        // OperatingLive then UnloadingLive — must not trigger event.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        exercise.Tick();

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.UnloadingLive).ToString(),
        });
        exercise.Tick();

        Assert.False(eventFired,
            "OnContextLoaded must not fire for non-load transitions such as UnloadingLive.");
    }
}
