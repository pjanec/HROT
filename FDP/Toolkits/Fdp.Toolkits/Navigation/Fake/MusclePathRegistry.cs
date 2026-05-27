using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Plain data entry stored in <see cref="MusclePathRegistry"/> for each route handle.
    /// Not an ECS component — lives in a managed dictionary.
    /// </summary>
    public sealed class FakePathPoolEntry
    {
        public int          RouteHandle;
        public NavWaypoint[] Waypoints        = Array.Empty<NavWaypoint>();
        public float        TotalDistanceMeters;
        public uint         NavmeshVersionAtPlan;
        /// <summary>0=Navmesh, 1=RoadGraph, 2=Spliced, 3=Volumetric.</summary>
        public byte         PrimaryBackend;
        /// <summary>Bit 0: HasOffMeshLinks.</summary>
        public byte         Flags;
        public byte         ReplanCount;
    }

    /// <summary>
    /// Test-API surface for direct inspection and mutation of <see cref="MusclePathRegistry"/>.
    /// </summary>
    public interface IFakeMusclePathRegistryTestApi
    {
        /// <summary>Insert or overwrite a path entry.</summary>
        void RegisterOrReplace(int routeHandle, NavWaypoint[] waypoints,
                               float totalDist, uint navmeshVersion,
                               byte primaryBackend, byte flags);

        /// <summary>Remove a path entry. Returns false if the handle was not present.</summary>
        bool Free(int routeHandle);

        /// <summary>Returns a snapshot of all entries for inspection.</summary>
        IReadOnlyDictionary<int, FakePathPoolEntry> Snapshot();

        /// <summary>Remove all entries (test cleanup).</summary>
        void Clear();

        /// <summary>Return accumulated stats.</summary>
        FakePathRegistryStats GetStats();
    }

    /// <summary>
    /// Authoritative Muscle-side path pool. Dictionary-backed; thread-safe reads via
    /// <see cref="ReaderWriterLockSlim"/>.
    /// </summary>
    public sealed class MusclePathRegistry : IPathRegistry, IFakeMusclePathRegistryTestApi
    {
        // Muscle-private handles start here and grow upward.
        // Brain-allocated handles are always < 0x40000000.
        private const int MuscleHandleBase = 0x40000000;

        private readonly Dictionary<int, FakePathPoolEntry> _entries = new();
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        private long _hits;
        private long _misses;

        // ── IPathRegistry ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool IsCached(int routeHandle)
        {
            _lock.EnterReadLock();
            try { return _entries.ContainsKey(routeHandle); }
            finally { _lock.ExitReadLock(); }
        }

        /// <inheritdoc/>
        public bool TryGetSummary(int routeHandle, out PathSummary summary)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_entries.TryGetValue(routeHandle, out var e))
                {
                    Interlocked.Increment(ref _misses);
                    summary = default;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                summary = new PathSummary
                {
                    RouteHandle          = e.RouteHandle,
                    TotalDistanceMeters  = e.TotalDistanceMeters,
                    WaypointCount        = e.Waypoints.Length,
                    NavmeshVersionAtPlan = e.NavmeshVersionAtPlan,
                    PrimaryBackend       = e.PrimaryBackend,
                    Flags                = e.Flags,
                    ReplanCount          = e.ReplanCount,
                };
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        /// <inheritdoc/>
        public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_entries.TryGetValue(routeHandle, out var e))
                {
                    Interlocked.Increment(ref _misses);
                    count = 0;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                int n = Math.Min(e.Waypoints.Length, dest.Length);
                e.Waypoints.AsSpan(0, n).CopyTo(dest);
                count = n;
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        /// <inheritdoc/>
        public bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                          Span<NavWaypoint> dest, out int actualCount)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_entries.TryGetValue(routeHandle, out var e))
                {
                    Interlocked.Increment(ref _misses);
                    actualCount = 0;
                    return false;
                }
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
            finally { _lock.ExitReadLock(); }
        }

        // ── IFakeMusclePathRegistryTestApi ───────────────────────────────────────

        /// <summary>Convenience overload: stores a path with default metadata values.</summary>
        public void StoreOrReplace(int routeHandle, NavWaypoint[] waypoints)
        {
            RegisterOrReplace(routeHandle, waypoints, 0f, 0u, 0, 0);
        }

        /// <inheritdoc/>
        public void RegisterOrReplace(int routeHandle, NavWaypoint[] waypoints,
                                      float totalDist, uint navmeshVersion,
                                      byte primaryBackend, byte flags)
        {
            _lock.EnterWriteLock();
            try
            {
                byte replanCount = 0;
                if (_entries.TryGetValue(routeHandle, out var existing))
                    replanCount = (byte)(existing.ReplanCount + 1);

                _entries[routeHandle] = new FakePathPoolEntry
                {
                    RouteHandle          = routeHandle,
                    Waypoints            = waypoints,
                    TotalDistanceMeters  = totalDist,
                    NavmeshVersionAtPlan = navmeshVersion,
                    PrimaryBackend       = primaryBackend,
                    Flags                = flags,
                    ReplanCount          = replanCount,
                };
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <inheritdoc/>
        public bool Free(int routeHandle)
        {
            _lock.EnterWriteLock();
            try { return _entries.Remove(routeHandle); }
            finally { _lock.ExitWriteLock(); }
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<int, FakePathPoolEntry> Snapshot()
        {
            _lock.EnterReadLock();
            try { return new Dictionary<int, FakePathPoolEntry>(_entries); }
            finally { _lock.ExitReadLock(); }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            _lock.EnterWriteLock();
            try { _entries.Clear(); }
            finally { _lock.ExitWriteLock(); }
        }

        /// <inheritdoc/>
        public FakePathRegistryStats GetStats()
        {
            _lock.EnterReadLock();
            int total = _entries.Count;
            _lock.ExitReadLock();

            return new FakePathRegistryStats
            {
                TotalEntries = total,
                HitCount     = (int)Interlocked.Read(ref _hits),
                MissCount    = (int)Interlocked.Read(ref _misses),
            };
        }
    }

    /// <summary>
    /// Lightweight stats snapshot returned by registry test APIs.
    /// </summary>
    public struct FakePathRegistryStats
    {
        public int  TotalEntries;
        public int  HitCount;
        public int  MissCount;
        /// <summary>Entries that were present but had a mismatched ReplanCount (Brain cache only).</summary>
        public int  StaleMisses;
        /// <summary>LRU evictions performed (Brain cache only).</summary>
        public int  Evictions;
    }
}
