namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// Compact bitmask state for tracking used and burned exposure slots.
    /// 4 bytes. Supports up to 16 slots (ushort mask width).
    /// </summary>
    public struct SlotRotationState
    {
        /// <summary>Bitmask of currently-in-use slots (bit i = slot i is occupied).</summary>
        public ushort UsedMask;
        /// <summary>Bitmask of permanently burned slots (bit i = slot i must not be reused).</summary>
        public ushort BurnedMask;
    }

    /// <summary>
    /// Exposed-slot rotation primitive with burn/reuse tracking (design §2 primitive 5).
    /// Generalizes <c>HillAttackMutableState.BurnedSlotsMask</c> /
    /// <c>WaveUsedSlotsMask</c> from the hill-attack doctrine.
    /// </summary>
    public static class SlotRotation
    {
        /// <summary>
        /// Acquires the next available (not burned, not in use) slot from
        /// <c>[0, totalSlots)</c>.
        /// </summary>
        /// <returns>The acquired slot index, or -1 when all slots are used or burned.</returns>
        public static int AcquireSlot(ref SlotRotationState rotation, int totalSlots)
        {
            for (int i = 0; i < totalSlots; i++)
            {
                if (((rotation.BurnedMask >> i) & 1) == 1) continue;
                if (((rotation.UsedMask   >> i) & 1) == 1) continue;
                rotation.UsedMask = (ushort)(rotation.UsedMask | (1 << i));
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Releases a previously acquired slot, making it available for re-acquisition.
        /// A burned slot remains unavailable even after release (burn dominates).
        /// </summary>
        public static void ReleaseSlot(ref SlotRotationState rotation, int slotIndex)
        {
            rotation.UsedMask = (ushort)(rotation.UsedMask & ~(1 << slotIndex));
        }

        /// <summary>
        /// Permanently burns a slot so it will never be returned by
        /// <see cref="AcquireSlot"/> again, even after <see cref="ReleaseSlot"/> is called.
        /// </summary>
        public static void BurnSlot(ref SlotRotationState rotation, int slotIndex)
        {
            rotation.BurnedMask = (ushort)(rotation.BurnedMask | (1 << slotIndex));
            rotation.UsedMask   = (ushort)(rotation.UsedMask   & ~(1 << slotIndex));
        }
    }
}
