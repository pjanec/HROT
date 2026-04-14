using Xunit;
using Fdp.Core;
using System;

namespace Fdp.Tests
{
    // Test component types are now in TestComponents.cs
    [ComponentId(169)]
    public struct Tag { } // Empty struct (tag component)
    
    // Disable parallelization for component tests to avoid registry conflicts
    // [Collection("ComponentTests")]
    public class ComponentTypeTests
    {
        public ComponentTypeTests()
        {
            // Do not clear registry as it affects other tests
        }
        
        [Fact]
        public void ComponentType_AssignsUniqueIDs()
        {
            int posId = ComponentType<Position>.ID;
            int velId = ComponentType<Velocity>.ID;
            int hpId = ComponentType<Health>.ID;
            
            Assert.NotEqual(posId, velId);
            Assert.NotEqual(posId, hpId);
            Assert.NotEqual(velId, hpId);
        }
        
        [Fact]
        public void ComponentType_SameTypeReturnsSameID()
        {
            int id1 = ComponentType<Position>.ID;
            int id2 = ComponentType<Position>.ID;
            
            Assert.Equal(id1, id2);
        }
        
        [Fact]
        public void ComponentType_IDsAreSequential()
        {
            // Do not clear registry
            // ComponentTypeRegistry.Clear();
            
            int id1 = ComponentType<Position>.ID;
            int id2 = ComponentType<Velocity>.ID;
            int id3 = ComponentType<Health>.ID;
            
            Assert.True(id1 >= 0);
            Assert.True(id2 > id1); // IDs should strictly increase in registration order
            Assert.True(id3 > id2);
        }
        
        [Fact]
        public void ComponentType_Size_IsCorrect()
        {
            // Position = 3 floats = 12 bytes
            Assert.Equal(12, ComponentType<Position>.Size);
            
            // Health = 1 int = 4 bytes
            Assert.Equal(4, ComponentType<Health>.Size);
        }
        
        [Fact]
        public void ComponentType_IsTag_DetectsEmptyStruct()
        {
            Assert.True(ComponentType<Tag>.IsTag);
            Assert.False(ComponentType<Position>.IsTag);
            Assert.False(ComponentType<Health>.IsTag);
        }
        
        [Fact]
        public void ComponentTypeRegistry_RegisteredCount()
        {
            // ComponentTypeRegistry.Clear();
            
            int initialCount = ComponentTypeRegistry.RegisteredCount;
            
            var id1 = ComponentType<Position>.ID;
            Assert.True(ComponentTypeRegistry.RegisteredCount >= initialCount);
            
            var id2 = ComponentType<Velocity>.ID;
            Assert.True(ComponentTypeRegistry.RegisteredCount >= initialCount);
            
            // Accessing same type again doesn't increment
            var id1_again = ComponentType<Position>.ID;
            // Count shouldn't change
            int countAfter = ComponentTypeRegistry.RegisteredCount;
            _ = ComponentType<Position>.ID;
            Assert.Equal(countAfter, ComponentTypeRegistry.RegisteredCount);
        }
        
        [Fact]
        public void ComponentTypeRegistry_GetType_ReturnsCorrectType()
        {
            // ComponentTypeRegistry.Clear();
            
            int posId = ComponentType<Position>.ID;
            int velId = ComponentType<Velocity>.ID;
            
            Assert.Equal(typeof(Position), ComponentTypeRegistry.GetType(posId));
            Assert.Equal(typeof(Velocity), ComponentTypeRegistry.GetType(velId));
        }
        
        [Fact]
        public void ComponentTypeRegistry_GetType_InvalidID_ReturnsNull()
        {
            // ComponentTypeRegistry.Clear();
            
            Assert.Null(ComponentTypeRegistry.GetType(999));
            Assert.Null(ComponentTypeRegistry.GetType(-1));
        }
    }
    
