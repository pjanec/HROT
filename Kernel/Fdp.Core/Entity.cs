using System;
using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Lightweight entity handle (Index + Generation).
    /// 48-bit total: 32-bit index + 16-bit generation.
    /// Designed to catch stale references via generation mismatch.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Entity : IEquatable<Entity>
    {
        /// <summary>
        /// Entity index in the entity array [0, MAX_ENTITIES).
        /// </summary>
        public readonly int Index;
        
        /// <summary>
        /// Generation number for this index.
        /// Incremented each time an entity at this index is destroyed.
        /// Used to detect stale entity handles.
        /// </summary>
        public readonly ushort Generation;
        
        public Entity(int index, ushort generation)
        {
            Index = index;
            Generation = generation;
        }
        
        /// <summary>
        /// Creates an entity from a packed ulong value (for ECB deserialization).
        /// </summary>
        public Entity(ulong packed)
        {
            Index = (int)(packed & 0xFFFFFFFF);
            Generation = (ushort)((packed >> 32) & 0xFFFF);
        }
        
        /// <summary>
        /// Packs index and generation into a single ulong for efficient serialization.
        /// Used by EntityCommandBuffer.
        /// </summary>
        public ulong PackedValue => ((ulong)Generation << 32) | (uint)Index;
        
        /// <summary>
        /// Returns true if this is a "null" entity OR an uninitialized default(Entity).
        /// </summary>
        public bool IsNull => Index < 0 || Generation == 0;
        
        /// <summary>
        /// Null entity constant.
        /// </summary>
        public static readonly Entity Null = new Entity(0, 0);
        
        public bool Equals(Entity other)
        {
            return Index == other.Index && Generation == other.Generation;
        }
        
        public override bool Equals(object? obj)
        {
            return obj is Entity other && Equals(other);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(Index, Generation);
        }
        
        public override string ToString()
        {
            if (IsNull)
                return "Entity.Null";
            
            return $"Entity({Index}, v{Generation})";
        }
        
        public static bool operator ==(Entity left, Entity right) => left.Equals(right);
        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
    }
}
