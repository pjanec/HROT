using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public unsafe class NativeMemoryAllocatorTests
    {
        [Fact]
        public void Reserve_ValidSize_ReturnsNonNull()
        {
            long size = 1024 * 1024; // 1MB
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            Assert.True(ptr != null);
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        [Fact]
        public void Reserve_LargeSize_OnlyReservesVirtualSpace()
        {
            // Reserve 100MB - should succeed without physical RAM
            long size = 100 * 1024 * 1024;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            Assert.True(ptr != null);
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        [Fact]
        public void Reserve_Returns64KBAligned()
        {
            long size = 64 * 1024;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            // VirtualAlloc aligns to 64KB boundaries by default
            long address = (long)ptr;
            Assert.Equal(0, address % (64 * 1024));
            Assert.True(NativeMemoryAllocator.Is64KBAligned(ptr));
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        [Fact]
        public void Commit_AfterReserve_CanWrite()
        {
            long size = 4096; // 1 page
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            // Commit to make writable
            NativeMemoryAllocator.Commit(ptr, size);
            
            // Write data
            int* intPtr = (int*)ptr;
            *intPtr = 123456;
            
            // Read back
            Assert.Equal(123456, *intPtr);
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        [Fact]
        public void Commit_PartialRange_Works()
        {
            long totalSize = 10 * 1024 * 1024; // 10MB reserved
            void* basePtr = NativeMemoryAllocator.Reserve(totalSize);
            
            // Commit only 64KB in the middle
            long offset = 5 * 1024 * 1024;
            long commitSize = 64 * 1024;
            byte* targetPtr = (byte*)basePtr + offset;
            
            NativeMemoryAllocator.Commit(targetPtr, commitSize);
            
            // Write to committed region
            *targetPtr = 255;
            Assert.Equal(255, *targetPtr);
            
            NativeMemoryAllocator.Free(basePtr, totalSize);
        }
        
        [Fact]
        public void Decommit_ReleasesPhysicalRAM()
        {
            long size = 64 * 1024;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            // Commit
            NativeMemoryAllocator.Commit(ptr, size);
            
            // Write data
            byte* bytePtr = (byte*)ptr;
            *bytePtr = 42;
            Assert.Equal(42, *bytePtr);
            
            // Decommit (address space still reserved)
            NativeMemoryAllocator.Decommit(ptr, size);
            
            // Can commit again
            NativeMemoryAllocator.Commit(ptr, size);
            
            // Memory is zeroed after recommit
            Assert.Equal(0, *bytePtr);
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        [Fact]
        public void Free_NullPointer_DoesNotThrow()
        {
            // Should handle gracefully
            NativeMemoryAllocator.Free(null, 0);
        }
        
        [Fact]
        public void Free_ReleasesMemory()
        {
            long size = 1024 * 1024;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            NativeMemoryAllocator.Commit(ptr, size);
            
            // This should not throw
            NativeMemoryAllocator.Free(ptr, size);
            
            // Accessing ptr now would cause access violation (don't test this!)
        }
        
        [Fact]
        public void Multiple_ReserveCommitFree_WorksCorrectly()
        {
            for (int i = 0; i < 10; i++)
            {
                long size = 64 * 1024;
                void* ptr = NativeMemoryAllocator.Reserve(size);
                NativeMemoryAllocator.Commit(ptr, size);
                
                // Write pattern
                int* intPtr = (int*)ptr;
                *intPtr = i * 1000;
                Assert.Equal(i * 1000, *intPtr);
                
                NativeMemoryAllocator.Free(ptr, size);
            }
        }
        
        [Fact]
        public void CommitMultiplePages_Works()
        {
            long totalSize = 1024 * 1024; // 1MB
            void* ptr = NativeMemoryAllocator.Reserve(totalSize);
            
            // Commit in 64KB chunks
            for (int i = 0; i < 16; i++)
            {
                long offset = i * 64 * 1024;
                byte* chunkPtr = (byte*)ptr + offset;
                NativeMemoryAllocator.Commit(chunkPtr, 64 * 1024);
                
                // Write to each chunk
                *chunkPtr = (byte)i;
            }
            
            // Verify all chunks
            for (int i = 0; i < 16; i++)
            {
                long offset = i * 64 * 1024;
                byte* chunkPtr = (byte*)ptr + offset;
                Assert.Equal((byte)i, *chunkPtr);
            }
            
            NativeMemoryAllocator.Free(ptr, totalSize);
        }
        
        [Fact]
        public void Alignment_MultipleAllocations_Consistent()
        {
            for (int i = 0; i < 5; i++)
            {
                long size = (i + 1) * 64 * 1024;
                void* ptr = NativeMemoryAllocator.Reserve(size);
                
                Assert.True(NativeMemoryAllocator.Is64KBAligned(ptr));
                
                NativeMemoryAllocator.Free(ptr, size);
            }
        }
        
        [Fact]
        public void ReserveCommitDecommitRecommit_Works()
        {
            long size = 64 * 1024;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            // First commit
            NativeMemoryAllocator.Commit(ptr, size);
            int* intPtr = (int*)ptr;
            *intPtr = 111;
            Assert.Equal(111, *intPtr);
            
            // Decommit
            NativeMemoryAllocator.Decommit(ptr, size);
            
            // Recommit
            NativeMemoryAllocator.Commit(ptr, size);
            
            // Memory zeroed
            Assert.Equal(0, *intPtr);
            
            // Can write again
            *intPtr = 222;
            Assert.Equal(222, *intPtr);
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        #if FDP_PARANOID_MODE
        [Fact]
        public void Reserve_NegativeSize_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                NativeMemoryAllocator.Reserve(-1);
            });
        }
        
        [Fact]
        public void Reserve_ZeroSize_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                NativeMemoryAllocator.Reserve(0);
            });
        }
        
        [Fact]
        public void Commit_NullPointer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                NativeMemoryAllocator.Commit(null, 4096);
            });
        }
        
        [Fact]
        public void Commit_NegativeSize_Throws()
        {
            long size = 4096;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            try
            {
                Assert.Throws<ArgumentException>(() =>
                {
                    NativeMemoryAllocator.Commit(ptr, -1);
                });
            }
            finally
            {
                NativeMemoryAllocator.Free(ptr, size);
            }
        }
        
        [Fact]
        public void Decommit_NegativeSize_Throws()
        {
            long size = 4096;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            NativeMemoryAllocator.Commit(ptr, size);
            
            try
            {
                Assert.Throws<ArgumentException>(() =>
                {
                    NativeMemoryAllocator.Decommit(ptr, -1);
                });
            }
            finally
            {
                NativeMemoryAllocator.Free(ptr, size);
            }
        }
        #endif
        
        [Fact]
        public void LargeReservation_MultipleGB_Works()
        {
            // Reserve 1GB (only virtual address space, not RAM)
            long size = 1024L * 1024 * 1024;
            void* ptr = NativeMemoryAllocator.Reserve(size);
            
            Assert.True(ptr != null);
            Assert.True(NativeMemoryAllocator.Is64KBAligned(ptr));
            
            // Commit only 64KB
            NativeMemoryAllocator.Commit(ptr, 64 * 1024);
            
            int* intPtr = (int*)ptr;
            *intPtr = 999;
            Assert.Equal(999, *intPtr);
            
            NativeMemoryAllocator.Free(ptr, size);
        }
        
        [Fact]
        public void StressTest_ManySmallAllocations()
        {
            const int count = 100;
            void*[] ptrs = new void*[count];
            
            // Allocate
            for (int i = 0; i < count; i++)
            {
                ptrs[i] = NativeMemoryAllocator.Reserve(64 * 1024);
                NativeMemoryAllocator.Commit(ptrs[i], 64 * 1024);
            }
            
            // Verify all aligned
            for (int i = 0; i < count; i++)
            {
                Assert.True(NativeMemoryAllocator.Is64KBAligned(ptrs[i]));
            }
            
            // Free all
            for (int i = 0; i < count; i++)
            {
                NativeMemoryAllocator.Free(ptrs[i], 64 * 1024);
            }
        }
    }
}
