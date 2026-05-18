using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    public unsafe class PagedAllocatorTests
    {
        [Fact]
        public void Rent_Returns_Valid_Page()
        {
            using var allocator = new HsmCommandAllocator(1);
            var page = allocator.Rent();
            
            Assert.True(page != null);
            Assert.Equal(0, page->BytesUsed);
            
            // Write something
            page->BytesUsed = 100;
            
            allocator.Return(page);
            
            // Rent again
            var page2 = allocator.Rent();
            // Should be reset
            Assert.Equal(0, page2->BytesUsed);
        }
        
        [Fact]
        public void Allocator_Expands_When_Empty()
        {
            using var allocator = new HsmCommandAllocator(0); // Empty
            var page = allocator.Rent();
            Assert.True(page != null);
            allocator.Return(page);
        }

        [Fact]
        public void Allocator_Handles_Multiple_Pages()
        {
            using var allocator = new HsmCommandAllocator(2);
            var p1 = allocator.Rent();
            var p2 = allocator.Rent();
            var p3 = allocator.Rent(); // Expands
            
            Assert.True(p1 != null);
            Assert.True(p2 != null);
            Assert.True(p3 != null);
            Assert.True(p1 != p2);
            Assert.True(p2 != p3);
            
            allocator.Return(p1);
            allocator.Return(p2);
            allocator.Return(p3);
        }
    }
}
