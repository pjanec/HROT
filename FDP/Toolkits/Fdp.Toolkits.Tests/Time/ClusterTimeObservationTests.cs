using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Messages;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// `T7` — the one <see cref="SwitchTimeModeEvent"/> fold, and the two properties of it that the
    /// measurement turned on: it is PROMPT where <c>GetMode()</c> is late, and it is a cluster
    /// DECISION rather than a local clock reading.
    /// </summary>
    public class ClusterTimeObservationTests
    {
        private static SwitchTimeModeEvent Pause(long barrier = 1000, float scale = 1f, double sim = 5.0)
            => new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = barrier,
                TimeScale        = scale,
                SimTimeSnapshot  = sim,
                FixedDelta       = 1f / 60f,
            };

        private static SwitchTimeModeEvent Resume(long anchor = 2000, float scale = 1f, double sim = 9.0)
            => new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = anchor,
                TimeScale        = scale,
                SimTimeSnapshot  = sim,
                FixedDelta       = 0f,
            };

        [Fact]
        public void ItStartsUnpausedAtUnitScale()
        {
            var obs = new ClusterTimeObservation();
            Assert.False(obs.PauseRequested);
            Assert.Equal(1f, obs.TimeScale);
        }

        [Fact]
        public void ADeterministicEvent_RecordsThePauseDecision()
        {
            var obs = new ClusterTimeObservation();
            obs.Apply(Pause());
            Assert.True(obs.PauseRequested);
        }

        [Fact]
        public void AContinuousEvent_ClearsIt()
        {
            var obs = new ClusterTimeObservation();
            obs.Apply(Pause());
            obs.Apply(Resume());
            Assert.False(obs.PauseRequested);
        }

        /// <summary>
        /// A Continuous event carries <c>FixedDelta = 0</c>, not a zero SCALE — so a zero scale is
        /// "no scale information", never "stopped". Both folds had this guard and it is worth a rail
        /// now that there is only one of them: losing it would silently zero the displayed rate on
        /// every resume.
        /// </summary>
        [Fact]
        public void AZeroTimeScale_IsNotAScaleChange()
        {
            var obs = new ClusterTimeObservation();
            obs.Apply(Pause(scale: 4f));
            obs.Apply(Resume(scale: 0f));
            Assert.Equal(4f, obs.TimeScale);
        }

        [Fact]
        public void AZeroBarrier_DoesNotOverwriteTheAnchor()
        {
            var obs = new ClusterTimeObservation();
            obs.Apply(Pause(barrier: 1234));
            obs.Apply(Resume(anchor: 0));
            Assert.Equal(1234, obs.BarrierWallTicks);
        }

        /// <summary>
        /// The resume snapshot is the authoritative "you are here" and a pause's snapshot is not:
        /// the master's pause event carries the frozen time, but a display that adopted it would
        /// jump backwards by the barrier window on nodes still running ahead of it.
        /// </summary>
        [Fact]
        public void OnlyAResume_SeedsTheDisplayedSimTime()
        {
            var obs = new ClusterTimeObservation();
            obs.Apply(Pause(sim: 5.0));
            Assert.Equal(0.0, obs.ResumeSimTime);

            obs.Apply(Resume(sim: 9.0));
            Assert.Equal(9.0, obs.ResumeSimTime);
        }

        /// <summary>
        /// ⭐ THE measurement rail behind `T7`, over the real master.
        ///
        /// <para>The fold sees the pause the moment it is issued, because the master publishes the
        /// event at the top of <c>SwitchToDeterministic</c> and stops advancing sim time from the
        /// same instant. <c>GetMode()</c> answers <c>Continuous</c> for the whole lookahead window
        /// (200 ms by default). So "swap the latch for the controller", which is what the design
        /// originally proposed for these caches, would have made the answer LATE by that window —
        /// this rail is why the proposal was reversed.</para>
        /// </summary>
        [Fact]
        public void TheFoldIsPrompt_WhereTheControllersModeIsLate()
        {
            long ticks = 0;
            var bus  = new FdpEventBus();
            var ctrl = new MasterSyncController(
                bus, new System.Collections.Generic.HashSet<int> { 1 },
                new TimeConfig { LookaheadWallTicks = 100 }, () => ticks);

            var obs = new ClusterTimeObservation();

            ctrl.SwitchToDeterministic(new System.Collections.Generic.HashSet<int> { 1 });
            bus.SwapBuffers();
            foreach (var ev in bus.Read<SwitchTimeModeEvent>())
                obs.Apply(ev);

            ticks += 10;                       // inside the window
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());   // the LATE reading
            Assert.True(obs.PauseRequested);                     // the PROMPT one

            // And the clock really is frozen already, so the prompt answer is the true one.
            Assert.Equal(0f, ctrl.Update().DeltaTime);
        }
    }
}
