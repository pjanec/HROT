using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests verifying TASK-E006: EntityQuery hot-first traversal.
    /// Component masks (hot) are checked before cold metadata to minimize cache pressure.
    /// All tests prove end-to-end behavior for 512-component expansion.
    /// </summary>
    public class EntityQueryHotFirstTests
    {
        // Component types with IDs in the upper range to prove 512-component expansion.
        [ComponentId(400)]
        private struct Marker400 { public int Value; }

        [ComponentId(310)]
        private struct Excluded310 { public int Value; }

        [ComponentId(390)]
        private struct Parallel390 { public int Value; }

        // ---------------------------------------------------------------
        // 1. Include filter with upper-range bit (end-to-end 512 expansion)
        // ---------------------------------------------------------------
        [Fact]
        public void IncludeFilter_UpperRangeBit400_EntityAppearsAndNonMatchDoesNot()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Marker400>();

            // Entity A has the component (bit 400 set in hot mask)
            var entityA = repo.CreateEntity();
            repo.AddComponent(entityA, new Marker400 { Value = 1 });

            // Entity B does not have the component
            var entityB = repo.CreateEntity();

            var query = repo.Query().With<Marker400>().Build();

            var matches = new List<Entity>();
            foreach (var e in query)
                matches.Add(e);

            Assert.Contains(entityA, matches);
            Assert.DoesNotContain(entityB, matches);
        }

        // ---------------------------------------------------------------
        // 2. Exclude filter with upper-range bit
        // ---------------------------------------------------------------
        [Fact]
        public void ExcludeFilter_EntityWithExcludedBit_DoesNotAppear()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Excluded310>();

            // Entity with the excluded component must be skipped
            var entityWithExc = repo.CreateEntity();
            repo.AddComponent(entityWithExc, new Excluded310 { Value = 99 });

            // Entity without the excluded component must appear
            var entityWithout = repo.CreateEntity();

            var query = repo.Query().Without<Excluded310>().Build();

            var matches = new List<Entity>();
            foreach (var e in query)
                matches.Add(e);

            Assert.DoesNotContain(entityWithExc, matches);
            Assert.Contains(entityWithout, matches);
        }

        // ---------------------------------------------------------------
        // 3. Dead entity never appears (hot mask zeroed on destroy)
        // ---------------------------------------------------------------
        [Fact]
        public void DestroyedEntity_NeverAppearsInQuery()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Marker400>();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Marker400 { Value = 1 });

            // Verify entity is found before destroy
            var query = repo.Query().With<Marker400>().Build();
            Assert.Equal(1, query.Count());

            // Destroy: hot mask is zeroed, cold IsActive=false
            repo.DestroyEntity(entity);

            // Must not appear after destroy
            Assert.Equal(0, query.Count());
            Assert.False(query.Any());

            // Also verify with empty include mask (no required components)
            var emptyQuery = repo.Query().Build();
            foreach (var e in emptyQuery)
                Assert.Fail($"Destroyed entity index {e.Index} appeared in empty-include query");
        }

        // ---------------------------------------------------------------
        // 4. Parallel iteration result set equals serial result set
        // ---------------------------------------------------------------
        [Fact]
        public void ForEachParallel_ResultMatchesSerialForEach()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Parallel390>();

            int total = 100;
            int withComponent = 0;

            for (int i = 0; i < total; i++)
            {
                var e = repo.CreateEntity();
                if (i % 3 == 0)
                {
                    repo.AddComponent(e, new Parallel390 { Value = i });
                    withComponent++;
                }
            }

            var query = repo.Query().With<Parallel390>().Build();

            // Serial results
            var serialResults = new HashSet<int>();
            foreach (var e in query)
                serialResults.Add(e.Index);

            Assert.Equal(withComponent, serialResults.Count);

            // Parallel results (may fall back to serial for small world, but result must match)
            var parallelResults = new System.Collections.Concurrent.ConcurrentBag<int>();
            query.ForEachParallel(e => parallelResults.Add(e.Index));

            Assert.Equal(serialResults.Count, parallelResults.Count);
            foreach (int idx in parallelResults)
                Assert.Contains(idx, serialResults);
        }

        // ---------------------------------------------------------------
        // 5. Count and Any correctness
        // ---------------------------------------------------------------
        [Fact]
        public void CountAndAny_EmptyWorld_ZeroAndFalse()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Marker400>();

            var query = repo.Query().With<Marker400>().Build();

            Assert.Equal(0, query.Count());
            Assert.False(query.Any());
        }

        [Fact]
        public void CountAndAny_ThreeMatchingEntities()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Marker400>();

            for (int i = 0; i < 3; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Marker400 { Value = i });
            }

            // Extra entity without component (must not count)
            repo.CreateEntity();

            var query = repo.Query().With<Marker400>().Build();

            Assert.Equal(3, query.Count());
            Assert.True(query.Any());
        }
    }
}
