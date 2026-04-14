using Xunit;
using Fdp.Kernel;
using System;

namespace Fdp.Tests
{
    // Reuse component types from ComponentTests
    // Position, Velocity, Health, Tag are already defined
    
    [Collection("ComponentTests")] // Share collection to avoid registry conflicts
    public class EntityRepositoryTests
    {
        public EntityRepositoryTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        // ================================================
        // ENTITY LIFECYCLE TESTS
        // ================================================
        
        [Fact]
        public void CreateEntity_ReturnsValidEntity()
        {
            using var repo = new EntityRepository();
            
            var entity = repo.CreateEntity();
            
            Assert.False(entity.IsNull);
            Assert.True(repo.IsAlive(entity));
            Assert.Equal(1, repo.EntityCount);
        }
        
        [Fact]
        public void CreateEntity_MultipleEntities()
        {
            using var repo = new EntityRepository();
            
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            var e3 = repo.CreateEntity();
            
            Assert.Equal(3, repo.EntityCount);
            Assert.True(repo.IsAlive(e1));
            Assert.True(repo.IsAlive(e2));
            Assert.True(repo.IsAlive(e3));
        }
        
        [Fact]
        public void DestroyEntity_MarksNotAlive()
        {
            using var repo = new EntityRepository();
            
            var entity = repo.CreateEntity();
            Assert.True(repo.IsAlive(entity));
            
            repo.DestroyEntity(entity);
            
            Assert.False(repo.IsAlive(entity));
            Assert.Equal(0, repo.EntityCount);
        }
        
        [Fact]
        public void DestroyEntity_ClearsComponentMask()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            Assert.True(repo.HasComponent<Position>(entity));
            
            repo.DestroyEntity(entity);
            
            // Create new entity at same index
            var entity2 = repo.CreateEntity();
            Assert.False(repo.HasComponent<Position>(entity2)); // Mask should be cleared
        }
        
        // ================================================
        // COMPONENT MANAGEMENT TESTS
        // ================================================
        
        [Fact]
        public void AddComponent_AddsComponent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            var pos = new Position { X = 1, Y = 2, Z = 3 };
            
            repo.AddComponent(entity, pos);
            
