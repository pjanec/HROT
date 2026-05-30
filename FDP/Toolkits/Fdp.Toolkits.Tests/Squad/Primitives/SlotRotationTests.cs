using Xunit;

namespace Fdp.Toolkit.Squad.Primitives.Tests
{
    /// <summary>
    /// P1-05: Tests for <see cref="SlotRotation"/>.
    /// Covers SC-P1-05-1 through SC-P1-05-3.
    /// </summary>
    public class SlotRotationTests
    {
        [Fact]
        public void AcquireSlot_8Slots_ReturnsSequentialThenMinusOne()
        {
            // SC-P1-05-1
            SlotRotationState rot = default;
            for (int expected = 0; expected < 8; expected++)
            {
                int slot = SlotRotation.AcquireSlot(ref rot, totalSlots: 8);
                Assert.Equal(expected, slot);
            }
            // 9th call — all slots used.
            int noSlot = SlotRotation.AcquireSlot(ref rot, totalSlots: 8);
            Assert.Equal(-1, noSlot);
        }

        [Fact]
        public void BurnThenRelease_SlotRemainsUnavailable()
        {
            // SC-P1-05-2
            SlotRotationState rot = default;
            SlotRotation.BurnSlot(ref rot, 3);
            SlotRotation.ReleaseSlot(ref rot, 3);

            // Acquire all 8 slots and verify slot 3 is never returned.
            for (int i = 0; i < 8; i++)
            {
                int slot = SlotRotation.AcquireSlot(ref rot, totalSlots: 8);
                Assert.NotEqual(3, slot);
            }
            // All non-burned slots exhausted; next should be -1.
            int last = SlotRotation.AcquireSlot(ref rot, totalSlots: 8);
            Assert.Equal(-1, last);
        }

        [Fact]
        public void AllSlotsBurned_AcquireReturnsMinusOne()
        {
            // SC-P1-05-3
            SlotRotationState rot = default;
            for (int i = 0; i < 4; i++)
                SlotRotation.BurnSlot(ref rot, i);

            int slot = SlotRotation.AcquireSlot(ref rot, totalSlots: 4);
            Assert.Equal(-1, slot);
        }
    }
}
