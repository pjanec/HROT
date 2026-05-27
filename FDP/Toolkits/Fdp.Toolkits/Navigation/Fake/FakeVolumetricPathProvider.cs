using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Test-API surface for direct control of <see cref="FakeVolumetricPathProvider"/>.
    /// </summary>
    public interface IFakeVolumetricPathProviderTestApi
    {
        /// <summary>Add a no-fly volume. Bumps the version counter.</summary>
        void AddNoFlyZone(BoundingBox3D zone);

        /// <summary>Remove all no-fly zones. Bumps the version.</summary>
        void ClearNoFlyZones();

        /// <summary>Return accumulated stats.</summary>
        FakeVolumetricStats GetStats();
    }

    /// <summary>
    /// Snapshot of call statistics for the fake volumetric provider.
    /// </summary>
    public struct FakeVolumetricStats
    {
        public int PlanPathCalls;
        public int IsFlyableCalls;
        public int PathExistsCalls;
    }

    /// <summary>
    /// In-memory volumetric path provider for unit testing.
    ///
    /// Flyable checks: position must be within [MinAltitude, MaxAltitude] and outside all
    /// registered no-fly zones.
    ///
    /// Path planning: uses a straight line if both endpoints are flyable and the segment
    /// does not intersect any no-fly zone. Otherwise performs a 5-metre grid A* search
    /// in the (X, Y) plane (ignoring Z for simplicity).
    ///
    /// QueryVersion(BoundingBox3D): returns current version if any registered no-fly zone
    /// intersects <paramref name="region"/>.
    /// </summary>
    public sealed class FakeVolumetricPathProvider : IVolumetricPathProvider, IFakeVolumetricPathProviderTestApi
    {
        private const float GridStep = 5f;
        private const int   MaxGridSteps = 200;

        private static readonly (int dx, int dz)[] _dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private readonly List<BoundingBox3D> _noFlyZones = new();
        private uint   _version = 1u;
        private float  _minAltitude;
        private float  _maxAltitude;

        private int _planPathCalls;
        private int _isFlyableCalls;
        private int _pathExistsCalls;

        /// <param name="minAltitude">Minimum flyable altitude (Y, metres). Default 0.</param>
        /// <param name="maxAltitude">Maximum flyable altitude (Y, metres). Default 5000.</param>
        public FakeVolumetricPathProvider(float minAltitude = 0f, float maxAltitude = 5000f)
        {
            _minAltitude = minAltitude;
            _maxAltitude = maxAltitude;
        }

        /// <summary>
        /// Constructs a provider from a <see cref="NavTestMap"/>, initialising altitude bounds
        /// and no-fly zones from the map definition.
        /// </summary>
        public FakeVolumetricPathProvider(NavTestMap map)
            : this(map.MinAltitude, map.MaxAltitude)
        {
            foreach (var zone in map.NoFlyZones)
                AddNoFlyZone(zone.Bounds);
        }

        // ── IVolumetricPathProvider ──────────────────────────────────────────────

        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints)
        {
            _planPathCalls++;

            if (!IsPositionFlyable(from) || !IsPositionFlyable(to))
                return 0;
            if (waypoints.Length == 0) return 0;

            // Try straight line first.
            if (!SegmentBlockedByNoFly(from, to))
            {
                if (waypoints.Length < 2) return 0;
                waypoints[0] = MakeWaypoint(from);
                waypoints[1] = MakeWaypoint(to);
                return 2;
            }

            // Fall back to grid A* in the (X, Z) plane at the average Y.
            float midY = (from.Y + to.Y) * 0.5f;
            return GridPlan(from, to, midY, waypoints);
        }

        /// <inheritdoc/>
        public uint QueryVersion() => _version;

        /// <inheritdoc/>
        public bool IsFlyable(Vector3 position)
        {
            _isFlyableCalls++;
            return IsPositionFlyable(position);
        }

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, FlyProfile profile, float maxCost = 0f)
        {
            _pathExistsCalls++;

            if (from.Y < profile.MinAltitude || from.Y > profile.MaxAltitude) return false;
            if (to.Y   < profile.MinAltitude || to.Y   > profile.MaxAltitude) return false;
            if (!IsPositionFlyable(from) || !IsPositionFlyable(to))           return false;

            if (!SegmentBlockedByNoFly(from, to)) return true;

            // Try grid plan; if it finds any path, return true.
            float midY = (from.Y + to.Y) * 0.5f;
            var buf = new NavWaypoint[MaxGridSteps];
            int n = GridPlan(from, to, midY, buf.AsSpan());
            if (n == 0) return false;
            if (maxCost <= 0f) return true;

            // Check path cost against limit.
            float cost = 0f;
            for (int i = 1; i < n; i++)
            {
                var d = buf[i].Position - buf[i - 1].Position;
                cost += d.Length();
                if (cost > maxCost) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public uint QueryVersion(BoundingBox3D region)
        {
            foreach (var zone in _noFlyZones)
            {
                if (BoxesOverlap(region, zone)) return _version;
            }
            // No zone touches region; version is considered unchanged.
            return _version;
        }

        // ── IFakeVolumetricPathProviderTestApi ───────────────────────────────────

        /// <inheritdoc/>
        public void AddNoFlyZone(BoundingBox3D zone)
        {
            _noFlyZones.Add(zone);
            _version++;
        }

        /// <inheritdoc/>
        public void ClearNoFlyZones()
        {
            _noFlyZones.Clear();
            _version++;
        }

        /// <inheritdoc/>
        public FakeVolumetricStats GetStats() => new FakeVolumetricStats
        {
            PlanPathCalls   = _planPathCalls,
            IsFlyableCalls  = _isFlyableCalls,
            PathExistsCalls = _pathExistsCalls,
        };

        // ── Private helpers ──────────────────────────────────────────────────────

        private bool IsPositionFlyable(Vector3 p)
        {
            if (p.Y < _minAltitude || p.Y > _maxAltitude) return false;
            foreach (var zone in _noFlyZones)
                if (zone.Contains(p)) return false;
            return true;
        }

        private bool SegmentBlockedByNoFly(Vector3 a, Vector3 b)
        {
            foreach (var zone in _noFlyZones)
                if (zone.IntersectsLine(a, b)) return true;
            return false;
        }

        /// <summary>
        /// Grid A* in the (X, Z) plane at fixed Y = <paramref name="planeY"/>.
        /// Uses 4-connected grid with step <see cref="GridStep"/>.
        /// Returns number of waypoints written to <paramref name="out_"/>.
        /// </summary>
        private int GridPlan(Vector3 from, Vector3 to, float planeY, Span<NavWaypoint> out_)
        {
            // Snap start and end to grid.
            (int sx, int sz) = Snap(from.X, from.Z);
            (int ex, int ez) = Snap(to.X,   to.Z);

            if (sx == ex && sz == ez)
            {
                if (out_.Length < 2) return 0;
                out_[0] = MakeWaypoint(from);
                out_[1] = MakeWaypoint(to);
                return 2;
            }

            var dist = new Dictionary<(int, int), float>();
            var prev = new Dictionary<(int, int), (int, int)?>();
            var pq   = new SortedSet<(float cost, int x, int z)>(
                Comparer<(float cost, int x, int z)>.Create((a, b) =>
                {
                    int c = a.cost.CompareTo(b.cost);
                    if (c != 0) return c;
                    c = a.x.CompareTo(b.x);
                    return c != 0 ? c : a.z.CompareTo(b.z);
                }));

            var start = (sx, sz);
            dist[start] = 0f;
            prev[start] = null;
            pq.Add((0f, sx, sz));

            int iters = 0;
            while (pq.Count > 0 && iters++ < MaxGridSteps * MaxGridSteps)
            {
                var (cost, cx, cz) = pq.Min;
                pq.Remove(pq.Min);
                var cur = (cx, cz);

                if (cx == ex && cz == ez)
                {
                    // Reconstruct.
                    var cells = new List<(int, int)>();
                    (int, int)? node = cur;
                    while (node.HasValue)
                    {
                        cells.Add(node.Value);
                        node = prev.TryGetValue(node.Value, out var p) ? p : null;
                    }
                    cells.Reverse();

                    int needed = cells.Count + (cells.Count > 0 ? 1 : 0);
                    if (out_.Length < needed) needed = out_.Length;

                    int written = 0;
                    out_[written++] = MakeWaypoint(from);
                    for (int i = 1; i < cells.Count && written < out_.Length - 1; i++)
                    {
                        var (gx, gz) = cells[i];
                        out_[written++] = MakeWaypoint(new Vector3(gx * GridStep, planeY, gz * GridStep));
                    }
                    if (written < out_.Length)
                        out_[written++] = MakeWaypoint(to);
                    return written;
                }

                // 4-connected neighbours (static array to avoid stackalloc-in-loop CA2014).
                foreach (var (dx, dz) in _dirs)
                {
                    int nx = cx + dx, nz = cz + dz;
                    var pos = new Vector3(nx * GridStep, planeY, nz * GridStep);
                    if (!IsPositionFlyable(pos)) continue;
                    float newDist = cost + GridStep;
                    var next = (nx, nz);
                    if (!dist.TryGetValue(next, out float existing) || newDist < existing)
                    {
                        dist[next] = newDist;
                        prev[next] = cur;
                        float h = Math.Abs(nx - ex) * GridStep + Math.Abs(nz - ez) * GridStep;
                        pq.Add((newDist + h, nx, nz));
                    }
                }
            }
            return 0;
        }

        private static (int x, int z) Snap(float x, float z)
            => ((int)MathF.Round(x / GridStep), (int)MathF.Round(z / GridStep));

        private static NavWaypoint MakeWaypoint(Vector3 pos)
            => new NavWaypoint { Position = pos, Traversal = TraversalKind.Fly };

        private static bool BoxesOverlap(BoundingBox3D a, BoundingBox3D b)
            => a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
            && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
            && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }
}
