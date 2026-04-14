using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Low-level wrapper for Windows VirtualAlloc.
    /// Manages reserve/commit separation for sparse memory allocation.
    /// </summary>
    public static unsafe class NativeMemoryAllocator
    {
        private const int MEM_COMMIT = 0x00001000;
        private const int MEM_RESERVE = 0x00002000;
        private const int MEM_RELEASE = 0x00008000;
        private const int MEM_DECOMMIT = 0x00004000;
        private const int PAGE_NOACCESS = 0x01;
        private const int PAGE_READWRITE = 0x04;
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void* VirtualAlloc(
            void* lpAddress,
            nuint dwSize,
            uint flAllocationType,
            uint flProtect);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(
            void* lpAddress,
            nuint dwSize,
            uint dwFreeType);
        
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
            if (sizeBytes > int.MaxValue * 4L) // Sanity check
                throw new ArgumentException("Size too large", nameof(sizeBytes));
            #endif
            
            void* ptr = VirtualAlloc(null, (nuint)sizeBytes, MEM_RESERVE, PAGE_NOACCESS);
            
            if (ptr == null)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw new OutOfMemoryException(
                    $"VirtualAlloc(Reserve) failed for {sizeBytes} bytes: {message} (Error: {error})");
            }
            
            return ptr;
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
            
            void* result = VirtualAlloc(ptr, (nuint)sizeBytes, MEM_COMMIT, PAGE_READWRITE);
            
            if (result == null)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw new InvalidOperationException(
                    $"VirtualAlloc(Commit) failed for {sizeBytes} bytes at {(long)ptr:X}: {message} (Error: {error})");
            }
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
            
            bool success = VirtualFree(ptr, (nuint)sizeBytes, MEM_DECOMMIT);
            
            #if FDP_PARANOID_MODE
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw new InvalidOperationException(
                    $"VirtualFree(Decommit) failed for {sizeBytes} bytes at {(long)ptr:X}: {message} (Error: {error})");
            }
            #endif
        }
        
        /// <summary>
        /// Frees the entire reserved region.
        /// MUST pass size=0 for MEM_RELEASE (Windows requirement).
        /// </summary>
        /// <param name="ptr">Pointer to reserved region</param>
        /// <param name="originalReservedSize">Original size passed to Reserve (for documentation only, not used)</param>
        public static void Free(void* ptr, long originalReservedSize)
        {
            if (ptr == null) return;
            
            // Windows requires size=0 when using MEM_RELEASE
            bool success = VirtualFree(ptr, 0, MEM_RELEASE);
            
            #if FDP_PARANOID_MODE
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw new InvalidOperationException(
                    $"VirtualFree(Release) failed at {(long)ptr:X}: {message} (Error: {error})");
            }
            #endif
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
