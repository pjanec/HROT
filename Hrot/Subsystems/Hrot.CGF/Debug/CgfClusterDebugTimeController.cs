using System;
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.CGF.Debug;

/// <summary>
/// ⭐⭐⭐ <b>cgf==editor slice 4 (<c>DQ30</c>) — the CGF debug time controller, replacing
/// <c>CgfNoOpTimeController</c>'s three empty request methods.</b>
///
/// <para>📄 Owning design: <c>docs/UX/UX_Feature_Cgf_Brain_Diagnostics.md</c> §3a *(the method table)* ·
/// <c>docs/UX/Design_Question_30_Debug_Pause_Resume.md</c> §A–§E *(all decided)* ·
/// <c>docs/DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md</c> §4/§5 *(the diagrams)*.</para>
///
/// <para>⛔⛔ <b>It does NOT hold a <c>MasterSyncController</c>, and cannot.</b> The slice-4
/// <c>classDiagram</c> draws one, but 📐 CGF's kernel time controller is a
/// <c>SlaveSyncController</c> *(<c>CgfApplication.cs:127</c>)* and the only production
/// <c>MasterSyncController</c> lives on the orchestrator *(<c>OrchestratorSubsystem.cs:176</c>)*.
/// ⭐ That is not an oversight in the owning design — §3a states it outright: *"CGF is a <b>slave</b>:
/// it cannot switch modes, only <b>request</b>."</para>
///
/// <para>⭐⭐ <b>So the "real cluster roster" the slice-4 items ask for is supplied by the node that
/// owns one.</b> This controller publishes the same time INTENTS the toolbar already publishes
/// (<see cref="PauseTimeIntent"/> / <see cref="ResumeTimeIntent"/> / <see cref="StepTimeIntent"/>);
/// <c>ClusterOpEgressTranslator</c> forwards them to the orchestrator, whose
/// <c>MasterSyncController</c> then calls <c>SwitchToDeterministic(roster)</c> with the live roster.
/// ⇒ the roster is never duplicated here, which is the point.</para>
///
/// <para>⚠ <b>It is NOT built on <c>ITimeTransportFacade</c>, deliberately.</b> That facade's
/// <c>TogglePlayPause()</c> is a TOGGLE — calling it to pause an already-paused cluster would
/// <i>resume</i> it — and its <c>Step()</c> carries an <c>OperatingEdit</c> state transition a
/// debugger must never trigger. ⭐ The shared implementation is the intent + the egress translator,
/// and both are reused unchanged.</para>
///
/// <para>🔒 <b>The halt scope is the simulation systems only, never the kernel tick</b>
/// *(<c>DQ30-A</c> — the one answer that does not deadlock)*: the kernel must keep ticking so
/// <c>SlaveSyncController.Update()</c> can drain the mode switch that carries this node's own
/// resume. 📐 Verified for this node: the slave controller is installed via
/// <c>ModuleHostKernel.SetTimeController</c>, so it is not a member of either togglable group —
/// the design's *"single highest-risk check"* passes.</para>
/// </summary>
public sealed class CgfClusterDebugTimeController : IEngineDebugTimeController
{
    private readonly FdpEventBus _controlBus;
    private readonly TogglableInputGroup? _inputGroup;
    private readonly TogglableSimulationGroup? _simGroup;
    private readonly Func<bool> _hasCluster;
    private readonly Action<string>? _log;
    private readonly float _stepSeconds;
    private readonly int _unansweredFreezeFrames;

    /// <summary>
    /// ⭐ The ONE implementation of the <see cref="SwitchTimeModeEvent"/> fold (`T7`) — shared with
    /// <c>ClusterTimeTransportAdapter</c> and <c>ClusterUiCache</c> rather than re-folded here.
    /// </summary>
    private readonly ClusterTimeObservation _clusterTime = new();

    private IDataBreakpointManager? _bpManager;

    private bool _halted;
    private bool _stepPending;
    private bool _stepLatched;
    private bool _resumePending;
    private int  _framesSinceFreezeRequest = -1;
    private bool _unansweredLogged;

