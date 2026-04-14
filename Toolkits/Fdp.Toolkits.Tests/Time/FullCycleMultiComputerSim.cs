using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost.Core.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// TC3-P5-T04: Full end-to-end scenario tests.
    /// Validates the complete Continuous → Pause → Step×5 → Resume cycle with one and
    /// two slaves that each have a large wall-clock offset from the master.
    /// </summary>
    public class FullCycleMultiComputerSim
    {
        private static void NtpHandshake(FdpEventBus slaveBus, SlaveSyncController slave,
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

        [Fact]
        public void FullCycle_OneSlaveOffset_PauseStepResume_SimTimesConverge()
        {
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L;

            float delta      = 1f / 60f;
            long  frameTicks = (long)(delta * Stopwatch.Frequency);

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg,
                () => masterTicks);
            var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

            // Phase 0: NTP handshake — slave gets offset = masterTicks - slaveTicks = -500_000_000
            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

            // Phase 1: 20 continuous frames
            for (int i = 0; i < 20; i++)
            {
                masterTicks += frameTicks; slaveTicks += frameTicks;
                master.Update(); masterBus.SwapBuffers();
                slave.Update(); slaveBus.SwapBuffers();
            }

            // Phase 2: Pause — barrier = masterTicks + 0 = masterTicks (LookaheadWallTicks=0)
            master.SwitchToDeterministic(new HashSet<int> { 1 });
            masterBus.SwapBuffers(); // SwitchTimeModeEvent → masterBus.current

            var modeEvts = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvts) slaveBus.Publish(e);
            slaveBus.SwapBuffers(); // event → slaveBus.current

            masterTicks += 1; slaveTicks += 1; // advance past barrier
            master.Update();  // BarrierPending → Stepping
            slave.Update();   // DrainModeSwitch → BarrierPending → Stepping

            Assert.Equal(TimeMode.Deterministic, master.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave.GetMode());

            // Phase 3: 5 deterministic steps
            for (int i = 0; i < 5; i++)
            {
                master.Step(delta);
                masterBus.SwapBuffers(); // AdvanceFrameIntent → masterBus.current

                var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) slaveBus.PublishManaged(x);
                slaveBus.SwapBuffers(); // intent → slaveBus.current
                slave.Update();         // snaps TotalTime to TargetSimTime, emits ACK
                slaveBus.SwapBuffers(); // ACK → slaveBus.current

                var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in acks) masterBus.PublishManaged(a);
                masterBus.SwapBuffers(); // ACK → masterBus.current
                master.Update();         // processes ACK, clears _pendingAcks

                Assert.Equal(master.GetCurrentState().TotalTime,
                             slave.GetCurrentState().TotalTime, precision: 10);
            }

            // Phase 4: Resume
            master.SwitchToContinuous();
            masterBus.SwapBuffers(); // SwitchTimeModeEvent(Continuous) → masterBus.current
            var resumeEvts = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in resumeEvts) slaveBus.Publish(e);
            slaveBus.SwapBuffers(); // event → slaveBus.current

            for (int i = 0; i < 20; i++)
            {
                masterTicks += frameTicks; slaveTicks += frameTicks;
                master.Update(); masterBus.SwapBuffers();
                slave.Update(); slaveBus.SwapBuffers();
            }

            Assert.Equal(TimeMode.Continuous, master.GetMode());
            Assert.Equal(TimeMode.Continuous, slave.GetMode());
            Assert.True(master.GetCurrentState().FrameNumber > 0);
        }

        [Fact]
        public void FullCycle_TwoSlavesLargeOffsets_AllSimTimesMatch()
        {
            long masterTicks = 0L;
            long s1Ticks     = 500_000_000L;
            long s2Ticks     = 300_000_000L;

            float delta      = 1f / 60f;

            var cfg  = new TimeConfig { LookaheadWallTicks = 0 };
            var mBus = new FdpEventBus();
            var s1Bus = new FdpEventBus();
            var s2Bus = new FdpEventBus();

            var master = new MasterSyncController(mBus, new HashSet<int> { 1, 2 }, cfg,
                () => masterTicks);
            var slave1 = new SlaveSyncController(s1Bus, 1, cfg, () => s1Ticks);
            var slave2 = new SlaveSyncController(s2Bus, 2, cfg, () => s2Ticks);

            NtpHandshake(s1Bus, slave1, masterTick: masterTicks, slaveTick: s1Ticks, nodeId: 1);
            NtpHandshake(s2Bus, slave2, masterTick: masterTicks, slaveTick: s2Ticks, nodeId: 2);

            // Pause — relay to both slaves
            master.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            mBus.SwapBuffers();

            var modeEvts = mBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvts) { s1Bus.Publish(e); s2Bus.Publish(e); }
            s1Bus.SwapBuffers(); s2Bus.SwapBuffers();

            masterTicks += 1; s1Ticks += 1; s2Ticks += 1;
            master.Update(); // BarrierPending → Stepping
            slave1.Update(); slave2.Update();

            // 5 steps
            for (int i = 0; i < 5; i++)
            {
                master.Step(delta);
                mBus.SwapBuffers(); // intent → mBus.current

                var intents = mBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) { s1Bus.PublishManaged(x); s2Bus.PublishManaged(x); }
                s1Bus.SwapBuffers(); s2Bus.SwapBuffers();
                slave1.Update(); slave2.Update();
                s1Bus.SwapBuffers(); s2Bus.SwapBuffers();

                var ack1 = s1Bus.ConsumeManaged<FrameStepCompletedEvent>();
                var ack2 = s2Bus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in ack1) mBus.PublishManaged(a);
                foreach (var a in ack2) mBus.PublishManaged(a);
                mBus.SwapBuffers(); // ACKs → mBus.current
                master.Update();    // processes ACKs

                double mt = master.GetCurrentState().TotalTime;
                Assert.Equal(mt, slave1.GetCurrentState().TotalTime, precision: 10);
                Assert.Equal(mt, slave2.GetCurrentState().TotalTime, precision: 10);
            }
        }
    }
}
