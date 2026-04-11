using Xunit;
using Fdp.Kernel;
using System;

namespace Fdp.Tests
{
    /// <summary>
    /// Focused unit tests for individual Flight Recorder primitives.
    /// Tests each component in isolation before integration.
    /// </summary>
    public class FlightRecorderPrimitivesTests
    {
        [Fact]
        public void GetChunkLiveness_EmptyChunk_AllFalse()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            
            // Act
            entityIndex.GetChunkLiveness(0, liveness);
            
            // Assert - All should be false (no entities created)
            for (int i = 0; i < chunkCapacity; i++)
            {
                Assert.False(liveness[i], $"Slot {i} should be dead");
            }
        }
        
        [Fact]
        public void GetChunkLiveness_SingleEntity_CorrectLiveness()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            var e1 = entityIndex.CreateEntity();
            
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            
            // Act
            entityIndex.GetChunkLiveness(0, liveness);
            
            // Assert
            Assert.True(liveness[e1.Index], $"Entity {e1.Index} should be alive");
            
            // All other slots should be dead
            for (int i = 0; i < chunkCapacity; i++)
            {
                if (i != e1.Index)
                {
                    Assert.False(liveness[i], $"Slot {i} should be dead");
                }
            }
        }
        
        [Fact]
        public void GetChunkLiveness_AfterDestroy_MarksAsDead()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            var e1 = entityIndex.CreateEntity();
            var e2 = entityIndex.CreateEntity();
            
            // Destroy e1
            entityIndex.DestroyEntity(e1);
            
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            
            // Act
            entityIndex.GetChunkLiveness(0, liveness);
            
            // Assert
            Assert.False(liveness[e1.Index], "Destroyed entity should be dead");
            Assert.True(liveness[e2.Index], "Active entity should be alive");
        }
        
        [Fact]
        public void SanitizeChunk_WithCorrectSizedLiveness_DoesNotThrow()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 123, Y = 456, Z = 789 });
            
            var table = repo.GetComponentTable<Position>();
            var chunkTable = table.GetChunkTable();
            var entityIndex = repo.GetEntityIndex();
            
            int chunkCapacity = entityIndex.GetChunkCapacity();
            
            // Note: EntityIndex capacity (based on EntityHeader size) will differ from
            // component table capacity (based on Position size). This is by design.
            // GetChunkLiveness uses EntityIndex capacity, which is what matters for iteration.
            
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, liveness);
            
            // Act - Should not throw even though Position table has different capacity
            chunkTable.SanitizeChunk(0, liveness);
            
            // Assert - Entity is alive, so data should NOT be zeroed
            ref readonly var pos = ref chunkTable.GetRefRO(e1.Index);
            Assert.Equal(123f, pos.X);
        }
        
        [Fact]
        public void SanitizeChunk_DeadEntity_ZeroesData()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 123, Y = 456, Z = 789 });
            
            // Destroy entity (leaves garbage in memory)
            repo.DestroyEntity(e1);
            
            var table = repo.GetComponentTable<Position>();
            var chunkTable = table.GetChunkTable();
            var entityIndex = repo.GetEntityIndex();
            
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, liveness);
            
            // Act
            chunkTable.SanitizeChunk(0, liveness);
            
            // Assert - Data should be zeroed
            ref readonly var pos = ref chunkTable.GetRefRO(e1.Index);
            Assert.Equal(0f, pos.X);
            Assert.Equal(0f, pos.Y);
            Assert.Equal(0f, pos.Z);
        }
        
        [Fact]
        public void CopyChunkToBuffer_AndRestore_RoundTrip()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 10, Y = 20, Z = 30 });
            
            var table = repo.GetComponentTable<Position>();
            var chunkTable = table.GetChunkTable();
            
            byte[] buffer = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            
            // Act - Copy
            int bytesCopied = chunkTable.CopyChunkToBuffer(0, buffer);
            
            // Modify original
            ref var pos = ref chunkTable[e1.Index];
            pos.X = 999;
            
            // Restore from buffer
            chunkTable.RestoreChunkFromBuffer(0, buffer);
            
            // Assert - Should have original values
            ref readonly var restored = ref chunkTable.GetRefRO(e1.Index);
            Assert.Equal(10f, restored.X);
            Assert.Equal(20f, restored.Y);
            Assert.Equal(30f, restored.Z);
            Assert.Equal(FdpConfig.CHUNK_SIZE_BYTES, bytesCopied);
        }
        
        [Fact]
        public void DestructionLog_CapturesDestruction()
        {
            // Arrange
            using var repo = new EntityRepository();
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            
            // Act
            repo.DestroyEntity(e1);
            
            // Assert
            var log = repo.GetDestructionLog();
            Assert.Single(log);
            Assert.Equal(e1.Index, log[0].Index);
            Assert.Equal(e1.Generation, log[0].Generation);
            
            // Clear works
            repo.ClearDestructionLog();
            Assert.Empty(repo.GetDestructionLog());
        }
        
        #region ForceRestoreEntity Tests (Critical for Playback)
        
        [Fact]
        public void ForceRestoreEntity_CreatesEntityWithExactState()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            int targetIndex = 42;
            int targetGeneration = 7;
            var componentMask = new BitMask256();
            componentMask.SetBit(3);
            componentMask.SetBit(5);
            
            // Act
            entityIndex.ForceRestoreEntity(targetIndex, isActive: true, targetGeneration, componentMask);
            
            // Assert
            var entity = new Entity(targetIndex, (ushort)targetGeneration);
            Assert.True(entityIndex.IsAlive(entity));
            
            ref var header = ref entityIndex.GetHeader(targetIndex);
            Assert.True(header.IsActive);
            Assert.Equal(targetGeneration, header.Generation);
            Assert.True(header.ComponentMask.IsSet(3));
            Assert.True(header.ComponentMask.IsSet(5));
        }
        
        [Fact]
        public void ForceRestoreEntity_SparseIndices_AllAccessible()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Act - Restore non-contiguous entities (simulating sparse snapshot)
            entityIndex.ForceRestoreEntity(0, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(10, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(100, isActive: true, 1, new BitMask256());
            
            // Assert
            Assert.True(entityIndex.IsAlive(new Entity(0, 1)));
            Assert.True(entityIndex.IsAlive(new Entity(10, 1)));
            Assert.True(entityIndex.IsAlive(new Entity(100, 1)));
            
            // Verify liveness reflects sparse state
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, liveness);
            
            Assert.True(liveness[0]);
            Assert.False(liveness[1]); // Gap
            Assert.True(liveness[10]);
            Assert.False(liveness[11]); // Gap
        }
        
        [Fact]
        public void ForceRestoreEntity_InactiveEntity_NotMarkedAlive()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Act - Force restore an inactive (dead) entity
            entityIndex.ForceRestoreEntity(5, isActive: false, 2, new BitMask256());
            
            // Assert
            Assert.False(entityIndex.IsAlive(new Entity(5, 2)));
            
            ref var header = ref entityIndex.GetHeader(5);
            Assert.False(header.IsActive);
            Assert.Equal(2, header.Generation);
        }
        
        #endregion
        
        #region RebuildMetadata Tests (Critical for Playback Index Repair)
        
        [Fact]
        public void RebuildMetadata_AfterForceRestore_RecalculatesCounts()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Simulate restoring 3 active entities and 1 inactive
            entityIndex.ForceRestoreEntity(0, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(1, isActive: false, 2, new BitMask256()); // Dead
            entityIndex.ForceRestoreEntity(2, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(5, isActive: true, 1, new BitMask256());
            
            // Act
            entityIndex.RebuildMetadata();
            
            // Assert - Should correctly count 3 active entities
            Assert.True(entityIndex.IsAlive(new Entity(0, 1)));
            Assert.False(entityIndex.IsAlive(new Entity(1, 2))); // Should still be dead
            Assert.True(entityIndex.IsAlive(new Entity(2, 1)));
            Assert.True(entityIndex.IsAlive(new Entity(5, 1)));
        }
        
        [Fact]
        public void RebuildMetadata_RecalculatesChunkPopulation()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            
            // Force restore 10 entities in chunk 0
            for (int i = 0; i < 10; i++)
            {
                entityIndex.ForceRestoreEntity(i, isActive: true, 1, new BitMask256());
            }
            
            // Force restore 5 entities in chunk 1
            int chunk1Start = chunkCapacity;
            for (int i = 0; i < 5; i++)
            {
                entityIndex.ForceRestoreEntity(chunk1Start + i, isActive: true, 1, new BitMask256());
            }
            
            // Act
            entityIndex.RebuildMetadata();
            
            // Assert
            Assert.Equal(10, entityIndex.GetChunkPopulation(0));
            Assert.Equal(5, entityIndex.GetChunkPopulation(1));
        }
        
        [Fact]
        public void RebuildMetadata_WithLargeGaps_HandlesEfficiently()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Create entities with large gaps (sparse snapshot)
            entityIndex.ForceRestoreEntity(0, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(1000, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(5000, isActive: true, 1, new BitMask256());
            
            // Act
            entityIndex.RebuildMetadata();
            
            // Assert - All entities should be accessible
            Assert.True(entityIndex.IsAlive(new Entity(0, 1)));
            Assert.True(entityIndex.IsAlive(new Entity(1000, 1)));
            Assert.True(entityIndex.IsAlive(new Entity(5000, 1)));
        }
        
        #endregion
        
        #region RebuildFreeList Tests (Critical for Post-Playback Entity Creation)
        
        [Fact]
        public void RebuildFreeList_AfterRestore_FillsGaps()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Restore sparse entities with gaps
            entityIndex.ForceRestoreEntity(0, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(5, isActive: true, 1, new BitMask256());
            entityIndex.ForceRestoreEntity(10, isActive: true, 1, new BitMask256());
            // Gaps: 1, 2, 3, 4, 6, 7, 8, 9
            
            // Act
            entityIndex.RebuildMetadata(); // This calls RebuildFreeList internally
            
            // Assert - Next created entity should use one of the gaps
            var newEntity = entityIndex.CreateEntity();
            Assert.True(newEntity.Index >= 1 && newEntity.Index <= 9, 
                $"Expected new entity to use gap, got index {newEntity.Index}");
            Assert.True(newEntity.Index != 5 && newEntity.Index != 10);
        }
        
        [Fact]
        public void RebuildFreeList_WithNoGaps_AllocatesSequentially()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Create contiguous entities
            for (int i = 0; i < 10; i++)
            {
                entityIndex.CreateEntity();
            }
            
            // Force clear and rebuild (simulating a perfect snapshot restore)
            entityIndex.RebuildMetadata();
            
            // Act - Next entity should be at index 10
            var newEntity = entityIndex.CreateEntity();
            
            // Assert
            Assert.Equal(10, newEntity.Index);
        }
        
        #endregion
        
        #region Full Restore Cycle (Integration of All Primitives)
        
        [Fact]
        public void FullRestoreCycle_EntityIndex_PreservesState()
        {
            // Arrange
            using var entityIndex = new EntityIndex();
            
            // Create entities with gaps
            var e1 = entityIndex.CreateEntity();
            var e2 = entityIndex.CreateEntity();
            var e3 = entityIndex.CreateEntity();
            entityIndex.DestroyEntity(e2); // Create gap at index 1
            
            // Capture original state
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> originalLiveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, originalLiveness);
            
            // Save EntityHeader chunk
            byte[] savedHeaders = new byte[FdpConfig.CHUNK_SIZE_BYTES];
            int bytesCopied = entityIndex.CopyChunkToBuffer(0, savedHeaders);
            Assert.True(bytesCopied > 0);
            
            // Act - Simulate loading from disk
            entityIndex.Clear();
            entityIndex.RestoreChunkFromBuffer(0, savedHeaders);
            entityIndex.RebuildMetadata();
            
            // Assert - Liveness should match perfectly
            Span<bool> restoredLiveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, restoredLiveness);
            
            for (int i = 0; i < chunkCapacity; i++)
            {
                Assert.Equal(originalLiveness[i], restoredLiveness[i]);
            }
            
            // Verify specific entities
            Assert.True(entityIndex.IsAlive(e1), "Entity 1 should be alive");
            Assert.False(entityIndex.IsAlive(e2), "Entity 2 should still be dead");
            Assert.True(entityIndex.IsAlive(e3), "Entity 3 should be alive");
        }
        
        #endregion
    }
}
