using System;
using Fdp.Core;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-4316</c> — scenario lifecycle LC-10, <i>Run to …</i> (📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md §2.8; the user approved option A,
/// <c>2026-11-07</c>): in Preview, run the cluster AS FAST AS IT STEPS until a time or a condition (a phase begins, a rule fires — any condition of the
/// condition language), then stay paused there. ⭐ It is the existing ACK-GATED STEPPED RUN (deterministic: the same steps a ▶ at 1× takes), one
/// step per frame — the next step goes only when the last one has LANDED (no ACKs pending AND the clock moved, the CE-105 rule) — with the window's
/// frame cap LIFTED while it runs (<see cref="FramePacing"/>), so the speed is how fast the slowest node steps. ⛔ Not a high time scale: a big
/// step changes WHAT happens (movers and rounds are tuned near 1/60).
/// <para>⭐⭐ The step is a setting of each run (<see cref="Start"/>'s <c>stepSeconds</c>, in (0, <see cref="MaxStepSeconds"/>]) — the user
/// (<c>2026-11-07</c>): <i>"not to be limited to 1/60th… 1/30th or 1/20th… the speed up will be tremendous"</i>. ⭐⭐ Two strategies:</para>
/// <list type="bullet">
/// <item>⭐ <b>TURBO</b> — a host that hands a <c>turbo</c> delegate (only the NETWORKLESS editor node, by construction): many steps per rendered
/// frame, each a fixed step taken at once and the node frame run in place, until a wall budget (<see cref="TurboBudgetSeconds"/>) is spent —
/// default step <see cref="StepSeconds"/>. The ONE documented exception to the shared clock (📄 docs/blueprints/DESIGN_Time_Architecture.md §20).</item>
/// <item>⭐ <b>NETWORKED</b> — no turbo: the ACK-gated step above, one per frame — default step <see cref="NetworkedStepSeconds"/> (the 10× quantum).</item>
/// </list>
/// </summary>
public sealed class RunToController
{
    /// <summary>The step every frame takes (the ▶ cadence).</summary>
    public const float StepSeconds = 1f / 60f;

    /// <summary>The networked run's default step — 1/6 s, the quantum 10× fast-forward takes at 60 fps.</summary>
    public const float NetworkedStepSeconds = 1f / 6f;

    /// <summary>The largest step a run may ask (1/6 s): past it movers and rounds change what they do.</summary>
    public const float MaxStepSeconds = 1f / 6f;

    /// <summary>The wall time one rendered frame gives the turbo loop (the map is drawn once per this).</summary>
    public const double TurboBudgetSeconds = 0.025;

    private readonly ITimeTransportFacade _time;
    private readonly Func<bool> _acksPending;
    private readonly Func<bool> _inPreview;
    private readonly Func<EntityRepository?> _world;
    private readonly Action<float, bool>? _turbo;
    private readonly Func<bool>? _enterPreview;
    private float _step = StepSeconds;

    // ⭐ W-4 (LC-10 from Edit) — a run asked before the Preview is up: kept until the Preview has started (paused), then started
    private bool _starting;
    private double? _pendingAt;
    private Func<EntityRepository, Entity, bool>? _pendingUntil;
    private float? _pendingStep;
    private int _startingFrames;

    /// <summary>How many frames a run asked from Edit waits for the Preview to come up before it gives up (~30 s at 60 fps).</summary>
    public const int EnterPreviewFrames = 1800;

    private double _target = double.NaN;
    private Func<EntityRepository, Entity, bool>? _until;
    private double _issuedAt = double.NaN;

    /// <param name="time">The host's transport (pause, step, the clock).</param>
    /// <param name="acksPending">The master is still waiting for this step's ACKs (<c>MasterSyncController.IsAwaitingStepAcks</c>).</param>
    /// <param name="inPreview">The run is a Preview (Run to is offered only there; leaving it ends the run).</param>
    /// <param name="world">The world the condition is asked about.</param>
    /// <param name="turbo">⭐ The networkless editor node only: run ONE node frame now, first taking a fixed step of the given seconds on the master
    /// when the bool is true. Null ⇒ the networked strategy.</param>
    /// <param name="enterPreview">⭐ W-4 — starts the Preview, PAUSED at its first frame (the run choice's dry run), true when it was asked; lets
    /// <see cref="Request"/> run from Edit. Null ⇒ Run to works only inside a Preview.</param>
    public RunToController(ITimeTransportFacade time, Func<bool> acksPending, Func<bool> inPreview, Func<EntityRepository?> world,
        Action<float, bool>? turbo = null, Func<bool>? enterPreview = null)
    {
        _enterPreview = enterPreview;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _acksPending = acksPending ?? throw new ArgumentNullException(nameof(acksPending));
        _inPreview = inPreview ?? throw new ArgumentNullException(nameof(inPreview));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _turbo = turbo;
    }

    /// <summary>This host runs the turbo strategy (the networkless editor node).</summary>
    public bool IsTurbo => _turbo != null;

    /// <summary>The step the current (or last) run takes.</summary>
    public float Step => _step;

    /// <summary>A run to … is going on.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>⭐ W-4 — a run was asked from Edit and waits for its Preview to come up.</summary>
    public bool IsStarting => _starting;

    /// <summary>The world the conditions are asked about (the Run to popup reads its phases and rules from it).</summary>
    public EntityRepository? World => _world();

    /// <summary>⭐ W-4 — this host can start a run from Edit (it was handed the Preview's door).</summary>
    public bool CanStartFromEdit => _enterPreview != null;

    /// <summary>
    /// ⭐ W-4 (LC-10, the mode menu's <i>Run to …</i>) — <see cref="Start"/> from anywhere a Preview can begin: inside a Preview it IS
    /// <see cref="Start"/>; from Edit it starts the Preview (paused) and starts the run once the Preview is up (<see cref="Tick"/>). The same
    /// refusals as <see cref="Start"/>, checked again when the Preview is up (its clock starts at the scenario's start).
    /// </summary>
    public bool Request(double? atSeconds, Func<EntityRepository, Entity, bool>? until, float? stepSeconds = null)
    {
        if (_inPreview()) return Start(atSeconds, until, stepSeconds);
        if (_enterPreview is null) return Start(atSeconds, until, stepSeconds);   // refused there, with its reason
        if (atSeconds is null && until is null) { Status = "Run to needs a time or a condition."; return false; }
        if (atSeconds is double t && !(t > 0)) { Status = $"{Clock(t)} is not after the start."; return false; }
        if (IsRunning || _starting) { Status = "a run to … is already going on."; return false; }
        if (!_enterPreview()) { Status = "the Preview cannot start now."; return false; }
        _starting = true; _startingFrames = 0;
        _pendingAt = atSeconds; _pendingUntil = until; _pendingStep = stepSeconds;
        Status = "starting the Preview…";
        return true;
    }

    /// <summary>Why the last run stopped (<i>"reached 12:00"</i>, <i>"the condition held at 8:34"</i>, <i>"left Preview"</i>), or what it is doing.</summary>
    public string Status { get; private set; } = "";

    /// <summary>
    /// Starts a run to sim time <paramref name="atSeconds"/> and / or until <paramref name="until"/> holds (the first that comes), each step
    /// <paramref name="stepSeconds"/> (default: <see cref="StepSeconds"/> on turbo, <see cref="NetworkedStepSeconds"/> otherwise). Refused (false,
    /// with <see cref="Status"/>) outside Preview, with neither, with a time not ahead of the clock, or with a step outside (0, <see cref="MaxStepSeconds"/>].
    /// </summary>
    public bool Start(double? atSeconds, Func<EntityRepository, Entity, bool>? until, float? stepSeconds = null)
    {
        if (!_inPreview()) { Status = "Run to works in Preview only."; return false; }
        if (atSeconds is null && until is null) { Status = "Run to needs a time or a condition."; return false; }
        float step = stepSeconds ?? (IsTurbo ? StepSeconds : NetworkedStepSeconds);
        if (!(step > 0f) || step > MaxStepSeconds + 1e-6f) { Status = $"a step of {step:0.####} s is outside (0, 1/6]."; return false; }
        if (atSeconds is double t && !(t > _time.TotalTime)) { Status = $"{Clock(t)} is not ahead of the clock ({Clock(_time.TotalTime)})."; return false; }
        _target = atSeconds ?? double.NaN;
        _until = until;
        _issuedAt = double.NaN;
        _step = step;
        IsRunning = true;
        Status = atSeconds is double a ? $"running to {Clock(a)}…" : "running until the condition holds…";
        if (!_time.IsPaused) _time.StepBy(StepSeconds);   // a stepped run starts paused: a step asked while running PAUSES (the transport's rule) — never the ▶ toggle and its armed mode
        FramePacing.SetUnthrottled(true);
        return true;
    }

    /// <summary>Stops the run where it is (paused).</summary>
    public void Cancel(string why = "stopped")
    {
        if (_starting) { ClearStarting(); Status = why; return; }
        if (!IsRunning) return;
        Finish($"{why} at {Clock(_time.TotalTime)}");
    }

    /// <summary>
    /// One rendered frame: stop when reached, else step when the last step has landed. Networked: at most one step. ⭐ Turbo: node frames (each
    /// with a step once the last has landed) until <see cref="TurboBudgetSeconds"/> of wall time is spent. Call once per frame on the main thread.
    /// </summary>
    public void Tick()
    {
        if (_starting) { WaitForPreview(); return; }
        if (_turbo is null) { Advance(); return; }
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long budget = (long)(TurboBudgetSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (IsRunning && System.Diagnostics.Stopwatch.GetTimestamp() - start < budget) Advance();
    }

    /// <summary>One decision: finish, step (networked: ask the transport; turbo: step and frame at once), or wait (turbo: frame only).</summary>
    private void Advance()
    {
        if (!IsRunning) return;
        if (!_inPreview()) { Finish("left Preview"); return; }
        double now = _time.TotalTime;
        bool landed = _time.IsPaused                                    // the pause has landed
            && !_acksPending() && (double.IsNaN(_issuedAt) || now > _issuedAt);   // and so has the last step
        if (!landed) { _turbo?.Invoke(_step, false); return; }

        if (!double.IsNaN(_target) && now >= _target - _step * 0.5) { Finish($"reached {Clock(now)}"); return; }
        if (_until is { } until && _world() is { } world && until(world, Entity.Null)) { Finish($"the condition held at {Clock(now)}"); return; }

        _issuedAt = now;
        if (_turbo is { } turbo) turbo(_step, true);
        else _time.StepBy(_step);
    }

    /// <summary>⭐ W-4 — a run asked from Edit: started once the Preview is up and paused; given up after <see cref="EnterPreviewFrames"/>.</summary>
    private void WaitForPreview()
    {
        if (_inPreview() && _time.IsPaused)
        {
            var (at, until, step) = (_pendingAt, _pendingUntil, _pendingStep);
            ClearStarting();
            Start(at, until, step);   // a refusal leaves its reason in Status
            return;
        }
        if (++_startingFrames > EnterPreviewFrames) { ClearStarting(); Status = "the Preview did not start."; }
    }

    private void ClearStarting() { _starting = false; _pendingUntil = null; _pendingAt = null; _pendingStep = null; }

    private void Finish(string status)
    {
        IsRunning = false;
        _until = null;
        Status = status;
        FramePacing.SetUnthrottled(false);
    }

    /// <summary>The clock as Run to words it (m:ss, h:mm:ss from an hour).</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
