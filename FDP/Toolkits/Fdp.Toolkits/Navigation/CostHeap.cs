using System;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ The open list of every graph search in the engine — a binary min-heap of (cost, node) on a reused array, for a Dijkstra / A* with lazy
    /// decrease-key (push again on a cheaper way; the caller skips a stale entry when it pops). Used by the navmesh searches
    /// (<c>Fdp.Toolkit.Navigation.Recast.CircleSearch</c>, CE-3427) and the road graph (<see cref="RoadGraphRouter"/>, CE-5043).
    /// <para>⭐ Ordered by cost, then by node index — a total order, so what pops first never depends on push order: a search over the same
    /// graph settles its nodes in the same order on every run and thread count, and the road router settles in exactly the order its former
    /// linear scan did (lowest cost, then the lowest node).</para>
    /// <para>0 B once warm: it grows by doubling on a bigger search than any before, never shrinks. ⛔ Not thread-safe: one per search owner.</para>
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §15.
    /// </summary>
    public sealed class CostHeap
    {
        private float[] _cost;
        private int[] _node;
        private int _count;

        public CostHeap(int capacity = 64)
        {
            capacity = Math.Max(1, capacity);
            _cost = new float[capacity];
            _node = new int[capacity];
        }

        public int Count => _count;

        public void Clear() => _count = 0;

        public void Push(float cost, int node)
        {
            if (_count == _cost.Length)
            {
                Array.Resize(ref _cost, _cost.Length * 2);
                Array.Resize(ref _node, _node.Length * 2);
            }
            int i = _count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(cost, node, _cost[p], _node[p])) break;
                _cost[i] = _cost[p]; _node[i] = _node[p];
                i = p;
            }
            _cost[i] = cost; _node[i] = node;
        }

        /// <summary>Removes and returns the cheapest entry (ties: the lowest node). ⛔ Call only while <see cref="Count"/> &gt; 0.</summary>
        public (float Cost, int Node) Pop()
        {
            float topC = _cost[0]; int topN = _node[0];
            int n = --_count;
            float lastC = _cost[n]; int lastN = _node[n];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= n) break;
                int r = l + 1;
                int m = r < n && Less(_cost[r], _node[r], _cost[l], _node[l]) ? r : l;
                if (!Less(_cost[m], _node[m], lastC, lastN)) break;
                _cost[i] = _cost[m]; _node[i] = _node[m];
                i = m;
            }
            if (n > 0) { _cost[i] = lastC; _node[i] = lastN; }
            return (topC, topN);
        }

        private static bool Less(float c1, int n1, float c2, int n2) => c1 < c2 || (c1 == c2 && n1 < n2);
    }
}
