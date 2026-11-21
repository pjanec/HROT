using System;
using Fdp.Core.FlightRecorder;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-3329</c> P1 (row 9, R4 as amended) — the replay's SIM axis, played: a running replay clock JUMPS over every flat run
/// of the recording's sim time (an operator pause, a gap between steps), so whatever changed during it lands at once — "a sudden change
/// at the time of the unpause".</b> 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §4, §5 R2/R4.
/// </summary>
/// <remarks>
/// <para>⭐ The wall clock stays the ONE replay clock (R-4012): every node's playhead is the anchor plus the clock's time, so a jump of
/// the master's clock moves every node at once. ⛔ The jump is <see cref="ITimeCommands.JumpTo"/>, never <c>SnapTo</c>: a snap pauses and
/// moves the timeline epoch, and the kernel would reset every slot at every pause it skips (F14).</para>
/// <para>⭐ Only while the cluster is in <c>OperatingReplay</c>, the clock is running and the session's axis is
/// <see cref="ReplayAxis.Sim"/> (R-3058 — any host switches it; on <see cref="ReplayAxis.Wall"/> pauses play out).</para>
/// <para>⭐⭐ <c>CE-3329 §12n</c> — the LOOP range, on EITHER axis: while the clock runs and the playhead is at or past the loop's
/// <c>To</c>, the clock <see cref="ITimeCommands.JumpTo"/>s back to <c>From</c> (a non-pausing move, so slaves follow it as an ordinary
/// continuous jump). A backward jump has its own landing guard — no second loop jump until the clock reads below <c>To</c> again.</para>
/// <para>⭐⭐ <c>CE-3329</c> row 9 — the per-segment RATE: on the Sim axis the clock runs at the OPERATOR's rate divided by the recorded
/// rate of the segment under the playhead (<see cref="ReplayTimeMap.RateAt"/>), so a run recorded at 2× plays its sim at the operator's
/// speed; on the Wall axis at the operator's rate. The operator's rate is kept apart (taken from the clock when the replay starts, then from
/// every <c>SetTimeScaleIntent</c> this manager did not issue) and put back when a REPLAY ends — ⛔ not a review: its return restores the
/// live rate (<see cref="ReviewProcessManager"/>, F19).</para>
/// <para>⭐ The end of the replay (its duration — a review's live point) pauses the clock once per arrival; a seek back re-arms it.
/// ⛔ SUPERSEDED: <c>ReplayProcessManager.Tick</c> held this (it keeps only the prepare aggregators).</para>
/// </remarks>
public sealed class ReplayClockProcessManager
{
    private readonly ITimeController _clock;
    private readonly ITimeCommands _time;
    private readonly Func<bool> _replaying;
    private readonly Func<ReplayAxis> _axis;
    private readonly Func<ReplayTimeMap> _map;
    private readonly Func<long> _anchorWallTicks;
    private readonly Func<(double FromSeconds, double ToSeconds)?> _loop;
    private double _pendingJumpTo = double.NaN;   // a jump in flight: the clock has not reached it yet
    private bool _loopJumpInFlight;               // ⭐ §12n — a BACKWARD jump issued, the clock still reads at/after To
    private readonly Func<double>? _durationSeconds;   // ⭐ row 9 — the replay's end (a review's live point), seconds after the zero
    private readonly Func<bool>? _reviewing;
    private readonly Fdp.Core.FdpEventBus? _bus;       // ⭐ row 9 — where the operator's rate requests arrive
    private bool _inReplay, _isReview, _rateChanged, _endPaused;
    private float _operatorRate = 1f;
    private float _lastIssuedRate = float.NaN;

    /// <summary>⭐ CE-3329 row 9 — the rate the operator asked for (kept apart from the per-segment rate the clock runs at).</summary>
    public float OperatorRate => _operatorRate;

    /// <summary>⭐ row 9 — how many times this manager changed the clock's rate / paused at the end (diagnostics, rails).</summary>
    public int RateChanges { get; private set; }

    /// <inheritdoc cref="RateChanges"/>
    public int EndPauses { get; private set; }

    /// <summary>⭐ CE-3329 §12n — how many loop wraps this manager made (diagnostics, rails).</summary>
    public int LoopJumps { get; private set; }

    /// <summary>How many jumps this manager made (diagnostics, rails).</summary>
    public int Jumps { get; private set; }

    public ReplayClockProcessManager(ITimeController clock, ITimeCommands time, Func<bool> replaying, Func<ReplayAxis> axis,
                                     Func<ReplayTimeMap> map, Func<long> anchorWallTicks,
                                     Func<(double FromSeconds, double ToSeconds)?> loop,
                                     Func<double>? durationSeconds = null, Func<bool>? reviewing = null, Fdp.Core.FdpEventBus? bus = null)
    {
        _durationSeconds = durationSeconds;
        _reviewing = reviewing;
        _bus = bus;
        _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _replaying = replaying ?? throw new ArgumentNullException(nameof(replaying));
        _axis = axis ?? throw new ArgumentNullException(nameof(axis));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _anchorWallTicks = anchorWallTicks ?? throw new ArgumentNullException(nameof(anchorWallTicks));
    }

