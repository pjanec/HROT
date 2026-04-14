using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// TC3-P5-T03: Tests that verify slave sim-time is identical to master sim-time
    /// after each lockstep frame.  The slave must snap to <c>TargetSimTime</c> from
    /// every <see cref="AdvanceFrameIntent"/>, yielding bit-exact agreement.
    /// </summary>
    public class LockstepSimTimeAccuracyTests
    {
        private static void NtpHandshake(
            FdpEventBus slaveBus, SlaveSyncController slave,
            long masterTick, long slaveTick, int nodeId = 1)
        {
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncOffsetCalculatedEvent
            {
                Rtt       = 0L,
                NewOffset = masterTick - slaveTick,
            });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
        }

        /// <summary>
        /// Transition master+slave pair into Stepping mode (LookaheadWallTicks must be 0).
        /// After this call both controllers are in <see cref="TimeMode.Deterministic"/>.
        /// </summary>
        private static void TransitionToStepping(
            MasterSyncController master, SlaveSyncController slave,
            FdpEventBus masterBus, FdpEventBus slaveBus,
            ref long masterTicks, ref long slaveTicks,
            HashSet<int> slaveIds)
        {
            // Initiate pause: emit SwitchTimeModeEvent with barrier = current masterTicks (LookaheadWallTicks=0)
            master.SwitchToDeterministic(slaveIds);
            masterBus.SwapBuffers(); // SwitchTimeModeEvent → masterBus.current

            // Relay mode-switch event to slave(s)
            var modeEvts = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvts) slaveBus.Publish(e);
            slaveBus.SwapBuffers(); // event → slaveBus.current

            // Advance ticks to cross barrier (barrier = masterTicks + 0 = masterTicks)
            masterTicks += 1;
            slaveTicks  += 1;

            master.Update(); // BarrierPending → Stepping
            slave.Update();  // DrainModeSwitch → BarrierPending; SyncedWallTicks >= barrier → Stepping
        }

        [Fact]
        public void FirstStep_SlaveSimTime_EqualsMasterSimTime()
        {
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L;

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg,
                () => masterTicks);
            var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);
            TransitionToStepping(master, slave, masterBus, slaveBus,
                ref masterTicks, ref slaveTicks, new HashSet<int> { 1 });

            Assert.Equal(TimeMode.Deterministic, master.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave.GetMode());

            // Step once
            float delta = 1f / 60f;
            master.Step(delta);
            masterBus.SwapBuffers(); // AdvanceFrameIntent → masterBus.current

            var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
            foreach (var i in intents) slaveBus.PublishManaged(i);
            slaveBus.SwapBuffers();   // intent → slaveBus.current
            slave.Update();           // snaps TotalTime to TargetSimTime, emits ACK
            slaveBus.SwapBuffers();   // ACK → slaveBus.current

            var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
            foreach (var a in acks) masterBus.PublishManaged(a);
            masterBus.SwapBuffers();  // ACK → masterBus.current
            master.Update();          // processes ACK, clears _pendingAcks

            double masterTime = master.GetCurrentState().TotalTime;
            double slaveTime  = slave.GetCurrentState().TotalTime;

            Assert.Equal(masterTime, slaveTime, precision: 10);
        }

        [Fact]
        public void TenSteps_SlaveSimTime_EqualsMasterSimTimeAfterEachStep()
        {
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L;

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg,
                () => masterTicks);
            var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);
            TransitionToStepping(master, slave, masterBus, slaveBus,
                ref masterTicks, ref slaveTicks, new HashSet<int> { 1 });

            float delta = 1f / 60f;
            for (int i = 0; i < 10; i++)
            {
                master.Step(delta);
                masterBus.SwapBuffers();  // intent → masterBus.current

                var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) slaveBus.PublishManaged(x);
                slaveBus.SwapBuffers();   // intent → slaveBus.current
                slave.Update();
                slaveBus.SwapBuffers();   // ACK → slaveBus.current

                var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in acks) masterBus.PublishManaged(a);
                masterBus.SwapBuffers();  // ACK → masterBus.current
                master.Update();          // processes ACK

                Assert.Equal(master.GetCurrentState().TotalTime,
                             slave.GetCurrentState().TotalTime, precision: 10);
            }
        }

        [Fact]
        public void TwoSlaves_BothSnapToMasterSimTime_PerStep()
        {
            long masterTicks = 0L, s1Ticks = 500_000_000L, s2Ticks = 300_000_000L;
            var cfg   = new TimeConfig { LookaheadWallTicks = 0 };
            var mBus  = new FdpEventBus();
            var s1Bus = new FdpEventBus();
            var s2Bus = new FdpEventBus();

            var master = new MasterSyncController(mBus, new HashSet<int> { 1, 2 }, cfg,
                () => masterTicks);
            var slave1 = new SlaveSyncController(s1Bus, 1, cfg, () => s1Ticks);
            var slave2 = new SlaveSyncController(s2Bus, 2, cfg, () => s2Ticks);

            NtpHandshake(s1Bus, slave1, masterTick: masterTicks, slaveTick: s1Ticks, nodeId: 1);
            NtpHandshake(s2Bus, slave2, masterTick: masterTicks, slaveTick: s2Ticks, nodeId: 2);

            // Transition to Stepping
            master.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            mBus.SwapBuffers();

            var modeEvts = mBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvts) { s1Bus.Publish(e); s2Bus.Publish(e); }
            s1Bus.SwapBuffers(); s2Bus.SwapBuffers();

            masterTicks += 1; s1Ticks += 1; s2Ticks += 1;
            master.Update(); // BarrierPending → Stepping
            slave1.Update(); // same
            slave2.Update();

            float delta = 1f / 60f;
            for (int i = 0; i < 5; i++)
            {
                master.Step(delta);
                mBus.SwapBuffers();  // intent → mBus.current

                var intents = mBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) { s1Bus.PublishManaged(x); s2Bus.PublishManaged(x); }
                s1Bus.SwapBuffers(); s2Bus.SwapBuffers();
                slave1.Update(); slave2.Update();
                s1Bus.SwapBuffers(); s2Bus.SwapBuffers();

                var ack1 = s1Bus.ConsumeManaged<FrameStepCompletedEvent>();
                var ack2 = s2Bus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in ack1) mBus.PublishManaged(a);
                foreach (var a in ack2) mBus.PublishManaged(a);
                mBus.SwapBuffers();  // ACKs → mBus.current
                master.Update();     // processes ACKs

                double masterTime = master.GetCurrentState().TotalTime;
                Assert.Equal(masterTime, slave1.GetCurrentState().TotalTime, precision: 10);
                Assert.Equal(masterTime, slave2.GetCurrentState().TotalTime, precision: 10);
            }
        }

        [Fact]
        public void Resume_AfterLockstep_SlaveContinuesFromMasterSimTime()
        {
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L;
            long frameTicks  = (long)(1.0 / 60 * Stopwatch.Frequency);

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg,
                () => masterTicks);
            var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);
            TransitionToStepping(master, slave, masterBus, slaveBus,
                ref masterTicks, ref slaveTicks, new HashSet<int> { 1 });

            // Step 3 times
            float delta = 1f / 60f;
            for (int i = 0; i < 3; i++)
            {
                master.Step(delta);
                masterBus.SwapBuffers();

                var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) slaveBus.PublishManaged(x);
                slaveBus.SwapBuffers();
                slave.Update();
                slaveBus.SwapBuffers();

                var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in acks) masterBus.PublishManaged(a);
                masterBus.SwapBuffers();
                master.Update();
            }

            // Resume: master emits SwitchTimeModeEvent(Continuous), relay to slave
            master.SwitchToContinuous();
            masterBus.SwapBuffers(); // event → masterBus.current
            var resumeEvts = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in resumeEvts) slaveBus.Publish(e);
            slaveBus.SwapBuffers(); // event → slaveBus.current

            masterTicks += frameTicks; slaveTicks += frameTicks;
            master.Update(); masterBus.SwapBuffers();
            slave.Update();  // DrainModeSwitch → ApplyResume → Continuous

            Assert.Equal(TimeMode.Continuous, master.GetMode());
            Assert.Equal(TimeMode.Continuous, slave.GetMode());
            // Slave should be within 50ms of master after resume
            Assert.True(Math.Abs(slave.GetCurrentState().TotalTime - master.GetCurrentState().TotalTime)
                < 0.05,
                "Slave TotalTime should be within 50ms of master after resume");
        }
    }
}
