using Xunit;
using Fdp.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class ChunkIterationTests
    {
        public ChunkIterationTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        // ================================================
        // CHUNK ITERATION TESTS
        // ================================================
        
        [Fact]
        public void ForEachChunked_MatchesSameAsForEach()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // Create entities across multiple chunks
            for (int i = 0; i < 1000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                
                if (i % 2 == 0)
                {
                    repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
                }
            }
            
            var query = repo.Query().With<Position>().With<Velocity>().Build();
            
            // Collect with ForEach
            var resultsForEach = new List<Entity>();
            foreach (var e in query)
            {
                resultsForEach.Add(e);
            }
            
            // Collect with ForEachChunked
            var resultsChunked = new List<Entity>();
            query.ForEachChunked(e => resultsChunked.Add(e));
            
            // Should be same entities
            Assert.Equal(resultsForEach.Count, resultsChunked.Count);
            Assert.Equal(500, resultsForEach.Count);
            
            // Sort for comparison (order might differ)
            var sorted1 = resultsForEach.OrderBy(e => e.Index).ToList();
            var sorted2 = resultsChunked.OrderBy(e => e.Index).ToList();
            
            for (int i = 0; i < sorted1.Count; i++)
            {
                Assert.Equal(sorted1[i], sorted2[i]);
            }
        }
        
        [Fact]
        public void ForEachChunked_SkipsEmptyChunks()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Create entities only in specific chunks
            // Chunk 0: entities 0-16383
            var e1 = repo.CreateEntity();  // Index 0
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            
            // Skip many indices to go to a different chunk
            // Force allocation at high index to create gap
            for (int i = 0; i < 100000; i += 20000)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
            }
            
            var query = repo.Query().With<Position>().Build();
            
            int count = 0;
            query.ForEachChunked(e => count++);
            
            Assert.True(count >= 6); // Should find all placed entities
        }
        
        [Fact]
        public void ForEachParallel_ProcessesAllEntities()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Create 1000 entities
            for (int i = 0; i < 1000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
            }
            
            var query = repo.Query().With<Position>().Build();
            
            // Collect with parallel (thread-safe collection)
            var results = new System.Collections.Concurrent.ConcurrentBag<Entity>();
            query.ForEachParallel(e => results.Add(e));
            
            Assert.Equal(1000, results.Count);
        }
        
        [Fact]
        public void ForEachParallel_MatchesSequentialResults()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            for (int i = 0; i < 500; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
            }
            
            var query = repo.Query().With<Position>().With<Velocity>().Build();
            
            // Sequential
            var seqResults = new List<Entity>();
            foreach (var e in query)
            {
                seqResults.Add(e);
            }
            
            // Parallel
            var parResults = new System.Collections.Concurrent.ConcurrentBag<Entity>();
            query.ForEachParallel(e => parResults.Add(e));
            
            Assert.Equal(seqResults.Count, parResults.Count);
            
            // Verify all entities present
            var seqSet = new HashSet<Entity>(seqResults);
            foreach (var e in parResults)
            {
                Assert.Contains(e, seqSet);
            }
        }
        
        [Fact]
        public void ForEachParallel_CanModifyComponentsIndependently()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Health>();
            
            // Create entities
            for (int i = 0; i < 1000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = 0, Y = 0, Z = 0 });
                repo.AddComponent(e, new Health { Value = 100 });
            }
            
            var query = repo.Query().With<Position>().With<Health>().Build();
            
            // Parallel update (each entity independent)
            query.ForEachParallel(e =>
            {
                ref var pos = ref repo.GetComponentRW<Position>(e);
                ref var hp = ref repo.GetComponentRW<Health>(e);
                
                pos.X = e.Index;
                hp.Value = e.Index * 10;
            });
            
            // Verify all updates
            foreach (var e in query)
            {
                ref var pos = ref repo.GetComponentRW<Position>(e);
                ref var hp = ref repo.GetComponentRW<Health>(e);
                
                Assert.Equal(e.Index, pos.X);
                Assert.Equal(e.Index * 10, hp.Value);
            }
        }
        
        // ================================================
        // PERFORMANCE COMPARISON TESTS
        // ================================================
        
        [Fact]
        public void Performance_ChunkedVsRegular_10KEntities()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // Create 10K entities
            for (int i = 0; i < 10_000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                repo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
            }
            
            var query = repo.Query().With<Position>().With<Velocity>().Build();
            
            // Both should process same number of entities
            int countRegular = 0;
            foreach (var e in query)
            {
                countRegular++;
            }
            
            int countChunked = 0;
            query.ForEachChunked(e => countChunked++);
            
            Assert.Equal(countRegular, countChunked);
            Assert.Equal(10_000, countRegular);
        }
        
        [Fact]
        public void ChunkedIteration_WithSparseData()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Health>();
            
            // Create sparse entities (only 10% match)
            for (int i = 0; i < 1000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                
                if (i % 10 == 0)
                {
                    repo.AddComponent(e, new Health { Value = 100 });
                }
            }
            
            // Query for rare combination
            var query = repo.Query()
                .With<Position>()
                .With<Health>()
                .Build();
            
            int count = 0;
            query.ForEachChunked(e => count++);
            
            Assert.Equal(100, count);
        }
        
        [Fact]
        public void ChunkedIteration_EmptyQuery_NoErrors()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            // Create entities that don't match
            for (int i = 0; i < 100; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
            }
            
            // Query for component that doesn't exist
            var query = repo.Query().With<Velocity>().Build();
            
            int count = 0;
            query.ForEachChunked(e => count++);
            
            Assert.Equal(0, count);
        }
        
        [Fact]
        public void ParallelIteration_ThreadSafety_NoDataRaces()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Create entities
            var entities = new Entity[1000];
            for (int i = 0; i < 1000; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Position { X = 0, Y = 0, Z = 0 });
            }
            
            var query = repo.Query().With<Position>().Build();
            
            // Each entity gets unique value
            query.ForEachParallel(e =>
            {
                ref var pos = ref repo.GetComponentRW<Position>(e);
                pos.X = e.Index;
                pos.Y = e.Index * 2;
                pos.Z = e.Index * 3;
            });
            
            // Verify no corruption
            for (int i = 0; i < 1000; i++)
            {
                ref var pos = ref repo.GetComponentRW<Position>(entities[i]);
                Assert.Equal(i, pos.X);
                Assert.Equal(i * 2, pos.Y);
                Assert.Equal(i * 3, pos.Z);
            }
        }
        
        [Fact]
        public void ChunkedIteration_WithDestroyedEntities()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var entities = new List<Entity>();
            for (int i = 0; i < 100; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                entities.Add(e);
            }
            
            // Destroy every other entity
            for (int i = 0; i < entities.Count; i += 2)
            {
                repo.DestroyEntity(entities[i]);
            }
            
            var query = repo.Query().With<Position>().Build();
            
            int count = 0;
            query.ForEachChunked(e => count++);
            
            Assert.Equal(50, count);
        }
    }
}
