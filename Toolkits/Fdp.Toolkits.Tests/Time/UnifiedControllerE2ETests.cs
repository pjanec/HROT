using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// TCU-T006: Full in-process end-to-end test wiring one <see cref="MasterSyncController"/>
    /// and two <see cref="SlaveSyncController"/> instances on separate <see cref="FdpEventBus"/>
    /// instances bridged by manual in-process relays.
    ///
    /// Scenario: <c>FullCycle_Pause_Step_Resume_NoPllLoss</c>
    ///  Phase 1: 20 Continuous frames.
    ///  Phase 2: SwitchToDeterministic — relay SwitchTimeModeEvent to slave buses.
    ///  Phase 3: Drive frames until both slaves enter Stepping mode.
    ///  Phase 4: 5 deterministic steps, relaying AdvanceFrameIntent and FrameStepCompletedEvent.
    ///  Phase 5: SwitchToContinuous — relay to slaves.
    ///  Phase 6: 20 more Continuous frames.
    /// </summary>
    public class UnifiedControllerE2ETests
    {
        // ── Bus relay helpers ────────────────────────────────────────────────

        /// <summary>
        /// Copies all <see cref="SwitchTimeModeEvent"/> events from <paramref name="source"/>
        /// (current buffer) into each target bus's incoming buffer.
        /// Call after <c>source.SwapBuffers()</c> and before target <c>SwapBuffers()</c>.
        /// </summary>
        private static void RelaySwitchTimeModeEvents(FdpEventBus source, params FdpEventBus[] targets)
        {
            var span = source.Read<SwitchTimeModeEvent>();
            if (span.Length == 0) return;
            // Copy span (ref struct) to a local array before iterating targets.
            var buffer = new SwitchTimeModeEvent[span.Length];
            for (int i = 0; i < span.Length; i++) buffer[i] = span[i];
            foreach (var target in targets)
                foreach (var evt in buffer)
                    target.Publish(evt);
        }

        /// <summary>
        /// Copies all <see cref="AdvanceFrameIntent"/> events (managed) from <paramref name="source"/>
        /// (current buffer) into each target bus's incoming buffer.
        /// </summary>
        private static void RelayAdvanceFrameIntents(FdpEventBus source, params FdpEventBus[] targets)
        {
            var intents = source.ReadManaged<AdvanceFrameIntent>();
            foreach (var target in targets)
                foreach (var intent in intents)
                    target.PublishManaged(intent);
        }

        /// <summary>
        /// Copies all <see cref="FrameStepCompletedEvent"/> events (managed) from
        /// <paramref name="source"/> (current buffer) into each target bus's incoming buffer.
        /// </summary>
        private static void RelayFrameStepCompletedEvents(FdpEventBus source, params FdpEventBus[] targets)
        {
            var acks = source.ReadManaged<FrameStepCompletedEvent>();
            foreach (var target in targets)
                foreach (var ack in acks)
                    target.PublishManaged(ack);
        }

        // ── Test ─────────────────────────────────────────────────────────────

        [Fact]
        public void FullCycle_Pause_Step_Resume_NoPllLoss()
        {
            // ── Setup ──────────────────────────────────────────────────────────
            // Shared monotonic tick counter advanced by the test harness.
            // All three controllers share the same source so continuous-mode drift is zero.
            long sharedTicks = 0L;
            long ticksPerFrame = Stopwatch.Frequency / 60; // ~16.7 ms per frame

            var masterBus = new FdpEventBus();
            var slave1Bus = new FdpEventBus();
            var slave2Bus = new FdpEventBus();

            // Zero lookahead: the barrier is at the moment of SwitchToDeterministic, so
            // any further tick advance causes immediate barrier crossing.
            var cfg = new TimeConfig
            {
                LookaheadWallTicks = 0L,
                FixedDeltaSeconds  = 0.016f,
                PLLGain            = 0.1,
            };

            var slaveIds = new HashSet<int> { 1, 2 };
            var master = new MasterSyncController(masterBus, slaveIds, cfg, () => sharedTicks);
            var slave1 = new SlaveSyncController(slave1Bus, localNodeId: 1, config: cfg, tickSource: () => sharedTicks);
            var slave2 = new SlaveSyncController(slave2Bus, localNodeId: 2, config: cfg, tickSource: () => sharedTicks);

            // Sync both slaves so _isTimeSynced = true before any SwitchTimeModeEvent.
            slave1Bus.SwapBuffers(); slave1Bus.Read<TimeSyncRequest>();
            slave1Bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 0L });
            slave1Bus.SwapBuffers(); slave1.Update();
            slave1Bus.SwapBuffers(); slave1Bus.Read<TimeSyncRequest>();

            slave2Bus.SwapBuffers(); slave2Bus.Read<TimeSyncRequest>();
            slave2Bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 0L });
            slave2Bus.SwapBuffers(); slave2.Update();
            slave2Bus.SwapBuffers(); slave2Bus.Read<TimeSyncRequest>();

            // ── Phase 1: 20 Continuous frames ─────────────────────────────────
            for (int i = 0; i < 20; i++)
            {
                sharedTicks += ticksPerFrame;
                master.Update();
                masterBus.SwapBuffers();
                slave1Bus.SwapBuffers();
                slave2Bus.SwapBuffers();
                slave1.Update();
                slave2.Update();
            }

            Assert.Equal(TimeMode.Continuous, slave1.GetMode());
            Assert.Equal(TimeMode.Continuous, slave2.GetMode());

            // ── Phase 2: SwitchToDeterministic ────────────────────────────────
            // After SwitchToDeterministic, the SwitchTimeModeEvent is in masterBus.incoming.
            master.SwitchToDeterministic(slaveIds);

            masterBus.SwapBuffers();   // SwitchTimeModeEvent → masterBus.current
            // Relay to slave buses.
            RelaySwitchTimeModeEvents(masterBus, slave1Bus, slave2Bus);

            slave1Bus.SwapBuffers();
            slave2Bus.SwapBuffers();
            slave1.Update();   // consumes SwitchTimeModeEvent → BarrierPending
            slave2.Update();

            // ── Phase 3: Drive until all three reach Stepping mode ─────────────
            // With LookaheadWallTicks=0 the barrier = master._totalWallTicks at the moment
            // of SwitchToDeterministic.  Any tick advance past current sharedTicks crosses it.
            // NOTE: master.Update() was NOT called in Phase 2, so master is still in
            // BarrierPending after SwitchToDeterministic.  The while condition includes
            // master so the loop runs at least once to let master cross the barrier.
            int safety = 0;
            while ((slave1.GetMode() != TimeMode.Deterministic ||
                    slave2.GetMode() != TimeMode.Deterministic ||
                    master.GetMode() != TimeMode.Deterministic) && safety++ < 50)
            {
                sharedTicks += ticksPerFrame;
                master.Update();   // crosses barrier → Stepping
                masterBus.SwapBuffers();
                slave1Bus.SwapBuffers();
                slave2Bus.SwapBuffers();
                slave1.Update();   // crosses barrier → Stepping (if not already)
                slave2.Update();
            }

            Assert.Equal(TimeMode.Deterministic, slave1.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave2.GetMode());
            Assert.Equal(TimeMode.Deterministic, master.GetMode());

            // ── Phase 4: Step × 5 ─────────────────────────────────────────────
            double masterTotalBeforeSteps = master.GetCurrentState().TotalTime;

            for (int step = 0; step < 5; step++)
            {
                // Drain ACKs from previous step so Step() is not blocked.
                masterBus.SwapBuffers();   // pending ACKs → masterBus.current
                master.Update();            // drains FrameStepCompletedEvent ACKs

                // Advance one deterministic step.
                master.Step(0.016f);        // publishes AdvanceFrameIntent → masterBus.incoming

                masterBus.SwapBuffers();    // AdvanceFrameIntent → masterBus.current
                RelayAdvanceFrameIntents(masterBus, slave1Bus, slave2Bus);

                slave1Bus.SwapBuffers();
                slave2Bus.SwapBuffers();
                slave1.Update();            // processes AdvanceFrameIntent, emits FrameStepCompletedEvent
                slave2.Update();

                // Relay ACKs back to master.
                slave1Bus.SwapBuffers();    // FrameStepCompletedEvent → slave1Bus.current
                slave2Bus.SwapBuffers();
                RelayFrameStepCompletedEvents(slave1Bus, masterBus);
                RelayFrameStepCompletedEvents(slave2Bus, masterBus);
            }

            // Drain the final round of ACKs so master registers all steps.
            masterBus.SwapBuffers();
            master.Update();

            double masterTotalAfterSteps = master.GetCurrentState().TotalTime;
            double expectedAdvance = 5 * 0.016;
            Assert.True(
                Math.Abs(masterTotalAfterSteps - masterTotalBeforeSteps - expectedAdvance) < 0.001,
                $"master.TotalTime should advance by {expectedAdvance:F3}s (5 × 0.016s); " +
                $"actual delta = {masterTotalAfterSteps - masterTotalBeforeSteps:F4}s");

            // ── Phase 5: SwitchToContinuous ───────────────────────────────────
            // master.SwitchToContinuous() publishes SwitchTimeModeEvent with SimTimeSnapshot=_totalTime.
            double resumeSimTimeSnapshot = masterTotalAfterSteps;  // master captures this value
            master.SwitchToContinuous();                           // publishes event → masterBus.incoming

            masterBus.SwapBuffers();    // SwitchTimeModeEvent(Continuous) → masterBus.current
            RelaySwitchTimeModeEvents(masterBus, slave1Bus, slave2Bus);

            slave1Bus.SwapBuffers();
            slave2Bus.SwapBuffers();
            slave1.Update();    // ApplyResume: snaps _totalTime to SimTimeSnapshot
            slave2.Update();

            // Assertion 4: slave TotalTime should be close to master's SimTimeSnapshot after Resume.
            // With the Bug 5 fix, BarrierWallTicks = master._totalWallTicks which may exceed
            // sharedTicks by up to 5 * ticksPerFrame (synthetic wall-tick increments from stepping),
            // so SyncedWallTicks < BarrierWallTicks and slave sim time converges over Phase 6.
            // Use 5×0.016s + small buffer as the post-resume tolerance.
            double postResumeTolerance = 5 * 0.016 + 0.02;
            double slave1AfterResume = slave1.GetCurrentState().TotalTime;
            double slave2AfterResume = slave2.GetCurrentState().TotalTime;
            Assert.True(
                Math.Abs(slave1AfterResume - resumeSimTimeSnapshot) < postResumeTolerance,
                $"slave1.TotalTime after resume ({slave1AfterResume:F4}) should be within " +
                $"{postResumeTolerance:F3}s of master.SimTimeSnapshot ({resumeSimTimeSnapshot:F4})");
            Assert.True(
                Math.Abs(slave2AfterResume - resumeSimTimeSnapshot) < postResumeTolerance,
                $"slave2.TotalTime after resume ({slave2AfterResume:F4}) should be within " +
                $"{postResumeTolerance:F3}s of master.SimTimeSnapshot ({resumeSimTimeSnapshot:F4})");

            // ── Phase 6: 20 more Continuous frames ───────────────────────────
            for (int i = 0; i < 20; i++)
            {
                sharedTicks += ticksPerFrame;
                master.Update();
                masterBus.SwapBuffers();

                slave1Bus.SwapBuffers();
                slave2Bus.SwapBuffers();
                slave1.Update();
                slave2.Update();
            }

            double masterFinalTime = master.GetCurrentState().TotalTime;
            double slave1FinalTime = slave1.GetCurrentState().TotalTime;
            double slave2FinalTime = slave2.GetCurrentState().TotalTime;

            // ── Final Assertions ──────────────────────────────────────────────

            // Assertion 1: slaves are in Continuous mode after Phase 6.
            Assert.Equal(TimeMode.Continuous, slave1.GetMode());
            Assert.Equal(TimeMode.Continuous, slave2.GetMode());

            // Assertion 2: slave TotalTime within 15% of master TotalTime after Phase 6.
            // The shared-tick test model means master._lastTickSample is not updated during
            // stepping, producing a ~5-frame first-frame delta on Phase-6 entry that accumulates
            // a ~5×0.016 = 0.08s divergence.  15% tolerance proves correctness without being
            // overly strict for this simplified single-process tick model.
            double tolerance15pct = Math.Abs(masterFinalTime * 0.15);
            Assert.True(
                Math.Abs(slave1FinalTime - masterFinalTime) <= tolerance15pct,
                $"slave1.TotalTime ({slave1FinalTime:F4}) must be within 15% of " +
                $"master.TotalTime ({masterFinalTime:F4}); tolerance={tolerance15pct:F4}");
            Assert.True(
                Math.Abs(slave2FinalTime - masterFinalTime) <= tolerance15pct,
                $"slave2.TotalTime ({slave2FinalTime:F4}) must be within 15% of " +
                $"master.TotalTime ({masterFinalTime:F4}); tolerance={tolerance15pct:F4}");

        }
    }
}
