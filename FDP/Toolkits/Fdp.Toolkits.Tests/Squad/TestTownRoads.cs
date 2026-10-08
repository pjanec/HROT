using System.Numerics;
using System;
using CarKinem.Road;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Tests.Squad
{
    /// <summary>
    /// ⭐ <c>CE-3128</c> — test-town's road GRAPH as the shipped <c>roads.json</c> declares it: Main Street (y 200, x 0–400) and
    /// Cross Street (x 200, y 0–400), four 5 m lanes each (20 m wide — the retired polygons' footprint), meeting at the junction
    /// (200, 200). Callers dispose the blob.
    /// </summary>
    internal static class TestTownRoads
    {
        public static RoadNetworkBlob Build()
        {
            var b = new RoadNetworkBuilder();
            var nodes = new[] { new Vector2(0, 200), new Vector2(200, 200), new Vector2(400, 200), new Vector2(200, 0), new Vector2(200, 400) };
            foreach (var n in nodes) b.AddNode(n);
            void Seg(int a, int c)
            {
                var t = nodes[c] - nodes[a];
                b.AddSegment(nodes[a], t, nodes[c], t, speedLimit: 13.9f, laneWidth: 5f, laneCount: 4, startNodeIdx: a, endNodeIdx: c);
            }
            Seg(0, 1); Seg(1, 2); Seg(3, 1); Seg(1, 4);
            return b.Build(cellSize: 10f, gridWidth: 40, gridHeight: 40);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3128</c> — open ground: every path is the straight line and its cost the distance. What a nav node's navmesh is to
    /// the route planner when nothing stands in the way — so a rail sees the planner's DECISION (direct vs roads), not a mesh.
    /// </summary>
    internal sealed class StraightNavmesh : INavmeshProvider
    {
        public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => true;
        public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF) { snapped = position; return true; }
        public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;
        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
            => Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));
        public uint QueryVersion() => 1;
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
        {
            if (waypoints.Length < 2) return 0;
            waypoints[0] = new NavWaypoint { Position = from };
            waypoints[1] = new NavWaypoint { Position = to };
            return 2;
        }
    }
}
