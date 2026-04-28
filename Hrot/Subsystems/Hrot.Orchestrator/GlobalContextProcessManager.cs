using System;
using System.Text.Json;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager that owns <see cref="GlobalContextClusterOpHandler"/> and manages
/// local orchestrator context save/load via bus events.
/// </summary>
public sealed class GlobalContextProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly GlobalContextClusterOpHandler _handler;

    public GlobalContextProcessManager(FdpEventBus bus, GlobalContextClusterOpHandler handler)
    {
        _bus     = bus     ?? throw new ArgumentNullException(nameof(bus));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Processes bus events for context save and load. Call once per frame before
    /// <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        // 1. On TransitionStateIntent: if trajectory passes through LoadingLive or LoadingEdit,
        // commit context immediately (same tick, before the fan-out). This mirrors the original
        // ClusterMaster.ProcessTransitionStateIntent() behavior where the commit was synchronous.
        // Reacts to any target state that implies a load step (e.g. OperatingLive implies LoadingLive).
        foreach (var intent in _bus.ReadManaged<TransitionStateIntent>())
        {
            var loadState = ResolveLoadState((ClusterState)(int)intent.TargetState);
            if (loadState == null || string.IsNullOrEmpty(intent.ScenarioId)) continue;

            CommitContextLoad(loadState.Value, intent.ScenarioId, intent.ExerciseId);
        }

        // 2. On SaveScenario: PrepareAsync + Commit + publish manifest entry.
        foreach (var intent in _bus.ReadManaged<ExecuteStorageOpIntent>())
        {
            if (intent.Operation != StorageOpType.SaveScenario) continue;

            var exerciseIdJson = intent.ExerciseId != Guid.Empty
                ? JsonSerializer.Serialize(new { ExerciseId = intent.ExerciseId })
                : string.Empty;
            var localCmd = ClusterNodeOpBuilder.LocalContextCmd(
                NodeOpType.SerializeLocal, Guid.NewGuid(), exerciseIdJson);

            _ = _handler.PrepareAsync(localCmd, CancellationToken.None)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted)
                    {
                        _handler.Commit(localCmd, null);
                        PublishManifestReady(_handler.CommitManifestEntry);
                    }
                    else
                    {
                        FdpLog<GlobalContextProcessManager>.Error(
                            "[GlobalContextProcessManager] PrepareAsync faulted: {0}",
                            t.Exception?.GetBaseException().Message ?? "unknown");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Returns the actual load state that the <paramref name="targetState"/> trajectory passes through,
    /// or <c>null</c> if the trajectory does not involve a context load.
    /// </summary>
    private static ClusterState? ResolveLoadState(ClusterState targetState)
        => targetState switch
        {
            ClusterState.LoadingLive   => ClusterState.LoadingLive,
            ClusterState.LoadingEdit   => ClusterState.LoadingEdit,
            ClusterState.OperatingLive => ClusterState.LoadingLive,
            ClusterState.OperatingEdit => ClusterState.LoadingEdit,
            _                          => null,
        };

    private void CommitContextLoad(ClusterState loadState, string scenarioId, Guid exerciseId)
    {
        var localPayload = JsonSerializer.Serialize(
            new NodeTransitionPayloadDto(
                TargetState: loadState,
                ScenarioId:  scenarioId,
                ExerciseId:  exerciseId),
            OrchestrationJsonOptions.Default);

        _handler.Commit(
            ClusterNodeOpBuilder.LocalContextCmd(NodeOpType.CommitState, Guid.NewGuid(), localPayload),
            null);

        FdpLog<GlobalContextProcessManager>.Info(
            "[GlobalContextProcessManager] CommitLoad executed for scenario '{0}' (loadState={1}).",
            scenarioId, loadState);
    }

    private void PublishManifestReady(FileManifestEntry? entry)
    {
        if (entry == null) return;
        _bus.PublishManaged(new GlobalContextManifestReadyEvent { Entry = entry });
    }
}
