using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Per-entity, per-handle Brain-side cache entry. Backed by a managed dictionary;
    /// not an ECS component in the fake implementation.
    /// </summary>
    public sealed class FakeBrainPathCacheEntry
    {
        public int           RouteHandle;
        public byte          LastObservedReplanCount;
        public float         TotalDistanceMeters;
        public uint          NavmeshVersionAtPlan;
        public byte          PrimaryBackend;
        public NavWaypoint[] Waypoints       = Array.Empty<NavWaypoint>();
        /// <summary>Monotone tick counter used for LRU eviction (larger = more recently used).</summary>
        public long          LastUsedTick;
    }

    /// <summary>
    /// Test-API surface for direct inspection and mutation of <see cref="BrainPathRegistry"/>.
    /// </summary>
    public interface IFakeBrainPathRegistryTestApi
    {
        /// <summary>
        /// Ingest a path response for <paramref name="entity"/>. Creates or replaces the
        /// cached entry for <paramref name="routeHandle"/>; updates <c>LastObservedReplanCount</c>.
        /// Returns false if the LRU cap was exceeded and a different entry was evicted to make room.
        /// </summary>
        bool TryIngestResponse(Entity entity, int routeHandle, NavWaypoint[] waypoints,
                               byte replanCount, float totalDist, uint navmeshVersion,
                               byte primaryBackend);

        /// <summary>Inspect a Brain entity's full cache.</summary>
        IReadOnlyList<FakeBrainPathCacheEntry> SnapshotEntityCache(Entity entity);

        /// <summary>Force-evict a specific entry. Returns false if not found.</summary>
        bool EvictEntry(Entity entity, int routeHandle);

        /// <summary>Return accumulated stats.</summary>
        FakePathRegistryStats GetStats();
    }

    /// <summary>
    /// Brain-side LRU path cache. Keyed by (Entity, routeHandle). Applies a strict
    /// ReplanCount cache-miss policy: if the stored <c>LastObservedReplanCount</c> differs
    /// from the caller-supplied current value, the lookup is treated as a miss.
    ///
    /// Default capacity: 32 entries across all entities (configurable via constructor).
    /// </summary>
    public sealed class BrainPathRegistry : IPathRegistry, IFakeBrainPathRegistryTestApi
    {
        private readonly int _maxEntries;
        // Key: (Entity.Index, Entity.Generation, routeHandle) packed into a long pair.
        private readonly Dictionary<(int entityIndex, ushort entityGen, int handle), FakeBrainPathCacheEntry>
            _cache = new();

        private long _tickClock;
        private long _hits;
        private long _misses;
        private long _staleMisses;
        private long _evictions;

        /// <param name="maxEntries">Maximum total cache entries across all entities (default 32).</param>
        public BrainPathRegistry(int maxEntries = 32)
        {
            _maxEntries = maxEntries;
        }

        // ── IPathRegistry — entity-agnostic overload (no replan check) ───────────

        /// <inheritdoc/>
        public bool IsCached(int routeHandle)
        {
            // Without entity context the check cannot apply the ReplanCount policy.
            // This overload is provided for SharedPathRegistry forwarding only.
            foreach (var kv in _cache)
            {
                if (kv.Key.handle == routeHandle)
                {
                    Interlocked.Increment(ref _hits);
                    return true;
                }
            }
            Interlocked.Increment(ref _misses);
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetSummary(int routeHandle, out PathSummary summary)
        {
            // TODO (Phase 4): apply per-entity ReplanCount check here.
            foreach (var kv in _cache)
            {
                if (kv.Key.handle != routeHandle) continue;
                var e = kv.Value;
                Interlocked.Increment(ref _hits);
                summary = new PathSummary
                {
                    RouteHandle          = e.RouteHandle,
                    TotalDistanceMeters  = e.TotalDistanceMeters,
                    WaypointCount        = e.Waypoints.Length,
                    NavmeshVersionAtPlan = e.NavmeshVersionAtPlan,
                    PrimaryBackend       = e.PrimaryBackend,
                    ReplanCount          = e.LastObservedReplanCount,
                };
                return true;
            }
            Interlocked.Increment(ref _misses);
            summary = default;
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count)
        {
            // Entity-agnostic: no replan check (use the entity overload in tests).
            foreach (var kv in _cache)
            {
                if (kv.Key.handle != routeHandle) continue;
                var e = kv.Value;
                e.LastUsedTick = Interlocked.Increment(ref _tickClock);
                Interlocked.Increment(ref _hits);
                int n = Math.Min(e.Waypoints.Length, dest.Length);
                e.Waypoints.AsSpan(0, n).CopyTo(dest);
                count = n;
                return true;
            }
            Interlocked.Increment(ref _misses);
            count = 0;
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                          Span<NavWaypoint> dest, out int actualCount)
        {
            // TODO (Phase 4): apply per-entity ReplanCount check here.
            foreach (var kv in _cache)
            {
                if (kv.Key.handle != routeHandle) continue;
                var e = kv.Value;
                e.LastUsedTick = Interlocked.Increment(ref _tickClock);
                Interlocked.Increment(ref _hits);
                if (startSegment >= e.Waypoints.Length)
                {
                    actualCount = 0;
                    return true;
                }
                int available = e.Waypoints.Length - startSegment;
                int n = Math.Min(Math.Min(available, maxCount), dest.Length);
                e.Waypoints.AsSpan(startSegment, n).CopyTo(dest);
                actualCount = n;
                return true;
            }
            Interlocked.Increment(ref _misses);
            actualCount = 0;
            return false;
        }

        // ── Entity-scoped lookups (replan-aware) ─────────────────────────────────

        /// <summary>
        /// Returns true if a fresh (non-stale) entry exists for <paramref name="entity"/>
        /// and <paramref name="routeHandle"/> with <c>LastObservedReplanCount == currentReplanCount</c>.
        /// </summary>
        public bool IsCached(Entity entity, int routeHandle, byte currentReplanCount)
        {
            var key = MakeKey(entity, routeHandle);
            if (!_cache.TryGetValue(key, out var e))
            {
                Interlocked.Increment(ref _misses);
                return false;
            }
            if (e.LastObservedReplanCount != currentReplanCount)
            {
                Interlocked.Increment(ref _staleMisses);
                return false;
            }
            Interlocked.Increment(ref _hits);
            return true;
        }

        /// <summary>
        /// Copies waypoints for <paramref name="entity"/> / <paramref name="routeHandle"/> into
        /// <paramref name="dest"/>. Applies the strict ReplanCount policy: returns false if the
        /// cached <c>LastObservedReplanCount</c> differs from <paramref name="currentReplanCount"/>.
        /// </summary>
        public bool TryGetWaypoints(Entity entity, int routeHandle, byte currentReplanCount,
                                    Span<NavWaypoint> dest, out int count)
        {
            var key = MakeKey(entity, routeHandle);
            if (!_cache.TryGetValue(key, out var e))
            {
                Interlocked.Increment(ref _misses);
                count = 0;
                return false;
            }
            if (e.LastObservedReplanCount != currentReplanCount)
            {
                Interlocked.Increment(ref _staleMisses);
                count = 0;
                return false;
            }
            e.LastUsedTick = Interlocked.Increment(ref _tickClock);
            Interlocked.Increment(ref _hits);
            int n = Math.Min(e.Waypoints.Length, dest.Length);
            e.Waypoints.AsSpan(0, n).CopyTo(dest);
            count = n;
            return true;
        }

        // ── IFakeBrainPathRegistryTestApi ────────────────────────────────────────

        /// <inheritdoc/>
        public bool TryIngestResponse(Entity entity, int routeHandle, NavWaypoint[] waypoints,
                                      byte replanCount, float totalDist, uint navmeshVersion,
                                      byte primaryBackend)
        {
            var key = MakeKey(entity, routeHandle);
            bool evicted = false;

            if (!_cache.ContainsKey(key) && _cache.Count >= _maxEntries)
            {
                EvictLru();
                evicted = true;
            }

            long tick = Interlocked.Increment(ref _tickClock);
            _cache[key] = new FakeBrainPathCacheEntry
            {
                RouteHandle               = routeHandle,
                LastObservedReplanCount   = replanCount,
                TotalDistanceMeters       = totalDist,
                NavmeshVersionAtPlan      = navmeshVersion,
                PrimaryBackend            = primaryBackend,
                Waypoints                 = waypoints,
                LastUsedTick              = tick,
            };
            return !evicted;
        }

        /// <inheritdoc/>
        public IReadOnlyList<FakeBrainPathCacheEntry> SnapshotEntityCache(Entity entity)
        {
            var result = new List<FakeBrainPathCacheEntry>();
            foreach (var kv in _cache)
            {
                if (kv.Key.entityIndex == entity.Index && kv.Key.entityGen == entity.Generation)
                    result.Add(kv.Value);
            }
            return result;
        }

        /// <inheritdoc/>
        public bool EvictEntry(Entity entity, int routeHandle)
        {
            var key = MakeKey(entity, routeHandle);
            if (_cache.Remove(key))
            {
                Interlocked.Increment(ref _evictions);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public FakePathRegistryStats GetStats()
        {
            return new FakePathRegistryStats
            {
                TotalEntries = _cache.Count,
                HitCount     = (int)Interlocked.Read(ref _hits),
                MissCount    = (int)Interlocked.Read(ref _misses),
                StaleMisses  = (int)Interlocked.Read(ref _staleMisses),
                Evictions    = (int)Interlocked.Read(ref _evictions),
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static (int entityIndex, ushort entityGen, int handle) MakeKey(Entity e, int handle)
            => (e.Index, e.Generation, handle);

        private void EvictLru()
        {
            (int entityIndex, ushort entityGen, int handle) lruKey = default;
            long lruTick = long.MaxValue;
            bool found = false;

            foreach (var kv in _cache)
            {
                if (kv.Value.LastUsedTick < lruTick)
                {
                    lruTick = kv.Value.LastUsedTick;
                    lruKey = kv.Key;
                    found = true;
                }
            }

            if (found)
            {
                _cache.Remove(lruKey);
                Interlocked.Increment(ref _evictions);
            }
        }
    }
}
