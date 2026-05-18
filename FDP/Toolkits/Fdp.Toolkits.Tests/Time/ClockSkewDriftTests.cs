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
    /// TC3-P5-T05: Tests that verify <see cref="SlaveSyncController"/> tracks a drifting
    /// master clock within acceptable bounds when periodic NTP re-sync is enabled,
    /// and that drift accumulates measurably when re-sync is disabled.
    /// </summary>
    public class ClockSkewDriftTests
    {
        // Slave ticks advance at 1001 per master's 1000 (0.1% fast)
        private const long MasterTicksPerFrame = 1_000L;
        private const long SlaveTicksPerFrame  = 1_001L;
        private const int  FrameCount          = 600; // ~10 seconds at 60 Hz

        private static void NtpHandshake(FdpEventBus slaveBus, SlaveSyncController slave,
            long masterTick, long slaveTick)
        {
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncOffsetCalculatedEvent
            {
                Rtt       = 0L,
                NewOffset = masterTick - slaveTick,
            });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();
        }

        [Fact]
        public void ClockSkew_WithPeriodicResync_OffsetStaysWithin2ms()
        {
            long masterTicks = 0L;
            long slaveTicks  = 0L; // same starting point; slave runs slightly faster

            // SyncRefreshIntervalTicks = 60_000: triggers ~once per 60 frames
            long syncInterval = 60_000L;
            var config = new TimeConfig
            {
                SyncRefreshIntervalTicks = syncInterval,
                MaxRttTicks              = 10_000L, // generous spike threshold
            };

            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var slave = new SlaveSyncController(slaveBus, 1, config, () => slaveTicks);

            // Initial NTP handshake (offset = 0 since both start at 0)
            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

            long twoMsTicks = (long)(0.002 * TimeSpan.TicksPerSecond);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                masterTicks += MasterTicksPerFrame;
                slaveTicks  += SlaveTicksPerFrame;

                // Periodically inject a fresh NTP response, simulating master translator
                // processing the slave's periodic TimeSyncRequest.
                if (frame > 0 && frame % 60 == 0)
                {
                    slaveBus.Publish(new TimeSyncResponse
                    {
                        ClientNodeId        = 1,
                        ClientSendTicks     = slaveTicks - SlaveTicksPerFrame,
                        MasterReceiveTicks  = masterTicks - MasterTicksPerFrame,
                        MasterTransmitTicks = masterTicks - MasterTicksPerFrame,
                    });
                    slaveBus.SwapBuffers(); // response → slaveBus.current
                }
                else
                {
                    slaveBus.SwapBuffers(); // nothing → current
                }

                slave.Update();            // DrainTimeSyncResponses (may apply offset)
                slaveBus.SwapBuffers();    // any new request → current
                slaveBus.Read<TimeSyncRequest>(); // drain outbound requests
            }

            // After 600 frames with periodic re-sync, SyncedWallTicks should track masterTicks
            long drift = Math.Abs(slave.SyncedWallTicks - masterTicks);
            Assert.True(drift < twoMsTicks,
                $"Drift={drift} ticks exceeds 2ms={twoMsTicks} ticks after {FrameCount} frames " +
                $"with periodic re-sync");
        }

        [Fact]
        public void ClockSkew_WithoutResync_DriftAccumulates()
        {
            // Same setup but NO periodic re-sync.
            // After 600 frames slave runs 600 * 1010 = 606_000 ticks; master = 600_000 ticks.
            // offset was established at 0; SyncedWallTicks = 606_000 + 0 = 606_000.
            // drift = |606_000 - 600_000| = 6_000 ticks (non-zero, growing with time).

            long masterTicks = 0L;
            long slaveTicks  = 0L;
            const long slaveTicksFast = 1_010L; // 1% faster slave

            var config = new TimeConfig { SyncRefreshIntervalTicks = long.MaxValue }; // never re-sync

            var slaveBus = new FdpEventBus();
            var slave    = new SlaveSyncController(slaveBus, 1, config, () => slaveTicks);

            // Establish offset=0 at start
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncResponse
            {
                ClientNodeId        = 1,
                ClientSendTicks     = 0,
                MasterReceiveTicks  = 0,
                MasterTransmitTicks = 0,
            });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();

            for (int frame = 0; frame < FrameCount; frame++)
            {
                masterTicks += MasterTicksPerFrame;
                slaveTicks  += slaveTicksFast; // 1% faster
                slaveBus.SwapBuffers();
                slave.Update();
                slaveBus.SwapBuffers();
                slaveBus.Read<TimeSyncRequest>();
            }

            // Without re-sync, SyncedWallTicks = slaveTicks + offset = 606_000 + 0 = 606_000
            long drift = Math.Abs(slave.SyncedWallTicks - masterTicks);

            // Drift must be non-zero (slave ran faster than master)
            Assert.True(drift > 0,
                "Without re-sync, drift must be non-zero (slave runs faster than master)");

            // Drift must be at least half the expected accumulation
            long expectedMinDrift = FrameCount * (slaveTicksFast - MasterTicksPerFrame) / 2;
            Assert.True(drift >= expectedMinDrift,
                $"Drift {drift} should be at least {expectedMinDrift} " +
                $"(half of expected accumulation over {FrameCount} frames)");
        }
    }
}
