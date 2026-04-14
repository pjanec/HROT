using Xunit;
using Fdp.Kernel;
using System;
using System.Collections.Generic;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class QueryTests
    {
        public QueryTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        // ================================================
        // QUERY BUILDER TESTS
        // ================================================
        
        [Fact]
        public void QueryBuilder_With_SingleComponent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var query = repo.Query().With<Position>().Build();
            
            Assert.NotNull(query);
            Assert.True(query.IncludeMask.IsSet(ComponentType<Position>.ID));
        }
        
        [Fact]
        public void QueryBuilder_With_MultipleComponents()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var query = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
            
            Assert.True(query.IncludeMask.IsSet(ComponentType<Position>.ID));
            Assert.True(query.IncludeMask.IsSet(ComponentType<Velocity>.ID));
        }
        
        [Fact]
        public void QueryBuilder_Without_SingleComponent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var query = repo.Query()
                .With<Position>()
                .Without<Velocity>()
                .Build();
            
            Assert.True(query.IncludeMask.IsSet(ComponentType<Position>.ID));
            Assert.True(query.ExcludeMask.IsSet(ComponentType<Velocity>.ID));
        }
        
        [Fact]
        public void QueryBuilder_FluentAPI_Chains()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            repo.RegisterComponent<Health>();
            
            var query = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Without<Health>()
                .Build();
            
            Assert.NotNull(query);
        }
        
        // ================================================
        // QUERY MATCHING TESTS
        // ================================================
        
        [Fact]
        public void Query_ForEach_MatchesCorrectEntities()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // Entity with Position only
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            // Entity with Position + Velocity
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            repo.AddComponent(e2, new Velocity { X = 1, Y = 1, Z = 1 });
            
            // Entity with Velocity only
            var e3 = repo.CreateEntity();
            repo.AddComponent(e3, new Velocity { X = 3, Y = 3, Z = 3 });
            
            // Query for Position
            var query = repo.Query().With<Position>().Build();
            
            var matches = new List<Entity>();
            foreach (var e in query)
            {
                matches.Add(e);
            }
            
            Assert.Equal(2, matches.Count); // e1 and e2
            Assert.Contains(e1, matches);
            Assert.Contains(e2, matches);
        }
        
        [Fact]
        public void Query_ForEach_MultipleComponents_AND()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            repo.RegisterComponent<Health>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            repo.AddComponent(e2, new Velocity { X = 1, Y = 1, Z = 1 });
            
            var e3 = repo.CreateEntity();
            repo.AddComponent(e3, new Position { X = 3, Y = 3, Z = 3 });
            repo.AddComponent(e3, new Velocity { X = 1, Y = 1, Z = 1 });
            repo.AddComponent(e3, new Health { Value = 100 });
            
            // Query for Position AND Velocity
            var query = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
            
            var matches = new List<Entity>();
            foreach (var e in query)
            {
                matches.Add(e);
            }
            
            Assert.Equal(2, matches.Count); // e2 and e3
        }
        
        [Fact]
        public void Query_Without_ExcludesComponent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            repo.AddComponent(e2, new Velocity { X = 1, Y = 1, Z = 1 });
            
            // Query for Position WITHOUT Velocity
            var query = repo.Query()
                .With<Position>()
                .Without<Velocity>()
                .Build();
            
            var matches = new List<Entity>();
            foreach (var e in query)
            {
                matches.Add(e);
            }
            
            Assert.Single(matches);
            Assert.Equal(e1, matches[0]);
        }
        
        [Fact]
        public void Query_Count_ReturnsCorrectCount()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            for (int i = 0; i < 100; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                
                if (i % 2 == 0)
                {
                    repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
                }
            }
            
            var query = repo.Query().With<Position>().Build();
            Assert.Equal(100, query.Count());
            
            var queryWithVel = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
            Assert.Equal(50, queryWithVel.Count());
        }
        
        [Fact]
        public void Query_Any_ReturnsTrue_WhenMatches()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1, Y = 1, Z = 1 });
            
            var query = repo.Query().With<Position>().Build();
            
            Assert.True(query.Any());
        }
        
        [Fact]
        public void Query_Any_ReturnsFalse_WhenNoMatches()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1, Y = 1, Z = 1 });
            
            var query = repo.Query().With<Velocity>().Build();
            
            Assert.False(query.Any());
        }
        
        [Fact]
        public void Query_FirstOrNull_ReturnsFirst()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            
            var query = repo.Query().With<Position>().Build();
            var first = query.FirstOrNull();
            
            Assert.Equal(e1, first);
        }
        
        [Fact]
        public void Query_FirstOrNull_ReturnsNull_WhenNoMatches()
        {
            using var repo = new EntityRepository();
            
            var query = repo.Query().With<Position>().Build();
            var first = query.FirstOrNull();
            
            Assert.Equal(Entity.Null, first);
        }
        
        // ================================================
        // QUERY WITH COMPONENT ACCESS TESTS
        // ================================================
        
        [Fact]
        public void Query_ForEach_CanAccessComponents()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            for (int i = 0; i < 10; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
            }
            
            var query = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
            
            // Update positions based on velocity
            foreach (var e in query)
            {
                ref var loopPos = ref repo.GetComponentRW<Position>(e);
                ref var loopVel = ref repo.GetComponentRW<Velocity>(e);
                
                loopPos.X += loopVel.X;
                loopPos.Y += loopVel.Y;
                loopPos.Z += loopVel.Z;
            }
            
            // Verify updates
            var firstEntity = query.FirstOrNull();
            var pos = repo.GetComponentRW<Position>(firstEntity);
            Assert.Equal(1, pos.X); // 0 + 1
        }
        
        // ================================================
        // EDGE CASE TESTS
        // ================================================
        
        [Fact]
        public void Query_EmptyRepository_ReturnsNoMatches()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var query = repo.Query().With<Position>().Build();
            
            Assert.Equal(0, query.Count());
            Assert.False(query.Any());
            Assert.Equal(Entity.Null, query.FirstOrNull());
        }
        
        [Fact]
        public void Query_DestroyedEntity_NotReturned()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            
            var query = repo.Query().With<Position>().Build();
            Assert.Equal(2, query.Count());
            
            repo.DestroyEntity(e1);
            
            Assert.Equal(1, query.Count());
        }
        
        [Fact]
        public void Query_ComponentRemoved_NotReturned()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1, Y = 1, Z = 1 });
            repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
            
            var query = repo.Query().With<Velocity>().Build();
            Assert.Equal(1, query.Count());
            
            repo.RemoveUnmanagedComponent<Velocity>(e);
            
            Assert.Equal(0, query.Count());
        }
        
        [Fact]
        public void Query_NoComponents_MatchesAllEntities()
        {
            using var repo = new EntityRepository();
            
            for (int i = 0; i < 10; i++)
            {
                repo.CreateEntity();
            }
            
            // Query with no filters matches all
            var query = repo.Query().Build();
            
            Assert.Equal(10, query.Count());
        }
        
        // ================================================
        // PERFORMANCE TESTS
        // ================================================
        
        [Fact]
        public void Query_Performance_10KEntities()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // Create 10K entities
            for (int i = 0; i < 10_000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                
                if (i % 2 == 0)
                {
                    repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
                }
            }
            
            var query = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
            
            int count = 0;
            foreach (var e in query)
            {
                count++;
            }
            
            Assert.Equal(5000, count);
        }
        
        [Fact]
        public void Query_Multiple_Independent()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            var e2 = repo.CreateEntity();
            repo.AddComponent(e2, new Velocity { X = 1, Y = 1, Z = 1 });
            
            var e3 = repo.CreateEntity();
            repo.AddComponent(e3, new Position { X = 2, Y = 2, Z = 2 });
            repo.AddComponent(e3, new Velocity { X = 1, Y = 1, Z = 1 });
            
            var queryPos = repo.Query().With<Position>().Build();
            var queryVel = repo.Query().With<Velocity>().Build();
            var queryBoth = repo.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
            
            Assert.Equal(2, queryPos.Count());
            Assert.Equal(2, queryVel.Count());
            Assert.Equal(1, queryBoth.Count());
        }
    }
}
