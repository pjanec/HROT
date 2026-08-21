using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Adapters;

/// <summary>
/// Implements <see cref="ITimeTransportFacade"/> for distributed cluster nodes (SimHost, CGF)
/// by tracking cluster state and sim-time snapshots received on the orchestration
/// <see cref="FdpEventBus"/> and dispatching time-control intents back onto the same bus
/// (which the <c>ClusterOpEgressTranslator</c> then forwards to the Orchestrator over DDS).
///
/// <para>Call <see cref="Update"/> once per frame BEFORE the bus <c>SwapBuffers</c> so
/// that state derived from the previous frame's events is current when the UI renders.</para>
/// </summary>
public sealed class ClusterTimeTransportAdapter : ITimeTransportFacade
{
    private readonly FdpEventBus    _bus;
    private readonly Func<double>?  _localSimTimeGetter;

    private ClusterState _currentState  = ClusterState.Idle;

    /// <summary>
    /// `T7`: the <see cref="SwitchTimeModeEvent"/> fold, shared with <c>ClusterUiCache</c>. The two
    /// classes are NOT collapsed — they are different roles on disjoint nodes (this one is the
    /// toolbar facade and also carries the commands; the cache is a broad read-model) — but the fold
    /// itself was duplicated line for line, and this is the one implementation of it.
    /// </summary>
    private readonly ClusterTimeObservation _clusterTime = new();

    /// <summary>
    /// Seeds "paused" before any event has been seen. Deliberate: a node that has not yet heard from
    /// the master must not offer a running transport, and the first mode event overwrites this.
    /// </summary>
    private bool _seenAnyModeEvent;

    /// <summary>
    /// Creates the adapter.
    /// </summary>
    /// <param name="bus">Orchestration event bus shared by the node.</param>
    /// <param name="localSimTimeGetter">
    ///   Optional delegate that returns the node's locally-measured sim time (seconds).
    ///   When provided, <see cref="TotalTime"/> uses this value for a smoother display;
    ///   otherwise the last snapshot from <see cref="SwitchTimeModeEvent"/> is used.
    /// </param>
    public ClusterTimeTransportAdapter(FdpEventBus bus, Func<double>? localSimTimeGetter = null)
    {
        _bus                = bus ?? throw new ArgumentNullException(nameof(bus));
        _localSimTimeGetter = localSimTimeGetter;
    }

    /// <summary>
    /// Drains <see cref="ClusterStateUpdateEvent"/> and <see cref="SwitchTimeModeEvent"/>
    /// from the bus read buffer to keep internal state current.
    /// Must be called once per frame BEFORE <c>EventBus.SwapBuffers()</c>.
    /// </summary>
    public void Update()
    {
        foreach (var ev in _bus.ReadManaged<ClusterStateUpdateEvent>())
            _currentState = ev.CurrentState;

        foreach (var ev in _bus.Read<SwitchTimeModeEvent>())
        {
            _clusterTime.Apply(ev);
            _seenAnyModeEvent = true;
        }
    }

    // ── ITimeTransportFacade ──────────────────────────────────────────────

    private bool IsOperating =>
        _currentState == ClusterState.OperatingLive    ||
        _currentState == ClusterState.OperatingEdit    ||
        _currentState == ClusterState.OperatingPreview ||
        _currentState == ClusterState.OperatingReplay;

    /// <inheritdoc/>
    public bool IsPlayPauseEnabled => _currentState == ClusterState.Idle || IsOperating;

    /// <inheritdoc/>
    public bool IsStepEnabled => IsPlayPauseEnabled;

    /// <inheritdoc/>
    public bool IsStopEnabled => IsOperating;

    /// <summary>
    /// The cluster's last pause DECISION (`T7`), not this node's clock. On a slave it runs ahead of
    /// the local <c>SlaveSyncController</c> by the barrier window — correctly, since that is the
    /// timeline the node is about to snap to. Before any mode event has arrived it reads paused.
    /// </summary>
    public bool IsPaused => !_seenAnyModeEvent || _clusterTime.PauseRequested;

    /// <inheritdoc/>
    public double TotalTime =>
        _localSimTimeGetter != null ? _localSimTimeGetter() : _clusterTime.ResumeSimTime;

    /// <inheritdoc/>
    public float TimeScale => _clusterTime.TimeScale;

    /// <inheritdoc/>
    public void TogglePlayPause()
    {
        if (IsPaused)
            _bus.PublishManaged(new ResumeTimeIntent());
        else
            _bus.PublishManaged(new PauseTimeIntent());
    }

    /// <inheritdoc/>
    public void Step()
    {
        if (_currentState == ClusterState.OperatingEdit)
        {
            _bus.PublishManaged(new TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingPreview,
                TimeMode      = "Deterministic",
            });
        }
        else if (!IsPaused)
        {
            _bus.PublishManaged(new PauseTimeIntent());
        }
        else
        {
            _bus.PublishManaged(new StepTimeIntent { DeltaSeconds = 1f / 60f });
        }
    }

    /// <inheritdoc/>
    public void Stop() => _bus.PublishManaged(new TransitionStateIntent
    {
        TransactionId = Guid.NewGuid(),
        TargetState   = ClusterState.Idle,
    });

    /// <inheritdoc/>
    public void SetTimeScale(float scale) =>
        _bus.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });
}
