using Xunit;
using Fdp.Kernel;
using System;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class ComponentMetadataTests
    {
        public ComponentMetadataTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void ComponentMetadataTable_SetAndGet_Works()
        {
            using var metadata = new ComponentMetadataTable();
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(0);
            desc.SetPart(1);
            
            metadata.SetPartDescriptor(0, 5, desc);
            
            var retrieved = metadata.GetPartDescriptor(0, 5);
            Assert.True(retrieved.HasPart(0));
            Assert.True(retrieved.HasPart(1));
            Assert.False(retrieved.HasPart(2));
        }
        
        [Fact]
        public void ComponentMetadataTable_DefaultDescriptor_IsAll()
        {
            using var metadata = new ComponentMetadataTable();
            
            // Never set, should return All
            var desc = metadata.GetPartDescriptor(0, 5);
            
            Assert.True(desc.HasPart(0));
            Assert.True(desc.HasPart(1));
            Assert.True(desc.HasPart(100));
        }
        
        [Fact]
        public void ComponentMetadataTable_HasPart_Works()
        {
            using var metadata = new ComponentMetadataTable();
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(5);
            
            metadata.SetPartDescriptor(0, 10, desc);
            
            Assert.True(metadata.HasPart(0, 10, 5));
            Assert.False(metadata.HasPart(0, 10, 0));
            Assert.False(metadata.HasPart(0, 10, 1));
        }
        
        [Fact]
        public void ComponentMetadataTable_ClearEntity_RemovesAll()
        {
            using var metadata = new ComponentMetadataTable();
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(0);
            
            metadata.SetPartDescriptor(0, 5, desc);
            metadata.SetPartDescriptor(0, 6, desc);
            
            Assert.Equal(1, metadata.EntityCount);
            
            metadata.ClearEntity(0);
            
            Assert.Equal(0, metadata.EntityCount);
        }
        
        [Fact]
        public void ComponentMetadataTable_ClearComponent_RemovesOne()
        {
            using var metadata = new ComponentMetadataTable();
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(0);
            
            metadata.SetPartDescriptor(0, 5, desc);
            metadata.SetPartDescriptor(0, 6, desc);
            
            metadata.ClearComponent(0, 5);
            
            // Entity still has component 6
            var retrieved = metadata.GetPartDescriptor(0, 6);
            Assert.True(retrieved.HasPart(0));
            
            // Component 5 returns default (All)
            var defaultDesc = metadata.GetPartDescriptor(0, 5);
            Assert.True(defaultDesc.HasPart(100)); // All parts
        }
        
        [Fact]
        public void ComponentMetadataTable_MultipleEntities()
        {
            using var metadata = new ComponentMetadataTable();
            
            for (int i = 0; i < 10; i++)
            {
                var desc = PartDescriptor.Empty();
                desc.SetPart(i);
                metadata.SetPartDescriptor(i, 0, desc);
            }
            
            Assert.Equal(10, metadata.EntityCount);
            
            // Verify each entity has correct part
            for (int i = 0; i < 10; i++)
            {
                Assert.True(metadata.HasPart(i, 0, i));
            }
        }
    }
    
    [Collection("ComponentTests")]
    public class EntityRepositoryMetadataTests
    {
        public EntityRepositoryMetadataTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void EntityRepository_SetPartDescriptor_Works()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(0);
            
            repo.SetPartDescriptor<Position>(entity, desc);
            
            var retrieved = repo.GetPartDescriptor<Position>(entity);
            Assert.True(retrieved.HasPart(0));
            Assert.False(retrieved.HasPart(1));
        }
        
        [Fact]
        public void EntityRepository_GetPartDescriptor_DefaultIsAll()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            // Never set, should return All
            var desc = repo.GetPartDescriptor<Position>(entity);
            
            Assert.True(desc.HasPart(0));
            Assert.True(desc.HasPart(1));
            Assert.True(desc.HasPart(100));
        }
        
        [Fact]
        public void EntityRepository_HasPart_Works()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(0);
            repo.SetPartDescriptor<Position>(entity, desc);
            
            Assert.True(repo.HasPart<Position>(entity, 0));
            Assert.False(repo.HasPart<Position>(entity, 1));
        }
        
        [Fact]
        public unsafe void Integration_DeltaSync_WithRepository()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<LargeComponent>();
            
            // Server and client entities
            var serverEntity = repo.CreateEntity();
            var clientEntity = repo.CreateEntity();
            
            // Initial state (both same)
            var serverComp = new LargeComponent();
            var clientComp = new LargeComponent();
            
            for (int i = 0; i < 256; i++)
            {
                serverComp.Data[i] = (byte)i;
                clientComp.Data[i] = (byte)i;
            }
            
            repo.AddComponent(serverEntity, serverComp);
            repo.AddComponent(clientEntity, clientComp);
            
            // Server modifies part 2
            ref var serverData = ref repo.GetComponentRW<LargeComponent>(serverEntity);
            serverData.Data[150] = 99;
            
            // Detect changes
            ref var clientData = ref repo.GetComponentRW<LargeComponent>(clientEntity);
            var changedParts = MultiPartComponent.GetChangedParts(clientComp, serverData);
            
            // Update client metadata
            repo.SetPartDescriptor<LargeComponent>(clientEntity, changedParts);
            
            // Verify only part 2 marked as changed
            Assert.False(repo.HasPart<LargeComponent>(clientEntity, 0));
            Assert.False(repo.HasPart<LargeComponent>(clientEntity, 1));
            Assert.True(repo.HasPart<LargeComponent>(clientEntity, 2));
            Assert.False(repo.HasPart<LargeComponent>(clientEntity, 3));
            
            // Apply delta
            MultiPartComponent.CopyParts(ref clientData, serverData, changedParts);
            
            // Verify sync
            Assert.Equal(99, clientData.Data[150]);
        }
        
        #if FDP_PARANOID_MODE
        [Fact]
        public void SetPartDescriptor_DeadEntity_Throws()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            repo.DestroyEntity(entity);
            
            var desc = PartDescriptor.Empty();
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                repo.SetPartDescriptor<Position>(entity, desc);
            });
        }
        
        [Fact]
        public void SetPartDescriptor_NoComponent_Throws()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entity = repo.CreateEntity();
            
            var desc = PartDescriptor.Empty();
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                repo.SetPartDescriptor<Position>(entity, desc);
            });
        }
        #endif
    }
}