    // [Collection("ComponentTests")]
    public class ComponentTableTests
    {
        public ComponentTableTests()
        {
            // ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void ComponentTable_Creation()
        {
            using var table = new ComponentTable<Position>();
            
            Assert.Equal(ComponentType<Position>.ID, table.ComponentTypeId);
            Assert.Equal(typeof(Position), table.ComponentType);
            Assert.Equal(12, table.ComponentSize);
        }
        
        [Fact]
        public void ComponentTable_GetSet_WorksCorrectly()
        {
            using var table = new ComponentTable<Position>();
            
            var pos = new Position { X = 1, Y = 2, Z = 3 };
            table.Set(0, pos);
            
            ref var retrieved = ref table.Get(0);
            Assert.Equal(1, retrieved.X);
            Assert.Equal(2, retrieved.Y);
            Assert.Equal(3, retrieved.Z);
        }
        
        [Fact]
        public void ComponentTable_Get_ReturnsReference()
        {
            using var table = new ComponentTable<Position>();
            
            table.Set(0, new Position { X = 1, Y = 2, Z = 3 });
            
            ref var pos = ref table.Get(0);
            pos.X = 10;
            
            // Verify modification persisted
            Assert.Equal(10, table.Get(0).X);
        }
        
        [Fact]
        public void ComponentTable_MultipleEntities()
        {
            using var table = new ComponentTable<Position>();
            
            for (int i = 0; i < 100; i++)
            {
                table.Set(i, new Position { X = i, Y = i * 2, Z = i * 3 });
            }
            
            // Verify all
            for (int i = 0; i < 100; i++)
            {
                ref var pos = ref table.Get(i);
                Assert.Equal(i, pos.X);
                Assert.Equal(i * 2, pos.Y);
                Assert.Equal(i * 3, pos.Z);
            }
        }
        
        [Fact]
        public void ComponentTable_DifferentTypes_IndependentStorage()
        {
            using var posTable = new ComponentTable<Position>();
            using var velTable = new ComponentTable<Velocity>();
            
            posTable.Set(0, new Position { X = 1, Y = 2, Z = 3 });
            velTable.Set(0, new Velocity { X = 10, Y = 20, Z = 30 });
            
            Assert.Equal(1, posTable.Get(0).X);
            Assert.Equal(10, velTable.Get(0).X);
        }
        
        [Fact]
        public void ComponentTable_LargeEntityIndex()
        {
            using var table = new ComponentTable<Health>();
            
            table.Set(999999, new Health { Value = 42 });
            
            Assert.Equal(42, table.Get(999999).Value);
        }
        
        [Fact]
        public void ComponentTable_GetChunkTable_ReturnsValidTable()
        {
            using var table = new ComponentTable<Position>();
            
            var chunkTable = table.GetChunkTable();
            
            Assert.NotNull(chunkTable);
            Assert.Equal(65536 / 12, chunkTable.ChunkCapacity); // 65536 / 12 = 5461
        }
        
        [Fact]
        public void ComponentTable_TagComponent_Works()
        {
            using var table = new ComponentTable<Tag>();
            
            // Tag components are 1 byte in C#
            Assert.Equal(1, table.ComponentSize);
            Assert.True(ComponentType<Tag>.IsTag);
            
            // Can still store/retrieve (though meaningless for tags)
            table.Set(0, new Tag());
            ref var tag = ref table.Get(0);
            // Tag has no fields, so nothing to assert
        }
        
        [Fact]
        public void ComponentTable_ThreadSafe_ConcurrentWrites()
        {
            using var table = new ComponentTable<Position>();
            
            System.Threading.Tasks.Parallel.For(0, 1000, i =>
            {
                table.Set(i, new Position { X = i, Y = i, Z = i });
            });
            
            // Verify all written
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(i, table.Get(i).X);
            }
        }
        
        [Fact]
        public void ComponentTable_Dispose_Cleanup()
        {
            var table = new ComponentTable<Position>();
            
            table.Set(0, new Position { X = 1, Y = 2, Z = 3 });
            
            table.Dispose();
            
            // After dispose, should not use
            // (Can't test access violation safely)
        }
    }
    
    // [Collection("ComponentTests")]
    public class IntegrationTests_ComponentSystem
    {
        public IntegrationTests_ComponentSystem()
        {
            // ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void Integration_EntityWithMultipleComponents()
        {
            using var entityIndex = new EntityIndex();
            using var posTable = new ComponentTable<Position>();
            using var velTable = new ComponentTable<Velocity>();
            using var hpTable = new ComponentTable<Health>();
            
            // Create entity
            var entity = entityIndex.CreateEntity();
            
            // Add components
            ref var header = ref entityIndex.GetHeader(entity.Index);
            header.ComponentMask.SetBit(ComponentType<Position>.ID);
            header.ComponentMask.SetBit(ComponentType<Velocity>.ID);
            header.ComponentMask.SetBit(ComponentType<Health>.ID);
            
            // Set component data
            posTable.Set(entity.Index, new Position { X = 100, Y = 200, Z = 300 });
            velTable.Set(entity.Index, new Velocity { X = 1, Y = 2, Z = 3 });
            hpTable.Set(entity.Index, new Health { Value = 100 });
            
            // Verify all components
            Assert.True(header.ComponentMask.IsSet(ComponentType<Position>.ID));
            Assert.True(header.ComponentMask.IsSet(ComponentType<Velocity>.ID));
            Assert.True(header.ComponentMask.IsSet(ComponentType<Health>.ID));
            
            Assert.Equal(100, posTable.Get(entity.Index).X);
            Assert.Equal(1, velTable.Get(entity.Index).X);
            Assert.Equal(100, hpTable.Get(entity.Index).Value);
        }
        
        [Fact]
        public void Integration_QueryByComponentMask()
        {
            using var entityIndex = new EntityIndex();
            using var posTable = new ComponentTable<Position>();
            using var velTable = new ComponentTable<Velocity>();
            
            // Create entities with different component combinations
            var e1 = entityIndex.CreateEntity();
            ref var h1 = ref entityIndex.GetHeader(e1.Index);
            h1.ComponentMask.SetBit(ComponentType<Position>.ID);
            posTable.Set(e1.Index, new Position { X = 1, Y = 1, Z = 1 });
            
            var e2 = entityIndex.CreateEntity();
            ref var h2 = ref entityIndex.GetHeader(e2.Index);
            h2.ComponentMask.SetBit(ComponentType<Position>.ID);
            h2.ComponentMask.SetBit(ComponentType<Velocity>.ID);
            posTable.Set(e2.Index, new Position { X = 2, Y = 2, Z = 2 });
            velTable.Set(e2.Index, new Velocity { X = 1, Y = 1, Z = 1 });
            
            var e3 = entityIndex.CreateEntity();
            ref var h3 = ref entityIndex.GetHeader(e3.Index);
            h3.ComponentMask.SetBit(ComponentType<Velocity>.ID);
            velTable.Set(e3.Index, new Velocity { X = 3, Y = 3, Z = 3 });
            
            // Query for entities with Position
            var queryMask = new BitMask256();
            queryMask.SetBit(ComponentType<Position>.ID);
            
            int matchCount = 0;
            for (int i = 0; i <= entityIndex.MaxIssuedIndex; i++)
            {
                ref var header = ref entityIndex.GetHeader(i);
                if (header.IsActive && BitMask256.HasAll(header.ComponentMask, queryMask))
                {
                    matchCount++;
                }
            }
            
            Assert.Equal(2, matchCount); // e1 and e2 have Position
        }
    }
}
