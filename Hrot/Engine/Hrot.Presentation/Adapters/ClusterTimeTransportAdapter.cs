using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
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
    private bool         _isPaused      = true;
    private float        _timeScale     = 1f;
    private double       _networkSimTime;

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
            _isPaused = ev.TargetMode == TimeMode.Deterministic;
            if (ev.TimeScale > 0f)
                _timeScale = ev.TimeScale;
            // SimTimeSnapshot is non-zero on Resume events; seed the displayed time.
            if (ev.TargetMode == TimeMode.Continuous && ev.SimTimeSnapshot > 0.0)
                _networkSimTime = ev.SimTimeSnapshot;
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

    /// <inheritdoc/>
    public bool IsPaused => _isPaused;

    /// <inheritdoc/>
    public double TotalTime => _localSimTimeGetter != null ? _localSimTimeGetter() : _networkSimTime;

    /// <inheritdoc/>
    public float TimeScale => _timeScale;

    /// <inheritdoc/>
    public void TogglePlayPause()
    {
        if (_isPaused)
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
        else if (!_isPaused)
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
