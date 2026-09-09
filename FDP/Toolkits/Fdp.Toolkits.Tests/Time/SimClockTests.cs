using Fdp.Core;
using Fdp.Toolkit.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// `T1` — the one named read surface for simulation time.
    ///
    /// <para>The rails that matter here are not "does the property return the field". They are the
    /// two traps the design names: that <c>IsAdvancing</c> must be <c>DeltaTime &gt; 0</c> and NOT
    /// the negation of <c>GlobalTime.IsPaused</c> (which is false while paused), and that the clock
    /// must read the live world rather than latch a copy.</para>
    /// </summary>
    public class SimClockTests
    {
        private static EntityRepository WorldWith(GlobalTime time)
        {
            var world = new EntityRepository();
            world.RegisterComponent<GlobalTime>();
            world.SetSingletonUnmanaged(time);
            return world;
        }

        // ── The M-42 trap ────────────────────────────────────────────────────

        /// <summary>
        /// THE rail for `T1`. A paused cluster clock has <c>DeltaTime == 0</c> and
        /// <c>TimeScale == 1</c> — because a pause switches the master to Stepping and never touches
        /// TimeScale. So the paused clock reports <c>IsPaused == false</c> and
        /// <c>IsAdvancing == false</c> AT THE SAME TIME.
        ///
        /// <para>If someone ever "simplifies" <c>IsAdvancing</c> to <c>!IsPaused</c>, this is the
        /// test that fails, and it is the only thing standing between the refactor and shipping
        /// twelve readers of a flag that never fires.</para>
        /// </summary>
        [Fact]
        public void IsAdvancing_IsNotTheNegationOfIsPaused_OnAPausedClock()
        {
            // Exactly the shape MasterSyncController.UpdateStepping produces while paused.
            var paused = new GlobalTime { DeltaTime = 0.0f, TimeScale = 1.0f, TotalTime = 12.5 };

            Assert.False(paused.IsAdvancing, "a zero delta is not advancing");
            Assert.True(paused.IsHalted);
#pragma warning disable CS0618 // asserting the obsolete flag's brokenness is this test's whole point
            Assert.False(paused.IsPaused,
                "GlobalTime.IsPaused is TimeScale == 0, and a pause does not touch TimeScale");
            Assert.NotEqual(paused.IsAdvancing, !paused.IsPaused);
#pragma warning restore CS0618
        }

        [Fact]
        public void IsAdvancing_IsTrue_WhenTheFrameCarriedADelta()
        {
            var running = new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f };

            Assert.True(running.IsAdvancing);
            Assert.False(running.IsHalted);
        }

        /// <summary>
        /// Slow motion is still motion: a small scale must not read as halted. Only the delta decides.
        /// </summary>
        [Fact]
        public void IsAdvancing_IsTrue_UnderSlowMotion()
        {
            var slow = new GlobalTime { DeltaTime = 0.001f, TimeScale = 0.05f };

            Assert.True(slow.IsAdvancing);
        }

        // ── SimClock reads the live world ────────────────────────────────────

        [Fact]
        public void SimClock_ReadsTheWorldsSingleton()
        {
            using var world = WorldWith(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalTime = 3.5, FrameNumber = 42,
            });

            var clock = SimClock.Of(world);

            Assert.True(clock.IsAdvancing);
            Assert.False(clock.IsHalted);
            Assert.Equal(3.5, clock.TotalTime, 3);
            Assert.Equal(1.0f, clock.TimeScale, 3);
            Assert.Equal(42L, clock.FrameNumber);
        }

        /// <summary>
        /// The clock must not latch. R-126: derived state is read from the source every time, never
        /// cached — a snapshot taken at construction is the same defect as the dozen stale IsPaused
        /// flags this surface exists to replace.
        /// </summary>
        [Fact]
        public void SimClock_DoesNotLatch_ItRereadsOnEveryAsk()
        {
            using var world = WorldWith(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            var clock = SimClock.Of(world);
            Assert.True(clock.IsAdvancing);

            // The cluster pauses: the kernel pushes a zero-delta frame into the SAME world.
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.0f, TimeScale = 1.0f });

            Assert.False(clock.IsAdvancing, "the same clock instance must report the NEW state");
            Assert.True(clock.IsHalted);
        }

        /// <summary>
        /// EntityRepository is an ISimulationView, and the view overload must reach the same clock —
        /// SimClock.Of casts internally rather than widening ISimulationView (1171 references, and
        /// no singleton accessor on it at all).
        /// </summary>
        [Fact]
        public void SimClock_Of_AView_ReachesTheSameClock()
        {
            using var world = WorldWith(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            Fdp.ModuleHost.Abstractions.ISimulationView view = world;

            Assert.True(SimClock.Of(view).IsAdvancing);
        }

        // ── The honest answer for a world that is not running ────────────────

        [Fact]
        public void SimClock_OfNull_ReportsHalted_RatherThanThrowing()
        {
            var clock = SimClock.Of((EntityRepository?)null);

            Assert.False(clock.IsAdvancing);
            Assert.True(clock.IsHalted);
            Assert.Equal(0.0, clock.TotalTime, 3);
        }

        /// <summary>
        /// A world that exists but has never been ticked has no GlobalTime singleton. Reading it is
        /// legitimate — UI paths run before the first tick — and the answer is "not advancing".
        /// </summary>
        [Fact]
        public void SimClock_BeforeTheFirstTick_ReportsHalted()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<GlobalTime>();

            var clock = SimClock.Of(world);

            Assert.False(clock.IsAdvancing);
            Assert.True(clock.IsHalted);
        }
    }
}
