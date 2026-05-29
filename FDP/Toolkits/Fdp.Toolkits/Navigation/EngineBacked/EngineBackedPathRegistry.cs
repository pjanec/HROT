using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CarKinem.Trajectory;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Real <see cref="IPathRegistry"/> adapter backed by <see cref="TrajectoryPoolManager"/>.
    /// RouteHandle equals NavState.TrajectoryId. All-in-one mode only.
    /// </summary>
    public sealed class EngineBackedPathRegistry : IPathRegistry
    {
        private readonly TrajectoryPoolManager _pool;

        // Metadata per registered handle (not stored in pool).
        private record struct EntryMeta(byte ReplanCount, float TotalDistanceMeters, byte PrimaryBackend);
        private readonly Dictionary<int, EntryMeta> _meta = new();
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        private long _hits;
        private long _misses;
        private long _staleMisses;

        public EngineBackedPathRegistry(TrajectoryPoolManager pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        // ── Registration API (not part of IPathRegistry) ─────────────────────

        /// <summary>
        /// Register (or replace) an entry. The caller must have already populated the
        /// trajectory in the pool via RegisterTrajectoryWithKey before calling this.
        /// </summary>
        public void Register(int handle, byte replanCount = 0,
                             float totalDistanceMeters = 0f, byte primaryBackend = 0)
        {
            _lock.EnterWriteLock();
            try { _meta[handle] = new EntryMeta(replanCount, totalDistanceMeters, primaryBackend); }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>Remove entry from registry and from pool.</summary>
        public bool Free(int handle)
        {
            _lock.EnterWriteLock();
            try
            {
                bool had = _meta.Remove(handle);
                _pool.RemoveTrajectory(handle);
                return had;
            }
            finally { _lock.ExitWriteLock(); }
        }

        // ── IPathRegistry ────────────────────────────────────────────────────

        public bool IsCached(int routeHandle)
        {
            _lock.EnterReadLock();
            try
            {
                bool found = _meta.ContainsKey(routeHandle) && _pool.TryGetTrajectory(routeHandle, out _);
                if (found) Interlocked.Increment(ref _hits); else Interlocked.Increment(ref _misses);
                return found;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetSummary(int routeHandle, out PathSummary summary)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.TryGetValue(routeHandle, out var m) || !_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    summary = default;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                summary = new PathSummary
                {
                    RouteHandle          = routeHandle,
                    TotalDistanceMeters  = m.TotalDistanceMeters > 0f ? m.TotalDistanceMeters : traj.TotalLength,
                    WaypointCount        = traj.Waypoints.Length,
                    PrimaryBackend       = m.PrimaryBackend,
                    ReplanCount          = m.ReplanCount,
                };
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.ContainsKey(routeHandle) || !_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    count = 0;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                CopyWaypointsFromPool(traj, dest, out count);
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                         Span<NavWaypoint> dest, out int actualCount)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.ContainsKey(routeHandle) || !_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    actualCount = 0;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                int start = Math.Max(0, startSegment);
                int available = traj.Waypoints.Length - start;
                actualCount = Math.Min(Math.Min(available, maxCount), dest.Length);
                if (actualCount <= 0) { actualCount = 0; return false; }
                for (int i = 0; i < actualCount; i++)
                {
                    var tw = traj.Waypoints[start + i];
                    dest[i] = new NavWaypoint
                    {
                        // Sim (Z-up) trajectory waypoint -> Recast (Y-up) NavWaypoint: altitude
                        // (Sim Z) goes into the Recast Y slot, not 0f (§0.1, P3D-404 sweep fix).
                        Position  = new Vector3(tw.Position.X, tw.Position.Z, tw.Position.Y),
                        Traversal = TraversalKind.Walk,
                        Surface   = SurfaceType.Generic,
                    };
                }
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        // ── Replan-aware overload (strict cache-miss policy) ─────────────────

        /// <summary>
        /// ReplanCount-aware lookup. Returns false (stale miss) if stored ReplanCount
        /// doesn't match <paramref name="expectedReplanCount"/>.
        /// </summary>
        public bool TryGetWaypoints(int routeHandle, byte expectedReplanCount,
                                    Span<NavWaypoint> dest, out int count)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.TryGetValue(routeHandle, out var m))
                {
                    Interlocked.Increment(ref _misses);
                    count = 0;
                    return false;
                }
                if (m.ReplanCount != expectedReplanCount)
                {
                    Interlocked.Increment(ref _staleMisses);
                    count = 0;
                    return false;
                }
                if (!_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    count = 0;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                CopyWaypointsFromPool(traj, dest, out count);
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Copy waypoints from <paramref name="traj"/> into <paramref name="dest"/>
        /// without acquiring any lock. Callers must hold the read lock.
        /// </summary>
        private static void CopyWaypointsFromPool(CustomTrajectory traj, Span<NavWaypoint> dest, out int count)
        {
            count = Math.Min(traj.Waypoints.Length, dest.Length);
            for (int i = 0; i < count; i++)
            {
                var tw = traj.Waypoints[i];
                dest[i] = new NavWaypoint
                {
                    // Sim (Z-up) -> Recast (Y-up): altitude (Sim Z) into Recast Y (§0.1, P3D-404 sweep fix).
                    Position  = new Vector3(tw.Position.X, tw.Position.Z, tw.Position.Y),
                    Traversal = TraversalKind.Walk,
                    Surface   = SurfaceType.Generic,
                };
            }
        }
    }
}