            Assert.True(repo.HasComponent<Position>(entity));
        }
        
        [Fact]
        public void AddComponent_SetsComponentMask()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            ref var header = ref repo.GetHeader(entity.Index);
            Assert.True(header.ComponentMask.IsSet(ComponentType<Position>.ID));
        }
        
        [Fact]
        public void GetComponent_ReturnsCorrectValue()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            ref var pos = ref repo.GetComponentRW<Position>(entity);
            
            Assert.Equal(10, pos.X);
            Assert.Equal(20, pos.Y);
            Assert.Equal(30, pos.Z);
        }
        
        [Fact]
        public void GetComponent_ReturnsReference_CanModify()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            ref var pos = ref repo.GetComponentRW<Position>(entity);
            pos.X = 100;
            
            Assert.Equal(100, repo.GetComponentRW<Position>(entity).X);
        }
        
        [Fact]
        public void RemoveComponent_RemovesComponent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            Assert.True(repo.HasComponent<Position>(entity));
            
            repo.RemoveComponent<Position>(entity);
            
            Assert.False(repo.HasComponent<Position>(entity));
        }
        
        [Fact]
        public void RemoveComponent_ClearsComponentMask()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            ref var header = ref repo.GetHeader(entity.Index);
            Assert.True(header.ComponentMask.IsSet(ComponentType<Position>.ID));
            
            repo.RemoveComponent<Position>(entity);
            
            Assert.False(header.ComponentMask.IsSet(ComponentType<Position>.ID));
        }
        
        [Fact]
        public void SetComponent_AddsIfNotPresent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            Assert.True(repo.HasComponent<Position>(entity));
            Assert.Equal(1, repo.GetComponentRW<Position>(entity).X);
        }
        
        [Fact]
        public void SetComponent_UpdatesIfPresent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            repo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            Assert.Equal(10, repo.GetComponentRW<Position>(entity).X);
        }
        
        [Fact]
        public void TryGetComponent_ReturnsTrue_WhenPresent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 5, Y = 10, Z = 15 });
            
            bool success = repo.TryGetComponent<Position>(entity, out var pos);
            
            Assert.True(success);
            Assert.Equal(5, pos.X);
            Assert.Equal(10, pos.Y);
            Assert.Equal(15, pos.Z);
        }
        
        [Fact]
        public void TryGetComponent_ReturnsFalse_WhenNotPresent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            
            bool success = repo.TryGetComponent<Position>(entity, out var pos);
            
            Assert.False(success);
            Assert.Equal(default, pos);
        }
        
        [Fact]
        public void TryGetComponent_ReturnsFalse_ForDeadEntity()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            repo.DestroyEntity(entity);
            
            bool success = repo.TryGetComponent<Position>(entity, out var pos);
            
            Assert.False(success);
        }
        
        // ================================================
        // MULTIPLE COMPONENTS TESTS
        // ================================================
        
        [Fact]
        public void Entity_CanHaveMultipleComponents()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            repo.RegisterComponent<Health>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            repo.AddComponent(entity, new Velocity { X = 0.1f, Y = 0.2f, Z = 0.3f });
            repo.AddComponent(entity, new Health { Value = 100 });
            
            Assert.True(repo.HasComponent<Position>(entity));
            Assert.True(repo.HasComponent<Velocity>(entity));
            Assert.True(repo.HasComponent<Health>(entity));
        }
        
        [Fact]
        public void Entity_DifferentComponentTypes_IndependentData()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            repo.AddComponent(entity, new Velocity { X = 1, Y = 2, Z = 3 });
            
            Assert.Equal(10, repo.GetComponentRW<Position>(entity).X);
            Assert.Equal(1, repo.GetComponentRW<Velocity>(entity).X);
        }
        
        [Fact]
        public void MultipleEntities_SameComponentType()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            var e3 = repo.CreateEntity();
            
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            repo.AddComponent(e3, new Position { X = 3, Y = 3, Z = 3 });
            
            Assert.Equal(1, repo.GetComponentRW<Position>(e1).X);
            Assert.Equal(2, repo.GetComponentRW<Position>(e2).X);
            Assert.Equal(3, repo.GetComponentRW<Position>(e3).X);
        }
        
        // ================================================
        // COMPONENT TABLE ACCESS TESTS
        // ================================================
        
        [Fact]
        public void GetComponentTable_ReturnsValidTable()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            var table = repo.GetComponentTable<Position>();
            
            Assert.NotNull(table);
            Assert.Equal(1, table.Get(entity.Index).X);
        }
        
        [Fact]
        public void GetComponentTable_Throws_IfNotRegistered()
        {
            using var repo = new EntityRepository();
            
            // Should throw because not registered
            Assert.Throws<InvalidOperationException>(() => 
            {
                repo.GetComponentTable<Position>();
            });
        }
        
        // ================================================
        // INTEGRATION TESTS
        // ================================================
        
        [Fact]
        public void IntegrationTest_FullLifecycle()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // Create entity
            var entity = repo.CreateEntity();
            Assert.True(repo.IsAlive(entity));
            Assert.Equal(1, repo.EntityCount);
            
            // Add components
            repo.AddComponent(entity, new Position { X = 100, Y = 200, Z = 300 });
            repo.AddComponent(entity, new Velocity { X = 1, Y = 2, Z = 3 });
            
            Assert.True(repo.HasComponent<Position>(entity));
            Assert.True(repo.HasComponent<Velocity>(entity));
            
            // Modify component
            ref var pos = ref repo.GetComponentRW<Position>(entity);
            pos.X = 999;
            
            Assert.Equal(999, repo.GetComponentRW<Position>(entity).X);
            
            // Remove component
            repo.RemoveComponent<Velocity>(entity);
            Assert.False(repo.HasComponent<Velocity>(entity));
            Assert.True(repo.HasComponent<Position>(entity)); // Position still there
            
            // Destroy entity
            repo.DestroyEntity(entity);
            Assert.False(repo.IsAlive(entity));
            Assert.Equal(0, repo.EntityCount);
        }
        
        [Fact]
        public void StressTest_ManyEntitiesWithComponents()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Health>();
            
            const int entityCount = 1000;
            var entities = new Entity[entityCount];
            
            // Create entities and add components
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = i, Y = i * 2, Z = i * 3 });
                repo.AddComponent(entities[i], new Health { Value = i });
            }
            
            Assert.Equal(entityCount, repo.EntityCount);
            
            // Verify all data
            for (int i = 0; i < entityCount; i++)
            {
                Assert.True(repo.IsAlive(entities[i]));
                Assert.Equal(i, repo.GetComponentRW<Position>(entities[i]).X);
                Assert.Equal(i, repo.GetComponentRW<Health>(entities[i]).Value);
            }
            
            // Destroy half
            for (int i = 0; i < entityCount / 2; i++)
            {
                repo.DestroyEntity(entities[i]);
            }
            
            Assert.Equal(entityCount / 2, repo.EntityCount);
        }
        
        [Fact]
        public void ThreadSafe_ConcurrentEntityCreation()
        {
            using var repo = new EntityRepository();
            
            const int entityCount = 1000;
            var entities = new Entity[entityCount];
            
            System.Threading.Tasks.Parallel.For(0, entityCount, i =>
            {
                entities[i] = repo.CreateEntity();
            });
            
            Assert.Equal(entityCount, repo.EntityCount);
            
            // Verify all entities are alive and unique
            for (int i = 0; i < entityCount; i++)
            {
                Assert.True(repo.IsAlive(entities[i]));
            }
        }
        
        #if FDP_PARANOID_MODE
        [Fact]
        public void AddComponent_DeadEntity_Throws()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.DestroyEntity(entity);
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            });
        }
        
        [Fact]
        public void GetComponent_NotPresent_Throws()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                ref var pos = ref repo.GetComponentRW<Position>(entity);
            });
        }
        
        [Fact]
        public void RemoveComponent_NotPresent_Throws()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                repo.RemoveComponent<Position>(entity);
            });
        }
        
        [Fact]
        public void DestroyEntity_AlreadyDestroyed_Throws()
        {
            using var repo = new EntityRepository();
            
            var entity = repo.CreateEntity();
            repo.DestroyEntity(entity);
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                repo.DestroyEntity(entity);
            });
        }
        #endif
    }
}
