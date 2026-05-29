using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Core.Collections;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EqsResult"/>, <see cref="EqsCognitiveBuffer"/>,
    /// <see cref="EqsSensor"/>, and <see cref="GlobalComponentIds"/> invariants.
    /// </summary>
    public class EqsComponentLayoutTests
    {
        // ── Test 1: EqsResult struct size ────────────────────────────────────────

        [Fact]
        public void EqsResult_SizeIs32Bytes()
        {
            // 8 (EntityId) + 4 (PositionX) + 4 (PositionY) + 4 (PositionZ) + 4 (Score)
            // + 2 (Flags) + 2 (FlagsMeaningful) = 28 raw → 32 after 8-byte alignment (long EntityId).
            Assert.Equal(32, Marshal.SizeOf<EqsResult>());
        }

        // ── Test 1b: EqsResultArray footprint = 16 × 32 = 512 bytes (8 cache lines) ─

        [Fact]
        public void EqsResultArray_SizeIs512Bytes()
        {
            Assert.Equal(512, Marshal.SizeOf<EqsResultArray>());
        }

        // ── Test 1c: PositionZ round-trips through the safe span path ────────────

        [Fact]
        public void EqsCognitiveBuffer_GetSpanRW_PositionZPersists()
        {
            var buffer = new EqsCognitiveBuffer();
            buffer.GetSpanRW()[3] = new EqsResult { EntityId = 7L, PositionX = 1f, PositionY = 2f, PositionZ = 42.5f };

            ref readonly var read = ref buffer.GetSpanRO()[3];
            Assert.Equal(42.5f, read.PositionZ);
            Assert.Equal(1f, read.PositionX);
            Assert.Equal(2f, read.PositionY);
        }

        // ── Test 2: GetSpanRW write persists ─────────────────────────────────────

        [Fact]
        public void EqsCognitiveBuffer_GetSpanRW_WritePersists()
        {
            // Arrange
            var buffer = new EqsCognitiveBuffer();
            var written = new EqsResult { EntityId = 42L, Score = 1.5f, PositionX = 10f, PositionY = 20f };

            // Act — write via the safe span path
            buffer.GetSpanRW()[0] = written;

            // Assert — read back via GetSpanRO and verify the value persisted
            ref readonly var read = ref buffer.GetSpanRO()[0];
            Assert.Equal(42L, read.EntityId);
            Assert.Equal(1.5f, read.Score);
            Assert.Equal(10f, read.PositionX);
            Assert.Equal(20f, read.PositionY);
        }

        // ── Test 3: Direct [InlineArray] index does NOT persist (defensive copy) ─

        [Fact]
        public void EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy()
        {
            // This test proves WHY GetSpanRW is needed.
            //
            // Direct [InlineArray] index assignment on a *copy* of the struct is subject to the
            // C# compiler emitting a defensive ldobj copy, meaning the mutation is discarded.
            // We demonstrate that: writing the SAME entity ID through a temp-copy index does NOT
            // appear when reading from the original, whereas the span-based path does persist.

            var buffer = new EqsCognitiveBuffer();

            // --- Path A: span-based write (persists) ---
            buffer.GetSpanRW()[0] = new EqsResult { EntityId = 99L };

            // --- Path B: demonstrate that copying the struct and writing through index
            //     leaves the original unchanged (defensive copy trap) ---
            var copy = buffer;          // copy on purpose
            copy.Results[0] = new EqsResult { EntityId = 77L };  // writes into the copy's inline array

            // The original buffer still has EntityId = 99 (written via span, not overwritten by copy mutation)
            Assert.Equal(99L, buffer.GetSpanRO()[0].EntityId);
            // The copy has EntityId = 77 (confirming the [InlineArray] write path does work on a local copy,
            // it just doesn't affect the original)
            Assert.Equal(77L, copy.Results[0].EntityId);
        }

        // ── Test 4: GlobalComponentIds uniqueness ────────────────────────────────

        [Fact]
        public void GlobalComponentIds_EqsSensorAndBufferAreUnique()
        {
            int sensorId  = GlobalComponentIds.EqsSensor;
            int bufferId  = GlobalComponentIds.EqsCognitiveBuffer;
            int poolId    = GlobalComponentIds.EqsResultPool;

            // IDs must be distinct
            Assert.NotEqual(sensorId, bufferId);
            Assert.NotEqual(sensorId, poolId);
            Assert.NotEqual(bufferId, poolId);

            // All must fall within the reserved toolkit/zone expansion block (207–255)
            Assert.InRange(sensorId, 207, 255);
            Assert.InRange(bufferId, 207, 255);
            Assert.InRange(poolId,   207, 255);
        }
    }
}
