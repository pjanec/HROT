using Xunit;
using Fdp.Kernel;
using System;

namespace Fdp.Tests
{
    public class FdpConfigTests
    {
        [Fact]
        public void Constants_AreCorrect()
        {
            Assert.Equal(1_000_000, FdpConfig.MAX_ENTITIES);
            Assert.Equal(65536, FdpConfig.CHUNK_SIZE_BYTES);
            Assert.Equal(256, FdpConfig.MAX_COMPONENT_TYPES);
        }
        
        [Fact]
        public void GetChunkCapacity_SmallStruct_ReturnsCorrectValue()
        {
            int capacity = FdpConfig.GetChunkCapacity<int>();
            Assert.Equal(16384, capacity);
        }
        
        [Fact]
        public void GetRequiredChunks_CalculatesCorrectly()
        {
            int chunks = FdpConfig.GetRequiredChunks<int>();
            Assert.Equal(62, chunks);
        }
    }
    
    public unsafe class NativeChunkTableTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            using var table = new NativeChunkTable<int>();
            
            Assert.Equal(16384, table.ChunkCapacity);
            Assert.Equal(62, table.TotalChunks);
        }
        
        [Fact]
        public void Indexer_ReadWrite_WorksCorrectly()
        {
            using var table = new NativeChunkTable<int>();
            
            table[0] = 111;
            table[100] = 222;
            table[999999] = 333;
            
            Assert.Equal(111, table[0]);
            Assert.Equal(222, table[100]);
            Assert.Equal(333, table[999999]);
        }
        
        [Fact]
        public void PopulationTracking_Works()
        {
            using var table = new NativeChunkTable<int>();
            
            table[0] = 1;
            
            Assert.Equal(0, table.GetPopulationCount(0));
            
            table.IncrementPopulation(0);
            Assert.Equal(1, table.GetPopulationCount(0));
            
            table.DecrementPopulation(0);
            Assert.Equal(0, table.GetPopulationCount(0));
        }
        
        [Fact]
        public void GetChunk_ReturnsCorrectChunk()
        {
            using var table = new NativeChunkTable<int>();
            
            table[0] = 123;
            
            var chunk = table.GetChunk(0);
            Assert.False(chunk.IsNull);
            Assert.Equal(16384, chunk.Capacity);
            Assert.Equal(123, chunk[0]);
        }
        
        [Fact]
        public void TryDecommitChunk_EmptyChunk_Succeeds()
        {
            using var table = new NativeChunkTable<int>();
            
            table[0] = 1;
            Assert.True(table.IsChunkCommitted(0));
            
            bool decommitted = table.TryDecommitChunk(0);
            Assert.True(decommitted);
            Assert.False(table.IsChunkCommitted(0));
        }
        
        [Fact]
        public void ThreadSafe_ConcurrentAllocation_Works()
        {
            using var table = new NativeChunkTable<int>();
            
            System.Threading.Tasks.Parallel.For(0, 10, i =>
            {
                int entityId = i * 16384;
                table[entityId] = i * 100;
            });
            
            var stats = table.GetMemoryStats();
            Assert.Equal(10, stats.CommittedChunks);
            
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(i * 100, table[i * 16384]);
            }
        }
        
        #region Flight Recorder Primitives
        
        [Fact]
        public void SanitizeChunk_DeadSlots_ZeroesMemory()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Write data to slots 0, 1, 2
            table[0] = new Position { X = 1, Y = 2, Z = 3 };
            table[1] = new Position { X = 4, Y = 5, Z = 6 };
            table[2] = new Position { X = 7, Y = 8, Z = 9 };
            
            // Create liveness mask: slot 0 alive, slot 1 dead, slot 2 alive
            int capacity = table.ChunkCapacity;
            Span<bool> liveness = stackalloc bool[capacity];
            liveness[0] = true;  // Alive
            liveness[1] = false; // Dead - should be zeroed
            liveness[2] = true;  // Alive
            
            // Act
            table.SanitizeChunk(0, liveness);
            
            // Assert
            ref readonly var pos0 = ref table.GetRefRO(0);
            Assert.Equal(1f, pos0.X); // Alive - should be unchanged
            Assert.Equal(2f, pos0.Y);
            Assert.Equal(3f, pos0.Z);
            
            ref readonly var pos1 = ref table.GetRefRO(1);
            Assert.Equal(0f, pos1.X); // Dead - should be zeroed
            Assert.Equal(0f, pos1.Y);
            Assert.Equal(0f, pos1.Z);
            
            ref readonly var pos2 = ref table.GetRefRO(2);
            Assert.Equal(7f, pos2.X); // Alive - should be unchanged
            Assert.Equal(8f, pos2.Y);
            Assert.Equal(9f, pos2.Z);
        }
        
        [Fact]
        public void SanitizeChunk_AllDead_ZeroesEntireChunk()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Fill first few slots with data
            for (int i = 0; i < 10; i++)
            {
                table[i] = new Position { X = i * 10, Y = i * 20, Z = i * 30 };
            }
            
            // Create liveness mask with all false (all dead)
            int capacity = table.ChunkCapacity;
            Span<bool> liveness = stackalloc bool[capacity];
            // All default to false
            
            // Act
            table.SanitizeChunk(0, liveness);
            
            // Assert - all slots should be zeroed
            for (int i = 0; i < 10; i++)
            {
                ref readonly var pos = ref table.GetRefRO(i);
                Assert.Equal(0f, pos.X);
                Assert.Equal(0f, pos.Y);
                Assert.Equal(0f, pos.Z);
            }
        }
        
        [Fact]
        public void SanitizeChunk_AllAlive_PreservesAllData()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Fill first few slots
            for (int i = 0; i < 10; i++)
            {
                table[i] = new Position { X = i * 10, Y = i * 20, Z = i * 30 };
            }
            
            // Create liveness mask with all true (all alive)
            int capacity = table.ChunkCapacity;
            Span<bool> liveness = stackalloc bool[capacity];
            for (int i = 0; i < capacity; i++)
            {
                liveness[i] = true;
            }
            
            // Act
            table.SanitizeChunk(0, liveness);
            
            // Assert - all data should be preserved
            for (int i = 0; i < 10; i++)
            {
                ref readonly var pos = ref table.GetRefRO(i);
                Assert.Equal(i * 10f, pos.X);
                Assert.Equal(i * 20f, pos.Y);
                Assert.Equal(i * 30f, pos.Z);
            }
        }
        
        [Fact]
        public void CopyChunkToBuffer_ReturnsCorrectSize()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            table[0] = new Position { X = 1, Y = 2, Z = 3 };
            
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            
            // Act
            int bytesCopied = table.CopyChunkToBuffer(0, buffer);
            
            // Assert
            Assert.Equal(FdpConfig.CHUNK_SIZE_BYTES, bytesCopied);
        }
        
        [Fact]
        public void CopyChunkToBuffer_UncommittedChunk_ReturnsZero()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            
            // Act - try to copy chunk 10 which was never allocated
            int bytesCopied = table.CopyChunkToBuffer(10, buffer);
            
            // Assert
            Assert.Equal(0, bytesCopied);
        }
        
        [Fact]
        public void CopyChunkToBuffer_CopiesActualData()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Write known data
            table[0] = new Position { X = 123, Y = 456, Z = 789 };
            table[1] = new Position { X = 111, Y = 222, Z = 333 };
            
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            
            // Act
            int bytesCopied = table.CopyChunkToBuffer(0, buffer);
            
            // Assert - verify data was actually copied
            Assert.True(bytesCopied > 0);
            
            // Read back the first Position from buffer
            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    Position* positions = (Position*)ptr;
                    Assert.Equal(123f, positions[0].X);
                    Assert.Equal(456f, positions[0].Y);
                    Assert.Equal(789f, positions[0].Z);
                    
                    Assert.Equal(111f, positions[1].X);
                    Assert.Equal(222f, positions[1].Y);
                    Assert.Equal(333f, positions[1].Z);
                }
            }
        }
        
        [Fact]
        public void RestoreChunkFromBuffer_RestoresData()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Create source data
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    Position* positions = (Position*)ptr;
                    positions[0] = new Position { X = 10, Y = 20, Z = 30 };
                    positions[1] = new Position { X = 40, Y = 50, Z = 60 };
                    positions[2] = new Position { X = 70, Y = 80, Z = 90 };
                }
            }
            
            // Act
            table.RestoreChunkFromBuffer(0, buffer);
            
            // Assert - data should be restored
            ref readonly var pos0 = ref table.GetRefRO(0);
            Assert.Equal(10f, pos0.X);
            Assert.Equal(20f, pos0.Y);
            Assert.Equal(30f, pos0.Z);
            
            ref readonly var pos1 = ref table.GetRefRO(1);
            Assert.Equal(40f, pos1.X);
            Assert.Equal(50f, pos1.Y);
            Assert.Equal(60f, pos1.Z);
            
            ref readonly var pos2 = ref table.GetRefRO(2);
            Assert.Equal(70f, pos2.X);
            Assert.Equal(80f, pos2.Y);
            Assert.Equal(90f, pos2.Z);
        }
        
        [Fact]
        public void CopyAndRestore_RoundTrip_PreservesData()
        {
            // Arrange
            using var sourceTable = new NativeChunkTable<Position>();
            using var targetTable = new NativeChunkTable<Position>();
            
            // Write test data to source
            for (int i = 0; i < 100; i++)
            {
                sourceTable[i] = new Position { X = i * 1.1f, Y = i * 2.2f, Z = i * 3.3f };
            }
            
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            
            // Act - Copy from source
            int bytesCopied = sourceTable.CopyChunkToBuffer(0, buffer);
            Assert.True(bytesCopied > 0);
            
            // Restore to target
            targetTable.RestoreChunkFromBuffer(0, buffer);
            
            // Assert - target should match source
            for (int i = 0; i < 100; i++)
            {
                ref readonly var source = ref sourceTable.GetRefRO(i);
                ref readonly var target = ref targetTable.GetRefRO(i);
                
                Assert.Equal(source.X, target.X);
                Assert.Equal(source.Y, target.Y);
                Assert.Equal(source.Z, target.Z);
            }
        }
        
        [Fact]
        public void SanitizeAndCopy_ProducesCleanSnapshot()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Write data with gaps (simulating destroyed entities)
            table[0] = new Position { X = 1, Y = 2, Z = 3 };
            table[1] = new Position { X = 999, Y = 999, Z = 999 }; // This will be marked dead
            table[2] = new Position { X = 4, Y = 5, Z = 6 };
            
            // Liveness: 0=alive, 1=dead, 2=alive
            int capacity = table.ChunkCapacity;
            Span<bool> liveness = stackalloc bool[capacity];
            liveness[0] = true;
            liveness[1] = false; // Dead slot
            liveness[2] = true;
            
            // Act - Sanitize then copy
            table.SanitizeChunk(0, liveness);
            
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            table.CopyChunkToBuffer(0, buffer);
            
            // Assert - Verify the buffer has clean data
            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    Position* positions = (Position*)ptr;
                    
                    // Slot 0 should have data
                    Assert.Equal(1f, positions[0].X);
                    
                    // Slot 1 should be zeroed
                    Assert.Equal(0f, positions[1].X);
                    Assert.Equal(0f, positions[1].Y);
                    Assert.Equal(0f, positions[1].Z);
                    
                    // Slot 2 should have data
                    Assert.Equal(4f, positions[2].X);
                }
            }
        }
        
        [Fact]
        public void RestoreChunkFromBuffer_OverwritesPreviousData()
        {
            // Arrange
            using var table = new NativeChunkTable<Position>();
            
            // Write initial data
            table[0] = new Position { X = 100, Y = 200, Z = 300 };
            
            // Create new buffer with different data
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    Position* positions = (Position*)ptr;
                    positions[0] = new Position { X = 1, Y = 2, Z = 3 };
                }
            }
            
            // Act - Restore overwrites
            table.RestoreChunkFromBuffer(0, buffer);
            
            // Assert - Old data should be completely replaced
            ref readonly var pos = ref table.GetRefRO(0);
            Assert.Equal(1f, pos.X);
            Assert.Equal(2f, pos.Y);
            Assert.Equal(3f, pos.Z);
        }
        
        #endregion
    }
}
