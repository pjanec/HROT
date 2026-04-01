using System;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using FDP.Toolkit.Time.Translators;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// Unit tests for MasterTimeSyncTranslator (TC3-P4-T01), SlaveTimeSyncTranslator (TC3-P4-T02),
    /// and the TimeNetworkModule factory methods (TC3-P4-T03).
    /// All tests use null-participant construction so no DDS infrastructure is required.
    /// </summary>
    public class TimeSyncTranslatorTests
    {
        // ── MasterTimeSyncTranslator ──────────────────────────────────────────

        /// <summary>TC3-P4-T01-SC1 — null participant; no code path must throw.</summary>
        [Fact]
        public void MasterTimeSyncTranslator_NullParticipant_PollIngress_IsNoOp()
        {
            var t = new MasterTimeSyncTranslator(participant: null);
            t.ScanAndPublish(null!);
            t.PollIngress(null!, null!);    // must not throw
        }

        /// <summary>TC3-P4-T01-SC2 — metadata must match the design spec.</summary>
        [Fact]
        public void MasterTimeSyncTranslator_DescriptorOrdinalAndTopicName_AreCorrect()
        {
            var t = new MasterTimeSyncTranslator(participant: null);
            Assert.Equal(205L, t.DescriptorOrdinal);
            Assert.Equal("TimeSyncRequest", t.TopicName);
        }

        // ── SlaveTimeSyncTranslator ───────────────────────────────────────────

        /// <summary>TC3-P4-T02-SC1 — null participant; no code path must throw.</summary>
        [Fact]
        public void SlaveTimeSyncTranslator_NullParticipant_IsNoOp()
        {
            var bus = new FdpEventBus();
            bus.Register<TimeSyncRequest>();
            var t = new SlaveTimeSyncTranslator(participant: null, eventBus: bus, localNodeId: 1);
            t.ScanAndPublish(null!);
            t.PollIngress(null!, null!);    // must not throw
        }

        /// <summary>
        /// TC3-P4-T02-SC2 — ScanAndPublish drains TimeSyncRequests from the bus even when the
        /// DDS writer is absent (null participant), preventing bus buildup.
        /// </summary>
        [Fact]
        public void SlaveTimeSyncTranslator_ScanAndPublish_DrainsRequestsFromBus()
        {
            var bus = new FdpEventBus();
            bus.Register<TimeSyncRequest>();
            var t = new SlaveTimeSyncTranslator(participant: null, eventBus: bus, localNodeId: 1);

            // Publish a request and make it readable
            bus.Publish(new TimeSyncRequest { ClientNodeId = 1, ClientSendTicks = 100 });
            bus.SwapBuffers();

            // Translator drains the request (DDS write is no-op with null participant)
            t.ScanAndPublish(null!);

            // After swap, bus should be empty
            bus.SwapBuffers();
            var remaining = bus.Consume<TimeSyncRequest>();
            Assert.True(remaining.IsEmpty,
                "SlaveTimeSyncTranslator should drain TimeSyncRequests from bus even with null participant");
        }

        /// <summary>TC3-P4-T02-SC3 — metadata must match the design spec.</summary>
        [Fact]
        public void SlaveTimeSyncTranslator_DescriptorOrdinalAndTopicName_AreCorrect()
        {
            var bus = new FdpEventBus();
            var t = new SlaveTimeSyncTranslator(participant: null, eventBus: bus, localNodeId: 1);
            Assert.Equal(206L, t.DescriptorOrdinal);
            Assert.Equal("TimeSyncResponse", t.TopicName);
        }

        // ── TimeNetworkModule factory methods ─────────────────────────────────

        /// <summary>TC3-P4-T03-SC1 — factory returns a non-null instance with null participant.</summary>
        [Fact]
        public void TimeNetworkModule_CreateMasterTimeSyncTranslator_NullParticipant_ReturnsInstance()
        {
            var t = TimeNetworkModule.CreateMasterTimeSyncTranslator(null);
            Assert.NotNull(t);
        }

        /// <summary>TC3-P4-T03-SC2 — factory returns a non-null instance with null participant.</summary>
        [Fact]
        public void TimeNetworkModule_CreateSlaveTimeSyncTranslator_NullParticipant_ReturnsInstance()
        {
            var bus = new FdpEventBus();
            var t = TimeNetworkModule.CreateSlaveTimeSyncTranslator(null, bus, 5);
            Assert.NotNull(t);
        }

        /// <summary>TC3-P4-T03-SC3 — null eventBus must throw ArgumentNullException.</summary>
        [Fact]
        public void TimeNetworkModule_CreateSlaveTimeSyncTranslator_NullBus_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                TimeNetworkModule.CreateSlaveTimeSyncTranslator(null, null!, 5));
        }

        // ── NTP formula (moved from controller to translator) ─────────────────

        /// <summary>
        /// Verifies the NTP formula: zero-latency case produces offset ≈ master tick.
        /// </summary>
        [Fact]
        public void SlaveTimeSyncTranslator_NtpFormula_ZeroLatency_CapturesMasterDomain()
        {
            // t1=0, t2=5_000_000, t3=5_000_000, t4=1
            var (offset, rtt) = SlaveTimeSyncTranslator.NtpCompute(0L, 5_000_000L, 5_000_000L, 1L);

            Assert.Equal(1L, rtt);
            Assert.True(Math.Abs(offset - 5_000_000L) <= 2,
                $"offset {offset} should be ~5_000_000");
        }

        /// <summary>
        /// Verifies the NTP formula: symmetric latency cancels out perfectly.
        /// </summary>
        [Fact]
        public void SlaveTimeSyncTranslator_NtpFormula_SymmetricLatency_CancelsOut()
        {
            // t1=0, t2=5_000_100, t3=5_000_100, t4=200 (100 up + 100 down)
            var (offset, rtt) = SlaveTimeSyncTranslator.NtpCompute(0L, 5_000_100L, 5_000_100L, 200L);

            Assert.Equal(200L, rtt);
            Assert.Equal(5_000_000L, offset);
        }

        /// <summary>
        /// Verifies the NTP formula: asymmetric latency keeps error within RTT/2.
        /// </summary>
        [Fact]
        public void SlaveTimeSyncTranslator_NtpFormula_AsymmetricLatency_ErrorWithinHalfRtt()
        {
            // t1=0, t2=5_000_100, t3=5_000_100, t4=400 (uplink=100, downlink=300)
            var (offset, rtt) = SlaveTimeSyncTranslator.NtpCompute(0L, 5_000_100L, 5_000_100L, 400L);

            long trueOffset = 5_000_000L;
            Assert.True(Math.Abs(offset - trueOffset) <= rtt / 2,
                $"Error {Math.Abs(offset - trueOffset)} must be <= RTT/2={rtt / 2}");
        }

        /// <summary>
        /// Verifies t1 overwrite: ScanAndPublish drains requests even with null participant.
        /// The tick capture happens inside the writer branch so can't be verified without DDS,
        /// but this test confirms the bus drain still occurs (no buildup).
        /// </summary>
        [Fact]
        public void SlaveTimeSyncTranslator_ScanAndPublish_DrainsRequest_NullParticipant()
        {
            var bus = new FdpEventBus();
            bus.Register<TimeSyncRequest>();
            long ticks = 12345L;
            var t = new SlaveTimeSyncTranslator(participant: null, eventBus: bus, localNodeId: 1,
                tickSource: () => ticks);

            bus.Publish(new TimeSyncRequest { ClientNodeId = 1, ClientSendTicks = 1L });
            bus.SwapBuffers();

            t.ScanAndPublish(null!); // no-op for DDS write, but must drain bus

            bus.SwapBuffers();
            Assert.True(bus.Consume<TimeSyncRequest>().IsEmpty,
                "Request must be drained from bus even with null DDS participant");
        }
    }
}
