using Xunit;
using Fdp.Core;
using System;

namespace Fdp.Tests
{
    public class EntityTests
    {
        [Fact]
        public void Entity_DefaultConstruction()
        {
            var entity = new Entity(42, 5);
            
            Assert.Equal(42, entity.Index);
            Assert.Equal(5, entity.Generation);
            Assert.False(entity.IsNull);
        }
        
        [Fact]
        public void Entity_NullEntity()
        {
            var entity = Entity.Null;
            
            Assert.True(entity.IsNull);
            Assert.Equal(0, entity.Index);
        }
        
        [Fact]
        public void Entity_Equality()
        {
            var e1 = new Entity(10, 3);
            var e2 = new Entity(10, 3);
            var e3 = new Entity(10, 4); // Different generation
            var e4 = new Entity(11, 3); // Different index
            
            Assert.True(e1 == e2);
            Assert.True(e1.Equals(e2));
            Assert.False(e1 == e3);
            Assert.False(e1 == e4);
        }
    }
    
    public class EntityIndexTests
    {
        [Fact]
        public void CreateEntity_ReturnsValidEntity()
        {
            using var index = new EntityIndex();
            
            var entity = index.CreateEntity();
            
            Assert.False(entity.IsNull);
            Assert.Equal(0, entity.Index); // First entity
            Assert.Equal(1, entity.Generation); // First generation
        }
        
        [Fact]
        public void CreateEntity_IncrementsActiveCount()
        {
            using var index = new EntityIndex();
            
            Assert.Equal(0, index.ActiveCount);
            
            var e1 = index.CreateEntity();
            Assert.Equal(1, index.ActiveCount);
            
            var e2 = index.CreateEntity();
            Assert.Equal(2, index.ActiveCount);
        }
        
        [Fact]
        public void CreateEntity_SequentialIndices()
        {
            using var index = new EntityIndex();
            
            var e0 = index.CreateEntity();
            var e1 = index.CreateEntity();
            var e2 = index.CreateEntity();
            
            Assert.Equal(0, e0.Index);
            Assert.Equal(1, e1.Index);
            Assert.Equal(2, e2.Index);
        }
        
        [Fact]
        public void IsAlive_NewEntity_ReturnsTrue()
        {
            using var index = new EntityIndex();
            
            var entity = index.CreateEntity();
            
            Assert.True(index.IsAlive(entity));
        }
        
        [Fact]
        public void IsAlive_NullEntity_ReturnsFalse()
        {
            using var index = new EntityIndex();
            
            Assert.False(index.IsAlive(Entity.Null));
        }
        
        [Fact]
        public void DestroyEntity_MarksNotAlive()
        {
            using var index = new EntityIndex();
            
            var entity = index.CreateEntity();
            Assert.True(index.IsAlive(entity));
            
            index.DestroyEntity(entity);
            Assert.False(index.IsAlive(entity));
        }
        
        [Fact]
        public void DestroyEntity_DecrementsActiveCount()
        {
            using var index = new EntityIndex();
            
            var e1 = index.CreateEntity();
            var e2 = index.CreateEntity();
            Assert.Equal(2, index.ActiveCount);
            
            index.DestroyEntity(e1);
            Assert.Equal(1, index.ActiveCount);
            
            index.DestroyEntity(e2);
            Assert.Equal(0, index.ActiveCount);
        }
        
        [Fact]
        public void DestroyEntity_IncrementGeneration()
        {
            using var index = new EntityIndex();
            
            var e1 = index.CreateEntity();
            Assert.Equal(1, e1.Generation);
            
            index.DestroyEntity(e1);
            
            // Recreate at same index
            var e2 = index.CreateEntity();
            Assert.Equal(0, e2.Index); // Same index
            Assert.Equal(2, e2.Generation); // Incremented generation
        }
        
        [Fact]
        public void DestroyEntity_RecyclesIndex()
        {
            using var index = new EntityIndex();
            
            var e1 = index.CreateEntity(); // Index 0
            var e2 = index.CreateEntity(); // Index 1
            
            index.DestroyEntity(e1); // Free index 0
            
            var e3 = index.CreateEntity(); // Should reuse index 0
            Assert.Equal(0, e3.Index);
            Assert.Equal(2, e3.Generation); // But with new generation
        }
        
