using System;
using System.Runtime.CompilerServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Helper for working with multi-part components.
    /// Breaks components into 64-byte parts for efficient network transmission.
    /// </summary>
    public static class MultiPartComponent
    {
        /// <summary>
        /// Part size in bytes (64 bytes for cache line alignment).
        /// </summary>
        public const int PART_SIZE = 64;
        
        /// <summary>
        /// Gets the number of parts needed for a component type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPartCount<T>() where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            return (size + PART_SIZE - 1) / PART_SIZE; // Ceiling division
        }
        
        /// <summary>
        /// Gets the number of parts needed for a size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPartCount(int sizeInBytes)
        {
            return (sizeInBytes + PART_SIZE - 1) / PART_SIZE;
        }
        
        /// <summary>
        /// Creates a descriptor with all parts set for a component type.
        /// </summary>
        public static PartDescriptor CreateFullDescriptor<T>() where T : unmanaged
        {
            var desc = PartDescriptor.Empty();
            int partCount = GetPartCount<T>();
            
            for (int i = 0; i < partCount; i++)
            {
                desc.SetPart(i);
            }
            
            return desc;
        }
        
        /// <summary>
        /// Checks if a component type requires multiple parts.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMultiPart<T>() where T : unmanaged
        {
            return GetPartCount<T>() > 1;
        }
        
        /// <summary>
        /// Gets the byte offset for a specific part.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPartOffset(int partIndex)
        {
            return partIndex * PART_SIZE;
        }
        
        /// <summary>
        /// Gets the size in bytes of a specific part.
        /// Last part might be smaller than PART_SIZE.
        /// </summary>
        public static int GetPartSize<T>(int partIndex) where T : unmanaged
        {
            int totalSize = Unsafe.SizeOf<T>();
            int offset = GetPartOffset(partIndex);
            
            if (offset >= totalSize)
                return 0;
            
            int remaining = totalSize - offset;
            return Math.Min(remaining, PART_SIZE);
        }
        
        /// <summary>
        /// Compares two components and creates a descriptor for changed parts.
        /// Returns a descriptor with bits set for parts that differ.
        /// </summary>
        public static unsafe PartDescriptor GetChangedParts<T>(in T oldValue, in T newValue) 
            where T : unmanaged
        {
            var desc = PartDescriptor.Empty();
            int partCount = GetPartCount<T>();
            
            fixed (T* oldPtr = &oldValue)
            fixed (T* newPtr = &newValue)
            {
                byte* oldBytes = (byte*)oldPtr;
                byte* newBytes = (byte*)newPtr;
                
                for (int partIdx = 0; partIdx < partCount; partIdx++)
                {
                    int offset = GetPartOffset(partIdx);
                    int partSize = GetPartSize<T>(partIdx);
                    
                    // Compare bytes in this part
                    bool changed = false;
                    for (int i = 0; i < partSize; i++)
                    {
                        if (oldBytes[offset + i] != newBytes[offset + i])
                        {
                            changed = true;
                            break;
                        }
                    }
                    
                    if (changed)
                    {
                        desc.SetPart(partIdx);
                    }
                }
            }
            
            return desc;
        }
        
        /// <summary>
        /// Copies specific parts from source to destination.
        /// Only copies parts indicated by the descriptor.
        /// </summary>
        public static unsafe void CopyParts<T>(ref T destination, in T source, in PartDescriptor descriptor)
            where T : unmanaged
        {
            int partCount = GetPartCount<T>();
            
            fixed (T* dstPtr = &destination)
            fixed (T* srcPtr = &source)
            {
                byte* dstBytes = (byte*)dstPtr;
                byte* srcBytes = (byte*)srcPtr;
                
                for (int partIdx = 0; partIdx < partCount; partIdx++)
                {
                    if (!descriptor.HasPart(partIdx))
                        continue;
                    
                    int offset = GetPartOffset(partIdx);
                    int partSize = GetPartSize<T>(partIdx);
                    
                    // Copy this part
                    for (int i = 0; i < partSize; i++)
                    {
                        dstBytes[offset + i] = srcBytes[offset + i];
                    }
                }
            }
        }
    }
}