    public void Tick()
    {
        ObserveOperatorRate();
        if (!_replaying()) { LeaveReplay(); _pendingJumpTo = double.NaN; _loopJumpInFlight = false; return; }
        if (!_inReplay)
        {
            _inReplay = true;
            _isReview = _reviewing?.Invoke() ?? false;
            _operatorRate = _clock.GetTimeScale();
            _rateChanged = _endPaused = false;
        }
        if (_clock.GetMode() != TimeMode.Continuous) { _pendingJumpTo = double.NaN; _loopJumpInFlight = false; return; }
        if (TickLoop()) return;   // ⭐ §12n — before the Sim axis's paused-stretch jump; a wrap ends the tick
        if (TickEnd()) return;    // ⭐ row 9 — the end pauses (a loop wraps before it)
        TickRate();
        if (_axis() != ReplayAxis.Sim) { _pendingJumpTo = double.NaN; return; }
        var map = _map();
        long anchor = _anchorWallTicks();
        if (!map.HasSimTime || anchor <= 0) return;

        double now = _clock.GetCurrentState().TotalTime;   // seconds after the replay's zero (the playhead)
        if (!double.IsNaN(_pendingJumpTo) && now < _pendingJumpTo) return;   // the last jump has not landed yet
        _pendingJumpTo = double.NaN;

        long wall = anchor + (long)Math.Round(now * TimeSpan.TicksPerSecond);
        if (map.NextUnpause(wall) is not { } end || end <= wall) return;
        double target = (end - anchor) / (double)TimeSpan.TicksPerSecond;
        _time.JumpTo(target);
        _pendingJumpTo = target;
        Jumps++;
        FdpLog<ReplayClockProcessManager>.Debug("[Replay] sim axis: jumped a paused stretch, {0:F3}s → {1:F3}s.", now, target);
    }

    /// <summary>⭐ CE-3329 §12n — wraps the playhead to the loop's start when it reaches the end. <see langword="true"/> = a wrap is
    /// in flight or was just issued (the rest of the tick is skipped).</summary>
    private bool TickLoop()
    {
        if (_loop() is not { } loop) { _loopJumpInFlight = false; return false; }
        double now = _clock.GetCurrentState().TotalTime;
        if (_loopJumpInFlight)
        {
            if (now >= loop.ToSeconds) return true;   // the wrap has not landed yet — never jump twice for one crossing
            _loopJumpInFlight = false;
        }
        if (now < loop.ToSeconds) return false;
        _pendingJumpTo = double.NaN;                  // a Sim-axis jump forward is moot once the playhead is wrapped
        _time.JumpTo(loop.FromSeconds);               // ⛔ JumpTo, never SnapTo: no pause, no epoch move (F14)
        _loopJumpInFlight = true;
        LoopJumps++;
        FdpLog<ReplayClockProcessManager>.Debug("[Replay] loop: wrapped {0:F3}s → {1:F3}s.", now, loop.FromSeconds);
        return true;
    }

    // ⭐ row 9 — every rate request this manager did not issue is the operator's
    private void ObserveOperatorRate()
    {
        if (_bus == null || !_inReplay) return;
        foreach (var intent in _bus.ReadManaged<Fdp.Toolkit.Time.Domain.SetTimeScaleIntent>())
            if (Math.Abs(intent.TimeScale - _lastIssuedRate) > 1e-6f) _operatorRate = intent.TimeScale;
    }

    // ⭐ row 9 — a replay that ends gives the operator's rate back; ⛔ a review's return restores the live rate itself (F19)
    private void LeaveReplay()
    {
        if (!_inReplay) return;
        // ⭐ CE-4067 — a FROZEN clock (0) is a live branch in flight: its restore resumes at the operator's rate; ⛔ setting it here would
        //   unfreeze the cluster while the nodes prepare the live run.
        if (!_isReview && _rateChanged && _clock.GetTimeScale() != 0f) _time.SetTimeScale(_operatorRate);
        _inReplay = _rateChanged = _endPaused = false;
        _lastIssuedRate = float.NaN;
    }

    // ⭐ row 9 — the replay's end pauses the running clock once per arrival; a seek back re-arms it
    private bool TickEnd()
    {
        double end = _durationSeconds?.Invoke() ?? 0;
        if (end <= 0) return false;
        if (_clock.GetCurrentState().TotalTime < end) { _endPaused = false; return false; }
        if (_endPaused) return true;
        _time.Pause();
        _endPaused = true;
        EndPauses++;
        FdpLog<ReplayClockProcessManager>.Debug("[Replay] the end ({0:F3}s) — paused.", end);
        return true;
    }

    // ⭐ row 9 — the clock's rate: the operator's, divided on the Sim axis by the recorded rate of the segment under the playhead
    private void TickRate()
    {
        double rate = _operatorRate;
        if (_axis() == ReplayAxis.Sim && _map() is { HasSimTime: true } map && _anchorWallTicks() is > 0 and var anchor)
        {
            double recorded = map.RateAt(anchor + (long)Math.Round(_clock.GetCurrentState().TotalTime * TimeSpan.TicksPerSecond));
            if (recorded > 1e-6) rate = _operatorRate / recorded;   // a flat run (0) is jumped, not slowed
        }
        rate = Math.Clamp(rate, 0.01, 64.0);
        if (Math.Abs(_clock.GetTimeScale() - rate) <= 1e-4) return;
        _lastIssuedRate = (float)rate;
        _time.SetTimeScale(_lastIssuedRate);
        _rateChanged = true;
        RateChanges++;
    }
}
