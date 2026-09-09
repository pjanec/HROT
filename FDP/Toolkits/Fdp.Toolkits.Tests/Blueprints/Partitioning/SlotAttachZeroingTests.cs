using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Tests.Blueprints.Partitioning
{
    /// <summary>
    /// FC-2/LV-1b (List Variables review F2, memory safety) -- <c>TryAttach</c> must ZERO the
    /// payload it hands out. A block reclaimed via <c>TryDetach</c> goes to the free list with the
    /// previous occupant's bytes intact; before this fix a re-attach handed those stale bytes to
    /// the new slot, and a stale non-zero value read as a fixed-list <c>Count</c> would drive an
    /// unbounded <c>[InlineArray]</c> indexer read. These tests pin the zero-on-attach contract for
    /// both the fresh (bump) and reused (free-list) allocation paths.
    /// </summary>
    public unsafe class SlotAttachZeroingTests
    {
        private const int MemorySize = 1024;

        private static byte[] NewInitializedBlock()
        {
            var bytes = new byte[MemorySize];
            fixed (byte* mem = bytes)
                BlueprintBlackboardPartitions.Initialize(mem, MemorySize, maxSlots: 8);
            return bytes;
        }

        [Fact]
        public void Attach_FreshAllocation_PayloadIsZeroed()
        {
            var bytes = NewInitializedBlock();
            fixed (byte* mem = bytes)
            {
                // Pre-poison the whole block area beyond the header/table so a non-zeroing attach
                // would visibly leak the poison into the payload.
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 1, 64, 0xAAAA, out int off1));
                for (int i = 0; i < 64; i++) mem[off1 + i] = 0xCD;
                BlueprintBlackboardPartitions.TryDetach(mem, 1);

                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 2, 64, 0xBBBB, out int off2));
                for (int i = 0; i < 64; i++)
                    Assert.Equal(0, mem[off2 + i]);
            }
        }

        [Fact]
        public void Attach_ReusedFreeListBlock_PreviousOccupantsBytesAreCleared()
        {
            var bytes = NewInitializedBlock();
            fixed (byte* mem = bytes)
            {
                // Slot A, then B (so A's freed block can't just merge into the bump frontier),
                // garbage into A, detach A, attach C at A's size -> takes A's free-list block.
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 10, 96, 1, out int offA));
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 11, 32, 2, out _));
                for (int i = 0; i < 96; i++) mem[offA + i] = 0xEE;    // "previous occupant's Count"
                Assert.True(BlueprintBlackboardPartitions.TryDetach(mem, 10));

                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 12, 96, 3, out int offC));
                Assert.Equal(offA, offC);                              // proven free-list reuse
                for (int i = 0; i < 96; i++)
                    Assert.Equal(0, mem[offC + i]);
            }
        }

        /// <summary>
        /// FC-3c (Q#21, action-path F2 verify) -- the STATEFUL-ACTION working-state attach path
        /// (<c>BehaviorIngressSystem.AttachSlotsToMemory</c>) funnels every allocation through
        /// <c>TryAttach</c>: fresh slots directly, and size/hash-MISMATCHED slots via the S2-3
        /// re-provision sequence (TryDetach then TryAttach at the manifest size). This pins the
        /// exact re-provision sequence: the re-attached slot's payload must come back zeroed
        /// even though the detached block carried the previous working state's bytes -- a stale
        /// non-zero byte read as a fixed-list <c>Count</c> would drive an OOB element read.
        /// </summary>
        [Fact]
        public void ReprovisionSequence_DetachThenAttachAtNewSize_PayloadIsZeroed()
        {
            var bytes = NewInitializedBlock();
            fixed (byte* mem = bytes)
            {
                // The slot as a stale version would have left it: attached, then filled with
                // live working-state bytes (incl. a plausible garbage list Count).
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 20, 48, 0x1111, out int offOld));
                for (int i = 0; i < 48; i++) mem[offOld + i] = 0x7F;

                // AttachSlotsToMemory's mismatch branch: detach the old slot, re-attach at the
                // NEW manifest size/hash (a grown DTO on hard reload).
                Assert.True(BlueprintBlackboardPartitions.TryDetach(mem, 20));
                Assert.True(BlueprintBlackboardPartitions.TryAttach(mem, 20, 80, 0x2222, out int offNew));

                for (int i = 0; i < 80; i++)
                    Assert.Equal(0, mem[offNew + i]);
            }
        }
    }
}