        [Fact]
        public void IsAlive_StaleEntity_ReturnsFalse()
        {
            using var index = new EntityIndex();
            
            var e1 = index.CreateEntity();
            index.DestroyEntity(e1);
            
            // e1 is now stale
            Assert.False(index.IsAlive(e1));
            
            // Create new entity at same index
            var e2 = index.CreateEntity();
            Assert.Equal(e1.Index, e2.Index); // Same index
            Assert.NotEqual(e1.Generation, e2.Generation); // Different generation
            
            // e1 is still stale
            Assert.False(index.IsAlive(e1));
            // e2 is alive
            Assert.True(index.IsAlive(e2));
        }
        
        [Fact]
        public void GetComponentMaskAndMetadata_ReturnValidReferences()
        {
            using var index = new EntityIndex();
            
            var entity = index.CreateEntity();
            
            ref readonly var meta = ref index.GetMetadata(entity.Index);
            
            Assert.True(meta.IsActive);
            Assert.Equal(1, meta.Generation);
        }
        
        [Fact]
        public void GetComponentMask_CanModify()
        {
            using var index = new EntityIndex();
            
            var entity = index.CreateEntity();
            
            index.GetComponentMask(entity.Index).SetBit(10);
            
            // Verify modification persisted
            Assert.True(index.GetComponentMask(entity.Index).IsSet(10));
        }
        
        [Fact]
        public void MaxIssuedIndex_TracksCorrectly()
        {
            using var index = new EntityIndex();
            
            Assert.Equal(-1, index.MaxIssuedIndex); // No entities yet
            
            index.CreateEntity();
            Assert.Equal(0, index.MaxIssuedIndex);
            
            index.CreateEntity();
            Assert.Equal(1, index.MaxIssuedIndex);
            
            index.CreateEntity();
            Assert.Equal(2, index.MaxIssuedIndex);
        }
        
        [Fact]
        public void MaxIssuedIndex_NotDecreasedOnDestroy()
        {
            using var index = new EntityIndex();
            
            var e1 = index.CreateEntity();
            var e2 = index.CreateEntity();
            Assert.Equal(1, index.MaxIssuedIndex);
            
            index.DestroyEntity(e2);
            Assert.Equal(1, index.MaxIssuedIndex); // Still 1, not decreased
        }
        
        [Fact]
        public void ThreadSafe_ConcurrentCreation()
        {
            using var index = new EntityIndex();
            
            const int entityCount = 1000;
            var entities = new Entity[entityCount];
            
            System.Threading.Tasks.Parallel.For(0, entityCount, i =>
            {
                entities[i] = index.CreateEntity();
            });
            
            Assert.Equal(entityCount, index.ActiveCount);
            
            // Verify all entities are unique and alive
            for (int i = 0; i < entityCount; i++)
            {
                Assert.True(index.IsAlive(entities[i]));
            }
        }
        
        [Fact]
        public void StressTest_CreateDestroyRecycle()
        {
            using var index = new EntityIndex();
            
            // Create 100 entities
            var entities = new Entity[100];
            for (int i = 0; i < 100; i++)
            {
                entities[i] = index.CreateEntity();
            }
            
            Assert.Equal(100, index.ActiveCount);
            
            // Destroy first 50
            for (int i = 0; i < 50; i++)
            {
                index.DestroyEntity(entities[i]);
            }
            
            Assert.Equal(50, index.ActiveCount);
            
            // Create 50 more (should recycle)
            for (int i = 0; i < 50; i++)
            {
                var e = index.CreateEntity();
                Assert.True(index.IsAlive(e));
            }
            
            Assert.Equal(100, index.ActiveCount);
        }
        
        #if FDP_PARANOID_MODE
        [Fact]
        public void DestroyEntity_StaleEntity_Throws()
        {
            using var index = new EntityIndex();
            
            var e1 = index.CreateEntity();
            index.DestroyEntity(e1);
            
            // Try to destroy again (stale entity)
            Assert.Throws<InvalidOperationException>(() =>
            {
                index.DestroyEntity(e1);
            });
        }
        
        [Fact]
        public void DestroyEntity_NullEntity_Throws()
        {
            using var index = new EntityIndex();
            
            Assert.Throws<ArgumentException>(() =>
            {
                index.DestroyEntity(Entity.Null);
            });
        }
        
        [Fact]
        public void GetComponentMask_InvalidIndex_Throws()
        {
            using var index = new EntityIndex();
            
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                ref var comp = ref index.GetComponentMask(999999);
            });
        }
        #endif
    }
}
