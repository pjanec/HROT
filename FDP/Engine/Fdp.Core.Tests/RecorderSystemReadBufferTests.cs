using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Xunit;

namespace Fdp.Tests
{
    // ── Test event type (ID 10003) ────────────────────────────────────────────

    [EventId(10003)]
    internal struct RecorderReadBufferTestEvent
    {
        public float Value;
    }

    /// <summary>
    /// Tests for the <c>serializeReadBuffer</c> flag on
    /// <see cref="RecorderSystem.RecordKeyframe"/> (S502).
    /// </summary>
    public sealed class RecorderSystemReadBufferTests : IDisposable
    {
        // Byte offsets inside a RecordKeyframe binary payload
        //   8  GlobalVersion (ulong)
        //   1  FrameType (byte)
        //   8  WallClockTicks (long)
        //   4  DestroyCount (int, always 0 for keyframe)
        //  --- WriteEvents starts here ---
        //   4  unmanagedStreamCount (int)  <-- we read this
        private const int UnmanagedCountOffset = 8 + 1 + 8 + 4;

        private readonly RecorderSystem _recorder = new RecorderSystem();

        public RecorderSystemReadBufferTests()
        {
            EventTypeRegistry.ClearForTesting();
        }

        public void Dispose()
        {
            EventTypeRegistry.ClearForTesting();
        }

        private static int ReadUnmanagedStreamCount(byte[] payload)
        {
            return BitConverter.ToInt32(payload, UnmanagedCountOffset);
        }

        // ── S502-T1: serializeReadBuffer=false reads pending buffer (write side)

        [Fact]
        public void RecordKeyframe_SerializeReadBufferFalse_ReadsPendingBuffer()
        {
            using var repo = new EntityRepository();
            var bus = repo.Bus;

            // Publish to write buffer, then swap so pending (write) is empty
            bus.Publish(new RecorderReadBufferTestEvent { Value = 1.0f });
            bus.SwapBuffers(); // event moves to read buffer; write buffer is now empty

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            _recorder.RecordKeyframe(repo, bw, wallClockTicks: 0L, eventBus: bus, serializeReadBuffer: false);
            bw.Flush();

            // Pending (write) buffer is empty after swap -> 0 streams
            var data = ms.ToArray();
            Assert.Equal(0, ReadUnmanagedStreamCount(data));
        }

        // ── S502-T2: serializeReadBuffer=true reads current (read) buffer

        [Fact]
        public void RecordKeyframe_SerializeReadBufferTrue_ReadsCurrentBuffer()
        {
            using var repo = new EntityRepository();
            var bus = repo.Bus;

            // Publish to write buffer, then swap so current (read) has the event
            bus.Publish(new RecorderReadBufferTestEvent { Value = 2.0f });
            bus.SwapBuffers(); // event moves to read buffer

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            _recorder.RecordKeyframe(repo, bw, wallClockTicks: 0L, eventBus: bus, serializeReadBuffer: true);
            bw.Flush();

            // Current (read) buffer has the event -> 1 stream
            var data = ms.ToArray();
            Assert.Equal(1, ReadUnmanagedStreamCount(data));
        }
    }
}
