using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost_Core.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// TC3-P5-T01: Unit tests for NTP offset computation inside
    /// <see cref="SlaveSyncController.DrainTimeSyncResponses"/>.
    /// Verifies zero-latency snap, symmetric cancellation, asymmetric bounding,
    /// spike rejection, hard-snap on first sync, and gentle steering on subsequent syncs.
    /// </summary>
    public class TimeSyncOffsetTests
    {
        private static long GetOffset(SlaveSyncController ctrl)
            => (long)typeof(SlaveSyncController)
                .GetField("_masterWallClockOffset",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

        [Fact]
        public void Offset_ZeroLatency_ExactlyCapturesMasterDomain()
        {
            // Zero-latency: t1=0, t2=5_000_000, t3=5_000_000, t4=1.
            // Formula (run by translator): offset = ((5_000_000-0) + (5_000_000-1))/2 = 4_999_999
            // This pre-computed value is what the translator publishes to the bus.
            long slaveTicks = 0L;
            long masterTick = 5_000_000L;

            var bus  = new FdpEventBus();
            var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            long expectedOffset = (masterTick + masterTick - 1L) / 2; // = 4_999_999
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 1L, NewOffset = expectedOffset });
            bus.SwapBuffers();
            ctrl.Update();

            long offset = GetOffset(ctrl);
            Assert.True(Math.Abs(offset - masterTick) <= 2,
                $"Offset {offset} should be ~{masterTick} (within 2 ticks). Got {Math.Abs(offset - masterTick)} tick error.");
        }

        [Fact]
        public void Offset_SymmetricLatency_CancelsOut()
        {
            // t1=0, t2=5_000_100, t3=5_000_100, t4=200 (100 up + 100 down).
            // Translator computes: offset = ((5_000_100-0) + (5_000_100-200))/2 = 5_000_000.

            long slaveTicks = 0L;
            var bus  = new FdpEventBus();
            var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 200L, NewOffset = 5_000_000L });
            bus.SwapBuffers();
            ctrl.Update();

            long offset = GetOffset(ctrl);
            Assert.Equal(5_000_000L, offset);
        }

        [Fact]
        public void Offset_AsymmetricLatency_IsWithinHalfRTT()
        {
            // t1=0, t2=5_000_100, t3=5_000_100, t4=400 (asymmetric: uplink=100, downlink=300).
            // Translator computes: RTT=400, offset = ((5_000_100-0)+(5_000_100-400))/2 = 4_999_900.
            // Error = |4_999_900 - 5_000_000| = 100 <= RTT/2 = 200.

            long slaveTicks = 0L;
            var bus  = new FdpEventBus();
            var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            long trueOffset     = 5_000_000L;
            long rtt            = 400L;
            long computedOffset = 4_999_900L;
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = rtt, NewOffset = computedOffset });
            bus.SwapBuffers();
            ctrl.Update();

            long offset = GetOffset(ctrl);
            Assert.True(Math.Abs(offset - trueOffset) <= rtt / 2,
                $"Error {Math.Abs(offset - trueOffset)} must be <= RTT/2 = {rtt / 2}");
        }

        [Fact]
        public void SpikeRejection_HighRTT_OffsetUnchanged()
        {
            long slaveTicks = 0L;
            var config = new TimeConfig { MaxRttTicks = 500 };
            var bus    = new FdpEventBus();
            var ctrl   = new SlaveSyncController(bus, 1, config, () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // RTT = 1001 > MaxRttTicks (500) → rejected by controller
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 1001L, NewOffset = 300_000L });
            bus.SwapBuffers();
            ctrl.Update();

            Assert.Equal(0L, GetOffset(ctrl));
        }

        [Fact]
        public void HardSnap_FirstSync_IgnoresWeight()
        {
            long slaveTicks = 0L;
            var bus  = new FdpEventBus();
            var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Translator pre-computed: offset=300_000, RTT=0
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 300_000L });
            bus.SwapBuffers();
            ctrl.Update();

            // Hard-snap: should be 300_000 not 300_000 * 0.1 = 30_000
            Assert.Equal(300_000L, GetOffset(ctrl));
        }

        [Fact]
        public void GentleSteering_SubsequentSync_WeightApplied()
        {
            long slaveTicks = 0L;
            var bus  = new FdpEventBus();
            var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // First sync → hard-snap to 300_000
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 300_000L });
            bus.SwapBuffers();
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>(); // drain any follow-up request

            // Second sync → newOffset = 310_000 → gentle steer
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 310_000L });
            bus.SwapBuffers();
            ctrl.Update();

            long expected = 300_000L + (long)((310_000L - 300_000L) * 0.1); // = 301_000
            Assert.Equal(expected, GetOffset(ctrl));
        }
    }
}
