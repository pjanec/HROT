using System;
using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    // Test managed components (Tier 2)
    public class ManagedComponentTests
    {
        // Simple test class
        [ComponentId(192)]
        public record PlayerName
        {
            public string Name { get; set; } = string.Empty;
            public int Level { get; set; }
        }
        
        [ComponentId(193)]
        public record InventoryData
        {
            public System.Collections.Generic.List<string> Items { get; set; } = new();
        }
        
        [Fact]
        public void RegisterComponent_Succeeds()
        {
            using var repo = new EntityRepository();
            
            // Should not throw
            repo.RegisterComponent<PlayerName>();
            repo.RegisterComponent<InventoryData>();
        }
        
        [Fact]
        public void AddManagedComponent_StoresValue()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerName>();
            
            var entity = repo.CreateEntity();
            var name = new PlayerName { Name = "Hero", Level = 5 };
            
            repo.AddManagedComponent(entity, name);
            
            Assert.True(repo.HasManagedComponent<PlayerName>(entity));
            
            var retrieved = repo.GetComponentRW<PlayerName>(entity);
            Assert.NotNull(retrieved);
            Assert.Equal("Hero", retrieved.Name);
            Assert.Equal(5, retrieved.Level);
        }
        
        [Fact]
        public void RemoveManagedComponent_ClearsValue()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<PlayerName>();
            
            var entity = repo.CreateEntity();
            repo.AddManagedComponent(entity, new PlayerName { Name = "Test" });
            
            Assert.True(repo.HasManagedComponent<PlayerName>(entity));
            
            repo.RemoveManagedComponent<PlayerName>(entity);
            
            Assert.False(repo.HasManagedComponent<PlayerName>(entity));
        }
        
        [Fact]
        public void MixedTier1And2_WorkTogether()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<PlayerName>();
            
            var entity = repo.CreateEntity();
            
            // Tier 1
            repo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            // Tier 2
            repo.AddManagedComponent(entity, new PlayerName { Name = "Hero", Level = 99 });
            
            // Assert both work
            Assert.True(repo.HasUnmanagedComponent<Position>(entity));
            Assert.True(repo.HasManagedComponent<PlayerName>(entity));
            
            ref readonly var pos = ref repo.GetComponentRO<Position>(entity);
            Assert.Equal(10f, pos.X);
            
            var name = repo.GetComponentRW<PlayerName>(entity);
            Assert.Equal("Hero", name?.Name);
        }
    }
}
