using System;

namespace Hrot.Stride.Core;

/// <summary>
/// Deterministic fixed-timestep host-loop driver for the Stride external loop.
///
/// <para>
/// Decoupled from GPU/window bring-up so it can be tested headlessly.
/// Given a tick callback and an elapsed-time source, accumulates wall-clock time
/// and calls the callback exactly the right number of times with a <b>fixed</b> dt —
/// independent of how many render frames actually elapsed.
/// </para>
///
/// <para>
/// The simulation clock advances by exactly <c>nTicks × FixedDt</c> regardless
/// of render rate or frame gaps. Leftover fractional time is carried over to the
/// next call of <see cref="AdvanceFrame"/>, so a partial step never produces an
/// extra tick.
/// </para>
///
/// <para>
/// Usage (typical, from an external host loop):
/// <code>
/// var driver = new StrideHostLoopDriver(fixedDt: 1f/60f, maxTicksPerFrame: 4);
/// // per frame:
/// driver.AdvanceFrame(wallDeltaSeconds, dt => strideBootstrapper.Tick(dt));
/// </code>
/// </para>
/// </summary>
public sealed class StrideHostLoopDriver
{
    // Double-precision accumulator avoids float rounding drift.
    // A small epsilon (1e-9 s ≈ 1 ns) in the comparison fires the tick when the
    // accumulator is within epsilon of the fixed step — handles the case where
    // (n * float(1/60)) widened to double is 29.9999... instead of 30.0000.
    // Epsilon is a tiny fraction of fixedDt so it never causes spurious ticks.
    private double _accumulator;
    private const  double Epsilon = 1e-9;

    /// <summary>
    /// Fixed simulation step in seconds (e.g. 1/60 for 60 Hz physics).
    /// Must be positive.
    /// </summary>
    public float FixedDt { get; }

    // Double-precision version of FixedDt used in accumulator comparisons.
    private readonly double _fixedDtDouble;

    /// <summary>
    /// Maximum number of simulation ticks allowed per call to <see cref="AdvanceFrame"/>.
    /// Prevents spiral-of-death when the host loop is very slow.
    /// Defaults to 8.
    /// </summary>
    public int MaxTicksPerFrame { get; }

    /// <summary>
    /// Total simulation time elapsed (seconds) — sum of all tick callbacks issued.
    /// Advances by exactly <c>FixedDt</c> per tick.
    /// </summary>
    public float SimulationTime { get; private set; }

    /// <summary>
    /// Total number of fixed-dt tick callbacks issued since construction.
    /// </summary>
    public int TotalTickCount { get; private set; }

    /// <param name="fixedDt">Fixed simulation step in seconds (must be &gt; 0).</param>
    /// <param name="maxTicksPerFrame">
    /// Maximum ticks fired per <see cref="AdvanceFrame"/> call (default 8).
    /// </param>
    public StrideHostLoopDriver(float fixedDt = 1f / 60f, int maxTicksPerFrame = 8)
    {
        if (fixedDt <= 0f)         throw new ArgumentOutOfRangeException(nameof(fixedDt), "Must be positive.");
        if (maxTicksPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame), "Must be positive.");

        FixedDt          = fixedDt;
        _fixedDtDouble   = fixedDt;   // widen once to double
        MaxTicksPerFrame = maxTicksPerFrame;
    }

    /// <summary>
    /// Advances the simulation by <paramref name="wallDelta"/> seconds of wall time.
    /// Fires <paramref name="tickCallback"/> with <see cref="FixedDt"/> for each
    /// accumulated fixed step, up to <see cref="MaxTicksPerFrame"/> per call.
    /// Leftover time is carried into the next call.
    /// </summary>
    /// <param name="wallDelta">Wall-clock time elapsed since last call (seconds). Clamped to ≥ 0.</param>
    /// <param name="tickCallback">Callback invoked once per fixed step with <see cref="FixedDt"/>.</param>
    /// <returns>Number of tick callbacks fired this frame.</returns>
    public int AdvanceFrame(float wallDelta, Action<float> tickCallback)
    {
        if (tickCallback == null) throw new ArgumentNullException(nameof(tickCallback));

        _accumulator += Math.Max(0.0, (double)wallDelta);

        int ticks = 0;
        while (_accumulator >= _fixedDtDouble && ticks < MaxTicksPerFrame)
        {
            _accumulator   -= _fixedDtDouble;
            SimulationTime += FixedDt;
            TotalTickCount++;
            ticks++;
            tickCallback(FixedDt);
        }
        return ticks;
    }

    /// <summary>
    /// Resets the accumulator and counters to zero (useful for tests and
    /// scenario reloads where you want to restart the sim clock).
    /// </summary>
    public void Reset()
    {
        _accumulator   = 0.0;
        SimulationTime = 0f;
        TotalTickCount = 0;
    }
}
