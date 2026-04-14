using System;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class AuthorityTests
    {
        public AuthorityTests()
        {
            ComponentTypeRegistry.Clear();
        }

        [Fact]
        public void SetAuthority_UpdatesMask()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position());
            
            Assert.False(repo.HasAuthority<Position>(e));
            
            repo.SetAuthority<Position>(e, true);
            Assert.True(repo.HasAuthority<Position>(e));
            
            repo.SetAuthority<Position>(e, false);
            Assert.False(repo.HasAuthority<Position>(e));
        }
        
        [Fact]
        public void SetAuthority_ThrowsIfComponentMissing()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e = repo.CreateEntity();
            // No component added
            
            Assert.Throws<InvalidOperationException>(() => repo.SetAuthority<Position>(e, true));
        }
        
        [Fact]
        public void Query_WithOwned_FiltersCorrectly()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position());
            repo.SetAuthority<Position>(e1, true); // Owned
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position());
            // Not owned (default)
            
            int count = 0;
            foreach (var e in repo.Query().WithOwned<Position>().Build())
            {
                count++;
                Assert.Equal(e1, e);
            }
            
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void Query_WithoutOwned_FiltersCorrectly()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position());
            repo.SetAuthority<Position>(e1, true); // Owned
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position());
            // Not owned
            
            int count = 0;
            // Should match e2
            foreach (var e in repo.Query().With<Position>().WithoutOwned<Position>().Build())
            {
                count++;
                Assert.Equal(e2, e);
            }
            
            Assert.Equal(1, count);
        }
    }
}
