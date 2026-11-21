using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>M-40</c> — a PAUSED CLOCK means the run state is <c>Paused</c>.</b>
///
/// <para>🔴🔴 <b>The defect</b> *(user, <c>2026-08-21</c>)*: <i>"it is fail in the value changing point
/// — value does not change although I do it when sim is paused."</i> 📐 <c>EditorSubsystem</c>'s
/// <c>isFrozen</c> had two arms, both reading the <b>DEBUGGER</b>. The pause a designer presses is
/// <c>ITimeTransportFacade.TogglePlayPause</c>, which sets the clock's <c>TimeScale</c> to 0 — ⛔ and
/// nothing asked the clock. ⇒ the panel answered <c>Running</c>, <c>TargetFor(Running)</c> is
/// <c>Nowhere</c>, and the dialog refused <b>while the sim was paused</b>.</para>
///
/// <para>⭐⭐ <b>What this pins is the CONTRACT the fix now depends on</b> — that
/// <see cref="GlobalTime.IsPaused"/> is the authority and that <c>RunStateSource</c> honours it.
/// ⛔ It does NOT prove <c>EditorSubsystem</c> passes the arm: 📌 the only precedent that constructs the
/// subsystem lives in <c>Hrot.ClusterRunner.Integration.Tests</c>, which <b>cannot finish</b>
/// (<c>BP-378</c>). ⚠ <b>That gap is real and stated rather than papered over</b> — the smoke suite's
/// T2 is where it belongs.</para>
/// </summary>
public sealed class ThePausedClockIsTheRunStateTests
{
    /// <summary>⭐ The clock's own convenience flag — <c>Fdp.Core/GlobalTime.cs:66</c>.</summary>
    [Theory]
    [InlineData(0.0f, true)]
    [InlineData(1.0f, false)]
    [InlineData(0.5f, false)]
    public void AZeroTimeScaleIsWhatPausedMeans(float scale, bool paused)
        => Assert.Equal(paused, new GlobalTime { TimeScale = scale }.IsPaused);

    /// <summary>
    /// ⭐⭐⭐ <b>A paused CLOCK yields <c>Paused</c> even when the DEBUGGER is not stopped.</b>
    /// ⚠ That combination is precisely the user's case, and it is what the old two arms could not express.
    /// </summary>
    [Fact]
    public void APausedClockAloneYieldsPaused()
    {
        var clock = new GlobalTime { TimeScale = 0.0f };

        var state = RunStateSource.Resolve(
            isSimUp:  () => true,
            isFrozen: () => /* no breakpoint, no stepping */ false || clock.IsPaused);

        Assert.Equal(VariableRunState.Paused, state);
    }

    /// <summary>⭐ And a running clock with no debugger stop is still <c>Running</c> — ⛔ the fix must not
    /// make everything look paused.</summary>
    [Fact]
    public void ARunningClockWithNoDebuggerStopIsRunning()
    {
        var clock = new GlobalTime { TimeScale = 1.0f };

        var state = RunStateSource.Resolve(
            isSimUp:  () => true,
            isFrozen: () => false || clock.IsPaused);

        Assert.Equal(VariableRunState.Running, state);
    }
}