    /// <summary>
    /// Creates the controller.
    /// </summary>
    /// <param name="controlBus">
    ///   The node's orchestration bus — the one <c>ClusterOpEgressTranslator</c> drains, and the one
    ///   the time translators publish <see cref="SwitchTimeModeEvent"/> onto.
    /// </param>
    /// <param name="inputGroup">CGF's togglable input group — half the halt actuator.</param>
    /// <param name="simGroup">CGF's togglable simulation group — the other half.</param>
    /// <param name="hasCluster">
    ///   ⭐ Is there anyone to answer a freeze request? <c>false</c> in the documented no-DDS mode
    ///   (<c>CgfApplication.cs:107</c>), where a purely local halt is the complete and correct
    ///   behaviour and <c>DQ30-E</c> forbids a warning.
    /// </param>
    /// <param name="log">Where an unanswered freeze is reported. ⛔ Never a modal — ruling 53.</param>
    /// <param name="stepSeconds">Fixed step delta; 60 Hz to match the editor adapter.</param>
    /// <param name="unansweredFreezeFrames">
    ///   How many frames a freeze request may go unacknowledged before <c>DQ30-E</c>'s log line.
    ///   ⚠ The barrier lookahead is ≈200 ms, so this must comfortably exceed it or a healthy
    ///   cluster gets reported as unreachable.
    /// </param>
    public CgfClusterDebugTimeController(
        FdpEventBus               controlBus,
        TogglableInputGroup?      inputGroup,
        TogglableSimulationGroup? simGroup,
        Func<bool>                hasCluster,
        Action<string>?           log                    = null,
        float                     stepSeconds            = 1.0f / 60.0f,
        int                       unansweredFreezeFrames = 120)
    {
        _controlBus             = controlBus ?? throw new ArgumentNullException(nameof(controlBus));
        _inputGroup             = inputGroup;
        _simGroup               = simGroup;
        _hasCluster             = hasCluster ?? throw new ArgumentNullException(nameof(hasCluster));
        _log                    = log;
        _stepSeconds            = stepSeconds;
        _unansweredFreezeFrames = unansweredFreezeFrames;
    }

    /// <summary>
    /// Late-binds the breakpoint manager, exactly as <c>CgfNoOpTimeController</c> did — the manager
    /// takes the controller in its own constructor, so the knot has to be tied afterwards.
    /// </summary>
    public void SetManager(IDataBreakpointManager manager) => _bpManager = manager;

    /// <summary>
    /// ⚠ <b>Unchanged from the no-op, and that is deliberate.</b> The read half was already live on
    /// CGF; <c>DQ30</c> §3 warns *"do not assume the flag means the clock stopped"*. ⛔ This is NOT a
    /// new notion of paused — <c>HaltReasonResolver</c> already owns the combined answer
    /// (<c>HaltReason.HeldByBreakpoint</c>), so nothing here latches a thirteenth flag (`R-126`).
    /// </summary>
    public bool IsPausedByDebugger => _bpManager?.IsPaused ?? false;

    /// <summary>
    /// ⭐⭐ <b>The <c>DQ30-C</c> gate: is the debugger holding this node's world frozen?</b>
    /// World-state ingress must not be applied while this is <c>true</c> — brain state at tick T read
    /// against replicated state at T+k is the exact confusion a debugger exists to prevent.
    /// ⭐ Control-plane ingress keeps polling regardless, or the resume could never arrive.
    /// </summary>
    public bool IsWorldStateFrozen => _halted;

    /// <summary>⚠ Test/diagnostic observability: are the sim groups currently running?</summary>
    public bool SimGroupsEnabled => _simGroup?.Enabled ?? false;

    /// <summary>
    /// ⭐⭐ <b>The cluster's last barrier anchor, in absolute wall ticks</b> — the master's
    /// <c>SwitchTimeModeEvent.BarrierWallTicks</c>, as folded by <see cref="ClusterTimeObservation"/>.
    /// <c>0</c> until a mode event has been seen.
    ///
    /// <para>⭐⭐⭐ <b>This is what makes <c>k</c> MEASURABLE, and it is a real diagnostic rather than test
    /// scaffolding.</b> `DQ30` §3 demands `k` be measured once and warns *"do not treat 'small' as
    /// verified"*. ⭐ It is directly comparable with <c>IDataBreakpointManager.PausedTick</c>, which is
    /// also <c>GlobalTime.TotalWallTicks</c> ⇒ <b><c>k = ClusterBarrierWallTicks − PausedTick</c></b>,
    /// in 100-ns units.</para>
    /// </summary>
    public long ClusterBarrierWallTicks => _clusterTime.BarrierWallTicks;

    /// <summary>
    /// ⭐ Has the cluster ANSWERED with a deterministic (pause) decision? ⚠ This is the cluster's
    /// DECISION, ⛔ not this node's clock — see <see cref="ClusterTimeObservation"/>'s own remarks: on a
    /// slave it runs ahead of the local clock by the barrier window, correctly.
    /// </summary>
    public bool ClusterPauseRequested => _clusterTime.PauseRequested;

    /// <summary>
    /// ① Halt this node's brain IMMEDIATELY — exact at the breakpoint tick, per ruling 61 — and
    /// ② ask the master to freeze the cluster.
    ///
    /// <para>⭐ The two halves are in that order on purpose: the local halt must not wait for the
    /// barrier, or the breakpoint would report a tick the brain had already run past.</para>
    /// </summary>
    public void RequestPause()
    {
        SetSimGroups(false);
        _halted        = true;
        _stepPending   = false;
        _resumePending = false;

        if (!_hasCluster())
        {
            // 🔒 DQ30-E: no participant ⇒ no cluster to freeze ⇒ the local halt IS the complete and
            //    correct behaviour. ⛔ Explicitly NOT a warning — a permanent warning in a supported
            //    mode is ruling 49's dead affordance in another costume.
            _framesSinceFreezeRequest = -1;
            return;
        }

        _controlBus.PublishManaged(new PauseTimeIntent());
        _framesSinceFreezeRequest = 0;
        _unansweredLogged         = false;
    }

