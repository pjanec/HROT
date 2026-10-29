using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using System.Collections.Generic;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

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
public sealed class ClusterTimeTransportAdapter : ITimeTransportFacade, IRunTransport
{
    private readonly FdpEventBus    _bus;
    private readonly Func<double>?  _localSimTimeGetter;

    // ⭐⭐⭐ CE-1436 (R-3066, docs/DESIGN_Cluster_View.md §5; AQ108 §2.8a) — the cluster facts (state, axis, exercise, recording flags, the exercise lists,
    //   the replay session) are READ off the node's ONE <see cref="ClusterView"/>; ⛔ this adapter folds none of them itself. The view is advanced by its
    //   owner (the node's <c>ClusterSlave.Tick</c>), not here.
    private readonly Func<ClusterView?> _view;
    private ClusterView? View => _view();
    private ClusterState _currentState => View?.State ?? ClusterState.Idle;
    private readonly ClusterReplayControls _replay;       // ⭐ CE-3329 row 21 — the review / replay members, written once
    private RunModeChoice? _run;                           // ⭐ CE-4097 — null on a host that attached none: today's behaviour, unchanged

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
    /// <param name="view">
    ///   ⭐ CE-1436 — the node's ONE cluster read model (<c>ClusterSlave.View</c>; on the editor the node's slave, which its in-process core shares). A delegate because
    ///   some hosts build the node after the toolbar; <see langword="null"/> from it (no node yet) reads as the empty view. ⛔ Required: a host that HAS a node
    ///   must pass its view — there is no private fold to fall back on.
    /// </param>
    /// <param name="localSimTimeGetter">
    ///   Optional delegate that returns the node's locally-measured sim time (seconds).
    ///   When provided, <see cref="TotalTime"/> uses this value for a smoother display;
    ///   otherwise the last snapshot from <see cref="SwitchTimeModeEvent"/> is used.
    /// </param>
    public ClusterTimeTransportAdapter(FdpEventBus bus, Func<ClusterView?> view, Func<double>? localSimTimeGetter = null)
    {
        _bus                = bus ?? throw new ArgumentNullException(nameof(bus));
        _view               = view ?? throw new ArgumentNullException(nameof(view));
        _localSimTimeGetter = localSimTimeGetter;
        _replay = new ClusterReplayControls(() => _currentState, () => View?.ReplaySession, () => View?.ReplayAxis ?? ReplayAxis.Sim,
            new Hrot.Presentation.DebugApi.OrchestrationBusMasterControl(() => _bus));
    }

    /// <summary>
    /// Drains <see cref="SwitchTimeModeEvent"/> from the bus read buffer (the one fact the node's <see cref="ClusterView"/> does not carry) and tells the
    /// run choice the cluster state the view holds.
    /// Must be called once per frame BEFORE <c>EventBus.SwapBuffers()</c>.
    /// </summary>
    public void Update()
    {
        // ⭐ CE-4097 D12 — the one hook that learns a scenario was opened for editing; it compares with the state it last saw, so a per-frame call equals a per-event one
        //   except for a state that left and returned inside ONE frame (not measured; the master moves between states through barrier rounds).
        _run?.OnClusterState(_currentState);

        foreach (var ev in _bus.Read<SwitchTimeModeEvent>())
        {
            _clusterTime.Apply(ev);
            _seenAnyModeEvent = true;
        }

        _run?.Update();   // ⭐ CE-4097 part two — a check-round answer that arrived, a deferred branch (no blocking)
    }

    // ── ITimeTransportFacade ──────────────────────────────────────────────

    private bool IsOperating =>
        _currentState == ClusterState.OperatingLive    ||
        _currentState == ClusterState.OperatingEdit    ||
        _currentState == ClusterState.OperatingPreview ||
        _currentState == ClusterState.OperatingReplay  ||
        _currentState == ClusterState.OperatingReview;   // ⭐ CE-3329

    /// <inheritdoc/>
    /// <remarks>⭐ CE-4097 part two (D22) — RAW: the transport's own answer. "Live armed on a host with no scenario session" disables the USER's ▶
    /// (<see cref="UserTransport.IsUserPlayEnabled"/>), not this — an HTTP <c>/sim/play</c> from Edit is the dry run on every host.</remarks>
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
        // ⭐ CE-4097 part two (AQ108 D22) — and it stays so whatever mode is ARMED: this is what POST /sim/play calls, and a route does what it names. The USER's ▶
        //   asks the run choice instead (UserTransport.UserPlayPause: Live → the unsaved check, then LoadForLive; AAR → the recording).
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
            _bus.PublishManaged(DryRunIntents.Enter(startPaused: true));   // step from edit = the dry run, paused at its first frame (⭐ D22: raw — never the armed mode, see TogglePlayPause)
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
    /// <remarks>⭐ CE-4097 — with a run choice attached ■ is R2 (back to where the run came from, the armed mode stays); without one, today's ■ (<see cref="StopRaw"/>).
    /// ⭐ ONE call: the toolbar's ■ and <c>POST /sim/stop</c> both end here (AQ108 D22).</remarks>
    public void Stop()
    {
        if (_run != null) { _run.Stop(); return; }
        StopRaw();
    }

