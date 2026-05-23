using System.Runtime.InteropServices;

namespace Fdp.Core
{
    /// <summary>
    /// 128-byte cold entity metadata struct (two CPU cache lines).
    /// Stores authority mask, generation, flags, timestamps, DIS type, and lifecycle state.
    /// Intended for use in a parallel cold NativeChunkTable alongside the hot BitMask512 table.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct EntityMetadataCold
    {
        /// <summary>
        /// Components owned by local authority (for network distribution). 512-bit authority mask.
        /// </summary>
        [FieldOffset(0)]
        public BitMask512 AuthorityMask;

        /// <summary>
        /// Generation number (incremented on destroy/recreate).
        /// </summary>
        [FieldOffset(64)]
        public ushort Generation;

        /// <summary>
        /// Flags for entity state.
        /// Bit 0: IsActive (1 = active, 0 = destroyed/recycled)
        /// Bits 1-15: Reserved for future use.
        /// </summary>
        [FieldOffset(66)]
        public ushort Flags;

        /// <summary>
        /// Tick number when this entity was last modified.
        /// </summary>
        [FieldOffset(68)]
        public ulong LastChangeTick;

        /// <summary>
        /// The full DIS entity type (8 bytes).
        /// </summary>
        [FieldOffset(76)]
        public DISEntityType DisType;

        /// <summary>
        /// Lifecycle state of the entity (Constructing, Active, TearDown).
        /// </summary>
        [FieldOffset(84)]
        public EntityLifecycle LifecycleState;

        // Remaining bytes (85-127) are implicit padding.

        /// <summary>
        /// Returns true when bit 0 of <see cref="Flags"/> is set (entity is active).
        /// </summary>
        public readonly bool IsActive => (Flags & 0x0001) != 0;

        /// <summary>
        /// Sets or clears bit 0 of <see cref="Flags"/> without touching any other bits.
        /// </summary>
        public void SetActive(bool active)
        {
            if (active)
                Flags |= 0x0001;
            else
                Flags &= unchecked((ushort)~0x0001);
        }
    }
}
