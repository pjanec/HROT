using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Time.Domain;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager (Saga) that auto-pauses the master clock when the active replay
/// session has advanced past the recorded duration.
///
/// <para>
/// On each <see cref="Tick"/> call the manager reads <see cref="ClusterOpCompletedEvent"/>
/// from the bus.  When a successful completion arrives that carries a
/// <see cref="ReplayPrepareResult"/> with a positive <c>DurationSeconds</c>, the manager
/// records the end time as <c>currentSimTime + DurationSeconds</c>.  Subsequent ticks
/// compare the master sim time against this threshold and publish a
/// <see cref="PauseTimeIntent"/> exactly once when the threshold is crossed.
/// </para>
///
/// <para>
/// Wired into <see cref="OrchestratorSubsystem"/> after <see cref="ClusterMaster"/>
/// and before <see cref="Panels.ClusterUiCache"/>.  The included
/// <see cref="ReplayConsensusAggregator"/> must be registered with
/// <see cref="ClusterMaster.RegisterAggregator"/> so that the
/// <see cref="ClusterOpCompletedEvent.ResultPayload"/> carries a typed
/// <see cref="ReplayPrepareResult"/> rather than <c>null</c>.
/// </para>
/// </summary>
public sealed class ReplayProcessManager
{
    private readonly FdpEventBus     _bus;
    private readonly ITimeController _timeController;

    // sim-time threshold past which the master clock should be paused
    private double _replayEndSimTime = double.MaxValue;
    private bool   _triggered;

    /// <param name="bus">Shared event bus.</param>
    /// <param name="timeController">
    /// Active master time controller used to read the current simulation time.
    /// </param>
    public ReplayProcessManager(FdpEventBus bus, ITimeController timeController)
    {
        _bus            = bus            ?? throw new ArgumentNullException(nameof(bus));
        _timeController = timeController ?? throw new ArgumentNullException(nameof(timeController));
    }

    /// <summary>
    /// Returns the <see cref="ReplayConsensusAggregator"/> that must be registered with
    /// <see cref="ClusterMaster.RegisterAggregator"/> so that replay durations flow through
    /// the 2PC pipeline as a typed <see cref="ReplayPrepareResult"/>.
    /// </summary>
    public INodeResponseAggregator CreateAggregator() => new ReplayConsensusAggregator();

    /// <summary>
    /// Checks whether the replay has ended and publishes <see cref="PauseTimeIntent"/> if so.
    /// Call once per frame in Phase 3, after <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        // Read ClusterOpCompletedEvent from the previous frame's back buffer (now front).
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.StatusCode != OrchestrationStatusCode.Success) continue;
            if (ev.ResultPayload is ReplayPrepareResult rpr && rpr.DurationSeconds > 0f)
            {
                _replayEndSimTime = _timeController.GetCurrentState().TotalTime + rpr.DurationSeconds;
                _triggered        = false;
            }
        }

        // Reset end time when the cluster leaves OperatingReplay.
        foreach (var ev in _bus.ReadManaged<ClusterStateUpdateEvent>())
        {
            if ((FdpClusterState)(int)ev.CurrentState != FdpClusterState.OperatingReplay)
            {
                _replayEndSimTime = double.MaxValue;
                _triggered        = false;
            }
        }

        if (_triggered || _replayEndSimTime >= double.MaxValue) return;

        if (_timeController.GetCurrentState().TotalTime >= _replayEndSimTime)
        {
            _bus.PublishManaged(new PauseTimeIntent());
            _triggered = true;
        }
    }
}
