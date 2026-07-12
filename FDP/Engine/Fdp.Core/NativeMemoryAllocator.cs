using System;
using Fdp.Core.Memory;

namespace Fdp.Core
{
    /// <summary>
    /// Cross-platform facade for virtual memory reserve/commit/decommit/free.
    /// Delegates the actual syscalls to a platform-specific backend selected once at
    /// startup: WindowsVirtualMemoryBackend (VirtualAlloc/VirtualFree) on Windows, or
    /// PosixVirtualMemoryBackend (mmap/mprotect/madvise) elsewhere.
    /// </summary>
    public static unsafe class NativeMemoryAllocator
    {
        private static readonly IVirtualMemoryBackend Backend =
            OperatingSystem.IsWindows() ? new WindowsVirtualMemoryBackend() : new PosixVirtualMemoryBackend();

        /// <summary>
        /// Reserves address space. Physical RAM cost: 0 bytes.
        /// Memory is 64KB aligned automatically by Windows.
        /// </summary>
        /// <param name="sizeBytes">Size to reserve in bytes</param>
        /// <returns>Pointer to reserved memory region</returns>
        public static void* Reserve(long sizeBytes)
        {
            #if FDP_PARANOID_MODE
            if (sizeBytes <= 0)
                throw new ArgumentException("Size must be positive", nameof(sizeBytes));
            if (sizeBytes > int.MaxValue * 8L) // Sanity check (accomodates 16KB component)
                throw new ArgumentException("Size too large", nameof(sizeBytes));
            #endif

            return Backend.Reserve(sizeBytes);
        }

        /// <summary>
        /// Commits a region, backing it with physical RAM.
        /// The region must have been previously reserved.
        /// </summary>
        /// <param name="ptr">Pointer to start of region to commit</param>
        /// <param name="sizeBytes">Size to commit in bytes</param>
        public static void Commit(void* ptr, long sizeBytes)
        {
            #if FDP_PARANOID_MODE
            if (ptr == null)
                throw new ArgumentNullException(nameof(ptr));
            if (sizeBytes <= 0)
                throw new ArgumentException("Size must be positive", nameof(sizeBytes));
            #endif

            Backend.Commit(ptr, sizeBytes);
        }

        /// <summary>
        /// Decommits a region, releasing physical RAM but keeping address space reserved.
        /// This is used for chunk recycling without full deallocation.
        /// </summary>
        /// <param name="ptr">Pointer to start of region to decommit</param>
        /// <param name="sizeBytes">Size to decommit in bytes</param>
        public static void Decommit(void* ptr, long sizeBytes)
        {
            if (ptr == null) return;

            #if FDP_PARANOID_MODE
            if (sizeBytes <= 0)
                throw new ArgumentException("Size must be positive", nameof(sizeBytes));
            #endif

            Backend.Decommit(ptr, sizeBytes);
        }

        /// <summary>
        /// Frees the entire reserved region.
        /// </summary>
        /// <param name="ptr">Pointer to reserved region</param>
        /// <param name="originalReservedSize">Original size passed to Reserve. Required by the POSIX backend (munmap needs the real length); ignored by the Windows backend (MEM_RELEASE uses size=0).</param>
        public static void Free(void* ptr, long originalReservedSize)
        {
            if (ptr == null) return;

            Backend.Free(ptr, originalReservedSize);
        }

        /// <summary>
        /// Checks if a pointer is 64KB aligned (Windows VirtualAlloc guarantee).
        /// </summary>
        public static bool Is64KBAligned(void* ptr)
        {
            return ((long)ptr & 0xFFFF) == 0;
        }
    }
}
