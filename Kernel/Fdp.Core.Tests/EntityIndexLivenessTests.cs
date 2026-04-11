using System;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public class EntityIndexLivenessTests
    {
        [Fact]
        public void GetChunkLiveness_IdentifiesAliveAndDeadEntities()
        {
            using var index = new EntityIndex();
            
            // Create 3 entities in first chunk (indices 0, 1, 2)
            var e0 = index.CreateEntity();
            var e1 = index.CreateEntity();
            var e2 = index.CreateEntity();
            
            // Destroy middle one
            index.DestroyEntity(e1);
            
            // Get liveness
            int chunkSize = index.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkSize];
            index.GetChunkLiveness(0, liveness);
            
            // Verify
            Assert.True(liveness[0], "Entity 0 should be alive");
            Assert.False(liveness[1], "Entity 1 should be dead");
            Assert.True(liveness[2], "Entity 2 should be alive");
            
            // Verify rest are false
            for (int i = 3; i < chunkSize; i++)
            {
                Assert.False(liveness[i], $"Slot {i} should be empty");
            }
        }

        [Fact]
        public void ForceRestore_RestoresEntityState_AndUpdatesCounts()
        {
            using var index = new EntityIndex();
            
            // Force restore an entity at index 10
            var mask = new BitMask256();
            mask.SetBit(1);
            
            // Act: Restore as Alive
            index.ForceRestoreEntity(10, true, 5, mask);
            
            // Assert
            var header = index.GetHeader(10);
            Assert.True(header.IsActive);
            Assert.Equal(5, header.Generation);
            Assert.True(header.ComponentMask.IsSet(1));
            
            Assert.Equal(1, index.ActiveCount);
            Assert.Equal(10, index.MaxIssuedIndex);
            
            // Check IsAlive logic via wrapper (requires constructing Entity struct)
            var e = new Entity(10, 5);
            Assert.True(index.IsAlive(e));
            
            // Act: Restore as Dead (overwrite)
            index.ForceRestoreEntity(10, false, 6, mask);
            
            // Assert
            Assert.Equal(0, index.ActiveCount);
            header = index.GetHeader(10);
            Assert.False(header.IsActive);
            
            // Check IsAlive
            var eDead = new Entity(10, 6);
            Assert.False(index.IsAlive(eDead));
        }

        [Fact]
        public void RebuildMetadata_RecalculatesActiveCount_AndFreeList()
        {
            // 1. Setup source index
            using var sourceIndex = new EntityIndex();
            var e0 = sourceIndex.CreateEntity(); // Keep
            var e1 = sourceIndex.CreateEntity(); // Destroy
            var e2 = sourceIndex.CreateEntity(); // Keep
            sourceIndex.DestroyEntity(e1);
            
            // 2. Snapshot the chunk
            int chunkIndex = 0;
            // NativeChunkTable requires full 64KB buffer
            byte[] processedBuffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            // Note: We need the actual buffer size. EntityHeader is 96 bytes?
            // Let's rely on CopyChunkToBuffer return value or similar, or just allocate safely large.
            // EntityHeader size checked in existing test as 96 bytes.
            // But let's verify what CopChunkToBuffer expects. 
            // It expects a Span<byte>.
            // NativeChunkTable<T> CopyChunkToBuffer copies raw structs.
            // So size = Capacity * sizeof(EntityHeader).
            
            int bytesWritten = sourceIndex.CopyChunkToBuffer(chunkIndex, processedBuffer);
            Assert.True(bytesWritten > 0);

            // 3. Create fresh index and inject data
            using var destIndex = new EntityIndex();
            destIndex.RestoreChunkFromBuffer(chunkIndex, processedBuffer);
            
            // At this point, metadata is NOT rebuilt.
            // ActiveCount should be 0 (initial)
            Assert.Equal(0, destIndex.ActiveCount);
            
            // 4. Rebuild
            destIndex.RebuildMetadata();
            
            // 5. Verify Metadata
            Assert.Equal(2, destIndex.ActiveCount); // e0 and e2
            Assert.Equal(2, destIndex.MaxIssuedIndex); // e2 is index 2
            
            // Verify Liveness
            Assert.True(destIndex.IsAlive(new Entity(0, e0.Generation)));
            Assert.False(destIndex.IsAlive(new Entity(1, e1.Generation))); // Was destroyed
            Assert.True(destIndex.IsAlive(new Entity(2, e2.Generation)));
            
            // 6. Verify Free List by creating new entity
            // Since index 1 is free, next CreateEntity should probably pick it up (LIFO or logic dependent)
            // or at least pick a valid free slot.
            var eNew = destIndex.CreateEntity();
            // Typically index 1 is in free list. 
            // If RebuildFreeList works, index 1 is available.
            Assert.True(eNew.Index == 1 || eNew.Index > 2, "Should pick free slot or append");
            
            // Specifically check that index 1 IS reused if it's the only hole
            if (eNew.Index != 1)
            {
                // If it didn't pick 1, maybe logic differs, but let's check if 1 is eventually used
                // or if the free list has it.
                // Note: RebuildFreeList implementation:
                // for i=0 to MaxIssuedIndex: if !Active -> add to free list.
                // So 1 should be in free list.
                // The free list is a stack usually?
                // _freeList[_freeCount++] = i.
                // So it adds 1.
                // CreateEntity: _freeList[--_freeCount].
                // So it should pop 1.
                Assert.Equal(1, eNew.Index);
            }
        }
    }
}
