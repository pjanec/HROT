using System;
using System.Runtime.CompilerServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Descriptor defining which parts of a component are present.
    /// Used for network synchronization to send only changed parts.
    /// Each bit represents a 64-byte part of the component.
    /// </summary>
    [ComponentId(GlobalComponentIds.PartDescriptor)]
    public struct PartDescriptor : IEquatable<PartDescriptor>
    {
        // Uses the existing BitMask256 for part tracking
        private BitMask256 _partMask;
        
        /// <summary>
        /// Creates a descriptor with all parts present.
        /// </summary>
        public static PartDescriptor All()
        {
            var desc = new PartDescriptor();
            desc._partMask.SetAll();
            return desc;
        }
        
        /// <summary>
        /// Creates an empty descriptor with no parts.
        /// </summary>
        public static PartDescriptor Empty()
        {
            return new PartDescriptor();
        }
        
        /// <summary>
        /// Sets a part as present.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPart(int partIndex)
        {
            _partMask.SetBit(partIndex);
        }
        
        /// <summary>
        /// Clears a part.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearPart(int partIndex)
        {
            _partMask.ClearBit(partIndex);
        }
        
        /// <summary>
        /// Checks if a part is present.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasPart(int partIndex)
        {
            return _partMask.IsSet(partIndex);
        }
        
        /// <summary>
        /// Checks if any parts are present.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAnyParts()
        {
            return !_partMask.IsEmpty();
        }
        
        /// <summary>
        /// Clears all parts.
        /// </summary>
        public void Clear()
        {
            _partMask.Clear();
        }
        
        /// <summary>
        /// Combines with another descriptor (union).
        /// </summary>
        public void UnionWith(in PartDescriptor other)
        {
            // Manually OR the masks since BitMask256 doesn't have static Or
            for (int i = 0; i < 256; i++)
            {
                if (other._partMask.IsSet(i))
                {
                    _partMask.SetBit(i);
                }
            }
        }
        
        /// <summary>
        /// Intersects with another descriptor (AND).
        /// </summary>
        public void IntersectWith(in PartDescriptor other)
        {
            // Manually AND the masks
            for (int i = 0; i < 256; i++)
            {
                if (!other._partMask.IsSet(i))
                {
                    _partMask.ClearBit(i);
                }
            }
        }
        
        /// <summary>
        /// Gets the raw bit mask.
        /// </summary>
        public readonly BitMask256 Mask => _partMask;
        
        public readonly bool Equals(PartDescriptor other)
        {
            return _partMask.Equals(other._partMask);
        }
        
        public override readonly bool Equals(object? obj)
        {
            return obj is PartDescriptor other && Equals(other);
        }
        
        public override readonly int GetHashCode()
        {
            return _partMask.GetHashCode();
        }
        
        public static bool operator ==(PartDescriptor left, PartDescriptor right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(PartDescriptor left, PartDescriptor right)
        {
            return !left.Equals(right);
        }
        
        public override readonly string ToString()
        {
            int count = 0;
            for (int i = 0; i < 256; i++)
            {
                if (HasPart(i))
                    count++;
            }
            return $"PartDescriptor({count} parts)";
        }
    }
}
