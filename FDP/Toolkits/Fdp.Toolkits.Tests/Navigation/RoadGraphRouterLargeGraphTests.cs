using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using CarKinem.Road;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// ⭐ <c>CE-5043</c> — the road router on a LARGE graph (handoff ui → terrain, "large terrain router and cover": Stechovice's imported graph has
    /// 19 257 nodes and 19 691 segments, and <see cref="RoadGraphRouter.Route"/> — a scan of every node per settled node and of every segment
    /// per settled node — took seconds per route). Now: node → segments built once per graph, the open list on <see cref="CostHeap"/>.
    /// <list type="bullet">
    ///   <item>the same routes: on seeded random graphs (parallel segments, self-loops, dangling ends, islands, equal-cost ties on a lattice)
    ///   the route, its metres and its leg are the former scan's (an oracle below, the former code);</item>
    ///   <item>a 20 k-node graph routes within a budget;</item>
    ///   <item>0 B per route once warm (CLAUDE.md HOT PATH — the router runs in the muscle's path solver and the perception danger solve).</item>
    /// </list>
    /// 🔴 Red-proofs: the heap ordered by cost only (ties by push order) ⇒ the lattice routes differ ⇒ red; the adjacency not rebuilt for a new
    /// graph ⇒ the second graph's routes differ ⇒ red; the former <c>new Stack</c> per route ⇒ the 0 B rail red.
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a (the router) · §15 (performance, R-220).
    /// </summary>
    public sealed class RoadGraphRouterLargeGraphTests
    {
        private readonly ITestOutputHelper _out;
        public RoadGraphRouterLargeGraphTests(ITestOutputHelper output) => _out = output;

        // ── graphs ───────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Straight(RoadNetworkBuilder b, List<Vector2> at, int a, int c)
        {
            Vector2 pa = a >= 0 && a < at.Count ? at[a] : new Vector2(-50, -50), pc = c >= 0 && c < at.Count ? at[c] : new Vector2(-60, -50);
            var d = pc - pa;
            b.AddSegment(pa, d, pc, d, 13.9f, 3.5f, 1, a, c);
        }

        private static RoadNetworkBlob RandomGraph(int seed, int nodes)
        {
            var rng = new Random(seed);
            var b = new RoadNetworkBuilder();
            var at = new List<Vector2>();
            for (int i = 0; i < nodes; i++) { var p = new Vector2(rng.Next(0, 1000), rng.Next(0, 1000)); at.Add(p); b.AddNode(p); }
            int island = nodes - nodes / 10;   // the last tenth is an island, reachable only within itself
            for (int i = 0; i < nodes; i++)
            {
                int links = 1 + rng.Next(3);
                for (int k = 0; k < links; k++)
                {
                    int lo = i < island ? 0 : island, hi = i < island ? island : nodes;
                    int j = lo + rng.Next(hi - lo);
                    if (rng.Next(2) == 0) Straight(b, at, i, j); else Straight(b, at, j, i);   // either direction (D5 walks both)
                    if (rng.Next(20) == 0) Straight(b, at, i, j);                           // a parallel twin
                }
                if (rng.Next(40) == 0) Straight(b, at, i, i);    // a self-loop
                if (rng.Next(60) == 0) Straight(b, at, i, -1);   // a dangling end (no node)
            }
            return b.Build(100f, 12, 12);
        }

        /// <summary>A unit lattice: every edge the same length ⇒ many routes of exactly equal cost (the tie-breaking test).</summary>
        private static RoadNetworkBlob Lattice(int w, int h, float spacing = 10f, float jitter = 0f, int seed = 1)
        {
            var rng = new Random(seed);
            var b = new RoadNetworkBuilder();
            var at = new List<Vector2>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x * spacing + (float)(rng.NextDouble() - 0.5) * jitter, y * spacing + (float)(rng.NextDouble() - 0.5) * jitter);
                    at.Add(p); b.AddNode(p);
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (x + 1 < w) Straight(b, at, i, i + 1);
                    if (y + 1 < h) Straight(b, at, i, i + w);
                }
            return b.Build(spacing * 8f, Math.Max(1, w / 8 + 1), Math.Max(1, h / 8 + 1));
        }

        // ── the former router, verbatim in its search (the oracle) ───────────────────────────────────────────────────────

        private static (bool ok, float metres, List<int> segs, List<bool> fwd) ScanRoute(in RoadNetworkBlob roads, RoadAccess entry, RoadAccess exit)
        {
            var segs = new List<int>(); var fwd = new List<bool>();
            var se = roads.Segments[entry.Segment]; var sx = roads.Segments[exit.Segment];
            float direct = entry.Segment == exit.Segment ? MathF.Abs(exit.Along - entry.Along) : float.MaxValue;
            int n = roads.Nodes.Length;
            var dist = new float[n]; var prevNode = new int[n]; var prevSeg = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prevNode[i] = -1; prevSeg[i] = -1; }
            void Relax(int node, float d, int fromNode, int viaSeg)
            {
                if (node < 0 || node >= n || d >= dist[node]) return;
                dist[node] = d; prevNode[node] = fromNode; prevSeg[node] = fromNode < 0 ? -1 : viaSeg;
            }
            Relax(se.StartNodeIndex, 1f * entry.Along, -1, entry.Segment);
            Relax(se.EndNodeIndex, 1f * (se.Length - entry.Along), -1, entry.Segment);
            while (true)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < float.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0) break;
                done[u] = true;
                for (int s = 0; s < roads.Segments.Length; s++)
                {
                    var seg = roads.Segments[s];
                    int v = seg.StartNodeIndex == u ? seg.EndNodeIndex : seg.EndNodeIndex == u ? seg.StartNodeIndex : -1;
                    if (v < 0 || v >= n || done[v]) continue;
                    Relax(v, dist[u] + 1f * seg.Length, u, s);
                }
            }
            float Reach(int node, float tail) => node >= 0 && node < n && dist[node] < float.MaxValue ? dist[node] + tail : float.MaxValue;
            float viaStart = Reach(sx.StartNodeIndex, 1f * exit.Along), viaEnd = Reach(sx.EndNodeIndex, 1f * (sx.Length - exit.Along));
            float graph = MathF.Min(viaStart, viaEnd);
            if (direct <= graph) return (direct != float.MaxValue, direct, segs, fwd);
            if (graph == float.MaxValue) return (false, float.MaxValue, segs, fwd);
            int last = viaStart <= viaEnd ? sx.StartNodeIndex : sx.EndNodeIndex;
            var hops = new Stack<(int, bool)>();
            for (int v = last; prevSeg[v] >= 0 && prevNode[v] >= 0; v = prevNode[v])
                hops.Push((prevSeg[v], roads.Segments[prevSeg[v]].EndNodeIndex == v));
            while (hops.Count > 0) { var (s, f) = hops.Pop(); segs.Add(s); fwd.Add(f); }
            return (true, graph, segs, fwd);
        }

        private static void AssertSameRoutes(in RoadNetworkBlob roads, RoadGraphRouter router, int seed, int pairs)
        {
            var rng = new Random(seed);
            int routed = 0;
            for (int k = 0; k < pairs; k++)
            {
                var a = RandomAccess(roads, rng); var b = RandomAccess(roads, rng);
                if (!a.IsValid || !b.IsValid) continue;
                bool ok = router.Route(roads, a, b, out float metres);
                var expect = ScanRoute(roads, a, b);
                Assert.Equal(expect.ok, ok);
                if (!ok) continue;
                routed++;
                Assert.Equal(expect.metres, metres);
                Assert.Equal(expect.segs, router.LastSegments);
                Assert.Equal(expect.fwd, router.LastForward);
            }
            Assert.True(routed > pairs / 3, $"too few connected pairs to prove anything: {routed}/{pairs}");
        }

        private static RoadAccess RandomAccess(in RoadNetworkBlob roads, Random rng)
        {
            int s = rng.Next(roads.Segments.Length);
            var seg = roads.Segments[s];
            if (seg.Length <= 0f || seg.StartNodeIndex < 0 || seg.EndNodeIndex < 0) return RoadAccess.None;
            float along = (float)rng.NextDouble() * seg.Length;
            if (rng.Next(5) == 0) along = 0f;   // exactly on a node
            return new RoadAccess(s, along, RoadGraphNavigator.SampleRoadSegment(seg, along).pos, 0f);
        }

        // The former NearestAccess, verbatim: every segment, 33 coarse samples + 9 refining.
        private static RoadAccess ScanNearest(in RoadNetworkBlob roads, Vector2 p, float maxDistance)
        {
            var best = RoadAccess.None;
            for (int s = 0; s < roads.Segments.Length; s++)
            {
                var seg = roads.Segments[s];
                if (seg.Length <= 0f) continue;
                float step = seg.Length / 32;
                float bestAlong = 0f, bestD2 = float.MaxValue;
                Vector2 bestPt = default;
                for (int k = 0; k <= 32; k++)
                {
                    var pt = RoadGraphNavigator.SampleRoadSegment(seg, k * step).pos;
                    float d2 = Vector2.DistanceSquared(p, pt);
                    if (d2 < bestD2) { bestD2 = d2; bestAlong = k * step; bestPt = pt; }
                }
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

        /// <summary>Curved roads: random tangents, so the curve bulges well outside the chord.</summary>
        private static RoadNetworkBlob CurvedGraph(int seed, int nodes)
        {
            var rng = new Random(seed);
            var b = new RoadNetworkBuilder();
            var at = new List<Vector2>();
            for (int i = 0; i < nodes; i++) { var q = new Vector2(rng.Next(-500, 500), rng.Next(-500, 500)); at.Add(q); b.AddNode(q); }
            for (int i = 0; i < nodes; i++)
            {
                int j = rng.Next(nodes);
                var d = at[j] - at[i];
                Vector2 Bend() => d + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * d.Length() * 2f;
                b.AddSegment(at[i], Bend(), at[j], Bend(), 13.9f, 3.5f, 1, i, j);
            }
            return b.Build(100f, 4, 4);
        }

        // ── the rails ─────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void OnRandomGraphs_TheRoutesAreTheFormerScansRoutes_AndANewGraphIsReindexed()
        {
            var router = new RoadGraphRouter();   // ONE router across graphs: its adjacency must follow the graph it is handed
            foreach (var (seed, nodes) in new[] { (1, 300), (2, 300), (3, 600), (4, 120) })
            {
                var g = RandomGraph(seed, nodes);
                try { AssertSameRoutes(g, router, seed * 31, 150); }
                finally { g.Dispose(); }
            }
        }

        [Fact]
        public void OnALattice_EqualCostTies_BreakAsTheScanBrokeThem()
        {
            var router = new RoadGraphRouter();
            var g = Lattice(25, 25);
            try { AssertSameRoutes(g, router, 77, 200); }
            finally { g.Dispose(); }
        }

        [Fact]
        public void NearestAccess_IsTheFormerScansAnswer_OnCurvedAndStraightRoads()
        {
            var rng = new Random(99);
            foreach (var g0 in new[] { CurvedGraph(5, 400), RandomGraph(6, 300), Lattice(30, 30, 20f, jitter: 5f) })
            {
                var g = g0;
                try
                {
                    for (int k = 0; k < 400; k++)
                    {
                        var q = new Vector2(rng.Next(-700, 1300), rng.Next(-700, 1300));
                        float reach = k % 3 == 0 ? float.MaxValue : k % 3 == 1 ? 500f : 15f;
                        var expect = ScanNearest(g, q, reach);
                        var got = RoadGraphRouter.NearestAccess(g, q, reach);
                        Assert.Equal(expect.Segment, got.Segment);
                        Assert.Equal(expect.Along, got.Along);
                        Assert.Equal(expect.Point, got.Point);
                        Assert.Equal(expect.Distance, got.Distance);
                    }
                }
                finally { g.Dispose(); }
            }
        }

        [Fact]
        public void A20kNodeGraph_RoutesWithinBudget_AndAllocatesNothingOnceWarm()
        {
            var g = Lattice(140, 140, 25f, jitter: 6f);   // 19 600 nodes, 38 920 segments — Stechovice's scale and then some
            try
            {
                var router = new RoadGraphRouter();
                var a = new RoadAccess(0, 1f, default, 0f);
                var b = new RoadAccess(g.Segments.Length - 1, 1f, default, 0f);
                Assert.True(router.Route(g, a, b, out _));   // warm: the adjacency, the arrays, the heap, the route lists
                Assert.True(router.Route(g, b, a, out _));

                const int Runs = 10;
                var sw = new Stopwatch();
                int routed = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                sw.Start();
                for (int i = 0; i < Runs; i++)
                    if (router.Route(g, i % 2 == 0 ? a : b, i % 2 == 0 ? b : a, out _)) routed++;
                sw.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                double ms = sw.Elapsed.TotalMilliseconds / Runs;
                _out.WriteLine($"route across {g.Nodes.Length} nodes / {g.Segments.Length} segments: {ms:0.00} ms, {allocated} B over {Runs} routes");

                RoadGraphRouter.NearestAccess(g, new Vector2(10f, 10f), float.MaxValue);   // JIT warm
                var near = Stopwatch.StartNew();
                var acc = RoadGraphRouter.NearestAccess(g, new Vector2(1700f, 1700f), float.MaxValue);
                near.Stop();
                _out.WriteLine($"NearestAccess (pruned): {near.Elapsed.TotalMilliseconds:0.00} ms (segment {acc.Segment})");
                Assert.True(near.Elapsed.TotalMilliseconds < 20.0, $"NearestAccess took {near.Elapsed.TotalMilliseconds:0.0} ms (budget 20 ms; the full scan took ~156 ms)");

                Assert.Equal(Runs, routed);
                Assert.Equal(0, allocated);
                Assert.True(ms < 50.0, $"a corner-to-corner route took {ms:0.0} ms (budget 50 ms; the scan took seconds)");
            }
            finally { g.Dispose(); }
        }
    }
}
