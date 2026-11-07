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
/// </summary>
public sealed class RunToController
{
    /// <summary>The step every frame takes (the ▶ cadence).</summary>
    public const float StepSeconds = 1f / 60f;

    private readonly ITimeTransportFacade _time;
    private readonly Func<bool> _acksPending;
    private readonly Func<bool> _inPreview;
    private readonly Func<EntityRepository?> _world;

    private double _target = double.NaN;
    private Func<EntityRepository, Entity, bool>? _until;
    private double _issuedAt = double.NaN;

    /// <param name="time">The host's transport (pause, step, the clock).</param>
    /// <param name="acksPending">The master is still waiting for this step's ACKs (<c>MasterSyncController.IsAwaitingStepAcks</c>).</param>
    /// <param name="inPreview">The run is a Preview (Run to is offered only there; leaving it ends the run).</param>
    /// <param name="world">The world the condition is asked about.</param>
    public RunToController(ITimeTransportFacade time, Func<bool> acksPending, Func<bool> inPreview, Func<EntityRepository?> world)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _acksPending = acksPending ?? throw new ArgumentNullException(nameof(acksPending));
        _inPreview = inPreview ?? throw new ArgumentNullException(nameof(inPreview));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>A run to … is going on.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Why the last run stopped (<i>"reached 12:00"</i>, <i>"the condition held at 8:34"</i>, <i>"left Preview"</i>), or what it is doing.</summary>
    public string Status { get; private set; } = "";

    /// <summary>
    /// Starts a run to sim time <paramref name="atSeconds"/> and / or until <paramref name="until"/> holds (the first that comes). Refused (false, with
    /// <see cref="Status"/>) outside Preview, with neither, or with a time not ahead of the clock.
    /// </summary>
    public bool Start(double? atSeconds, Func<EntityRepository, Entity, bool>? until)
    {
        if (!_inPreview()) { Status = "Run to works in Preview only."; return false; }
        if (atSeconds is null && until is null) { Status = "Run to needs a time or a condition."; return false; }
        if (atSeconds is double t && !(t > _time.TotalTime)) { Status = $"{Clock(t)} is not ahead of the clock ({Clock(_time.TotalTime)})."; return false; }
        _target = atSeconds ?? double.NaN;
        _until = until;
        _issuedAt = double.NaN;
        IsRunning = true;
        Status = atSeconds is double a ? $"running to {Clock(a)}…" : "running until the condition holds…";
        if (!_time.IsPaused) _time.StepBy(StepSeconds);   // a stepped run starts paused: a step asked while running PAUSES (the transport's rule) — never the ▶ toggle and its armed mode
        FramePacing.SetUnthrottled(true);
        return true;
    }

    /// <summary>Stops the run where it is (paused).</summary>
    public void Cancel(string why = "stopped")
    {
        if (!IsRunning) return;
        Finish($"{why} at {Clock(_time.TotalTime)}");
    }

    /// <summary>One frame: stop when reached, else step once when the last step has landed. Call once per frame on the main thread.</summary>
    public void Tick()
    {
        if (!IsRunning) return;
        if (!_inPreview()) { Finish("left Preview"); return; }
        if (!_time.IsPaused) return;                                   // the pause has not landed yet
        double now = _time.TotalTime;
        if (_acksPending() || (!double.IsNaN(_issuedAt) && !(now > _issuedAt))) return;   // the last step has not landed

        if (!double.IsNaN(_target) && now >= _target - StepSeconds * 0.5) { Finish($"reached {Clock(now)}"); return; }
        if (_until is { } until && _world() is { } world && until(world, Entity.Null)) { Finish($"the condition held at {Clock(now)}"); return; }

        _issuedAt = now;
        _time.StepBy(StepSeconds);
    }

    private void Finish(string status)
    {
        IsRunning = false;
        _until = null;
        Status = status;
        FramePacing.SetUnthrottled(false);
    }

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
