using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>A point ON the road network: segment <see cref="Segment"/>, <see cref="Along"/> metres from its start node.</summary>
    public readonly struct RoadAccess
    {
        public readonly int Segment;
        public readonly float Along;
        public readonly Vector2 Point;
        public readonly float Distance;

        public RoadAccess(int segment, float along, Vector2 point, float distance)
        {
            Segment = segment; Along = along; Point = point; Distance = distance;
        }

        public bool IsValid => Segment >= 0;
        public static readonly RoadAccess None = new(-1, 0f, default, float.MaxValue);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3128</c> (§5.2a D3–D5) — the road half of a route, over a <see cref="RoadNetworkBlob"/>:
    /// <list type="bullet">
    ///   <item><see cref="NearestAccess"/> — the nearest point ON the network (mid-segment, not only a node);</item>
    ///   <item><see cref="Route"/> — Dijkstra on the graph walked BOTH ways (D5), between two such points;</item>
    ///   <item><see cref="EmitLeg"/> — the found route as points along each segment's Hermite curve (D4).</item>
    /// </list>
    /// ⭐ One instance per solver: its search arrays are reused across requests (R-220 — no per-request scratch on the
    /// background thread). ⛔ Not thread-safe; a caller on another thread owns its own instance.
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a.
    /// </summary>
    public sealed class RoadGraphRouter
    {
        /// <summary>Points per whole segment in an emitted leg (as <c>RoadNetworkGizmo</c> draws the curve).</summary>
        public const int SamplesPerSegment = 8;

        private const int ProjectionSamples = 32;

        private float[] _dist = Array.Empty<float>();
        private int[] _prevNode = Array.Empty<int>();
        private int[] _prevSeg = Array.Empty<int>();
        private bool[] _done = Array.Empty<bool>();
        private float[] _segFactor = Array.Empty<float>();   // K12 — per segment, this request's under-fire factor

        // ⭐ CE-5043 — the large-terrain router (handoff ui → terrain, Stechovice: 19 257 nodes, 19 691 segments): the node → incident segments
        // come from the graph's ONE derived index (RoadGraphIndex, CE-5045 — built once per graph, shared with the danger classifier), each node's in
        // ascending segment order as the former scan of every segment met them; and the open list is the engine's heap instead of a scan of every node.
        // ⇒ O((N + S) log N) per route instead of O(N · (N + S)); the same costs, the same settle order, so the same routes.
        private readonly CostHeap _open = new(256);

        // The last route found (Route → EmitLeg).
        private readonly List<int> _segments = new();      // segment per hop, in travel order
        private readonly List<bool> _forward = new();      // walked start→end?
        private RoadAccess _entry, _exit;
        /// <summary>For rails: the last route's segments and directions (what <see cref="EmitLeg"/> walks).</summary>
        internal List<int> LastSegments => _segments;
        internal List<bool> LastForward => _forward;
        private bool _sameSegment;
        private bool _exitViaStartNode;

        /// <summary>True when <paramref name="roads"/> has anything to route on.</summary>
        public static bool HasRoads(in RoadNetworkBlob roads)
            => roads.Nodes.IsCreated && roads.Nodes.Length > 0 && roads.Segments.IsCreated && roads.Segments.Length > 0;

        /// <summary>
        /// The point of the network nearest <paramref name="p"/>, within <paramref name="maxDistance"/> m, or
        /// <see cref="RoadAccess.None"/>. Every segment is considered (pruned exactly by its curve's box, CE-5043); the projection samples
        /// the curve by arc length (<see cref="RoadGraphNavigator.SampleRoadSegment"/>), so <see cref="RoadAccess.Along"/> means
        /// the same thing <see cref="EmitLeg"/> walks.
        /// </summary>
        public static RoadAccess NearestAccess(in RoadNetworkBlob roads, Vector2 p, float maxDistance)
        {
            if (!HasRoads(roads)) return RoadAccess.None;
            // ⭐ CE-5043 — exact pruning (19 691 segments on Stechovice: 156 ms per call over every segment × 41 samples, two calls a plan).
            // ① An upper bound: the projection onto the segment whose start point is nearest — the final answer is at most that.
            // ② Every segment in ascending order as before, skipping one whose curve cannot come within the bound: a cubic Hermite is the Bézier
            //    P0, P0 + T0/3, P1 − T1/3, P1, so the curve lies inside those four points' box. A skipped segment's every sample is farther than
            //    the bound ⇒ it could never have won (nor tied) ⇒ the same answer, the same tie (lowest segment), with ~all projections skipped.
            int first = -1; float firstD2 = float.MaxValue;
            for (int s = 0; s < roads.Segments.Length; s++)
            {
                ref readonly var seg = ref roads.Segments[s];
                if (seg.Length <= 0f) continue;
                float d2 = Vector2.DistanceSquared(p, seg.P0);
                if (d2 < firstD2) { firstD2 = d2; first = s; }
            }
            if (first < 0) return RoadAccess.None;
            float bound = MathF.Min(maxDistance, Project(roads.Segments[first], p, out _, out _));

            var best = RoadAccess.None;
            for (int s = 0; s < roads.Segments.Length; s++)
            {
                var seg = roads.Segments[s];
                if (seg.Length <= 0f) continue;
                float limit = MathF.Min(bound, best.Distance);
                if (BoxDistanceSquared(seg, p) > limit * limit * 1.0001f + 1e-3f) continue;   // a margin for rounding: only ever skips less
                float d = Project(seg, p, out float along, out Vector2 at);
                if (d <= maxDistance && d < best.Distance) best = new RoadAccess(s, along, at, d);
            }
            return best;
        }

        /// <summary>The arc-length projection of <paramref name="p"/> onto <paramref name="seg"/> (coarse samples, then a refine): its distance.</summary>
        private static float Project(in RoadSegment seg, Vector2 p, out float bestAlong, out Vector2 bestPt)
        {
            float step = seg.Length / ProjectionSamples;
            float bestD2 = float.MaxValue;
            bestAlong = 0f; bestPt = default;
            for (int k = 0; k <= ProjectionSamples; k++)
            {
                var pt = RoadGraphNavigator.SampleRoadSegment(seg, k * step).pos;
                float d2 = Vector2.DistanceSquared(p, pt);
                if (d2 < bestD2) { bestD2 = d2; bestAlong = k * step; bestPt = pt; }
            }
            // refine around the best coarse sample
            float lo = MathF.Max(0f, bestAlong - step), hi = MathF.Min(seg.Length, bestAlong + step);
            for (int k = 0; k <= 8; k++)
            {
                float a = lo + (hi - lo) * k / 8f;
                var pt = RoadGraphNavigator.SampleRoadSegment(seg, a).pos;
                float d2 = Vector2.DistanceSquared(p, pt);
                if (d2 < bestD2) { bestD2 = d2; bestAlong = a; bestPt = pt; }
            }
            return MathF.Sqrt(bestD2);
        }

        /// <summary>The squared distance from <paramref name="p"/> to the box of the segment's Bézier control points (≤ to any point of the curve).</summary>
        private static float BoxDistanceSquared(in RoadSegment seg, Vector2 p)
        {
            Vector2 b1 = seg.P0 + seg.T0 / 3f, b2 = seg.P1 - seg.T1 / 3f;
            var min = Vector2.Min(Vector2.Min(seg.P0, seg.P1), Vector2.Min(b1, b2));
            var max = Vector2.Max(Vector2.Max(seg.P0, seg.P1), Vector2.Max(b1, b2));
            float dx = MathF.Max(0f, MathF.Max(min.X - p.X, p.X - max.X));
            float dy = MathF.Max(0f, MathF.Max(min.Y - p.Y, p.Y - max.Y));
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// The shortest road distance from <paramref name="entry"/> to <paramref name="exit"/>, every segment walkable both ways
        /// (D5). False when they are not connected. Remembers the route for <see cref="EmitLeg"/>.
        /// </summary>
        public bool Route(in RoadNetworkBlob roads, in RoadAccess entry, in RoadAccess exit, out float roadMetres)
            => Route(roads, entry, exit, out roadMetres, null, 0);

        /// <summary>
        /// ⭐ CE-2468 UF-5 (DESIGN_Minefields.md §9b.1 K12) — the same, the road priced for side <paramref name="sidePlus1"/> − 1: a segment whose MIDPOINT is covered by
        /// a known enemy (<see cref="Fdp.Toolkit.Terrain.UnderFireSight.Covered"/>) costs × <see cref="Fdp.Toolkit.Terrain.UnderFireSight.FactorOf"/>(1) — the navmesh
        /// polygons' own factor. <paramref name="roadMetres"/> is then a COST (the planner compares it), not a length. Null / 0 / a side with nothing covered = plain metres.
        /// </summary>
        public bool Route(in RoadNetworkBlob roads, in RoadAccess entry, in RoadAccess exit, out float roadMetres,
            Fdp.Toolkit.Terrain.UnderFireSight? underFire, byte sidePlus1)
        {
            roadMetres = float.MaxValue;
            _segments.Clear(); _forward.Clear();
            _entry = entry; _exit = exit; _sameSegment = false;
            if (!entry.IsValid || !exit.IsValid || !HasRoads(roads)) return false;

            var se = roads.Segments[entry.Segment];
            var sx = roads.Segments[exit.Segment];

            // Same segment: straight along it (a detour through the graph can only be longer).
            float direct = entry.Segment == exit.Segment ? MathF.Abs(exit.Along - entry.Along) : float.MaxValue;

            int n = roads.Nodes.Length;
            Ensure(n);
            var index = RoadGraphIndex.For(roads);
            int[] adjStart = index.AdjStart, adjSeg = index.AdjSeg;
            for (int i = 0; i < n; i++) { _dist[i] = float.MaxValue; _prevNode[i] = -1; _prevSeg[i] = -1; _done[i] = false; }
            _open.Clear();
            PriceSegments(roads, underFire, sidePlus1);
            float fe = _segFactor[entry.Segment], fx = _segFactor[exit.Segment];
            if (entry.Segment == exit.Segment) direct *= fe;
            // Virtual source on the entry segment: its two end nodes are reached along the segment.
            Relax(se.StartNodeIndex, fe * entry.Along, -1, entry.Segment);
            Relax(se.EndNodeIndex, fe * (se.Length - entry.Along), -1, entry.Segment);

            while (_open.Count > 0)
            {
                // The cheapest unsettled node, ties to the lowest index — what the former scan of every node picked (CostHeap's order).
                var (c, u) = _open.Pop();
                if (_done[u] || c != _dist[u]) continue;   // a stale entry (lazy decrease-key)
                _done[u] = true;
                for (int k = adjStart[u], end = adjStart[u + 1]; k < end; k++)
                {
                    int s = adjSeg[k];
                    var seg = roads.Segments[s];
                    int v = seg.StartNodeIndex == u ? seg.EndNodeIndex : seg.EndNodeIndex == u ? seg.StartNodeIndex : -1;
                    if (v < 0 || v >= n || _done[v]) continue;
                    Relax(v, _dist[u] + _segFactor[s] * seg.Length, u, s);
                }
            }

            float viaStart = Reach(sx.StartNodeIndex, fx * exit.Along);
            float viaEnd = Reach(sx.EndNodeIndex, fx * (sx.Length - exit.Along));
            float graph = MathF.Min(viaStart, viaEnd);

            if (direct <= graph)
            {
                if (direct == float.MaxValue) return false;
                _sameSegment = true;
                roadMetres = direct;
                return true;
            }
            if (graph == float.MaxValue) return false;

            _exitViaStartNode = viaStart <= viaEnd;
            int last = _exitViaStartNode ? sx.StartNodeIndex : sx.EndNodeIndex;
            // Node chain back to the virtual source, collected backwards and reversed in place (0 B — was a new Stack per route).
            for (int v = last; _prevSeg[v] >= 0 && _prevNode[v] >= 0; v = _prevNode[v])
            {
                var seg = roads.Segments[_prevSeg[v]];
                _segments.Add(_prevSeg[v]); _forward.Add(seg.EndNodeIndex == v);
            }
            _segments.Reverse(); _forward.Reverse();
            roadMetres = graph;
            return true;

            float Reach(int node, float tail) => node >= 0 && node < n && _dist[node] < float.MaxValue ? _dist[node] + tail : float.MaxValue;
        }

        /// <summary>
        /// Appends the last <see cref="Route"/> to <paramref name="output"/> as points along the curves, entry to exit, in travel
        /// direction. <paramref name="z"/> gives each point's height (the navmesh's, or 0).
        /// </summary>
        /// <summary>⭐ CE-9003 (C-7, R-9006) — drive the outer lane on the travel side instead of the centre line (<see cref="LaneOffset"/>) — only on a graph whose lines are carriageway centres (<see cref="RoadNetworkBlob.LinesAreCentres"/>). Set by the planner for
        /// <see cref="RoadUse.StronglyPrefer"/> (the convoy's road use); every other road use keeps the centre line as before.</summary>
        public bool KeepLane { get; set; }

        public void EmitLeg(in RoadNetworkBlob roads, List<Vector3> output, Func<int, float, Vector2, float> z)
        {
            bool lane = KeepLane && roads.LinesAreCentres;   // ⭐ a lane-line graph (imported from SUMO) is driven as drawn
            if (_sameSegment)
            {
                Partial(roads, _entry.Segment, _entry.Along, _exit.Along, output, z, lane);
                return;
            }
            var se = roads.Segments[_entry.Segment];
            // Leave the entry segment toward the node the route continues from.
            int firstNode = _segments.Count > 0
                ? (_forward[0] ? roads.Segments[_segments[0]].StartNodeIndex : roads.Segments[_segments[0]].EndNodeIndex)
                : (_exitViaStartNode ? roads.Segments[_exit.Segment].StartNodeIndex : roads.Segments[_exit.Segment].EndNodeIndex);
            float entryEnd = firstNode == se.StartNodeIndex ? 0f : se.Length;
            Partial(roads, _entry.Segment, _entry.Along, entryEnd, output, z, lane);

            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = roads.Segments[_segments[i]];
                if (_forward[i]) Partial(roads, _segments[i], 0f, seg.Length, output, z, lane);
                else Partial(roads, _segments[i], seg.Length, 0f, output, z, lane);
            }

            var sx = roads.Segments[_exit.Segment];
            Partial(roads, _exit.Segment, _exitViaStartNode ? 0f : sx.Length, _exit.Along, output, z, lane);
        }

        /// <summary>Points from <paramref name="from"/> to <paramref name="to"/> metres along a segment (either direction).</summary>
        private static void Partial(in RoadNetworkBlob roads, int segIndex, float from, float to, List<Vector3> output, Func<int, float, Vector2, float> z, bool keepLane)
        {
            var seg = roads.Segments[segIndex];
            float span = MathF.Abs(to - from);
            int steps = Math.Max(1, (int)MathF.Ceiling(SamplesPerSegment * (seg.Length > 0f ? span / seg.Length : 1f)));
            float lane = keepLane ? LaneOffset(in seg) : 0f;
            float dirSign = to >= from ? 1f : -1f;
            for (int k = 0; k <= steps; k++)
            {
                float a = from + (to - from) * k / steps;
                var (p, tangent, _) = RoadGraphNavigator.SampleRoadSegment(seg, a);
                // ⭐ CE-9003 (C-7, R-9006) — the outer lane on the travel side (right-hand traffic, the only side SUMO models): a multi-lane road is driven
                //   in its lane, never on the centre line where it met the oncoming traffic head-on
                if (lane > 0f && tangent.LengthSquared() > 1e-8f)
                {
                    var d = Vector2.Normalize(tangent) * dirSign;
                    p += new Vector2(d.Y, -d.X) * lane;
                }
                Append(output, new Vector3(p, z(segIndex, a, p)));
            }
        }

        /// <summary>
        /// ⭐ CE-9003 (C-7, R-9006; docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md) — how far right of the centre line the outer lane on the travel side lies:
        /// <c>LaneWidth · (LaneCount − 1) / 2</c> (the count is the whole carriageway — half-width = <c>LaneWidth · LaneCount / 2</c>); 0 for a one-lane road.
        /// </summary>
        public static float LaneOffset(in RoadSegment seg) => seg.LaneCount > 1 && seg.LaneWidth > 0f ? seg.LaneWidth * (seg.LaneCount - 1) * 0.5f : 0f;

        /// <summary>
        /// Adds <paramref name="p"/>; a point within 10 cm of the last one REPLACES it (stitched legs share their ends, and the
        /// later point wins — so a route's final point is exactly the requested end, never a road sample a few cm short of it).
        /// </summary>
        public static void Append(List<Vector3> output, Vector3 p)
        {
            if (output.Count > 0 && Vector2.DistanceSquared(new Vector2(output[^1].X, output[^1].Y), new Vector2(p.X, p.Y)) < 0.01f)
            {
                output[^1] = p;
                return;
            }
            output.Add(p);
        }

        private void Relax(int node, float d, int fromNode, int viaSeg)
        {
            if (node < 0 || node >= _dist.Length || d >= _dist[node]) return;
            _dist[node] = d; _prevNode[node] = fromNode; _prevSeg[node] = fromNode < 0 ? -1 : viaSeg;
            _open.Push(d, node);
        }

        /// <summary>K12 — each segment's factor for this request: 1, or the covered factor where its midpoint is covered (reused array, 0 B steady).</summary>
        private void PriceSegments(in RoadNetworkBlob roads, Fdp.Toolkit.Terrain.UnderFireSight? underFire, byte sidePlus1)
        {
            int m = roads.Segments.Length;
            if (_segFactor.Length < m) _segFactor = new float[m];
            bool priced = underFire != null && sidePlus1 != 0 && underFire.HasSide(sidePlus1);
            float covered = Fdp.Toolkit.Terrain.UnderFireSight.FactorOf(1f);
            for (int s = 0; s < m; s++)
            {
                _segFactor[s] = 1f;
                if (!priced) continue;
                var seg = roads.Segments[s];
                var (mid, _, _) = RoadGraphNavigator.SampleRoadSegment(seg, seg.Length * 0.5f);
                if (underFire!.Covered(sidePlus1, mid)) _segFactor[s] = covered;
            }
        }

        private void Ensure(int n)
        {
            if (_dist.Length >= n) return;
            _dist = new float[n]; _prevNode = new int[n]; _prevSeg = new int[n]; _done = new bool[n];
        }
    }
}
