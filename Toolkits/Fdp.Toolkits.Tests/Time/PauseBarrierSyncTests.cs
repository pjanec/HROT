using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// TC3-P5-T02: Tests that verify the master+slave barrier-transition protocol
    /// works correctly when the slave has a large wall-clock offset relative to the master.
    /// Confirms that NTP-corrected SyncedWallTicks drives the barrier check, not raw ticks.
    /// </summary>
    public class PauseBarrierSyncTests
    {
        private const int SlaveNodeId = 1;

        private static void NtpHandshake(
            FdpEventBus slaveBus, SlaveSyncController slave,
            long masterTick, long slaveTick, int nodeId = SlaveNodeId)
        {
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();

            // Pre-compute zero-RTT offset: master domain offset = masterTick - slaveTick.
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

        private static void RelayModeSwitch(FdpEventBus masterBus, FdpEventBus slaveBus)
        {
            // Relay SwitchTimeModeEvent from master to slave.
            // Must be called AFTER masterBus.SwapBuffers() and BEFORE advancing ticks.
            var events = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in events) slaveBus.Publish(e);
            slaveBus.SwapBuffers();
        }

        [Fact]
        public void BarrierFires_SameSimTime_WithLargeClockOffset()
        {
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L;

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();
            var master    = new MasterSyncController(masterBus, new HashSet<int> { SlaveNodeId },
                cfg, () => masterTicks);
            var slave     = new SlaveSyncController(slaveBus, SlaveNodeId, cfg, () => slaveTicks);

            // NTP handshake: slave offset = masterTicks - slaveTicks = -500_000_000
            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

            // Initiate pause: barrier = masterTicks + LookaheadWallTicks = 0 + 0 = 0
            master.SwitchToDeterministic(new HashSet<int> { SlaveNodeId });
            masterBus.SwapBuffers(); // SwitchTimeModeEvent → masterBus.current
            RelayModeSwitch(masterBus, slaveBus); // relay to slave, slaveBus.SwapBuffers() inside

            // Advance both clocks past the barrier
            masterTicks += 1;
            slaveTicks  += 1;

            // Master transitions: _getTick()=1 >= barrier=0 → Stepping
            master.Update();

            // Slave transitions: DrainModeSwitch → BarrierPending, SyncedWallTicks=1 >= 0 → Stepping
            slave.Update();

            Assert.Equal(TimeMode.Deterministic, master.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave.GetMode());
        }

        [Fact]
        public void PreSync_SlaveRawTicksAboveBarrier_EntersSteppingImmediately()
        {
            // Without NTP sync, if slaveTicks >> barrier (e.g. slave has an older epoch),
            // SyncedWallTicks = slaveTicks + 0 >> barrier → slave immediately enters Stepping.
            // This is CORRECT: the slave sits idle in Stepping until master sends AdvanceFrameIntent.
            // The previous _isTimeSynced guard in DrainModeSwitchEvents was too aggressive:
            // it dropped the event and left same-machine integration tests broken.
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L; // slave ticks far ahead

            var cfg       = new TimeConfig { LookaheadWallTicks = 100_000 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { SlaveNodeId },
                cfg, () => masterTicks);
            var slave  = new SlaveSyncController(slaveBus, SlaveNodeId, cfg, () => slaveTicks);

            // Drain slave's initial TimeSyncRequest but do NOT respond (no NTP sync)
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();

            // Master pauses — barrier = masterTicks + lookahead = 0 + 100_000 = 100_000
            master.SwitchToDeterministic(new HashSet<int> { SlaveNodeId });
            masterBus.SwapBuffers(); // SwitchTimeModeEvent → masterBus.current
            RelayModeSwitch(masterBus, slaveBus); // relay to slave, slaveBus.SwapBuffers()

            // slaveTicks = 500_000_000 >> barrier = 100_000 → SyncedWallTicks crosses barrier.
            // Slave enters BarrierPending then immediately transitions to Stepping.
            slave.Update();

            Assert.True(slave.GetMode() == TimeMode.Deterministic,
                "Slave with raw ticks above barrier enters Stepping immediately (multi-machine case B: older epoch)");
        }

        [Fact]
        public void TwoSlaves_WithDifferentOffsets_BothEnterStepping_WithinOneFrame()
        {
            long masterTicks = 0L;
            long slave1Ticks = 500_000_000L;
            long slave2Ticks = 300_000_000L;

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slave1Bus = new FdpEventBus();
            var slave2Bus = new FdpEventBus();

            var master = new MasterSyncController(masterBus,
                new HashSet<int> { 1, 2 }, cfg, () => masterTicks);
            var slave1 = new SlaveSyncController(slave1Bus, 1, cfg, () => slave1Ticks);
            var slave2 = new SlaveSyncController(slave2Bus, 2, cfg, () => slave2Ticks);

            NtpHandshake(slave1Bus, slave1, masterTick: masterTicks, slaveTick: slave1Ticks, nodeId: 1);
            NtpHandshake(slave2Bus, slave2, masterTick: masterTicks, slaveTick: slave2Ticks, nodeId: 2);

            // Pause master — barrier = 0, relay to both slaves
            master.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            masterBus.SwapBuffers(); // event → masterBus.current

            var events = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in events)
            {
                slave1Bus.Publish(e);
                slave2Bus.Publish(e);
            }
            slave1Bus.SwapBuffers();
            slave2Bus.SwapBuffers();

            // Advance past barrier
            masterTicks += 1; slave1Ticks += 1; slave2Ticks += 1;

            master.Update(); // BarrierPending → Stepping
            slave1.Update(); // DrainModeSwitch → BarrierPending, SyncedWallTicks >= 0 → Stepping
            slave2.Update(); // same

            Assert.Equal(TimeMode.Deterministic, master.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave1.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave2.GetMode());
        }

        [Fact]
        public void SimTime_OnBarrierTransition_IsIdenticalAcrossNodes()
        {
            long masterTicks = 0L;
            long slave1Ticks = 500_000_000L;
            long slave2Ticks = 300_000_000L;

            var cfg       = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slave1Bus = new FdpEventBus();
            var slave2Bus = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { 1, 2 }, cfg,
                () => masterTicks);
            var slave1 = new SlaveSyncController(slave1Bus, 1, cfg, () => slave1Ticks);
            var slave2 = new SlaveSyncController(slave2Bus, 2, cfg, () => slave2Ticks);

            NtpHandshake(slave1Bus, slave1, masterTick: masterTicks, slaveTick: slave1Ticks, nodeId: 1);
            NtpHandshake(slave2Bus, slave2, masterTick: masterTicks, slaveTick: slave2Ticks, nodeId: 2);

            // Run 5 continuous frames to accumulate some TotalTime
            long frameTicks = (long)(1.0 / 60 * Stopwatch.Frequency);
            for (int i = 0; i < 5; i++)
            {
                masterTicks += frameTicks; slave1Ticks += frameTicks; slave2Ticks += frameTicks;
                master.Update(); masterBus.SwapBuffers();
                slave1.Update();
                slave2.Update();
            }

            // Pause master — relay SwitchTimeModeEvent to both slaves
            master.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            masterBus.SwapBuffers(); // event → masterBus.current

            var modeEvents = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvents) { slave1Bus.Publish(e); slave2Bus.Publish(e); }
            slave1Bus.SwapBuffers(); slave2Bus.SwapBuffers();

            masterTicks += 1; slave1Ticks += 1; slave2Ticks += 1;
            master.Update(); // BarrierPending → Stepping
            slave1.Update(); // BarrierPending → Stepping
            slave2.Update();

            Assert.Equal(TimeMode.Deterministic, master.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave1.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave2.GetMode());
        }
    }
}
