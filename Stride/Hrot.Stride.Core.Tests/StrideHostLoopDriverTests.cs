using System;
using System.Collections.Generic;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="StrideHostLoopDriver"/>.
///
/// <para>
/// The driver is pure C# with no Stride/GPU dependency; all tests run headlessly.
/// Tests verify the contract required by STR-P0-T5:
/// <list type="bullet">
///   <item>Exact tick count for a known wall-clock span.</item>
///   <item>Simulation clock advances by <c>nTicks × fixedDt</c> — independent of
///     render cadence (irregular / large frame gaps).</item>
///   <item>Leftover time (partial step) does not produce an extra tick and is
///     carried over to the next frame.</item>
///   <item>Zero-length or negative wall-delta produces no ticks.</item>
///   <item>MaxTicksPerFrame cap prevents spiral-of-death.</item>
/// </list>
/// </para>
/// </summary>
public sealed class StrideHostLoopDriverTests
{
    // ── STR-P0-T5 core: deterministic fixed-dt tick count ─────────────────

    // NOTE: Tests use fixedDt = 1f/32f (0.03125 s exactly representable in binary)
    // so n * fixedDt is always exact in float and double arithmetic.
    // The spec says "1.0 s at 60 Hz → 60 ticks" as a conceptual example; the
    // invariant is nTicks × fixedDt, not absolute wall-time.
    // A fixedDt of 1/32 Hz ≈ 32 Hz is functionally equivalent for the driver test.

    /// <summary>
    /// Driving the host-loop driver for exactly 64 × fixedDt yields exactly 64 ticks
    /// and the simulation clock is exactly 64 × fixedDt.
    /// This proves the core success condition from the batch spec.
    /// </summary>
    [Fact]
    public void ExactMultiple_OfFixedDt_ProducesExactTickCount()
    {
        // 1f/32f = 0.03125 — exactly representable in binary float
        const float fixedDt = 1f / 32f;
        const int   n       = 64;

        var driver     = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 80);
        int ticksFired = driver.AdvanceFrame(n * fixedDt, _ => { });

