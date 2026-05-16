using System;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator.Panels;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Xunit;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using FdpNodeOpType   = Fdp.Toolkit.Orchestration.NodeOpType;
using ClusterState    = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="ClusterUiCache"/> (CGF1-S0506).
///
/// Each test creates an <see cref="FdpEventBus"/>, publishes an event, calls Update(),
/// and asserts the cache reflects the published state.
/// </summary>
[Collection("ClusterUiCacheTests")]
public sealed class ClusterUiCacheTests : IDisposable
{
    private readonly FdpEventBus    _bus;
    private readonly ClusterUiCache _uiCache;

    public ClusterUiCacheTests()
    {
        _bus     = new FdpEventBus();
        _uiCache = new ClusterUiCache(_bus);
    }

    public void Dispose()
    {
        _uiCache.Dispose();
        _bus.Dispose();
    }

    // ── Helper: publish, swap, update ─────────────────────────────────────────

    private void Tick()
    {
        _bus.SwapBuffers();
        _uiCache.Update();
    }

    /// <summary>
    /// CGF1-S0506 SC2: Publishing a ClusterStateUpdateEvent must be reflected in
    /// <c>CurrentState</c> and <c>IsBootstrapped</c> after <c>Update()</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ReflectsClusterStateTopic()
    {
        _bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = FdpClusterState.LoadingLive });

        Tick();

        Assert.Equal(ClusterState.LoadingLive, _uiCache.CurrentState);
        Assert.True(_uiCache.IsBootstrapped,
            "IsBootstrapped must be true for any state other than Degraded.");
    }

    /// <summary>
    /// CGF1-S0506 SC3: Publishing an ExecuteNodeOpIntent must appear in TxHistory
    /// and HasInFlightTransaction must become true.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_Sniffs2PcTraffic()
    {
        var txId = Guid.NewGuid();
        _bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = FdpNodeOpType.PrepareState,
            DomainPayload = null,
        });

        Tick();

        Assert.Equal(1, _uiCache.TxHistory.Count);
        Assert.True(_uiCache.HasInFlightTransaction,
            "HasInFlightTransaction must be true after ExecuteNodeOpIntent arrives.");
    }

    /// <summary>
    /// CGF1-S0506 SC-Inventory: Publishing an AssetInventoryUpdateEvent must be reflected
    /// in <c>AvailableScenarios</c> after <c>Update()</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_UpdatesInventoryFromTopic()
    {
        _bus.PublishManaged(new AssetInventoryUpdateEvent
        {
            LocalScenarios           = new[] { "scene1" },
            LocalExercises           = Array.Empty<ExerciseInventoryItem>(),
            ArchivedExercises        = Array.Empty<ExerciseInventoryItem>(),
            UnarchivedLocalExercises = Array.Empty<ExerciseInventoryItem>(),
        });

        Tick();

        Assert.Equal(1, _uiCache.AvailableScenarios.Length);
        Assert.Equal("scene1", _uiCache.AvailableScenarios[0]);
    }

    /// <summary>
    /// CGF1-S0506 SC-TimeMode: Publishing a SwitchTimeModeEvent with Deterministic mode
    /// must set <c>IsPaused</c> to <c>true</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_UpdatesIsPausedFromTimeMode()
    {
        _bus.Publish(new SwitchTimeModeEvent
        {
            TargetMode       = TimeMode.Deterministic,
            BarrierWallTicks = 0L,
            FixedDelta       = 1f / 60f,
        });

        Tick();

        Assert.True(_uiCache.IsPaused,
            "IsPaused must be true when SwitchTimeModeEvent.TargetMode == Deterministic.");
    }

    /// <summary>
    /// CGF1-S0506: After an ExecuteNodeOpIntent is snooped and then a ClusterOpCompletedEvent
    /// with Success code is received, the transaction is removed from in-flight.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ClosesInFlightTxOnSysOpStatusSuccess()
    {
        var txId = Guid.NewGuid();

        // First: sow an ExecuteNodeOpIntent to create an in-flight tx
        _bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = FdpNodeOpType.PrepareState,
            DomainPayload = null,
        });

        Tick();
        Assert.True(_uiCache.HasInFlightTransaction);

        // Then: publish a ClusterOpCompletedEvent with Success (closes the in-flight)
        _bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId  = txId,   // matches TransactionId used as in-flight key
            StatusCode = OrchestrationStatusCode.Success,
        });

        Tick();

        Assert.False(_uiCache.HasInFlightTransaction,
            "HasInFlightTransaction must be false after ClusterOpCompletedEvent.Success.");
        Assert.True(_uiCache.TxHistory[0].Completed,
            "tx.Completed must be true after Success status code.");
    }

    /// <summary>
    /// Publishing a SwitchTimeModeEvent (Continuous) must update <c>MasterSimTime</c>,
    /// <c>MasterWallTicks</c>, and <c>MasterTimeScale</c> after <c>Update()</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_UpdatesTimeScaleFromTimePulse()
    {
        _bus.Publish(new SwitchTimeModeEvent
        {
            BarrierWallTicks = 123456789L,
            SimTimeSnapshot  = 42.5,
            TimeScale        = 2.0f,
            TargetMode       = TimeMode.Continuous,
        });

        Tick();

        Assert.Equal(42.5,        _uiCache.MasterSimTime,  precision: 3);
        Assert.Equal(2.0f,        _uiCache.MasterTimeScale);
        Assert.Equal(123456789L,  _uiCache.MasterWallTicks);
    }

    /// <summary>
    /// PACK-C002 SC3: Publishing an ExecuteNodeOpIntent with a typed EditLoadHandlerPayload
    /// must create an in-flight transaction with the correct TargetDsmState —
    /// no JsonDocument.Parse is involved.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_Tracks2PcTransactionWithTypedPayload_NoJsonParsing()
    {
        var txId = Guid.NewGuid();
        _bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = FdpNodeOpType.PrepareState,
            DomainPayload = new EditLoadHandlerPayload(
                ScenarioId:    null,
                IsNewScenario: false,
                TargetState:   (FdpClusterState)(int)ClusterState.LoadingLive),
        });

        Tick();

        Assert.Equal(1, _uiCache.TxHistory.Count);
        Assert.Equal(ClusterState.LoadingLive, _uiCache.TxHistory[0].TargetDsmState);
    }

    // ── ReplayDuration aggregation tests ──────────────────────────────────────

    /// <summary>
    /// When <see cref="ClusterOpCompletedEvent"/> arrives with a
    /// <see cref="ReplayPrepareResult"/> <c>ResultPayload</c>,
    /// <see cref="ClusterUiCache.ReplayDuration"/> must reflect the reported duration
    /// rather than the default 3600 s.
    /// Aggregation is performed by <c>ReplayConsensusAggregator</c> in ClusterMaster;
    /// ClusterUiCache only reads the already-aggregated value from the event.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ReplayDuration_UpdatedFromSingleNodeResponse()
    {
        const float expectedDuration = 120f;
        var txId = Guid.NewGuid();

        // Step 1: sow in-flight transaction for PrepareReplay.
        _bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = FdpNodeOpType.PrepareReplay,
            DomainPayload = null,
        });
        Tick();

        // Step 2: cluster-level completion carries the aggregated ReplayPrepareResult
        // (produced by ReplayConsensusAggregator in ClusterMaster).
        _bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = txId,
            StatusCode    = OrchestrationStatusCode.Success,
            ResultPayload = new ReplayPrepareResult(MaxNetworkId: 0L, DurationSeconds: expectedDuration),
        });
        Tick();

        Assert.Equal(expectedDuration, _uiCache.ReplayDuration);
    }

    /// <summary>
    /// <see cref="ClusterUiCache.ReplayDuration"/> must be updated when
    /// <see cref="ClusterOpCompletedEvent.ResultPayload"/> carries a
    /// <see cref="ReplayPrepareResult"/>.
    /// The aggregation of per-node durations is performed by <c>ReplayConsensusAggregator</c>
    /// in ClusterMaster; this test verifies that ClusterUiCache correctly reads the
    /// pre-aggregated result from the cluster-level completion event.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ReplayDuration_TakesMaxFromTwoNodeResponses()
    {
        var txId = Guid.NewGuid();

        _bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = FdpNodeOpType.PrepareReplay,
            DomainPayload = null,
        });
        Tick();

        // Cluster-level completion carries the aggregated max (180 s) pre-computed by
        // ReplayConsensusAggregator — ClusterUiCache just reads ResultPayload.
        _bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = txId,
            StatusCode    = OrchestrationStatusCode.Success,
            ResultPayload = new ReplayPrepareResult(MaxNetworkId: 0L, DurationSeconds: 180f),
        });
        Tick();

        Assert.Equal(180f, _uiCache.ReplayDuration);
    }

    // ── TC2-P2-T1: ITimeController injection tests ────────────────────────

    /// <summary>
    /// TC2-P2-T1-SC1: When a local ITimeController is injected, MasterSimTime reads
    /// from it immediately — no Update() or events required.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_MasterSimTime_ReadsFromLocalController_WhenInjected()
    {
        var fakeCtrl = new FakeTimeController { TotalTime = 77.5 };
        using var cache = new ClusterUiCache(_bus, fakeCtrl);

        Assert.Equal(77.5, cache.MasterSimTime, precision: 3);
    }

    /// <summary>
    /// TC2-P2-T1-SC2: Without a controller, MasterSimTime falls back to the network
    /// value from a SwitchTimeModeEvent (Continuous mode).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_MasterSimTime_FallsBackToNetwork_WhenNoController()
    {
        using var busNoCtrl   = new FdpEventBus();
        using var cacheNoCtrl = new ClusterUiCache(busNoCtrl);

        busNoCtrl.Publish(new SwitchTimeModeEvent
        {
            SimTimeSnapshot = 33.0,
            TimeScale       = 1.0f,
            TargetMode      = TimeMode.Continuous,
        });

        busNoCtrl.SwapBuffers();
        cacheNoCtrl.Update();

        Assert.Equal(33.0, cacheNoCtrl.MasterSimTime, precision: 3);
    }

    /// <summary>
    /// TC2-P2-T1-SC3: When a controller is injected, a network SwitchTimeModeEvent
    /// with a different value must not change MasterSimTime — the controller takes priority.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_MasterSimTime_IgnoresNetworkPulse_WhenControllerInjected()
    {
        var fakeCtrl = new FakeTimeController { TotalTime = 50.0 };
        using var busWithCtrl   = new FdpEventBus();
        using var cacheWithCtrl = new ClusterUiCache(busWithCtrl, fakeCtrl);

        busWithCtrl.Publish(new SwitchTimeModeEvent
        {
            SimTimeSnapshot = 99.0,
            TimeScale       = 1.0f,
            TargetMode      = TimeMode.Continuous,
        });

        busWithCtrl.SwapBuffers();
        cacheWithCtrl.Update();

        // Network event must not override the controller value
        Assert.Equal(50.0, cacheWithCtrl.MasterSimTime, precision: 3);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeTimeController : ITimeController
    {
        public double TotalTime { get; set; }
        public GlobalTime Update()           => GetCurrentState();
        public void SetTimeScale(float s)    { }
        public float GetTimeScale()          => 1f;
        public TimeMode GetMode()            => TimeMode.Continuous;
        public GlobalTime GetCurrentState()  => new GlobalTime { TotalTime = TotalTime };
        public void SeedState(GlobalTime s)  => TotalTime = s.TotalTime;
        public void Dispose()                { }
    }
}

[CollectionDefinition("ClusterUiCacheTests", DisableParallelization = true)]
public class ClusterUiCacheTestCollection { }