    /// <summary>
    /// Asks the master to return to continuous time. ⭐ On a clustered node the sim groups are
    /// re-enabled when this node's own resume arrives as a <see cref="SwitchTimeModeEvent"/>, whose
    /// <c>ApplyResume</c> → <c>ApplyTimeSnap</c> is the zero-dt snap that closes the k-tick gap
    /// (<c>DQ30-B</c>, option A — already implemented in <c>SlaveSyncController</c>).
    ///
    /// <para>⚠ <b>With no cluster the resume is applied locally and at once</b> — the mirror of
    /// <c>DQ30-E</c>. Waiting for an event that can never arrive would leave the offline node halted
    /// for good, which is a worse failure than the one E is about.</para>
    /// </summary>
    public void RequestResume()
    {
        _stepPending  = false;
        _framesSinceFreezeRequest = -1;

        if (!_hasCluster())
        {
            SetSimGroups(true);
            _halted        = false;
            _resumePending = false;
            return;
        }

        _controlBus.PublishManaged(new ResumeTimeIntent());
        _resumePending = true;
    }

    /// <summary>
    /// Asks the master for exactly one deterministic tick. ⭐ The grant re-enables the sim groups for
    /// <b>one</b> kernel update and no more — see <see cref="BeginFrame"/>/<see cref="EndFrame"/>.
    ///
    /// <para>⛔ A latched re-enable that survives a frame boundary is a silent RESUME the operator
    /// would read as "one step" — the design names this as its second risk, and the latch below is
    /// what forecloses it.</para>
    /// </summary>
    public void RequestStepOneTick()
    {
        if (!_halted) return;

        if (_hasCluster())
            _controlBus.PublishManaged(new StepTimeIntent { DeltaSeconds = _stepSeconds });

        _stepPending = true;
    }

    /// <summary>
    /// ⭐⭐ Folds any <see cref="SwitchTimeModeEvent"/> the cluster published, and applies
    /// <c>DQ30-E</c>'s unanswered-freeze rule. Call once per frame, before
    /// <see cref="BeginFrame"/> and while the bus read buffer still holds this frame's events.
    /// </summary>
    public void ObserveClusterTime()
    {
        bool sawModeEvent = false;
        foreach (var ev in _controlBus.Read<SwitchTimeModeEvent>())
        {
            _clusterTime.Apply(ev);
            sawModeEvent = true;
        }

        if (sawModeEvent)
        {
            if (_clusterTime.PauseRequested)
            {
                // ⭐ The cluster answered the freeze. Stop counting; nothing to report.
                _framesSinceFreezeRequest = -1;
            }
            else if (_resumePending)
            {
                // ⭐ This node's own resume landed — SlaveSyncController has snapped its sim-time
                //   baseline to the master's authoritative snapshot, so the brain may run again.
                SetSimGroups(true);
                _halted        = false;
                _resumePending = false;
            }
        }

        if (_framesSinceFreezeRequest < 0 || _unansweredLogged) return;

        if (++_framesSinceFreezeRequest < _unansweredFreezeFrames) return;

        // 🔒 DQ30-E / ruling 64 — halt locally anyway and SAY the cluster is still running.
        //    ⛔ A log line, never a modal: a headless origin logs (ruling 53, the CE-024 correction).
        _unansweredLogged = true;
        _log?.Invoke(
            "Debug freeze request went unanswered — this CGF node is halted at the breakpoint, " +
            "but the cluster is still running.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The step actuator's opening half.</b> Call immediately BEFORE the kernel update.
    /// Re-enables the sim groups only when a step is pending; <see cref="EndFrame"/> takes them back
    /// down in the same frame, so "exactly one tick" holds by construction rather than by discipline.
    /// </summary>
    public void BeginFrame()
    {
        if (!_halted || !_stepPending) return;

        _stepPending = false;
        _stepLatched = true;
        SetSimGroups(true);
    }

    /// <summary>⭐⭐⭐ The step actuator's closing half. Call immediately AFTER the kernel update.</summary>
    public void EndFrame()
    {
        if (!_stepLatched) return;

        _stepLatched = false;
        SetSimGroups(false);
    }

    private void SetSimGroups(bool enabled)
    {
        if (_inputGroup != null) _inputGroup.Enabled = enabled;
        if (_simGroup   != null) _simGroup.Enabled   = enabled;
    }
}
