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

        // The last route found (Route → EmitLeg).
        private readonly List<int> _segments = new();      // segment per hop, in travel order
        private readonly List<bool> _forward = new();      // walked start→end?
        private RoadAccess _entry, _exit;
        private bool _sameSegment;
        private bool _exitViaStartNode;

        /// <summary>True when <paramref name="roads"/> has anything to route on.</summary>
        public static bool HasRoads(in RoadNetworkBlob roads)
            => roads.Nodes.IsCreated && roads.Nodes.Length > 0 && roads.Segments.IsCreated && roads.Segments.Length > 0;

        /// <summary>
        /// The point of the network nearest <paramref name="p"/>, within <paramref name="maxDistance"/> m, or
        /// <see cref="RoadAccess.None"/>. Every segment is tested (road graphs are hundreds of segments); the projection samples
        /// the curve by arc length (<see cref="RoadGraphNavigator.SampleRoadSegment"/>), so <see cref="RoadAccess.Along"/> means
        /// the same thing <see cref="EmitLeg"/> walks.
        /// </summary>
        public static RoadAccess NearestAccess(in RoadNetworkBlob roads, Vector2 p, float maxDistance)
        {
            if (!HasRoads(roads)) return RoadAccess.None;
            var best = RoadAccess.None;
            for (int s = 0; s < roads.Segments.Length; s++)
            {
                var seg = roads.Segments[s];
                if (seg.Length <= 0f) continue;
                float step = seg.Length / ProjectionSamples;
                float bestAlong = 0f, bestD2 = float.MaxValue;
                Vector2 bestPt = default;
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
                float d = MathF.Sqrt(bestD2);
                if (d <= maxDistance && d < best.Distance) best = new RoadAccess(s, bestAlong, bestPt, d);
            }
            return best;
        }

        /// <summary>
        /// The shortest road distance from <paramref name="entry"/> to <paramref name="exit"/>, every segment walkable both ways
        /// (D5). False when they are not connected. Remembers the route for <see cref="EmitLeg"/>.
        /// </summary>
        public bool Route(in RoadNetworkBlob roads, in RoadAccess entry, in RoadAccess exit, out float roadMetres)
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
            for (int i = 0; i < n; i++) { _dist[i] = float.MaxValue; _prevNode[i] = -1; _prevSeg[i] = -1; _done[i] = false; }
            // Virtual source on the entry segment: its two end nodes are reached along the segment.
            Relax(se.StartNodeIndex, entry.Along, -1, entry.Segment);
            Relax(se.EndNodeIndex, se.Length - entry.Along, -1, entry.Segment);

            while (true)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!_done[i] && _dist[i] < float.MaxValue && (u < 0 || _dist[i] < _dist[u])) u = i;
                if (u < 0) break;
                _done[u] = true;
                for (int s = 0; s < roads.Segments.Length; s++)
                {
                    var seg = roads.Segments[s];
                    int v = seg.StartNodeIndex == u ? seg.EndNodeIndex : seg.EndNodeIndex == u ? seg.StartNodeIndex : -1;
                    if (v < 0 || v >= n || _done[v]) continue;
                    Relax(v, _dist[u] + seg.Length, u, s);
                }
            }

            float viaStart = Reach(sx.StartNodeIndex, exit.Along);
            float viaEnd = Reach(sx.EndNodeIndex, sx.Length - exit.Along);
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
            // Node chain back to the virtual source.
            var hops = new Stack<(int seg, bool fwd)>();
            for (int v = last; _prevSeg[v] >= 0 && _prevNode[v] >= 0; v = _prevNode[v])
            {
                var seg = roads.Segments[_prevSeg[v]];
                hops.Push((_prevSeg[v], seg.EndNodeIndex == v));
            }
            while (hops.Count > 0) { var h = hops.Pop(); _segments.Add(h.seg); _forward.Add(h.fwd); }
            roadMetres = graph;
            return true;

            float Reach(int node, float tail) => node >= 0 && node < n && _dist[node] < float.MaxValue ? _dist[node] + tail : float.MaxValue;
        }

        /// <summary>
        /// Appends the last <see cref="Route"/> to <paramref name="output"/> as points along the curves, entry to exit, in travel
        /// direction. <paramref name="z"/> gives each point's height (the navmesh's, or 0).
        /// </summary>
        public void EmitLeg(in RoadNetworkBlob roads, List<Vector3> output, Func<Vector2, float> z)
        {
            if (_sameSegment)
            {
                Partial(roads, _entry.Segment, _entry.Along, _exit.Along, output, z);
                return;
            }
            var se = roads.Segments[_entry.Segment];
            // Leave the entry segment toward the node the route continues from.
            int firstNode = _segments.Count > 0
                ? (_forward[0] ? roads.Segments[_segments[0]].StartNodeIndex : roads.Segments[_segments[0]].EndNodeIndex)
                : (_exitViaStartNode ? roads.Segments[_exit.Segment].StartNodeIndex : roads.Segments[_exit.Segment].EndNodeIndex);
            float entryEnd = firstNode == se.StartNodeIndex ? 0f : se.Length;
            Partial(roads, _entry.Segment, _entry.Along, entryEnd, output, z);

            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = roads.Segments[_segments[i]];
                if (_forward[i]) Partial(roads, _segments[i], 0f, seg.Length, output, z);
                else Partial(roads, _segments[i], seg.Length, 0f, output, z);
            }

            var sx = roads.Segments[_exit.Segment];
            Partial(roads, _exit.Segment, _exitViaStartNode ? 0f : sx.Length, _exit.Along, output, z);
        }

        /// <summary>Points from <paramref name="from"/> to <paramref name="to"/> metres along a segment (either direction).</summary>
        private static void Partial(in RoadNetworkBlob roads, int segIndex, float from, float to, List<Vector3> output, Func<Vector2, float> z)
        {
            var seg = roads.Segments[segIndex];
            float span = MathF.Abs(to - from);
            int steps = Math.Max(1, (int)MathF.Ceiling(SamplesPerSegment * (seg.Length > 0f ? span / seg.Length : 1f)));
            for (int k = 0; k <= steps; k++)
            {
                float a = from + (to - from) * k / steps;
                var p = RoadGraphNavigator.SampleRoadSegment(seg, a).pos;
                Append(output, new Vector3(p, z(p)));
            }
        }

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
        }

        private void Ensure(int n)
        {
            if (_dist.Length >= n) return;
            _dist = new float[n]; _prevNode = new int[n]; _prevSeg = new int[n]; _done = new bool[n];
        }
    }
}
