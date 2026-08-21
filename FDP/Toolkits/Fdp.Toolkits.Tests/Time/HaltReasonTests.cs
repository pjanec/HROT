using Fdp.Core;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// `T6` — <see cref="HaltReason"/>: <i>why</i> time is stopped, not just <i>that</i> it is.
    /// </summary>
    public class HaltReasonTests
    {
        // Named so each rail reads as a sentence rather than five bare booleans.
        private static HaltReason Resolve(
            bool publishing = true,
            bool advancing = false,
            bool rewound = false,
            bool awaitingAcks = false,
            bool deterministic = false)
            => HaltReasonResolver.Resolve(publishing, advancing, rewound, awaitingAcks, deterministic);

        [Fact]
        public void AnAdvancingClock_IsRunning()
            => Assert.Equal(HaltReason.Running, Resolve(advancing: true));

        [Fact]
        public void ADeterministicIdleClock_IsPausedByTheOperator()
            => Assert.Equal(HaltReason.PausedByOperator, Resolve(deterministic: true));

        [Fact]
        public void AStepAwaitingAcks_IsSteppingHeld_NotPaused()
            => Assert.Equal(HaltReason.SteppingHeld,
                Resolve(deterministic: true, awaitingAcks: true));

        [Fact]
        public void ARewoundWorld_IsHeldByTheBreakpoint()
            => Assert.Equal(HaltReason.HeldByBreakpoint, Resolve(rewound: true));

        /// <summary>
        /// THE ordering rail. While the clock push is suspended the singleton is frozen at its last
        /// value, which may still carry a non-zero delta — so a resolver that asked "is it advancing"
        /// first would report Running while replay preparation holds four system groups disabled.
        /// If anyone reorders these branches, this is the test that fails.
        /// </summary>
        [Fact]
        public void NotPublishing_OutranksAStaleAdvancingClock()
        {
            Assert.Equal(HaltReason.NotPublishing,
                Resolve(publishing: false, advancing: true));

            // And it outranks every other explanation too — nothing below it can be trusted while
            // the world's clock is frozen.
            Assert.Equal(HaltReason.NotPublishing,
                Resolve(publishing: false, advancing: true, rewound: true,
                        awaitingAcks: true, deterministic: true));
        }

        /// <summary>
        /// The breakpoint owns the world while rewound, even mid-step: the step cannot proceed until
        /// the debugger lets go, so reporting SteppingHeld would name the wrong holder.
        /// </summary>
        [Fact]
        public void ARewoundWorld_OutranksAnOutstandingStep()
            => Assert.Equal(HaltReason.HeldByBreakpoint,
                Resolve(rewound: true, awaitingAcks: true, deterministic: true));

        /// <summary>
        /// Halted, publishing, continuous, nothing holding it — no probe explains this. Saying so is
        /// the honest answer; if it ever shows up, the missing probe is the finding.
        /// </summary>
        [Fact]
        public void AnUnexplainedHalt_IsUnknown_NotGuessedAt()
            => Assert.Equal(HaltReason.Unknown, Resolve());

        // ── The probes exist and answer truthfully ───────────────────────────

        /// <summary>
        /// `T6`'s prerequisite, and the thing AS-10 said this slice would have to earn: the kernel's
        /// publish state was settable but not readable, so nothing could consult it.
        /// </summary>
        [Fact]
        public void TheKernel_ReportsWhetherItIsPublishingTheClock()
        {
            using var repo = new EntityRepository();
            var kernel = new Fdp.ModuleHost.ModuleHostKernel(repo, new EventAccumulator());

            Assert.True(kernel.IsPublishingGlobalTime);

            kernel.SuspendGlobalTimePush();
            Assert.False(kernel.IsPublishingGlobalTime);

            kernel.ResumeGlobalTimePush();
            Assert.True(kernel.IsPublishingGlobalTime);
        }

        /// <summary>
        /// The master distinguishes "mid-step, waiting on the cluster" from "paused and idle" — both
        /// are deterministic mode with a zero delta, and without this they are indistinguishable.
        /// </summary>
        [Fact]
        public void TheMaster_ReportsWhetherAStepIsAwaitingAcks()
        {
            long ticks = 0;
            var bus  = new FdpEventBus();
            var ctrl = new MasterSyncController(
                bus, new System.Collections.Generic.HashSet<int> { 1 },
                new TimeConfig { LookaheadWallTicks = 0 }, () => ticks);

            Assert.False(ctrl.IsAwaitingStepAcks);   // continuous

            ctrl.SwitchToDeterministic(new System.Collections.Generic.HashSet<int> { 1 });
            ticks += 1;
            ctrl.Update();
            Assert.False(ctrl.IsAwaitingStepAcks);   // paused, idle

            ctrl.Step(1.0f);
            Assert.True(ctrl.IsAwaitingStepAcks);    // mid-step

            bus.PublishManaged(new Fdp.Toolkit.Time.Domain.FrameStepCompletedEvent { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();
            ticks += 1;
            ctrl.Update();
            Assert.False(ctrl.IsAwaitingStepAcks);   // acknowledged
        }

        /// <summary>
        /// End to end over the real master: the reason tracks the controller through pause and step
        /// without anything latching a copy of it.
        /// </summary>
        [Fact]
        public void TheReasonTracksTheRealMaster_ThroughPauseAndStep()
        {
            long ticks = 0;
            var bus  = new FdpEventBus();
            var ctrl = new MasterSyncController(
                bus, new System.Collections.Generic.HashSet<int> { 1 },
                new TimeConfig { LookaheadWallTicks = 0 }, () => ticks);

            HaltReason Ask(bool advancing) => HaltReasonResolver.Resolve(
                isPublishing: true,
                isAdvancing: advancing,
                isRewound: false,
                isAwaitingStepAcks: ctrl.IsAwaitingStepAcks,
                isDeterministic: ctrl.GetMode() == TimeMode.Deterministic);

            Assert.Equal(HaltReason.Running, Ask(advancing: true));

            ctrl.SwitchToDeterministic(new System.Collections.Generic.HashSet<int> { 1 });
            ticks += 1;
            ctrl.Update();
            Assert.Equal(HaltReason.PausedByOperator, Ask(advancing: false));

            ctrl.Step(1.0f);
            Assert.Equal(HaltReason.SteppingHeld, Ask(advancing: false));
        }
    }
}