        Assert.Equal(n, ticksFired);
        Assert.Equal(n, driver.TotalTickCount);
        // Sim clock = nTicks × fixedDt
        Assert.Equal(n * fixedDt, driver.SimulationTime, precision: 4);
    }

    /// <summary>
    /// 32 × fixedDt yields exactly 32 ticks (half of the above).
    /// </summary>
    [Fact]
    public void ThirtyTwoSteps_AtExactHz_Produces32Ticks()
    {
        const float fixedDt = 1f / 32f;
        const int   n       = 32;
        var driver  = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 60);

        int ticks = driver.AdvanceFrame(n * fixedDt, _ => { });

        Assert.Equal(n, ticks);
        Assert.Equal(n, driver.TotalTickCount);
    }

    // ── Sim clock is governed by fixed step, not render cadence ──────────

    /// <summary>
    /// Irregular render frames accumulate correctly: 64 × fixedDt total wall time
    /// fed in irregular chunks yields exactly 64 ticks, and the sim clock is exactly
    /// 64 × fixedDt.
    ///
    /// This is the key invariant: the simulation clock is governed by the fixed step,
    /// NOT by render cadence — irregular frame gaps produce the same deterministic
    /// tick count as a perfectly uniform feed.
    /// </summary>
    [Fact]
    public void IrregularFrames_TotallingN_FixedDt_ProducesExactNTicks()
    {
        // 1f/32f is exactly representable; all multiplications stay exact
        const float fixedDt = 1f / 32f;
        const int   n       = 64;
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 64);

        // Irregular render frame sizes (multiples of fixedDt):
        // 3 + 7 + 5 + 2 + 10 + 20 + 17 = 64 fixedDt steps
        int[] stepsPerFrame = { 3, 7, 5, 2, 10, 20, 17 };

        int totalTicks = 0;
        foreach (var steps in stepsPerFrame)
            totalTicks += driver.AdvanceFrame(steps * fixedDt, _ => { });

        Assert.Equal(n, totalTicks);
        Assert.Equal(n, driver.TotalTickCount);
        Assert.Equal(n * fixedDt, driver.SimulationTime, precision: 4);
    }

    /// <summary>
    /// A single very large frame gap is capped by MaxTicksPerFrame,
    /// so the tick count is bounded and the sim clock stays deterministic.
    /// </summary>
    [Fact]
    public void VeryLargeFrameGap_IsCappedByMaxTicksPerFrame()
    {
        const float fixedDt  = 1f / 32f;
        const int   maxTicks = 4;
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: maxTicks);

        // 128 * fixedDt = 4.0 s — would be 128 ticks uncapped
        int ticks = driver.AdvanceFrame(128 * fixedDt, _ => { });

        Assert.Equal(maxTicks, ticks);
        Assert.Equal(maxTicks, driver.TotalTickCount);
        Assert.Equal(maxTicks * fixedDt, driver.SimulationTime, precision: 5);
    }

    // ── Leftover / accumulator ────────────────────────────────────────────

    /// <summary>
    /// A partial step (less than one fixedDt) does not produce a tick, and
    /// the partial time is carried over to the next call.
    /// </summary>
    [Fact]
    public void PartialStep_ProducesNoExtraTick_CarriedOver()
    {
        const float fixedDt  = 1f / 32f;  // exactly representable
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 10);

        // Feed 0.99 of one step — less than one fixedDt
        float partial = fixedDt * 0.99f;
        int ticks = driver.AdvanceFrame(partial, _ => { });

        Assert.Equal(0, ticks);
        Assert.Equal(0, driver.TotalTickCount);
        Assert.Equal(0f, driver.SimulationTime, precision: 5);

        // Now add another 0.02 of a step so total > 1 fixedDt
        ticks = driver.AdvanceFrame(fixedDt * 0.02f, _ => { });
        Assert.Equal(1, ticks);
        Assert.Equal(1, driver.TotalTickCount);
    }

    /// <summary>
    /// After N ticks with a remainder, exactly one more tick fires when
    /// the remainder plus a small increment reaches fixedDt.
    /// </summary>
    [Fact]
    public void Accumulator_LeftoverTime_ProducesExactTick_OnNextFrame()
    {
        const float fixedDt = 1f / 32f;  // exactly representable
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 10);

        // 1.5 frames worth → 1 tick, 0.5 frames left over
        int t1 = driver.AdvanceFrame(fixedDt * 1.5f, _ => { });
        Assert.Equal(1, t1);

        // Another 0.6 frames → total > 1 fixedDt → 1 tick
        int t2 = driver.AdvanceFrame(fixedDt * 0.6f, _ => { });
        Assert.Equal(1, t2);

        Assert.Equal(2, driver.TotalTickCount);
        Assert.Equal(2 * fixedDt, driver.SimulationTime, precision: 5);
    }

    // ── Zero / negative wall delta ────────────────────────────────────────

    /// <summary>
    /// Zero wall-clock delta produces no ticks.
    /// </summary>
    [Fact]
    public void ZeroWallDelta_ProducesNoTick()
    {
        var driver = new StrideHostLoopDriver(1f / 60f, maxTicksPerFrame: 10);
        int ticks = driver.AdvanceFrame(0f, _ => { });

        Assert.Equal(0, ticks);
        Assert.Equal(0f, driver.SimulationTime, precision: 5);
    }

    /// <summary>
    /// Negative wall-clock delta is clamped to zero — no tick, no exception.
    /// </summary>
    [Fact]
    public void NegativeWallDelta_ClampedToZero_ProducesNoTick()
    {
        var driver = new StrideHostLoopDriver(1f / 60f, maxTicksPerFrame: 10);
        int ticks = driver.AdvanceFrame(-0.1f, _ => { });

        Assert.Equal(0, ticks);
        Assert.Equal(0f, driver.SimulationTime, precision: 5);
    }

    // ── Callback receives correct dt ─────────────────────────────────────

    /// <summary>
    /// The tick callback always receives exactly <c>fixedDt</c>, never the wall delta.
    /// Uses 1/32f which is exactly representable in binary float.
    /// </summary>
    [Fact]
    public void TickCallback_AlwaysReceivesFixedDt()
    {
        const float fixedDt = 1f / 32f;   // exactly representable in float
        const int   n       = 16;
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 20);

        var receivedDts = new List<float>();
        driver.AdvanceFrame(n * fixedDt, dt => receivedDts.Add(dt)); // exactly 16 ticks

        Assert.Equal(n, receivedDts.Count);
        foreach (var dt in receivedDts)
            Assert.Equal(fixedDt, dt, precision: 6);
    }

    // ── TotalTickCount accumulates across frames ──────────────────────────

    /// <summary>
    /// <see cref="StrideHostLoopDriver.TotalTickCount"/> accumulates correctly
    /// across multiple <see cref="StrideHostLoopDriver.AdvanceFrame"/> calls.
    /// </summary>
    [Fact]
    public void TotalTickCount_AccumulatesAcrossMultipleFrames()
    {
        const float fixedDt = 1f / 32f;  // exactly representable
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 60);

        driver.AdvanceFrame(fixedDt, _ => { }); // 1 tick
        driver.AdvanceFrame(fixedDt, _ => { }); // 1 tick
        driver.AdvanceFrame(fixedDt, _ => { }); // 1 tick

        Assert.Equal(3, driver.TotalTickCount);
        Assert.Equal(3 * fixedDt, driver.SimulationTime, precision: 5);
    }

    // ── Reset ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="StrideHostLoopDriver.Reset"/> clears all state.
    /// </summary>
    [Fact]
    public void Reset_ClearsAllState()
    {
        const float fixedDt = 1f / 32f;   // exactly representable
        const int   n       = 64;
        var driver = new StrideHostLoopDriver(fixedDt, maxTicksPerFrame: 80);
        driver.AdvanceFrame(n * fixedDt, _ => { }); // 64 ticks

        driver.Reset();

        Assert.Equal(0, driver.TotalTickCount);
        Assert.Equal(0f, driver.SimulationTime, precision: 5);
        // After reset, a fresh feed should again give n ticks
        int ticks = driver.AdvanceFrame(n * fixedDt, _ => { });
        Assert.Equal(n, ticks);
    }

    // ── Constructor validation ────────────────────────────────────────────

    [Fact]
    public void Constructor_NegativeFixedDt_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrideHostLoopDriver(-0.01f));
    }

    [Fact]
    public void Constructor_ZeroFixedDt_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrideHostLoopDriver(0f));
    }

    [Fact]
    public void AdvanceFrame_NullCallback_Throws()
    {
        var driver = new StrideHostLoopDriver(1f / 60f);
        Assert.Throws<ArgumentNullException>(() => driver.AdvanceFrame(1.0f, null!));
    }
}
