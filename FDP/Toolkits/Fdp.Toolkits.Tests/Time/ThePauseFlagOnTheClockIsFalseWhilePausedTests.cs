using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>WHICH FIELD OF THE CLOCK MEANS "STOPPED"?</b> — <c>M-42</c>, measured 2026-08-21.
    ///
    /// <para>🔴🔴 <b>Why this class exists.</b> <c>GlobalTime</c> carries a convenience flag,
    /// <c>IsPaused =&gt; TimeScale == 0.0f</c>, and it reads like the answer. It is not: a pause never
    /// touches <c>TimeScale</c>, so <b>the flag is FALSE while the simulation is paused</b>. It has zero
    /// production readers, which is the only reason that has never bitten — and an arm added to
    /// <c>EditorSubsystem</c> on 2026-08-21 read it and could never fire.</para>
    ///
    /// <para>⭐⭐ <b>The true predicate is <c>DeltaTime</c></b>, read from the <c>GlobalTime</c> singleton
    /// the kernel pushes into the live world each frame.</para>
    ///
    /// <para>⚠⚠ <b>These rails corrected their own author.</b> The first version asserted that
    /// <c>SwitchToDeterministic</c> enters <c>Stepping</c> — it does not. It enters
    /// <c>BarrierPending</c> and stays there for the whole lookahead window, so <c>GetMode()</c> answers
    /// <c>Continuous</c> the entire time. ⭐ <b>And that made the case for <c>DeltaTime</c> stronger, not
    /// weaker</b>: <c>UpdateBarrierPending</c> returns a zero delta from the very first frame after the
    /// pause is issued, so <c>DeltaTime</c> is both the honest reading AND the prompt one, while
    /// <c>GetMode()</c> is wrong and late.</para>
    ///
    /// <para>⚠ <b>Behavioural.</b> Every assertion drives a real <c>MasterSyncController</c> over an
    /// injected tick source and reads the <c>GlobalTime</c> it actually returns — ⛔ no source scan, and
    /// no wall-clock waiting.</para>
    /// </summary>
    public class ThePauseFlagOnTheClockIsFalseWhilePausedTests
    {
        /// <summary>⭐ A clock whose wall time this test moves by hand. ⛔ Without it the barrier below
        /// could only be crossed by really sleeping for the lookahead window.</summary>
        private sealed class FakeTicks
        {
            public long Value;
            public long Get() => Value;
            public void Advance(long ticks) => Value += ticks;
        }

        private static (MasterSyncController Clock, FakeTicks Ticks) Standalone()
        {
            var ticks = new FakeTicks { Value = 1_000_000L };
            var clock = new MasterSyncController(
                new FdpEventBus(), new HashSet<int>(), TimeConfig.Default, ticks.Get);
            clock.SetTimeScale(1.0f);
            return (clock, ticks);
        }

        /// <summary>⭐ Drives the controller past the pause barrier into <c>Stepping</c>.</summary>
        private static GlobalTime PauseAndSettle(MasterSyncController clock, FakeTicks ticks)
        {
            clock.SwitchToDeterministic(new HashSet<int>());
            ticks.Advance(TimeConfig.Default.LookaheadWallTicks * 2);
            return clock.Update();
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE ONE THAT MATTERS.</b> A paused clock does not advance — <c>DeltaTime</c> is
        /// zero — ⛔ <b>and <c>IsPaused</c> is still false.</b>
        /// </summary>
        [Fact]
        public void APausedClockReportsZeroDeltaAndYetIsPausedIsFalse()
        {
            var (clock, ticks) = Standalone();

            var time = PauseAndSettle(clock, ticks);

            Assert.Equal(TimeMode.Deterministic, clock.GetMode());
            Assert.True(time.DeltaTime == 0f,
                $"a paused clock must not advance, but DeltaTime was {time.DeltaTime}.");
            Assert.False(time.IsPaused,
                "GlobalTime.IsPaused is TimeScale == 0, and a pause does not touch TimeScale. "
              + "If this now passes, the pause path changed and M-42 must be re-measured — do not "
              + "simply delete this rail.");
        }

        /// <summary>
        /// ⭐⭐ <b>And the delta goes to zero IMMEDIATELY</b>, on the first frame after the pause is
        /// issued — ⛔ <b>while <c>GetMode()</c> still answers <c>Continuous</c></b> for the whole
        /// lookahead window. 📌 This is why <c>IsPausedByDebugger</c> (<c>GetMode() == Deterministic</c>)
        /// is not a pause reading: it is late as well as wrong.
        /// </summary>
        [Fact]
        public void TheDeltaGoesToZeroBeforeTheModeChanges()
        {
            var (clock, ticks) = Standalone();

            clock.SwitchToDeterministic(new HashSet<int>());
            ticks.Advance(TimeConfig.Default.LookaheadWallTicks / 4);   // still inside the barrier
            var duringBarrier = clock.Update();

            Assert.Equal(TimeMode.Continuous, clock.GetMode());
            Assert.True(duringBarrier.DeltaTime == 0f,
                "sim time is frozen from the moment the pause is issued, so the delta must already "
              + $"be zero here — it was {duringBarrier.DeltaTime}.");
        }

        /// <summary>⭐ The step that follows a pause DOES advance, so <c>DeltaTime</c> distinguishes
        /// "halted" from "stepping" — which is the whole point of using it.</summary>
        [Fact]
        public void TheTickAfterAStepAdvancesAgain()
        {
            var (clock, ticks) = Standalone();
            PauseAndSettle(clock, ticks);

            clock.Step(1f / 60f);
            var stepped = clock.Update();

            Assert.True(stepped.DeltaTime > 0f,
                $"the tick after Step must advance, but DeltaTime was {stepped.DeltaTime}.");
        }

        /// <summary>
        /// ⚠⚠ <b>And the delta must be read from the WORLD, never from the controller.</b>
        /// <c>GetCurrentState()</c> is <c>BuildGlobalTime(0f, 0f)</c> — it hard-codes the delta, so a
        /// delta-based predicate read through it answers "halted" forever, in every mode.
        /// </summary>
        [Fact]
        public void GetCurrentStateHardCodesTheDeltaToZeroAndIsNotAClockReading()
        {
            var (clock, ticks) = Standalone();

            ticks.Advance(TimeSpan.TicksPerSecond / 60);
            clock.Update();                                   // continuous; time really is advancing
            Assert.Equal(TimeMode.Continuous, clock.GetMode());

            Assert.True(clock.GetCurrentState().DeltaTime == 0f,
                "GetCurrentState() is a state snapshot for transfer, not a per-frame clock reading. "
              + "Read GlobalTime from the live world's singleton instead.");
        }
    }
}
