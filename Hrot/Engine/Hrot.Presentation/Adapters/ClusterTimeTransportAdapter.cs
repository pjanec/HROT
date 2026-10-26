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
/// ⭐⭐⭐ <b>THE time facade — on EVERY host, the editor included</b> (user, `2026-10-26`: *"One facade."*; CE-3329 row 21). The editor is
/// a one-node cluster (DESIGN_Time_Architecture: *"editor IS the cluster … no exceptions"*): its master sits on the same orchestration
/// bus, so the same intents and the same broadcasts serve it. ⛔ SUPERSEDED: <c>EditorTimeTransportFacade</c> (a second implementation
/// that drove the editor's dry run through a local shortcut).
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
    private ReplayAxis _axis = ReplayAxis.Sim;            // ⭐ CE-3329 R-3058 — rides every cluster-state publication
    private ReplaySession? _session;                      // ⭐ CE-3329 — the session layer (ReplaySessionTopic)
    private readonly ClusterReplayControls _replay;       // ⭐ CE-3329 row 21 — the review / replay members, written once

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
        _replay = new ClusterReplayControls(() => _currentState, () => _session, () => _axis,
            new Hrot.Presentation.DebugApi.OrchestrationBusMasterControl(() => _bus));
    }

    /// <summary>
    /// Drains <see cref="ClusterStateUpdateEvent"/> and <see cref="SwitchTimeModeEvent"/>
    /// from the bus read buffer to keep internal state current.
    /// Must be called once per frame BEFORE <c>EventBus.SwapBuffers()</c>.
    /// </summary>
    public void Update()
    {
        foreach (var ev in _bus.ReadManaged<ClusterStateUpdateEvent>())
        {
            _currentState = ev.CurrentState;
            if (!ev.MasterGone) _axis = ev.ReplayAxis;
        }

        foreach (var ev in _bus.ReadManaged<ReplaySessionUpdateEvent>())
            if (ev.Session != null) _session = ev.Session;

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
        _currentState == ClusterState.OperatingReplay  ||
        _currentState == ClusterState.OperatingReview;   // ⭐ CE-3329

    /// <inheritdoc/>
    public bool IsPlayPauseEnabled => _currentState == ClusterState.Idle || IsOperating;

    /// <inheritdoc/>
    public bool IsStepEnabled => IsPlayPauseEnabled;

    /// <summary>⭐ Stop leaves what runs: a dry run (back to edit), a review (back to live, R-3060), a live run or a replay (to idle).
    /// ⛔ Not in edit — leaving edit is unloading the scenario, not a transport action.</summary>
    public bool IsStopEnabled => IsOperating && _currentState != ClusterState.OperatingEdit;

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
        // ⭐ CE-3329 row 21 — Play from EDIT is the dry run, running (the master resumes it when it lands); the same request on every host
        if (_currentState == ClusterState.OperatingEdit)
        {
            _bus.PublishManaged(DryRunIntents.Enter(startPaused: false));
            return;
        }
        if (IsPaused)
            _bus.PublishManaged(new ResumeTimeIntent());
        else
            _bus.PublishManaged(new PauseTimeIntent());
    }

    /// <inheritdoc/>
    public void Step() => StepBy(1f / 60f);

    /// <inheritdoc/>
    public void StepBy(float seconds)
    {
        if (_currentState == ClusterState.OperatingEdit)
        {
            _bus.PublishManaged(DryRunIntents.Enter(startPaused: true));   // step from edit = the dry run, paused at its first frame
        }
        else if (!IsPaused)
        {
            _bus.PublishManaged(new PauseTimeIntent());
        }
        else
        {
            _bus.PublishManaged(new StepTimeIntent { DeltaSeconds = seconds });
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (ClusterStates.IsReview(_currentState)) { ExitReview(); return; }   // R-3060 — ■ in a review = back to live
        // ⭐ a dry run goes back to edit (the master puts the clock back, PreviewClockProcessManager); anything else to idle
        if (_currentState == ClusterState.OperatingPreview) { _bus.PublishManaged(DryRunIntents.Exit()); return; }
        _bus.PublishManaged(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.Idle });
    }

    /// <inheritdoc/>
    public void SetTimeScale(float scale) =>
        _bus.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });

    // ── ⭐ CE-3329 row 21 — the review / replay session (one implementation: ClusterReplayControls) ──────────────────────────

    /// <inheritdoc/>
    public TransportMode Mode => _replay.Mode;

    /// <inheritdoc/>
    public ReplayAxis Axis => _replay.Axis;

    /// <inheritdoc/>
    public void SetAxis(ReplayAxis axis) => _replay.SetAxis(axis);

    /// <inheritdoc/>
    public void EnterReview(double? at = null) => _replay.EnterReview(at);

    /// <inheritdoc/>
    public void ExitReview() => _replay.ExitReview();

    /// <inheritdoc/>
    public double? LivePoint => _replay.LivePoint;

    /// <inheritdoc/>
    public void Seek(double t) => _replay.Seek(t);

    /// <inheritdoc/>
    public void SetLoop(double from, double to) => _replay.SetLoop(from, to);   // ⭐ CE-3329 §12n

    /// <inheritdoc/>
    public void ClearLoop() => _replay.ClearLoop();

    /// <inheritdoc/>
    public (double From, double To)? Loop => _replay.Loop;

    /// <summary>The session layer as this host last heard it (<see langword="null"/> before the first sample).</summary>
    public ReplaySession? Session => _session;
}
