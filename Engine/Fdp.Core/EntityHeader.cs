using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// 96-byte entity header stored in NativeChunkTable.
    /// Contains component existence mask, authority mask, and metadata.
    /// 96 bytes = multiple of 32 (AVX2) and 64 (cache line).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 96)]
    public struct EntityHeader
    {
        /// <summary>
        /// Components present on this entity (bit per component type).
        /// </summary>
        [FieldOffset(0)]
        public BitMask256 ComponentMask;
        
        /// <summary>
        /// Components owned by local authority (for network distribution).
        /// If bit is set, we have authority to modify this component.
        /// </summary>
        [FieldOffset(32)]
        public BitMask256 AuthorityMask;
        
        /// <summary>
        /// Generation number (incremented on destroy/recreate).
        /// </summary>
        [FieldOffset(64)]
        public ushort Generation;
        
        /// <summary>
        /// Flags for entity state.
        /// Bit 0: IsActive (1 = active, 0 = destroyed/recycled)
        /// Bits 1-15: Reserved for future use
        /// </summary>
        [FieldOffset(66)]
        public ushort Flags;
        
        /// <summary>
        /// Tick number when this entity was last modified.
        /// Updated on creation, structural changes, and SetComponent.
        /// </summary>
        [FieldOffset(68)]
        public ulong LastChangeTick;

        /// <summary>
        /// The Full DIS Type (Takes 8 bytes of the 20 reserved)
        /// </summary>
        [FieldOffset(76)] 
        public DISEntityType DisType;

        /// <summary>
        /// Lifecycle state of the entity (Constructing, Active, TearDown).
        /// </summary>
        [FieldOffset(84)]
        public EntityLifecycle LifecycleState;
        
        // Padding: 11 bytes remaining
        // Total so far: 84 + 1 = 85 bytes. Reserved to 96.
        
        /// <summary>
        /// Checks if entity is currently active (not destroyed).
        /// </summary>
        public readonly bool IsActive
        {
            get => (Flags & 0x0001) != 0;
        }
        
        /// <summary>
        /// Sets the active flag.
        /// </summary>
        public void SetActive(bool active)
        {
            if (active)
                Flags |= 0x0001;
            else
                Flags &= unchecked((ushort)~0x0001);
        }
        
        /// <summary>
        /// Clears all data in the header.
        /// </summary>
        public void Clear()
        {
            ComponentMask.Clear();
            AuthorityMask.Clear();
            Generation = 0;
            Flags = 0;
            LastChangeTick = 0;
            LifecycleState = EntityLifecycle.Constructing; 
        }
        
        public override readonly string ToString()
        {
            return $"EntityHeader [Active: {IsActive}, Gen: {Generation}, Components: {CountComponents()}]";
        }
        
        private readonly int CountComponents()
        {
            int count = 0;
            for (int i = 0; i < 256; i++)
            {
                if (ComponentMask.IsSet(i))
                    count++;
            }
            return count;
        }
    }
}
