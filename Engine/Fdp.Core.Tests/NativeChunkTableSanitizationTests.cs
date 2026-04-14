using System;
using Xunit;
using Fdp.Kernel;
using System.Runtime.InteropServices;

namespace Fdp.Tests
{
    public class NativeChunkTableSanitizationTests
    {
        [Fact]
        public void SanitizeChunk_ZerosOutDeadEntities()
        {
            // Arrange
            using var table = new NativeChunkTable<int>();
            int capacity = table.ChunkCapacity;
            
            // Allocate chunk 0 and fill with data
            // We use GetRefRW to ensure allocation and set values
            for (int i = 0; i < capacity; i++)
            {
                ref int val = ref table.GetRefRW(i, 1);
                val = unchecked((int)0xFFFFFFFF); // Set to -1/All ones
            }
            
            // Verify filling worked
            Assert.Equal(-1, table.GetRefRO(0));
            Assert.Equal(-1, table.GetRefRO(capacity - 1));
            
            // Create liveness map:
            // 0 -> Alive
            // 1 -> Dead
            // 2 -> Alive
            // Rest -> Dead
            Span<bool> liveness = stackalloc bool[capacity];
            liveness[0] = true;
            liveness[1] = false;
            liveness[2] = true;
            for (int i = 3; i < capacity; i++) liveness[i] = false;
            
            // Act
            table.SanitizeChunk(0, liveness);
            
            // Assert
            Assert.Equal(-1, table.GetRefRO(0)); // Should remain
            Assert.Equal(0, table.GetRefRO(1));  // Should be zeroed
            Assert.Equal(-1, table.GetRefRO(2)); // Should remain
            
            // Rest should be zeroed
            for (int i = 3; i < capacity; i++)
            {
                Assert.Equal(0, table.GetRefRO(i));
            }
        }
        
        [Fact]
        public void CopyChunkToBuffer_CopiesExactlyChunkSize()
        {
            using var table = new NativeChunkTable<long>();
            ref long val = ref table.GetRefRW(0, 1);
            val = 0xDEADBEEF;
            
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            
            int copied = table.CopyChunkToBuffer(0, buffer);
            
            Assert.Equal(FdpConfig.CHUNK_SIZE_BYTES, copied);
            
            // Verify content manually
            // long is 8 bytes. "EF BE AD DE 00 00 00 00" (Little Endian)
            Assert.Equal(0xEF, buffer[0]);
            Assert.Equal(0xBE, buffer[1]);
            Assert.Equal(0xAD, buffer[2]);
            Assert.Equal(0xDE, buffer[3]);
        }

        [Fact]
        public void RestoreChunkFromBuffer_OverwritesContent()
        {
            using var table = new NativeChunkTable<int>();
            
            // Setup buffer with specific pattern
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            Span<int> bufferAsInt = MemoryMarshal.Cast<byte, int>(buffer);
            bufferAsInt[0] = 42;
            bufferAsInt[1] = 100;
            
            // Act
            table.RestoreChunkFromBuffer(0, buffer);
            
            // Assert
            Assert.Equal(42, table.GetRefRO(0));
            Assert.Equal(100, table.GetRefRO(1));
            
            // Verify memory was committed
            Assert.True(table.IsChunkCommitted(0));
        }

        [Fact]
        public void SanitizeChunk_IgnoredUncommittedChunk()
        {
            using var table = new NativeChunkTable<int>();
            
            // Chunk 0 is not committed yet
            Span<bool> liveness = stackalloc bool[table.ChunkCapacity];
            
            // Should not throw
            table.SanitizeChunk(0, liveness);
            
            Assert.False(table.IsChunkCommitted(0));
        }
    }
}
