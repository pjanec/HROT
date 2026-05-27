using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    public class MusclePathRegistryTests
    {
        private static NavWaypoint[] MakeWaypoints(params Vector3[] positions)
        {
            var wp = new NavWaypoint[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                wp[i] = new NavWaypoint { Position = positions[i] };
            return wp;
        }

        [Fact]
        public void MusclePathRegistry_RegisterAndQuery_ReturnsEntry()
        {
            var registry = new MusclePathRegistry();
            var waypoints = MakeWaypoints(new Vector3(0, 0, 0), new Vector3(1, 0, 0));

            registry.RegisterOrReplace(42, waypoints, 10f, 1u, 0, 0);

            Assert.True(registry.IsCached(42));
            Assert.True(registry.TryGetSummary(42, out var summary));
            Assert.Equal(42, summary.RouteHandle);
            Assert.Equal(10f, summary.TotalDistanceMeters);
            Assert.Equal(2, summary.WaypointCount);

            var dest = new NavWaypoint[4];
            Assert.True(registry.TryGetWaypoints(42, dest.AsSpan(), out int count));
            Assert.Equal(2, count);
            Assert.Equal(new Vector3(1, 0, 0), dest[1].Position);
        }

        [Fact]
        public void MusclePathRegistry_Free_RemovesEntry()
        {
            var registry = new MusclePathRegistry();
            registry.RegisterOrReplace(99, MakeWaypoints(Vector3.Zero), 0f, 0u, 0, 0);

            bool freed = registry.Free(99);

            Assert.True(freed);
            Assert.False(registry.IsCached(99));
        }

        [Fact]
        public void MusclePathRegistry_Free_MissingHandle_ReturnsFalse()
        {
            var registry = new MusclePathRegistry();
            Assert.False(registry.Free(12345));
        }

        [Fact]
        public void MusclePathRegistry_RegisterOrReplace_IncrementsReplanCount()
        {
            var registry = new MusclePathRegistry();
            registry.RegisterOrReplace(7, MakeWaypoints(Vector3.Zero), 0f, 0u, 0, 0);
            registry.RegisterOrReplace(7, MakeWaypoints(Vector3.One), 5f, 1u, 0, 0);

            Assert.True(registry.TryGetSummary(7, out var summary));
            Assert.Equal(1, summary.ReplanCount);
        }

        [Fact]
        public void MusclePathRegistry_TryGetWaypointsSlice_ReturnsSubrange()
        {
            var registry = new MusclePathRegistry();
            var waypoints = MakeWaypoints(
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(2, 0, 0),
                new Vector3(3, 0, 0));
            registry.RegisterOrReplace(10, waypoints, 3f, 0u, 0, 0);

            var dest = new NavWaypoint[2];
            Assert.True(registry.TryGetWaypointsSlice(10, 1, 2, dest.AsSpan(), out int actual));
            Assert.Equal(2, actual);
            Assert.Equal(new Vector3(1, 0, 0), dest[0].Position);
            Assert.Equal(new Vector3(2, 0, 0), dest[1].Position);
        }

        [Fact]
        public void MusclePathRegistry_GetStats_ReflectsHitsAndMisses()
        {
            var registry = new MusclePathRegistry();
            registry.RegisterOrReplace(1, MakeWaypoints(Vector3.Zero), 0f, 0u, 0, 0);

            registry.TryGetSummary(1, out _);   // hit
            registry.TryGetSummary(999, out _); // miss

            var stats = registry.GetStats();
            Assert.Equal(1, stats.TotalEntries);
            Assert.Equal(1, stats.HitCount);
            Assert.Equal(1, stats.MissCount);
        }

        // ── Additional tests to complete DD-Tests-Nav §3.4 ───────────────────────

        [Fact]
        public void MusclePathRegistry_RegisterOrReplace_ExistingHandle_ReplacesInPlace()
        {
            var registry = new MusclePathRegistry();

            registry.RegisterOrReplace(42, MakeWaypoints(new Vector3(0, 0, 0)), 1f, 0u, 0, 0);
            registry.RegisterOrReplace(42, MakeWaypoints(new Vector3(1, 0, 0), new Vector3(2, 0, 0)), 2f, 0u, 0, 0);

            var dest = new NavWaypoint[4];
            Assert.True(registry.TryGetWaypoints(42, dest.AsSpan(), out int count));
            Assert.Equal(2, count);
            Assert.Equal(new Vector3(1, 0, 0), dest[0].Position);
            Assert.Equal(new Vector3(2, 0, 0), dest[1].Position);
        }

        [Fact]
        public void MusclePathRegistry_TryGetWaypoints_UnknownHandle_ReturnsFalse()
        {
            var registry = new MusclePathRegistry();
            var dest = new NavWaypoint[4];
            Assert.False(registry.TryGetWaypoints(999, dest.AsSpan(), out _));
        }

        [Fact]
        public void MusclePathRegistry_BrainAndMuscleHandles_NoCollision()
        {
            var registry = new MusclePathRegistry();

            // Brain-allocated handles >= 0x40000000, Muscle-private < 0x40000000.
            int muscleHandle = 1;
            int brainHandle  = 0x40000001;

            registry.RegisterOrReplace(muscleHandle, MakeWaypoints(new Vector3(0, 0, 0)), 1f, 0u, 0, 0);
            registry.RegisterOrReplace(brainHandle,  MakeWaypoints(new Vector3(5, 0, 0)), 9f, 0u, 0, 0);

            Assert.True(registry.IsCached(muscleHandle));
            Assert.True(registry.IsCached(brainHandle));

            Assert.True(registry.TryGetSummary(muscleHandle, out var ms));
            Assert.True(registry.TryGetSummary(brainHandle,  out var bs));

            Assert.Equal(1f, ms.TotalDistanceMeters);
            Assert.Equal(9f, bs.TotalDistanceMeters);
        }
    }

    public class BrainPathRegistryTests
    {
        private static Entity E(int index) => new Entity(index, 1);

        private static NavWaypoint[] MakeWaypoints(int count)
        {
            var wp = new NavWaypoint[count];
            for (int i = 0; i < count; i++)
                wp[i] = new NavWaypoint { Position = new Vector3(i, 0, 0) };
            return wp;
        }

        [Fact]
        public void BrainPathRegistry_FreshReplanCount_CacheHit()
        {
            var registry = new BrainPathRegistry();
            var entity = E(1);
            registry.TryIngestResponse(entity, 5, MakeWaypoints(3), replanCount: 0,
                                       totalDist: 2f, navmeshVersion: 1u, primaryBackend: 0);

            Assert.True(registry.IsCached(entity, 5, currentReplanCount: 0));

            var dest = new NavWaypoint[3];
            Assert.True(registry.TryGetWaypoints(entity, 5, currentReplanCount: 0, dest.AsSpan(), out int count));
            Assert.Equal(3, count);
        }

        [Fact]
        public void BrainPathRegistry_StaleReplanCount_CacheMiss()
        {
            var registry = new BrainPathRegistry();
            var entity = E(2);
            registry.TryIngestResponse(entity, 6, MakeWaypoints(2), replanCount: 0,
                                       totalDist: 1f, navmeshVersion: 1u, primaryBackend: 0);

            // currentReplanCount = 1 but stored count = 0 => stale
            Assert.False(registry.IsCached(entity, 6, currentReplanCount: 1));

            var dest = new NavWaypoint[2];
            Assert.False(registry.TryGetWaypoints(entity, 6, currentReplanCount: 1, dest.AsSpan(), out _));

            var stats = registry.GetStats();
            Assert.True(stats.StaleMisses >= 1);
        }

        [Fact]
        public void BrainPathRegistry_MissingHandle_ReturnsFalse()
        {
            var registry = new BrainPathRegistry();
            Assert.False(registry.IsCached(E(3), 999, currentReplanCount: 0));

            var dest = new NavWaypoint[1];
            Assert.False(registry.TryGetWaypoints(E(3), 999, 0, dest.AsSpan(), out _));
        }

        [Fact]
        public void BrainPathRegistry_LruEviction_DropsOldestEntry()
        {
            var registry = new BrainPathRegistry(maxEntries: 2);
            var entity = E(10);

            registry.TryIngestResponse(entity, 1, MakeWaypoints(1), 0, 0f, 0u, 0);
            registry.TryIngestResponse(entity, 2, MakeWaypoints(1), 0, 0f, 0u, 0);

            // Touch handle 1 to make it more recently used
            var dummy = new NavWaypoint[1];
            registry.TryGetWaypoints(entity, 1, 0, dummy.AsSpan(), out _);

            // Inserting a 3rd entry exceeds the cap; LRU entry (handle 2, never touched after insert)
            // should be evicted.
            registry.TryIngestResponse(entity, 3, MakeWaypoints(1), 0, 0f, 0u, 0);

            Assert.True(registry.IsCached(entity, 1, 0));
            Assert.False(registry.IsCached(entity, 2, 0)); // evicted
            Assert.True(registry.IsCached(entity, 3, 0));
        }

        [Fact]
        public void BrainPathRegistry_PerEntityIsolation()
        {
            var registry = new BrainPathRegistry();
            var e1 = E(20);
            var e2 = E(21);

            registry.TryIngestResponse(e1, 100, MakeWaypoints(2), 0, 5f, 0u, 0);

            // e2 should not see e1's entry
            Assert.False(registry.IsCached(e2, 100, 0));
            Assert.True(registry.IsCached(e1, 100, 0));
        }

        // ── Additional tests to complete DD-Tests-Nav §3.5 ───────────────────────

        [Fact]
        public void BrainPathRegistry_EvictEntry_ExistingHandle_Removes()
        {
            var registry = new BrainPathRegistry();
            var entity = E(30);
            registry.TryIngestResponse(entity, 77, MakeWaypoints(2), replanCount: 0,
                                       totalDist: 3f, navmeshVersion: 1u, primaryBackend: 0);

            Assert.True(registry.IsCached(entity, 77, currentReplanCount: 0));

            registry.EvictEntry(entity, 77);

            Assert.False(registry.IsCached(entity, 77, currentReplanCount: 0));
        }

        [Fact]
        public void BrainPathRegistry_Stats_ZeroAtStart()
        {
            var registry = new BrainPathRegistry();
            var stats = registry.GetStats();
            Assert.Equal(0, stats.TotalEntries);
            Assert.Equal(0, stats.HitCount);
            Assert.Equal(0, stats.MissCount);
        }
    }

    public class SharedPathRegistryTests
    {
        private static NavWaypoint[] MakeWaypoints(int count)
        {
            var wp = new NavWaypoint[count];
            for (int i = 0; i < count; i++)
                wp[i] = new NavWaypoint { Position = new Vector3(i, 0, 0) };
            return wp;
        }

        [Fact]
        public void SharedPathRegistry_QueryFromBothRoles_ReturnsConsistentData()
        {
            var muscle = new MusclePathRegistry();
            var shared = new SharedPathRegistry(muscle);

            muscle.RegisterOrReplace(77, MakeWaypoints(3), 9f, 2u, 0, 0);

            // Both direct muscle access and shared registry see the same data.
            Assert.True(shared.IsCached(77));
            Assert.True(shared.TryGetSummary(77, out var summary));
            Assert.Equal(9f, summary.TotalDistanceMeters);
            Assert.Equal(3, summary.WaypointCount);

            var dest = new NavWaypoint[3];
            Assert.True(shared.TryGetWaypoints(77, dest.AsSpan(), out int count));
            Assert.Equal(3, count);
        }

        [Fact]
        public void SharedPathRegistry_SameTickVisibility_NoStaleness()
        {
            var muscle = new MusclePathRegistry();
            var shared = new SharedPathRegistry(muscle);

            // Register and immediately query — no delay, no cache layer.
            muscle.RegisterOrReplace(55, MakeWaypoints(2), 4f, 0u, 0, 0);
            Assert.True(shared.IsCached(55));
        }

        [Fact]
        public void SharedPathRegistry_Free_VisibleThroughShared()
        {
            var muscle = new MusclePathRegistry();
            var shared = new SharedPathRegistry(muscle);

            muscle.RegisterOrReplace(33, MakeWaypoints(1), 1f, 0u, 0, 0);
            muscle.Free(33);

            Assert.False(shared.IsCached(33));
        }
    }
}
