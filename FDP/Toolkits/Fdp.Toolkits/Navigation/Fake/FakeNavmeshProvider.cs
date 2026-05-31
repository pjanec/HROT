using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Test-API surface for direct control of <see cref="FakeNavmeshProvider"/>.
    /// </summary>
    public interface IFakeNavmeshProviderTestApi
    {
        /// <summary>
        /// Marks a polygon as blocked (non-walkable) in the specified layer and bumps
        /// that layer's version.  Passing <see cref="NavLayerMask.All"/> replicates the
        /// legacy behaviour of blocking in every layer that contains the polygon.
        /// Returns false if no polygon with that ID is found in the requested layer(s).
        /// </summary>
        bool BlockPolygon(int polygonId, NavLayerMask layer = NavLayerMask.All);

        /// <summary>
        /// Marks a polygon as unblocked (walkable) across all layers and bumps the version.
        /// Returns false if no polygon with that ID is found.
        /// </summary>
        bool UnblockPolygon(int polygonId);

        /// <summary>
        /// Adds an off-mesh link to the layer that owns <paramref name="fromPolygonId"/>.
        /// Bumps the layer version.
        /// </summary>
        void AddOffMeshLink(OffMeshLink link);

        /// <summary>
        /// Bumps the version of all layers whose spatial bounds overlap with
        /// <paramref name="region"/> and whose layer bit appears in <paramref name="layer"/>.
        /// Does NOT change any polygon walkability.
        /// </summary>
        void BumpVersion(BoundingBox2D region, NavLayerMask layer);

        /// <summary>
        /// Returns the <see cref="NavTestMap"/> that was used to construct this provider,
        /// or null if constructed directly from layers.
        /// </summary>
        NavTestMap? GetLoadedMap();
    }

    /// <summary>
    /// In-memory navmesh provider backed by <see cref="FakeNavLayer"/> instances.
    /// Intended for unit testing of navigation systems; not for production use.
    ///
    /// Walkability queries use (X, Z) plane point-in-polygon (winding number algorithm).
    /// Pathfinding uses Dijkstra over polygon adjacency lists and off-mesh links.
    /// </summary>
    public sealed class FakeNavmeshProvider : INavmeshProvider, IFakeNavmeshProviderTestApi
    {
        private readonly List<FakeNavLayer> _layers;
        private NavTestMap? _loadedMap;

        /// <param name="layers">
        /// One or more nav layers.  The layer order is irrelevant; lookups scan all layers
        /// whose bit appears in the caller-supplied <c>layerMask</c>.
        /// </param>
        public FakeNavmeshProvider(params FakeNavLayer[] layers)
        {
            _layers = new List<FakeNavLayer>(layers);
        }

        /// <summary>
        /// Constructs a provider from a <see cref="NavTestMap"/>, recording the map
        /// for later retrieval via <see cref="IFakeNavmeshProviderTestApi.GetLoadedMap"/>.
        /// </summary>
        public FakeNavmeshProvider(NavTestMap map) : this(map.Layers)
        {
            _loadedMap = map;
        }

        // ── INavmeshProvider ─────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF)
            => FindPolygon(position, layerMask) != null;

        /// <inheritdoc/>
        public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
        {
            var poly = FindPolygon(position, layerMask);
            if (poly == null)
            {
                snapped = position;
                return false;
            }
            // Return centroid Y from first vertex, keep caller's Y for flat terrain.
            float y = poly.Vertices.Length > 0 ? poly.Vertices[0].Y : position.Y;
            snapped = new Vector3(position.X, y, position.Z);
            return true;
        }

        /// <inheritdoc/>
        public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF)
        {
            int count = 0;
            float r2 = radius * radius;
            foreach (var layer in _layers)
            {
                if ((layer.Layer & layerMask) == 0) continue;
                foreach (var poly in layer.Polygons)
                {
                    if (poly.IsBlocked) continue;
                    var c = poly.Centroid();
                    float dx = c.X - center.X;
                    float dz = c.Z - center.Z;
                    if (dx * dx + dz * dz <= r2)
                    {
                        if (count < results.Length)
                            results[count] = c;
                        count++;
                    }
                }
            }
            return count;
        }

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
        {
            var (layer, fromPoly) = FindLayerAndPolygon(from, layerMask);
            if (layer == null || fromPoly == null) return false;
            var toPoly = FindPolygonInLayer(layer, to);
            if (toPoly == null) return false;
            if (fromPoly.Id == toPoly.Id) return true;
            return BfsPathExists(layer, fromPoly, toPoly);
        }

        /// <inheritdoc/>
        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
        {
            var waypointBuf = new NavWaypoint[256];
            int n = PlanPath(from, to, waypointBuf.AsSpan(), layerMask);
            if (n < 2) return n == 0 ? float.MaxValue : 0f;

            float cost = 0f;
            for (int i = 1; i < n; i++)
            {
                float dx = waypointBuf[i].Position.X - waypointBuf[i - 1].Position.X;
                float dz = waypointBuf[i].Position.Z - waypointBuf[i - 1].Position.Z;
                float dy = waypointBuf[i].Position.Y - waypointBuf[i - 1].Position.Y;
                cost += MathF.Sqrt(dx * dx + dz * dz + dy * dy);
            }
            return cost;
        }

        /// <inheritdoc/>
        public uint QueryVersion()
        {
            uint max = 0;
            foreach (var layer in _layers)
                if (layer.Version > max) max = layer.Version;
            return max;
        }

        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
        {
            var (layer, fromPoly) = FindLayerAndPolygon(from, layerMask);
            if (layer == null || fromPoly == null) return 0;
            var toPoly = FindPolygonInLayer(layer, to);
            if (toPoly == null) return 0;

            // Waypoints[0] = start position, waypoints[last] = end position.
            if (waypoints.Length == 0) return 0;

            if (fromPoly.Id == toPoly.Id)
            {
                if (waypoints.Length < 2) return 0;
                waypoints[0] = MakeWaypoint(from, TraversalKind.Walk);
                waypoints[1] = MakeWaypoint(to,   TraversalKind.Walk);
                return 2;
            }

            // Run Dijkstra to find the polygon path.
            var polyPath = Dijkstra(layer, fromPoly, toPoly);
            if (polyPath == null) return 0;

            // Build waypoint list: start pos + polygon centroids + end pos,
            // inserting off-mesh link waypoints where transitions use one.
            var wpList = new List<NavWaypoint>();
            wpList.Add(MakeWaypoint(from, TraversalKind.Walk));

            for (int i = 0; i < polyPath.Count - 1; i++)
            {
                int curId  = polyPath[i];
                int nextId = polyPath[i + 1];

                // Check if this transition uses an off-mesh link.
                OffMeshLink? link = FindLink(layer, curId, nextId);
                if (link != null)
                {
                    wpList.Add(MakeWaypoint(link.StartPos, TraversalKind.Walk));
                    wpList.Add(MakeWaypoint(link.EndPos, link.Kind));
                }
                else
                {
                    // Use centroid of destination polygon as intermediate point.
                    var poly = GetPolygonById(layer, nextId);
                    if (poly != null && i < polyPath.Count - 2)
                        wpList.Add(MakeWaypoint(poly.Centroid(), TraversalKind.Walk));
                }
            }

            wpList.Add(MakeWaypoint(to, TraversalKind.Walk));

            int count = Math.Min(wpList.Count, waypoints.Length);
            for (int i = 0; i < count; i++) waypoints[i] = wpList[i];
            return count;
        }

        // ── IFakeNavmeshProviderTestApi ──────────────────────────────────────────

        /// <inheritdoc/>
        public bool BlockPolygon(int polygonId, NavLayerMask layer = NavLayerMask.All)
        {
            bool found = false;
            foreach (var navLayer in _layers)
            {
                // Only block in layers whose bit is set in the requested mask.
                if ((navLayer.Layer & (uint)layer) == 0) continue;

                foreach (var poly in navLayer.Polygons)
                {
                    if (poly.Id == polygonId)
                    {
                        poly.IsBlocked = true;
                        navLayer.Version++;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <inheritdoc/>
        public bool UnblockPolygon(int polygonId)
        {
            bool found = false;
            foreach (var layer in _layers)
            {
                foreach (var poly in layer.Polygons)
                {
                    if (poly.Id == polygonId)
                    {
                        poly.IsBlocked = false;
                        layer.Version++;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <inheritdoc/>
        public void AddOffMeshLink(OffMeshLink link)
        {
            foreach (var layer in _layers)
            {
                foreach (var poly in layer.Polygons)
                {
                    if (poly.Id == link.FromPolygonId)
                    {
                        var expanded = new OffMeshLink[layer.OffMeshLinks.Length + 1];
                        Array.Copy(layer.OffMeshLinks, expanded, layer.OffMeshLinks.Length);
                        expanded[layer.OffMeshLinks.Length] = link;
                        layer.OffMeshLinks = expanded;
                        layer.Version++;
                        return;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public NavTestMap? GetLoadedMap() => _loadedMap;

        /// <inheritdoc/>
        public void BumpVersion(BoundingBox2D region, NavLayerMask layer)
        {
            foreach (var navLayer in _layers)
            {
                // Only bump layers whose bit is set in the requested mask.
                if (layer != NavLayerMask.All && (navLayer.Layer & (uint)layer) == 0)
                    continue;

                // Bump if any polygon centroid falls within the region.
                bool overlaps = false;
                foreach (var poly in navLayer.Polygons)
                {
                    var c = poly.Centroid();
                    if (region.Contains(new System.Numerics.Vector2(c.X, c.Z)))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps)
                    navLayer.Version++;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Find any walkable polygon in any matching layer that contains <paramref name="pos"/>
        /// (X, Z plane).
        /// </summary>
        private NavPolygon? FindPolygon(Vector3 pos, uint layerMask)
        {
            foreach (var layer in _layers)
            {
                if ((layer.Layer & layerMask) == 0) continue;
                foreach (var poly in layer.Polygons)
                {
                    if (!poly.IsBlocked && PointInPolygon(pos.X, pos.Z, poly))
                        return poly;
                }
            }
            return null;
        }

        private (FakeNavLayer? layer, NavPolygon? poly) FindLayerAndPolygon(Vector3 pos, uint layerMask)
        {
            foreach (var layer in _layers)
            {
                if ((layer.Layer & layerMask) == 0) continue;
                foreach (var poly in layer.Polygons)
                {
                    if (!poly.IsBlocked && PointInPolygon(pos.X, pos.Z, poly))
                        return (layer, poly);
                }
            }
            return (null, null);
        }

        private static NavPolygon? FindPolygonInLayer(FakeNavLayer layer, Vector3 pos)
        {
            foreach (var poly in layer.Polygons)
                if (!poly.IsBlocked && PointInPolygon(pos.X, pos.Z, poly))
                    return poly;
            return null;
        }

        private static NavPolygon? GetPolygonById(FakeNavLayer layer, int id)
        {
            foreach (var poly in layer.Polygons)
                if (poly.Id == id) return poly;
            return null;
        }

        private static int PolyIndex(FakeNavLayer layer, int id)
        {
            for (int i = 0; i < layer.Polygons.Length; i++)
                if (layer.Polygons[i].Id == id) return i;
            return -1;
        }

        /// <summary>
        /// Winding-number point-in-polygon test using the (X, Z) plane.
        /// Works for arbitrary simple polygons (convex or concave).
        /// </summary>
        private static bool PointInPolygon(float px, float pz, NavPolygon poly)
        {
            var verts = poly.Vertices;
            int n = verts.Length;
            if (n < 3) return false;

            int winding = 0;
            for (int i = 0; i < n; i++)
            {
                float x1 = verts[i].X,          z1 = verts[i].Z;
                float x2 = verts[(i + 1) % n].X, z2 = verts[(i + 1) % n].Z;

                if (z1 <= pz)
                {
                    if (z2 > pz && IsLeft(x1, z1, x2, z2, px, pz) > 0)
                        winding++;
                }
                else
                {
                    if (z2 <= pz && IsLeft(x1, z1, x2, z2, px, pz) < 0)
                        winding--;
                }
            }
            return winding != 0;
        }

        /// <summary>
        /// Positive if (px, pz) is to the left of the edge from (x1,z1) to (x2,z2).
        /// </summary>
        private static float IsLeft(float x1, float z1, float x2, float z2, float px, float pz)
            => (x2 - x1) * (pz - z1) - (px - x1) * (z2 - z1);

        /// <summary>BFS: can we reach <paramref name="to"/> from <paramref name="from"/>?</summary>
        private static bool BfsPathExists(FakeNavLayer layer, NavPolygon from, NavPolygon to)
        {
            return Dijkstra(layer, from, to) != null;
        }

        /// <summary>
        /// Dijkstra on the polygon adjacency + off-mesh link graph.
        /// Returns the ordered list of polygon IDs, or null if no path exists.
        /// </summary>
        private static List<int>? Dijkstra(FakeNavLayer layer, NavPolygon from, NavPolygon to)
        {
            // Build a fast polygon-id-to-index map.
            var idToIdx = new Dictionary<int, int>();
            for (int i = 0; i < layer.Polygons.Length; i++)
                idToIdx[layer.Polygons[i].Id] = i;

            var dist = new Dictionary<int, float>();
            var prev = new Dictionary<int, int>();
            var pq   = new SortedSet<(float cost, int id)>(Comparer<(float cost, int id)>.Create(
                (a, b) => a.cost != b.cost ? a.cost.CompareTo(b.cost) : a.id.CompareTo(b.id)));

            dist[from.Id] = 0f;
            pq.Add((0f, from.Id));

            while (pq.Count > 0)
            {
                var (cost, curId) = pq.Min;
                pq.Remove(pq.Min);

                if (curId == to.Id)
                {
                    // Reconstruct path.
                    var path = new List<int>();
                    int cur = to.Id;
                    while (cur != from.Id)
                    {
                        path.Add(cur);
                        cur = prev[cur];
                    }
                    path.Add(from.Id);
                    path.Reverse();
                    return path;
                }

                if (!idToIdx.TryGetValue(curId, out int curIdx)) continue;
                var curPoly = layer.Polygons[curIdx];
                if (curPoly.IsBlocked) continue;

                // Normal adjacency edges.
                if (curIdx < layer.Adjacency.Length)
                {
                    foreach (int adjIdx in layer.Adjacency[curIdx])
                    {
                        if (adjIdx < 0 || adjIdx >= layer.Polygons.Length) continue;
                        var adjPoly = layer.Polygons[adjIdx];
                        if (adjPoly.IsBlocked) continue;
                        float edgeCost = EdgeCost(curPoly, adjPoly);
                        float newDist  = cost + edgeCost;
                        if (!dist.TryGetValue(adjPoly.Id, out float existing) || newDist < existing)
                        {
                            dist[adjPoly.Id] = newDist;
                            prev[adjPoly.Id] = curId;
                            pq.Add((newDist, adjPoly.Id));
                        }
                    }
                }

                // Off-mesh link edges.
                foreach (var link in layer.OffMeshLinks)
                {
                    if (link.FromPolygonId != curId) continue;
                    int destIdx = idToIdx.TryGetValue(link.ToPolygonId, out int di) ? di : -1;
                    if (destIdx < 0 || layer.Polygons[destIdx].IsBlocked) continue;
                    float edgeCost = link.Cost;
                    float newDist  = cost + edgeCost;
                    if (!dist.TryGetValue(link.ToPolygonId, out float existing) || newDist < existing)
                    {
                        dist[link.ToPolygonId] = newDist;
                        prev[link.ToPolygonId] = curId;
                        pq.Add((newDist, link.ToPolygonId));
                    }
                }
            }
            return null;
        }

        private static float EdgeCost(NavPolygon a, NavPolygon b)
        {
            var ca = a.Centroid();
            var cb = b.Centroid();
            float dx = ca.X - cb.X, dz = ca.Z - cb.Z, dy = ca.Y - cb.Y;
            return MathF.Sqrt(dx * dx + dz * dz + dy * dy);
        }

        private static OffMeshLink? FindLink(FakeNavLayer layer, int fromId, int toId)
        {
            foreach (var link in layer.OffMeshLinks)
                if (link.FromPolygonId == fromId && link.ToPolygonId == toId)
                    return link;
            return null;
        }

        private static NavWaypoint MakeWaypoint(Vector3 pos, TraversalKind traversal)
            => new NavWaypoint { Position = pos, Traversal = traversal };
    }
}
