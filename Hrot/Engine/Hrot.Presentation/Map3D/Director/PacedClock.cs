using System;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §0, R-3038; D-1) — real time on a stepped run: ⭐⭐ <b>exactly one fixed step per rendered
/// frame</b>, and the frame is HELD until that step is due — never skipped, never doubled. Step <c>k</c> (counting from 1) is due
/// <c>k / TickHz / Speed</c> wall seconds after <see cref="Start"/>. A frame that comes late steps at once (no hold); the run then falls
/// behind real time, which <see cref="RealtimeFactor"/> reports — and which is why a take may run at 20 or 15 Hz (R-4063).
/// <para>Pure: the wall clock is passed in, so the rails need no sleeping.</para>
/// </summary>
public sealed class PacedClock
{
    private double _startWall;
    private long _stepsAtStart;
    private double _lastWall;
    private double _expectedWall, _actualWall;

    public PacedClock(int tickHz, double speed = 1d)
    {
        if (tickHz <= 0) throw new ArgumentOutOfRangeException(nameof(tickHz));
        TickHz = tickHz;
        Speed = speed > 0 ? speed : 1d;
    }

    public int TickHz { get; }

    /// <summary>Sim seconds per wall second (0.5 = slow motion on screen).</summary>
    public double Speed { get; private set; }

    /// <summary>The size of every step.</summary>
    public float StepSeconds => 1f / TickHz;

    /// <summary>Steps taken since <see cref="Start"/>.</summary>
    public long Steps { get; private set; }

    /// <summary>Frames that came after their step was due (the renderer could not keep up).</summary>
    public long LateFrames { get; private set; }

    public bool Started { get; private set; }

    /// <summary>Starts (or restarts after a pause or a gap) the real-time reckoning at <paramref name="wallNow"/>.</summary>
    public void Start(double wallNow)
    {
        _startWall = wallNow;
        _stepsAtStart = Steps;
        _lastWall = wallNow;
        Started = true;
    }

    /// <summary>Changes the speed from now on.</summary>
    public void SetSpeed(double speed, double wallNow)
    {
        Speed = speed > 0 ? speed : 1d;
        Start(wallNow);
    }

    /// <summary>When this frame's one step is due (wall seconds).</summary>
    public double DueAt => _startWall + (Steps - _stepsAtStart + 1) / (TickHz * Speed);

    /// <summary>How long the frame must be held before its step (0 when it is due or late).</summary>
    public double HoldFor(double wallNow) => Math.Max(0d, DueAt - wallNow);

    /// <summary>Records the frame's one step, taken at <paramref name="wallNow"/> (after any hold).</summary>
    public void Stepped(double wallNow)
    {
        if (wallNow > DueAt + 0.5 / (TickHz * Speed)) LateFrames++;
        _expectedWall += 1d / (TickHz * Speed);
        _actualWall += Math.Max(0d, wallNow - _lastWall);
        Steps++;
        _lastWall = wallNow;
    }

    /// <summary>
    /// The wall time the steps were due over the wall time they took, summed over every step since construction (each step against the
    /// speed it was taken at; the time across a <see cref="Start"/> — a pause, a gap — not counted): 1 = real time.
    /// </summary>
    public double RealtimeFactor => _actualWall > 0 ? _expectedWall / _actualWall : 1d;
}
