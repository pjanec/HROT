using System;
using System.Runtime.InteropServices;

namespace Fdp.Core.Memory
{
    /// <summary>
    /// Low-level wrapper for POSIX mmap/mprotect/madvise (Linux x64).
    /// Manages reserve/commit separation for sparse memory allocation, mirroring the
    /// Windows VirtualAlloc reserve/commit model on top of mmap.
    /// </summary>
    internal sealed unsafe class PosixVirtualMemoryBackend : IVirtualMemoryBackend
    {
        private const int PROT_NONE = 0x0;
        private const int PROT_READ = 0x1;
        private const int PROT_WRITE = 0x2;
        private const int MAP_PRIVATE = 0x02;
        private const int MAP_ANONYMOUS = 0x20;
        private const int MAP_NORESERVE = 0x4000;
        private const int MADV_DONTNEED = 4;

        private const long ALIGN = 65536;

        private static readonly void* MAP_FAILED = (void*)-1;

        [DllImport("libc", SetLastError = true)]
        private static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

        [DllImport("libc", SetLastError = true)]
        private static extern int munmap(void* addr, nuint length);

        [DllImport("libc", SetLastError = true)]
        private static extern int mprotect(void* addr, nuint length, int prot);

        [DllImport("libc", SetLastError = true)]
        private static extern int madvise(void* addr, nuint length, int advice);

        private static long RoundUpToPage(long size)
        {
            long page = Environment.SystemPageSize;
            return ((size + page - 1) / page) * page;
        }

        /// <summary>
        /// Reserves address space. Physical RAM cost: 0 bytes.
        /// Returns a 64KB aligned pointer, using a stateless aligned-mmap trim so no
        /// per-allocation bookkeeping is needed.
        /// </summary>
        /// <param name="sizeBytes">Size to reserve in bytes</param>
        /// <returns>Pointer to reserved memory region</returns>
        public void* Reserve(long sizeBytes)
        {
            long len = RoundUpToPage(sizeBytes);
            long over = len + ALIGN;

            void* basePtr = mmap(null, (nuint)over, PROT_NONE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE, -1, 0);

            if (basePtr == MAP_FAILED)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new OutOfMemoryException(
                    $"mmap(Reserve) failed for {sizeBytes} bytes: errno {error}");
            }

            nuint baseAddr = (nuint)basePtr;
            nuint aligned = ((baseAddr + (nuint)ALIGN - 1) / (nuint)ALIGN) * (nuint)ALIGN;

            nuint front = aligned - baseAddr;
            if (front > 0)
            {
                munmap(basePtr, front);
            }

            nuint back = (nuint)over - front - (nuint)len;
            if (back > 0)
            {
                munmap((void*)(aligned + (nuint)len), back);
            }

            return (void*)aligned;
        }

        /// <summary>
        /// Commits a region, backing it with physical RAM.
        /// The region must have been previously reserved.
        /// </summary>
        /// <param name="ptr">Pointer to start of region to commit</param>
        /// <param name="sizeBytes">Size to commit in bytes</param>
        public void Commit(void* ptr, long sizeBytes)
        {
            long len = RoundUpToPage(sizeBytes);
            int result = mprotect(ptr, (nuint)len, PROT_READ | PROT_WRITE);

            if (result != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException(
                    $"mprotect(Commit) failed for {sizeBytes} bytes at {(long)ptr:X}: errno {error}");
            }
        }

        /// <summary>
        /// Decommits a region, releasing physical RAM but keeping address space reserved.
        /// This is used for chunk recycling without full deallocation.
        /// madvise(MADV_DONTNEED) returns the physical pages so a later recommit reads back as zero.
        /// </summary>
        /// <param name="ptr">Pointer to start of region to decommit</param>
        /// <param name="sizeBytes">Size to decommit in bytes</param>
        public void Decommit(void* ptr, long sizeBytes)
        {
            long len = RoundUpToPage(sizeBytes);
            int result = mprotect(ptr, (nuint)len, PROT_NONE);

            #if FDP_PARANOID_MODE
            if (result != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException(
                    $"mprotect(Decommit) failed for {sizeBytes} bytes at {(long)ptr:X}: errno {error}");
            }
            #else
            _ = result;
            #endif

            madvise(ptr, (nuint)len, MADV_DONTNEED);
        }

        /// <summary>
        /// Frees the entire reserved region.
        /// Linux munmap needs the real length (unlike Windows MEM_RELEASE which uses 0);
        /// the facade passes the original reserved size, matched by the trim in Reserve
        /// leaving exactly that many bytes mapped starting at the returned pointer.
        /// </summary>
        /// <param name="ptr">Pointer to reserved region</param>
        /// <param name="originalReservedSize">Original size passed to Reserve</param>
        public void Free(void* ptr, long originalReservedSize)
        {
            long len = RoundUpToPage(originalReservedSize);
            int result = munmap(ptr, (nuint)len);

            #if FDP_PARANOID_MODE
            if (result != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException(
                    $"munmap(Free) failed at {(long)ptr:X}: errno {error}");
            }
            #else
            _ = result;
            #endif
        }
    }
}
