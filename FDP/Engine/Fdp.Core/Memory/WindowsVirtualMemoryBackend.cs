using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fdp.Core.Memory
{
    /// <summary>
    /// Low-level wrapper for Windows VirtualAlloc.
    /// Manages reserve/commit separation for sparse memory allocation.
    /// </summary>
    internal sealed unsafe class WindowsVirtualMemoryBackend : IVirtualMemoryBackend
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

        public void* Reserve(long sizeBytes)
        {
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

        public void Commit(void* ptr, long sizeBytes)
        {
            void* result = VirtualAlloc(ptr, (nuint)sizeBytes, MEM_COMMIT, PAGE_READWRITE);

            if (result == null)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw new InvalidOperationException(
                    $"VirtualAlloc(Commit) failed for {sizeBytes} bytes at {(long)ptr:X}: {message} (Error: {error})");
            }
        }

        public void Decommit(void* ptr, long sizeBytes)
        {
            bool success = VirtualFree(ptr, (nuint)sizeBytes, MEM_DECOMMIT);

            #if FDP_PARANOID_MODE
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw new InvalidOperationException(
                    $"VirtualFree(Decommit) failed for {sizeBytes} bytes at {(long)ptr:X}: {message} (Error: {error})");
            }
            #else
            _ = success;
            #endif
        }

        public void Free(void* ptr, long originalReservedSize)
        {
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
            #else
            _ = success;
            #endif
        }
    }
}
