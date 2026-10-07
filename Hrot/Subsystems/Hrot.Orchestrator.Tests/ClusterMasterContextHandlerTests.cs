using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
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

    private static void RegisterNode(FdpEventBus bus, ClusterMaster exercise, int nodeId = 1)
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId            = nodeId,
            SubsystemName     = "SimHost",
            LocalStateId      = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc      = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();
    }

    /// <summary>
    /// Creates temp dirs: {_tempDir}/{scenarioId}/Orchestrator.json for the handler and
    /// {_tempDir}/nas/{scenarioId}/placeholder.json for the gateway NAS source.
    /// </summary>
    private void SetupScenarioFiles(string scenarioId, long wallTicks = 12_345L, double simTime = 5.0)
    {
        var localDir = Path.Combine(_tempDir, Fdp.Toolkit.Orchestration.OrchestrationConstants.ScenariosDirectoryName, scenarioId);
        Directory.CreateDirectory(localDir);
        var ctxDto = new GlobalContextDto
        {
            StartWallTicks      = wallTicks,
            SceneId             = "scene_" + scenarioId,
            ScenarioId          = scenarioId,
            ScenarioTimeSeconds = simTime,
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

        using var participant = new DdsParticipant(15);
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        var handler = new GlobalContextClusterOpHandler(bus, string.Empty);
        handler.LocalTempRoot = _tempDir;

        bool eventFired    = false;
        long capturedTicks = 0;
        handler.OnContextLoaded += (ticks, _) => { capturedTicks = ticks; eventFired = true; };
        var gcpm = new GlobalContextProcessManager(bus, handler);

        // Publish TransitionStateIntent directly to the bus (production path via ClusterOpMasterTranslator).
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = (Fdp.Toolkit.Orchestration.ClusterState)(int)ClusterState.LoadingLive,
            ScenarioId    = scenarioId,
        });
        bus.SwapBuffers();
        gcpm.Tick();

        Assert.True(eventFired,
            "GlobalContextProcessManager must invoke the local GlobalContextClusterOpHandler " +
            "when TransitionStateIntent with LoadingLive arrives, firing OnContextLoaded.");
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

        using var participant = new DdsParticipant(15);
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        var handler = new GlobalContextClusterOpHandler(bus, string.Empty);
        handler.LocalTempRoot = _tempDir;

        bool eventFired = false;
        handler.OnContextLoaded += (_, _) => eventFired = true;
        var gcpm = new GlobalContextProcessManager(bus, handler);

        // Request FINAL state OperatingLive — planner: PrefetchScenario -> LoadingLive -> OperatingLive.
        // GlobalContextProcessManager must still commit for the implied LoadingLive step.
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = (Fdp.Toolkit.Orchestration.ClusterState)(int)ClusterState.OperatingLive,
            ScenarioId    = scenarioId,
        });
        bus.SwapBuffers();
        gcpm.Tick();

        Assert.True(eventFired,
            "GlobalContextProcessManager must invoke the local handler when OperatingLive " +
            "implies a LoadingLive step.");
    }

    // ── ⭐ Q86 §4-G (R-215): a load resets the clock to 0; paused-or-running is a property of the load ──

    /// <summary>Drives one live load through the process manager, then reports <paramref name="reached"/>; returns
    /// how many <see cref="Fdp.Toolkit.Time.Domain.ResumeTimeIntent"/> the manager published.</summary>
    private int ResumesAfterLiveLoad(string scenarioId, string? timeMode, Fdp.Toolkit.Orchestration.ClusterState reached)
    {
        SetupScenarioFiles(scenarioId);
        using var participant = new DdsParticipant(15);
        var bus = new FdpEventBus();
        var handler = new GlobalContextClusterOpHandler(bus, string.Empty) { LocalTempRoot = _tempDir };
        var gcpm = new GlobalContextProcessManager(bus, handler);

        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
            ScenarioId    = scenarioId,
            TimeMode      = timeMode,
        });
        bus.SwapBuffers();
        gcpm.Tick();

        foreach (var state in new[] { Fdp.Toolkit.Orchestration.ClusterState.LoadingLive, reached })
        {
            bus.PublishManaged(new ClusterStateTransitionedEvent { NewStateId = state, SubsystemName = "Cluster" });
            bus.SwapBuffers();
            gcpm.Tick();
        }
        bus.SwapBuffers();
        return bus.ReadManaged<Fdp.Toolkit.Time.Domain.ResumeTimeIntent>().Count;
    }

    /// <summary>
    /// ⭐ Q86 §4-B — the handler publishes the restored context on the BUS (the translator writes the DDS topic), and
    /// the scenario id is the SCENARIO's. ⛔ It used to write the topic directly, with the scene id in ScenarioId.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Q86B_ALoad_PublishesTheContextOnTheBus_WithTheScenarioId()
    {
        const string scenarioId = "q86b_ctx";
        SetupScenarioFiles(scenarioId);
        var bus = new FdpEventBus();
        var gcpm = new GlobalContextProcessManager(bus, new GlobalContextClusterOpHandler(bus, string.Empty) { LocalTempRoot = _tempDir });

        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
            ScenarioId    = scenarioId,
        });
        bus.SwapBuffers();
        gcpm.Tick();
        bus.SwapBuffers();

        var ctx = Assert.Single(bus.ReadManaged<OrchestratorContextChangedEvent>());
        Assert.Equal(scenarioId, ctx.ScenarioId);
        Assert.Equal("scene_" + scenarioId, ctx.SceneId);
    }

    [Fact(Timeout = 10_000)]
    public void Q86G_ALiveLoad_ThatDidNotAskToStartPaused_RunsOnceTheClusterIsLive()
        => Assert.Equal(1, ResumesAfterLiveLoad("q86g_run", null, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive));

    [Fact(Timeout = 10_000)]
    public void Q86G_ALiveLoad_ThatAskedToStartPaused_StaysPaused()
        => Assert.Equal(0, ResumesAfterLiveLoad("q86g_paused", GlobalContextProcessManager.StartPausedTimeMode,
                                                Fdp.Toolkit.Orchestration.ClusterState.OperatingLive));

    [Fact(Timeout = 10_000)]
    public void Q86G_ALiveLoad_ThatEndsAnywhereButLive_DoesNotResume()
        => Assert.Equal(0, ResumesAfterLiveLoad("q86g_abort", null, Fdp.Toolkit.Orchestration.ClusterState.Idle));

    /// <summary>
    /// When no GlobalContextClusterOpHandler is registered, a TransitionState(LoadingLive)
    /// must complete without throwing.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_LoadingLive_WithoutContextHandler_DoesNotThrow()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

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

    // CE-278: CommitSerializeLocal_ProducesPhase2Envelope removed — CommitSerializeLocal (the SaveScenario=2
    // exercises/<id>/Orchestrator.json writer) is retired. The $meta-envelope contract for the sidecar is now
    // exercised on the Export path by AssetInventoryProcessManagerTests.ExportComplete_WritesExerciseSidecar.

    /// <summary>
    /// OnContextLoaded must NOT fire for transitions that do not pass through
    /// LoadingLive/LoadingEdit, such as UnloadingLive.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_UnloadingLive_DoesNotFireContextLoadedEvent()
    {
        using var participant = new DdsParticipant(15);
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        var handler = new GlobalContextClusterOpHandler(bus, string.Empty);
        handler.LocalTempRoot = _tempDir;

        bool eventFired = false;
        handler.OnContextLoaded += (_, _) => eventFired = true;
        var gcpm = new GlobalContextProcessManager(bus, handler);

        // LoadingLive with no ScenarioId — CommitLoad skips, event NOT fired.
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = (Fdp.Toolkit.Orchestration.ClusterState)(int)ClusterState.LoadingLive,
            ScenarioId    = null,
        });
        bus.SwapBuffers();
        gcpm.Tick();
        Assert.False(eventFired, "Sanity: event should not fire without ScenarioId.");

        // OperatingLive without ScenarioId — must not trigger event.
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = (Fdp.Toolkit.Orchestration.ClusterState)(int)ClusterState.OperatingLive,
            ScenarioId    = null,
        });
        bus.SwapBuffers();
        gcpm.Tick();

        // UnloadingLive — no load state implied, must not fire.
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = (Fdp.Toolkit.Orchestration.ClusterState)(int)ClusterState.UnloadingLive,
        });
        bus.SwapBuffers();
        gcpm.Tick();

        Assert.False(eventFired,
            "OnContextLoaded must not fire for non-load transitions such as UnloadingLive.");
    }
}
