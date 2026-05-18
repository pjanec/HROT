using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Thread-safe allocator for 4KB Command Pages.
    /// Uses unmanaged memory to ensure fixed addresses.
    /// Implements Task 7: Paged Command Buffer Allocator.
    /// </summary>
    public unsafe class HsmCommandAllocator : IDisposable
    {
        private readonly ConcurrentStack<IntPtr> _pool = new ConcurrentStack<IntPtr>();
        private readonly ConcurrentBag<IntPtr> _allAllocations = new ConcurrentBag<IntPtr>();
        private bool _disposed;

        /// <summary>
        /// Create a new allocator with initial capacity.
        /// </summary>
        public HsmCommandAllocator(int initialCapacity = 16)
        {
            for (int i = 0; i < initialCapacity; i++)
            {
                _pool.Push(CreatePage());
            }
        }

        /// <summary>
        /// Rent a command page from the pool.
        /// Returns a pointer to a zeroed header page.
        /// </summary>
        public CommandPage* Rent()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HsmCommandAllocator));

            if (!_pool.TryPop(out IntPtr ptr))
            {
                ptr = CreatePage();
            }
            
            CommandPage* page = (CommandPage*)ptr;
            // Reset header fields
            page->BytesUsed = 0;
            page->PageIndex = 0;
            page->NextPageOffset = 0;
            page->Reserved = 0;
            // We do NOT zero the Data buffer for performance. 
            // BytesUsed determines valid data range.
            
            return page;
        }

        /// <summary>
        /// Return a command page to the pool.
        /// </summary>
        public void Return(CommandPage* page)
        {
            if (_disposed) return; // If disposed, we let it leak or implicitly cleaned? 
                                   // Actually if disposed, the bag handles free. 
                                   // But adding to stack is useless.
            if (page == null) return;
            
            _pool.Push((IntPtr)page);
        }

        private IntPtr CreatePage()
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(CommandPage));
            _allAllocations.Add(ptr);
            return ptr;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Free all memory
            foreach (var ptr in _allAllocations)
            {
                Marshal.FreeHGlobal(ptr);
            }
            
            _pool.Clear();
            // _allAllocations is ConcurrentBag, Clear doesn't exist on older .NET or sometimes? 
            // It's mostly for iteration here.
        }
    }
}
