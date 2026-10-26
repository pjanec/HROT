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
/// <para>⚠ As built (design §12h): the per-segment RATE (a run recorded at 2× plays its sim at 2× here) and the end-of-replay pause stay
/// with <see cref="ReplayProcessManager"/>, which this does not absorb yet.</para>
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

    /// <summary>⭐ CE-3329 §12n — how many loop wraps this manager made (diagnostics, rails).</summary>
    public int LoopJumps { get; private set; }

    /// <summary>How many jumps this manager made (diagnostics, rails).</summary>
    public int Jumps { get; private set; }

    public ReplayClockProcessManager(ITimeController clock, ITimeCommands time, Func<bool> replaying, Func<ReplayAxis> axis,
                                     Func<ReplayTimeMap> map, Func<long> anchorWallTicks,
                                     Func<(double FromSeconds, double ToSeconds)?> loop)
    {
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
        if (!_replaying() || _clock.GetMode() != TimeMode.Continuous) { _pendingJumpTo = double.NaN; _loopJumpInFlight = false; return; }
        if (TickLoop()) return;   // ⭐ §12n — before the Sim axis's paused-stretch jump; a wrap ends the tick
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
}
