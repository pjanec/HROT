using System;
using CarKinem.Road;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ <c>CE-5045</c> — the road graph's derived topology, built ONCE per graph and shared by everything that walks it: each node's incident segments
    /// (CSR: node u's are <see cref="AdjSeg"/>[<see cref="AdjStart"/>[u] .. <see cref="AdjStart"/>[u + 1]), in ascending segment order; a segment whose
    /// two ends are the same node is listed once) and each node's junction radius (the widest incident band, D7). Its readers: the road router's
    /// Dijkstra (<see cref="RoadGraphRouter"/>, CE-5043) and the danger classifier's junction test (<c>DangerAlongRouteClassifier</c>), which both used to
    /// derive it by scanning every segment — the classifier per node per route point, 19 257 × 19 691 on Stechovice, which held the EQS commit for minutes.
    /// <para>⭐ Per THREAD (<see cref="ThreadStaticAttribute"/>): a solver's worker reads it lock-free and no thread writes another's (the parallel rule:
    /// no shared mutable static). Rebuilt only when handed a different graph — its arrays' addresses or lengths differ (a terrain load publishes a new
    /// blob, <c>TerrainResidency</c>). 0 B once built for a graph.</para>
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §15.
    /// </summary>
    public sealed class RoadGraphIndex
    {
        /// <summary>Node u's incident segments start at <c>AdjSeg[AdjStart[u]]</c>; length nodes + 1.</summary>
        public int[] AdjStart { get; private set; } = Array.Empty<int>();
        /// <summary>The incident segment indices, grouped by node.</summary>
        public int[] AdjSeg { get; private set; } = Array.Empty<int>();
        /// <summary>Per node: the widest band (half the carriageway) among its incident segments — a junction's radius (D7).</summary>
        public float[] JunctionRadius { get; private set; } = Array.Empty<float>();

        private IntPtr _nodes, _segments;
        private int _n = -1, _m = -1;

        [ThreadStatic] private static RoadGraphIndex? t_index;

        /// <summary>The index of <paramref name="roads"/> on this thread (built on the first call for a graph). ⛔ The graph must have roads.</summary>
        public static RoadGraphIndex For(in RoadNetworkBlob roads) => (t_index ??= new RoadGraphIndex()).Ensure(roads);

        /// <summary>How many segments meet at node <paramref name="node"/>.</summary>
        public int Degree(int node) => AdjStart[node + 1] - AdjStart[node];

        private unsafe RoadGraphIndex Ensure(in RoadNetworkBlob roads)
        {
            int n = roads.Nodes.Length, m = roads.Segments.Length;
            var nodesAt = n > 0 ? (IntPtr)System.Runtime.CompilerServices.Unsafe.AsPointer(ref roads.Nodes[0]) : IntPtr.Zero;
            var segsAt = m > 0 ? (IntPtr)System.Runtime.CompilerServices.Unsafe.AsPointer(ref roads.Segments[0]) : IntPtr.Zero;
            if (n == _n && m == _m && nodesAt == _nodes && segsAt == _segments) return this;

            var start = AdjStart.Length >= n + 1 ? AdjStart : new int[n + 1];
            var seg = AdjSeg.Length >= 2 * m ? AdjSeg : new int[Math.Max(1, 2 * m)];
            var radius = JunctionRadius.Length >= n ? JunctionRadius : new float[n];
            Array.Clear(start, 0, n + 1);
            Array.Clear(radius, 0, n);
            for (int s = 0; s < m; s++)
            {
                var r = roads.Segments[s];
                int a = r.StartNodeIndex, b = r.EndNodeIndex;
                float half = 0.5f * r.LaneWidth * Math.Max(1, r.LaneCount);
                if (a >= 0 && a < n) { start[a + 1]++; radius[a] = MathF.Max(radius[a], half); }
                if (b >= 0 && b < n && b != a) { start[b + 1]++; radius[b] = MathF.Max(radius[b], half); }
            }
            for (int i = 0; i < n; i++) start[i + 1] += start[i];
            var cursor = new int[n];   // once per graph
            for (int i = 0; i < n; i++) cursor[i] = start[i];
            for (int s = 0; s < m; s++)
            {
                var r = roads.Segments[s];
                int a = r.StartNodeIndex, b = r.EndNodeIndex;
                if (a >= 0 && a < n) seg[cursor[a]++] = s;
                if (b >= 0 && b < n && b != a) seg[cursor[b]++] = s;
            }
            AdjStart = start; AdjSeg = seg; JunctionRadius = radius;
            _n = n; _m = m; _nodes = nodesAt; _segments = segsAt;
            return this;
        }
    }
}
