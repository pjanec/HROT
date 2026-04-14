using System.Reflection;
using CycloneDDS.Schema;
using Fdp.Kernel;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using MessagePack;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// Unit tests for TCU-M001 (wire DTO fields and round-trip) and
    /// TCU-M002 (local domain message types via FdpEventBus).
    /// </summary>
    public class TimeMessagesTests
    {
        // ── TCU-M001: SwitchTimeModeWireDto round-trip ─────────────────────────

        [Fact]
        public void SwitchTimeModeWireDto_RoundTrip()
        {
            var original = new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = 9_876_543_210L,
                FixedDelta       = 0.016f,
                SimTimeSnapshot  = 123.456,
                TimeScale        = 2.0f,
            };

            var result = SwitchTimeModeWireDto.ToWire(original).ToEvent();

            Assert.Equal(original.TargetMode,       result.TargetMode);
            Assert.Equal(original.BarrierWallTicks,  result.BarrierWallTicks);
            Assert.Equal(original.FixedDelta,        result.FixedDelta);
            Assert.Equal(original.SimTimeSnapshot,   result.SimTimeSnapshot);
            Assert.Equal(original.TimeScale,         result.TimeScale);
        }

        [Fact]
        public void SwitchTimeModeWireDto_ToWire_PreservesAllFields()
        {
            var evt = new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = 1_000_000L,
                FixedDelta       = 0.033f,
                SimTimeSnapshot  = 99.99,
                TimeScale        = 0.5f,
            };

            var wire = SwitchTimeModeWireDto.ToWire(evt);

            Assert.Equal((int)evt.TargetMode,   wire.TargetModeInt);
            Assert.Equal(evt.BarrierWallTicks,   wire.BarrierWallTicks);
            Assert.Equal(evt.FixedDelta,         wire.FixedDelta);
            Assert.Equal(evt.SimTimeSnapshot,    wire.SimTimeSnapshot);
            Assert.Equal(evt.TimeScale,          wire.TimeScale);
        }

        // ── TCU-M001: FrameOrderDescriptor ────────────────────────────────────

        [Fact]
        public void FrameOrderDescriptor_HasTargetSimTime()
        {
            var descriptor = new FrameOrderDescriptor { TargetSimTime = 42.5 };
            Assert.Equal(42.5, descriptor.TargetSimTime);
        }

        [Fact]
        public void FrameOrderDescriptor_PlainFields_NoCsharpProperties()
        {
            var props = typeof(FrameOrderDescriptor)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            Assert.Empty(props);
        }

        // ── TCU-M001: Additional plain-field verification ─────────────────────

        [Fact]
        public void FrameAckDescriptor_PlainFields_NoCsharpProperties()
        {
            var props = typeof(FrameAckDescriptor)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            Assert.Empty(props);
        }

        [Fact]
        public void SwitchTimeModeEvent_PlainFields_NoCsharpProperties()
        {
            var props = typeof(SwitchTimeModeEvent)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            Assert.Empty(props);
        }

        // ── TCU-M002: Local domain events via FdpEventBus ─────────────────────

        [Fact]
        public void AdvanceFrameIntent_CanBePublishedAndConsumed()
        {
            // Domain types carry no [EventId] — use the managed path which identifies
            // types by full name hash rather than requiring an attribute.
            var bus = new FdpEventBus();

            bus.PublishManaged(new AdvanceFrameIntent
            {
                FrameID       = 7L,
                FixedDelta    = 0.016f,
                TargetSimTime = 3.14,
            });

            bus.SwapBuffers();

            var events = bus.ConsumeManaged<AdvanceFrameIntent>();

            Assert.Single(events);
            Assert.Equal(7L,    events[0].FrameID);
            Assert.Equal(0.016f, events[0].FixedDelta);
            Assert.Equal(3.14,  events[0].TargetSimTime);
        }

        [Fact]
        public void FrameStepCompletedEvent_CanBePublishedAndConsumed()
        {
            var bus = new FdpEventBus();

            bus.PublishManaged(new FrameStepCompletedEvent
            {
                FrameID = 42L,
                NodeID  = 5,
            });

            bus.SwapBuffers();

            var events = bus.ConsumeManaged<FrameStepCompletedEvent>();

            Assert.Single(events);
            Assert.Equal(42L, events[0].FrameID);
            Assert.Equal(5,   events[0].NodeID);
        }

        // ── TC3-P1-T01: TimeSyncRequest / TimeSyncResponse ───────────────────

        /// <summary>TC3-P1-T01-SC1 — MessagePack round-trip preserves all TimeSyncRequest fields.</summary>
        [Fact]
        public void TimeSyncRequest_RoundTrip_PreservesAllFields()
        {
            var original = new TimeSyncRequest
            {
                ClientNodeId    = 99,
                ClientSendTicks = 1_234_567_890_123L,
            };

            byte[] bytes = MessagePackSerializer.Serialize(original);
            var result   = MessagePackSerializer.Deserialize<TimeSyncRequest>(bytes);

            Assert.Equal(original.ClientNodeId,    result.ClientNodeId);
            Assert.Equal(original.ClientSendTicks, result.ClientSendTicks);
        }

        /// <summary>TC3-P1-T01-SC2 — MessagePack round-trip preserves all TimeSyncResponse fields.</summary>
        [Fact]
        public void TimeSyncResponse_RoundTrip_PreservesAllFields()
        {
            var original = new TimeSyncResponse
            {
                ClientNodeId       = 7,
                ClientSendTicks    = 1_111_111_111L,
                MasterReceiveTicks = 2_222_222_222L,
                MasterTransmitTicks = 3_333_333_333L,
            };

            byte[] bytes = MessagePackSerializer.Serialize(original);
            var result   = MessagePackSerializer.Deserialize<TimeSyncResponse>(bytes);

            Assert.Equal(original.ClientNodeId,        result.ClientNodeId);
            Assert.Equal(original.ClientSendTicks,     result.ClientSendTicks);
            Assert.Equal(original.MasterReceiveTicks,  result.MasterReceiveTicks);
            Assert.Equal(original.MasterTransmitTicks, result.MasterTransmitTicks);
        }

        /// <summary>TC3-P1-T01-SC3 — FdpEventBus publish/consume round-trip for TimeSyncRequest.</summary>
        [Fact]
        public void TimeSyncRequest_FdpEventBus_PublishConsume_RoundTrip()
        {
            var bus = new FdpEventBus();
            bus.Register<TimeSyncRequest>();

            var original = new TimeSyncRequest
            {
                ClientNodeId    = 42,
                ClientSendTicks = 9_876_543_210L,
            };

            bus.Publish(original);
            bus.SwapBuffers();

            var events = bus.Consume<TimeSyncRequest>().ToArray();

            Assert.Single(events);
            Assert.Equal(original.ClientNodeId,    events[0].ClientNodeId);
            Assert.Equal(original.ClientSendTicks, events[0].ClientSendTicks);
        }

        /// <summary>TC3-P1-T01-SC4 — FdpEventBus publish/consume round-trip for TimeSyncResponse.</summary>
        [Fact]
        public void TimeSyncResponse_FdpEventBus_PublishConsume_RoundTrip()
        {
            var bus = new FdpEventBus();
            bus.Register<TimeSyncResponse>();

            var original = new TimeSyncResponse
            {
                ClientNodeId        = 3,
                ClientSendTicks     = 1_000_000L,
                MasterReceiveTicks  = 1_001_000L,
                MasterTransmitTicks = 1_001_500L,
            };

            bus.Publish(original);
            bus.SwapBuffers();

            var events = bus.Consume<TimeSyncResponse>().ToArray();

            Assert.Single(events);
            Assert.Equal(original.ClientNodeId,        events[0].ClientNodeId);
            Assert.Equal(original.ClientSendTicks,     events[0].ClientSendTicks);
            Assert.Equal(original.MasterReceiveTicks,  events[0].MasterReceiveTicks);
            Assert.Equal(original.MasterTransmitTicks, events[0].MasterTransmitTicks);
        }

        // ── Bug 2 regression: SwitchTimeModeWireDto must have TransientLocal QoS ──

        /// <summary>
        /// Bug 2 regression — SwitchTimeModeWireDto must carry TransientLocal durability so
        /// CycloneDDS buffers the last sample.  Late-joining slaves (IG, ExCon) receive the
        /// cached baseline the moment they connect, eliminating the startup offset.
        /// </summary>
        [Fact]
        public void SwitchTimeModeWireDto_HasTransientLocalQos()
        {
            var qos = typeof(SwitchTimeModeWireDto).GetCustomAttribute<DdsQosAttribute>();

            Assert.NotNull(qos);
            Assert.Equal(DdsDurability.TransientLocal, qos!.Durability);
            Assert.Equal(DdsHistoryKind.KeepLast,      qos.HistoryKind);
            Assert.Equal(1,                            qos.HistoryDepth);
        }

        /// <summary>
        /// TimeSyncOffsetCalculatedEvent is a local-bus-only event; it must be
        /// an unmanaged struct (two longs) registered on the native bus path.
        /// </summary>
        [Fact]
        public void TimeSyncOffsetCalculatedEvent_IsUnmanagedStructWithTwoLongFields()
        {
            var t = typeof(TimeSyncOffsetCalculatedEvent);
            Assert.True(t.IsValueType && !t.IsEnum);

            var bus = new FdpEventBus();
            bus.Register<TimeSyncOffsetCalculatedEvent>();
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 7L, NewOffset = 42L });
            bus.SwapBuffers();
            var events = bus.Consume<TimeSyncOffsetCalculatedEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(7L,  events[0].Rtt);
            Assert.Equal(42L, events[0].NewOffset);
        }
    }
}
