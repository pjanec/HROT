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

    // ⭐ Q86 §4-G (R-215) — a load resets the clock to 0 and HOLDS it while the world is rebuilt; whether it then
    //   runs is a property of the LOAD (TransitionStateIntent.TimeMode). True while a live load that did NOT ask to
    //   start paused is in flight: the clock resumes when the cluster reports OperatingLive.
    private bool _resumeWhenLive;

    /// <summary>The <see cref="TransitionStateIntent.TimeMode"/> value that asks a load to start paused.</summary>
    public const string StartPausedTimeMode = "Deterministic";

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

            CommitContextLoad(loadState.Value, intent.ScenarioId, intent.ExerciseId);   // jumps the clock to 0, paused
            _resumeWhenLive = loadState == ClusterState.LoadingLive && intent.TimeMode != StartPausedTimeMode;
        }

        // ⭐ Q86 §4-G — the load is done when the cluster reports its operating state: run, unless it asked not to.
        //   Any other state reached first (Idle after an abort, Degraded, Edit) drops the pending resume.
        foreach (var ev in _bus.ReadManaged<ClusterStateTransitionedEvent>())
        {
            if (!_resumeWhenLive || ev.SubsystemName != "Cluster") continue;
            if (ev.NewStateId == Fdp.Toolkit.Orchestration.ClusterState.LoadingLive) continue;
            if (ev.NewStateId == Fdp.Toolkit.Orchestration.ClusterState.OperatingLive)
                _bus.PublishManaged(new Fdp.Toolkit.Time.Domain.ResumeTimeIntent());
            _resumeWhenLive = false;
        }

        // CE-278: the SaveScenario=2 SAVE branch (PrepareAsync + Commit + PublishManifestReady, which wrote
        // exercises/<id>/Orchestrator.json) is retired. Only the LOAD branch above remains — the transition
        // context restore, unrelated to the retired op. The exercise sidecar is now written on the Export
        // path by AssetInventoryProcessManager (§5).
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
}
