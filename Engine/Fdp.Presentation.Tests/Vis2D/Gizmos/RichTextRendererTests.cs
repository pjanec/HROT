using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Gizmos;
using Raylib_cs;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    public class RichTextRendererTests
    {
        // Builds a FixedString32 from a raw byte sequence for precise control.
        private static FixedString32 MakeRaw(params byte[] bytes)
        {
            FixedString32 result = default;
            ref byte start = ref Unsafe.As<FixedString32, byte>(ref result);
            Span<byte> span = MemoryMarshal.CreateSpan(ref start, 32);
            for (int i = 0; i < bytes.Length && i < 32; i++)
                span[i] = bytes[i];
            return result;
        }

        // SC-GZ014-1: Bytes [0x01, 'H', 'i', 0x02, '!', 0x00] => [("Hi", Red), ("!", Green)].
        [Fact]
        public void SC_GZ014_1_ControlBytes_SplitIntoColoredChunks()
        {
            var text = MakeRaw(0x01, (byte)'H', (byte)'i', 0x02, (byte)'!', 0x00);

            var chunks = RichTextRenderer.ParseChunks(ref text);

            Assert.Equal(2, chunks.Count);
            Assert.Equal("Hi", chunks[0].Text);
            Assert.Equal(Color.Red, chunks[0].Color);
            Assert.Equal("!", chunks[1].Text);
            Assert.Equal(Color.Green, chunks[1].Color);
        }

        // SC-GZ014-2: "Hello" with no control bytes => single White chunk.
        [Fact]
        public void SC_GZ014_2_NoControlBytes_SingleWhiteChunk()
        {
            var text = new FixedString32("Hello");

            var chunks = RichTextRenderer.ParseChunks(ref text);

            Assert.Equal(1, chunks.Count);
            Assert.Equal("Hello", chunks[0].Text);
            Assert.Equal(Color.White, chunks[0].Color);
        }

        // SC-GZ014-5: ParseChunks in a tight loop allocates only the returned List<> per call.
        // We verify no large hidden allocations occur (the list itself is expected).
        [Fact]
        public void SC_GZ014_5_ParseChunks_LowAllocationPerCall()
        {
            var text = new FixedString32("Hi");

            // Warm-up: ensure any JIT overhead has occurred.
            for (int i = 0; i < 10; i++)
                _ = RichTextRenderer.ParseChunks(ref text);

            long before = GC.GetTotalMemory(forceFullCollection: false);

            const int iterations = 100;
            for (int i = 0; i < iterations; i++)
                _ = RichTextRenderer.ParseChunks(ref text);

            long after = GC.GetTotalMemory(forceFullCollection: false);

            // Each call allocates one List<>. Allow generously: 500 bytes per call.
            long budget = iterations * 500L;
            Assert.True(after - before < budget,
                $"Allocation {after - before} bytes exceeded budget {budget} bytes over {iterations} calls.");
        }

        // Additional: Yellow control byte (0x03) is recognised.
        [Fact]
        public void Yellow_ControlByte_Recognised()
        {
            var text = MakeRaw(0x03, (byte)'Y', 0x00);

            var chunks = RichTextRenderer.ParseChunks(ref text);

            Assert.Equal(1, chunks.Count);
            Assert.Equal("Y", chunks[0].Text);
            Assert.Equal(Color.Yellow, chunks[0].Color);
        }

        // Additional: Unknown control byte (e.g. 0x10) defaults to White.
        [Fact]
        public void Unknown_ControlByte_DefaultsToWhite()
        {
            var text = MakeRaw(0x10, (byte)'X', 0x00);

            var chunks = RichTextRenderer.ParseChunks(ref text);

            Assert.Equal(1, chunks.Count);
            Assert.Equal("X", chunks[0].Text);
            Assert.Equal(Color.White, chunks[0].Color);
        }
    }
}
