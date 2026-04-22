using System;
using System.Collections.Generic;
using Fdp.Core;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="EventAccumulator"/> integration with
    /// <see cref="FdpEventBus.FlushToReplica"/> (S503).
    /// </summary>
    public sealed class ReferenceCheckpointEventTests
    {
        // Raw synthetic type ID used to inject events without requiring [EventId] structs.
        private const int SyntheticNativeTypeId = 20001;
        private const int SyntheticElementSize  = 4; // sizeof(int)

        private static ReadOnlySpan<byte> MakeFourBytes() =>
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        // ── S503-T1: Events captured on live bus flush to replica bus current ─

        [Fact]
        public void FlushToReplica_InjectsEventsIntoCurrent_WhenFrameIsNew()
        {
            using var liveBus    = new FdpEventBus();
            using var replicaBus = new FdpEventBus();
            var accumulator = new EventAccumulator();

            // Inject directly into live bus current buffer (simulates post-swap state)
            liveBus.InjectIntoCurrentBySize(SyntheticNativeTypeId, SyntheticElementSize,
                MakeFourBytes());

            // Capture frame 1 from live bus
            accumulator.CaptureFrame(liveBus, frameIndex: 1);

            // Flush frames newer than tick 0
            accumulator.FlushToReplica(replicaBus, lastSeenTick: 0);

            // Replica's current buffer should now contain the event
            var streams = new List<INativeEventStream>();
            replicaBus.PopulateCurrentStreams(streams);

            Assert.Single(streams);
            Assert.True(streams[0].GetRawBytes().Length > 0);
        }

        // ── S503-T2: No exception when accumulator is empty ──────────────────

        [Fact]
        public void FlushToReplica_NoEvents_DoesNotThrow()
        {
            using var replicaBus = new FdpEventBus();
            var accumulator = new EventAccumulator();

            var ex = Record.Exception(() => accumulator.FlushToReplica(replicaBus, lastSeenTick: 0));
            Assert.Null(ex);
        }

        // ── S503-T3: FlushToReplica with lastSeenTick >= frameIndex does nothing

        [Fact]
        public void FlushToReplica_LastSeenTickAtMax_NothingFlushed()
        {
            using var liveBus    = new FdpEventBus();
            using var replicaBus = new FdpEventBus();
            var accumulator = new EventAccumulator();

            liveBus.InjectIntoCurrentBySize(SyntheticNativeTypeId, SyntheticElementSize,
                MakeFourBytes());
            accumulator.CaptureFrame(liveBus, frameIndex: 1);

            // Frame 1 <= uint.MaxValue, so nothing should be flushed
            accumulator.FlushToReplica(replicaBus, lastSeenTick: uint.MaxValue);

            var streams = new List<INativeEventStream>();
            replicaBus.PopulateCurrentStreams(streams);

            Assert.Empty(streams);
        }
    }
}