    /// <summary>■ exactly as before the run choice existed — the raw transport the choice stands on for the cases that keep today's behaviour.</summary>
    private void StopRaw()
    {
        if (ClusterStates.IsReview(_currentState)) { ExitReview(); return; }   // R-3060 — ■ in a review = back to live
        // ⭐ a dry run goes back to edit (the master puts the clock back, PreviewClockProcessManager); anything else to idle
        if (_currentState == ClusterState.OperatingPreview) { _bus.PublishManaged(DryRunIntents.Exit()); return; }
        _bus.PublishManaged(ScenarioLoadSequence.UnloadIntent());   // ⭐ CE-4101 — the ONE Idle intent (no session here: nothing of a scenario to ask about)
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
    public void EnterReviewAtSim(double simSeconds) => _replay.EnterReviewAtSim(simSeconds);

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

    /// <summary>The session layer as the node's <see cref="ClusterView"/> last heard it (inactive before the first sample; <see langword="null"/> with no node).</summary>
    public ReplaySession? Session => View?.ReplaySession;

    // ── ⭐ CE-4097 (AQ108 §2.8) — the run choice and the raw transport it stands on ──────────────────────────────────

    /// <summary>⭐ The host's run choice (what ▶ runs, R1–R3); <see langword="null"/> until <see cref="AttachRunChoice"/>.</summary>
    public RunModeChoice? RunChoice => _run;

    /// <summary>
    /// ⭐ Builds this host's ONE <see cref="RunModeChoice"/> over this facade and makes ■ / ▶ / Step from Edit delegate to it (every host calls this one line).
    /// </summary>
    /// <param name="hostDefault">AQ108 D12: Preview in the editor, Live on CGF / the all-in-one / the server.</param>
    /// <param name="control">The host's scenario session (<see langword="null"/> on SimHost / IG: Live-armed ▶ is then disabled with its reason).</param>
    /// <param name="unsaved">The unsaved-changes seam; a rail passes a fake. Default: <see cref="ClusterUnsavedChangesQuery"/> — the master's ONE check round (CE-4093), asked on
    /// this adapter's bus (<c>CheckUnsavedIntent</c> through <c>IClusterMasterControl</c>) and read through <paramref name="unsavedReads"/>.</param>
    /// <param name="unsavedReads">⭐ How this host reads the round's answer (its node's provider / the editor's in-process core); <see langword="null"/> ⇒ it observes nothing and a named scenario counts as unchanged.</param>
    public RunModeChoice AttachRunChoice(RunArmed hostDefault, IRunScenarioControl? control, IUnsavedChangesQuery? unsaved = null, UnsavedCheckReads? unsavedReads = null)
    {
        unsaved ??= new ClusterUnsavedChangesQuery(() => control?.LoadedScenarioName,
            new Hrot.Presentation.DebugApi.OrchestrationBusMasterControl(() => _bus), unsavedReads);
        _run = new RunModeChoice(this, hostDefault, control, unsaved);
        _run.OnClusterState(_currentState);   // the state the view held before the choice existed
        return _run;
    }

    /// <summary>⭐ CE-4090 — runs are recorded: in a live run / DAR / AAR the run is not flagged <c>RunNotRecorded</c>; before one, the "Record live runs" setting is on.</summary>
    public bool RecordsLiveRuns =>
        _currentState is ClusterState.OperatingLive or ClusterState.OperatingReplay or ClusterState.OperatingReview
            ? !(View?.RunNotRecorded ?? false)
            : !(View?.RecordLiveRunsOff ?? false);

    /// <summary>The exercise the cluster state names (<see cref="Guid.Empty"/> before any).</summary>
    public Guid ActiveExercise => View?.ExerciseId ?? Guid.Empty;

    /// <summary>⭐ The scenario of <see cref="ActiveExercise"/>, from the inventory's <c>ExerciseInventoryItem.ScenarioId</c>; <see langword="null"/> when the inventory has not listed it yet.</summary>
    public string? ActiveExerciseScenario => ActiveExerciseItem()?.ScenarioId;

    /// <inheritdoc/>
    public (Guid Parent, long BranchWallTicks)? ActiveExerciseBranch =>
        ActiveExerciseItem() is { } run && run.ParentExerciseId != Guid.Empty ? (run.ParentExerciseId, run.BranchWallTicks) : null;

    /// <summary>The inventory item of <see cref="ActiveExercise"/>, over the view's local + archived + unarchived-local lists (the last list that names it wins — the scopes this adapter always folded).</summary>
    private ExerciseInventoryItem? ActiveExerciseItem()
    {
        var v = View;
        if (v == null || v.ExerciseId == Guid.Empty) return null;
        foreach (var list in new[] { v.UnarchivedLocalExercises, v.ArchivedExercises, v.AvailableExercises })
            foreach (var item in list)
                if (item.ExerciseId == v.ExerciseId) return item;
        return null;
    }

    /// <inheritdoc/>
    public void SeekWall(long wallTicks)
    {
        if (Session is { Active: true } s) Seek(ClusterTimelineCursor.ToFacadeAxis(this, s, wallTicks));
    }

    /// <inheritdoc/>
    public long? ReplayZeroWallTicks => Session is { Active: true } s ? s.ZeroWallTicks : null;

    void IRunTransport.StopRaw() => StopRaw();

    void IRunTransport.EnterDryRun(bool startPaused) => _bus.PublishManaged(DryRunIntents.Enter(startPaused));

    void IRunTransport.Resume() => _bus.PublishManaged(new ResumeTimeIntent());

    void IRunTransport.BranchLive() => _bus.PublishManaged(ReplaySessionIntents.BranchLive());   // the ONE construction of the branch intent
}
