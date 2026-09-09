using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// `T4` — the one write surface. Paths B (toolbar), C (debugger) and D (BTree/HSM tracer) used
    /// to call <c>SwitchToDeterministic</c> straight on the editor's controller: no intent, no bus,
    /// and nothing outside the process learned the simulation had stopped. These rails pin the
    /// replacement shape — publish, and let the node's drainer honour it.
    /// </summary>
    public class IntentTimeCommandsTests
    {
        private static FdpEventBus RegisteredBus()
        {
            var bus = new FdpEventBus();
            OrchestrationEventRegistry.RegisterAll(bus);
            return bus;
        }

        [Fact]
        public void Pause_PublishesAPauseIntent()
        {
            var bus = RegisteredBus();
            new IntentTimeCommands(bus).Pause();
            bus.SwapBuffers();

            Assert.Single(bus.ReadManaged<PauseTimeIntent>());
        }

        [Fact]
        public void Resume_PublishesAResumeIntent()
        {
            var bus = RegisteredBus();
            new IntentTimeCommands(bus).Resume();
            bus.SwapBuffers();

            Assert.Single(bus.ReadManaged<ResumeTimeIntent>());
        }

        [Fact]
        public void StepOneTick_PublishesAStepIntent_CarryingTheConfiguredDelta()
        {
            var bus = RegisteredBus();
            new IntentTimeCommands(bus, fixedStepSeconds: 0.25f).StepOneTick();
            bus.SwapBuffers();

            var steps = bus.ReadManaged<StepTimeIntent>();
            Assert.Single(steps);
            Assert.Equal(0.25f, steps[0].DeltaSeconds, 3);
        }

        [Fact]
        public void SetTimeScale_PublishesTheScale()
        {
            var bus = RegisteredBus();
            new IntentTimeCommands(bus).SetTimeScale(0.5f);
            bus.SwapBuffers();

            var scales = bus.ReadManaged<SetTimeScaleIntent>();
            Assert.Single(scales);
            Assert.Equal(0.5f, scales[0].TimeScale, 3);
        }

        [Fact]
        public void ItRefuses_ANonAdvancingStep_AndANegativeScale()
        {
            var bus = RegisteredBus();
            Assert.Throws<ArgumentOutOfRangeException>(() => new IntentTimeCommands(bus, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IntentTimeCommands(bus).SetTimeScale(-1f));
            Assert.Throws<ArgumentNullException>(() => new IntentTimeCommands(null!));
        }

        // ── The end-to-end shape: a published intent actually moves the clock ─────

        /// <summary>
        /// THE rail for `T4`. A command published on the bus must reach the master and change the
        /// mode — that is the whole claim, and it is what tells the difference between "path D now
        /// publishes intents" and "path D publishes into the void". The latter is exactly the
        /// failure `T3` fixed on the editor, and it produces no error of any kind.
        /// </summary>
        [Fact]
        public void APublishedIntent_ReachesTheMasterAndChangesTheMode()
        {
            long ticks = 0;
            var bus  = RegisteredBus();
            var ctrl = new MasterSyncController(
                bus, new System.Collections.Generic.HashSet<int>(),
                new TimeConfig { LookaheadWallTicks = 0 }, () => ticks);
            var commands = new IntentTimeCommands(bus);

            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());

            commands.Pause();
            bus.SwapBuffers();
            ticks += 1;
            ctrl.Update();   // drains PauseTimeIntent -> SwitchToDeterministic -> barrier
            ticks += 1;
            ctrl.Update();   // crosses the barrier into Stepping

            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            commands.Resume();
            bus.SwapBuffers();
            ticks += 1;
            ctrl.Update();

            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());
        }

        /// <summary>
        /// And a step published as an intent advances sim time — with `TM-001`'s deferral this also
        /// means a burst of toolbar clicks is queued rather than dropped, which is why routing the
        /// toolbar through intents is safe to do now and would not have been before.
        /// </summary>
        [Fact]
        public void APublishedStepIntent_AdvancesSimTime()
        {
            long ticks = 0;
            var bus  = RegisteredBus();
            var ctrl = new MasterSyncController(
                bus, new System.Collections.Generic.HashSet<int>(),
                new TimeConfig { LookaheadWallTicks = 0 }, () => ticks);

            ctrl.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());
            ticks += 1;
            ctrl.Update();
            double before = ctrl.GetCurrentState().TotalTime;

            new IntentTimeCommands(bus, fixedStepSeconds: 1.0f).StepOneTick();
            bus.SwapBuffers();
            ticks += 1;
            ctrl.Update();

            Assert.Equal(before + 1.0, ctrl.GetCurrentState().TotalTime, 3);
        }
    }
}
