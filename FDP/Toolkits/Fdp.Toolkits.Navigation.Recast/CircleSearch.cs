using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐ <c>CE-3427</c> (docs/designs/navig-2/Navigation_Design_v2_0.md §15) — the one-search path costs' Dijkstra, done here instead of
/// DotRecast's <c>FindPolysAroundCircle</c>: the SAME expansion (a polygon's node at the midpoint of the portal it is entered through, costs
/// from the query filter, a polygon whose portal lies beyond the radius not entered) on per-thread arrays that are REUSED, and it STOPS as
/// soon as every goal polygon is settled. 📐 DotRecast's node pool allocates 88 B per node on every query (measured): 69 KB a path-cost batch,
/// ≈ 178 KB a frame of SimHost garbage in the scale-1 fight, at ≈ 2.4 µs a polygon.
/// <para>⭐ It also runs the A* (<see cref="RunAStar"/>) — DotRecast's <c>FindPath</c> rules on the same reused arrays, capped, so neither
/// search allocates once warm (one implementation of the node store, the heap and the portal rule for both).</para>
/// <para>One instance per thread (the provider keeps it <see cref="ThreadStaticAttribute">thread-static</see>): not thread-safe.</para>
/// </summary>
internal sealed class CircleSearch
{
    private const int OffMeshConnection = 1;   // DT_POLYTYPE_OFFMESH_CONNECTION

    public readonly int Capacity;
    public readonly long[] Refs;        // node → polygon ref
    public readonly int[] Parent;       // node → its parent node, −1 at the start
    public readonly float[] Cost;       // node → the best cost found
    private readonly float[] _total;    // node → cost + heuristic (the circle search: = cost)
    private readonly float[] _h;        // node → the heuristic (A*)
    private readonly RcVec3f[] _pos;    // node → where the search entered it (the portal's midpoint; the start's own point)
    private readonly bool[] _closed;
    private readonly Dictionary<long, int> _node;   // polygon ref → node (cleared, never reallocated once grown)
    private (float Cost, int Node)[] _heap;
    private int _heapCount;
    private readonly Dictionary<long, int> _goals = new();
    public int Count;

    public CircleSearch(int capacity)
    {
        Capacity = capacity;
        Refs = new long[capacity];
        Parent = new int[capacity];
        Cost = new float[capacity];
        _total = new float[capacity];
        _h = new float[capacity];
        _pos = new RcVec3f[capacity];
        _closed = new bool[capacity];
        _node = new Dictionary<long, int>(capacity);
        _heap = new (float, int)[capacity];
    }

    /// <summary>The node of <paramref name="polyRef"/>, or −1 when the search never reached it.</summary>
    public int NodeOf(long polyRef) => _node.TryGetValue(polyRef, out int n) ? n : -1;

    /// <summary>True when <paramref name="node"/> was settled (its cost and corridor are final).</summary>
    public bool Settled(int node) => node >= 0 && _closed[node];

    /// <summary>
    /// Expands from <paramref name="startRef"/> at <paramref name="center"/> through every polygon whose entry portal lies within
    /// <paramref name="radius"/>, cheapest first, until each of <paramref name="goals"/> (polygon refs; 0 = none) is settled, the open set
    /// empties, or <see cref="Capacity"/> nodes exist. Returns false when the capacity ran out first.
    /// </summary>
    public bool Run(DtNavMesh nav, long startRef, RcVec3f center, float radius, IDtQueryFilter filter, ReadOnlySpan<long> goals)
    {
        _node.Clear();
        _goals.Clear();
        _heapCount = 0;
        Count = 0;
        int goalsLeft = 0;
        for (int g = 0; g < goals.Length; g++)
            if (goals[g] != 0 && _goals.TryAdd(goals[g], 0)) goalsLeft++;
        float r2 = radius * radius;

        int start = NewNode(startRef, -1, 0f, center);
        Push(0f, start);
        bool full = false;
        while (_heapCount > 0)
        {
            var (c, best) = Pop();
            if (_closed[best] || c > _total[best]) continue;   // a stale entry (the lazy decrease-key)
            _closed[best] = true;
            if (_goals.ContainsKey(Refs[best]) && --goalsLeft == 0) break;

            long bestRef = Refs[best];
            nav.GetTileAndPolyByRefUnsafe(bestRef, out var bestTile, out var bestPoly);
            long parentRef = Parent[best] >= 0 ? Refs[Parent[best]] : 0;
            DtMeshTile? parentTile = null; DtPoly? parentPoly = null;
            if (parentRef != 0) nav.GetTileAndPolyByRefUnsafe(parentRef, out parentTile, out parentPoly);

            for (int i = bestPoly.firstLink; i != DtDetour.DT_NULL_LINK; i = bestTile.links[i].next)
            {
                var link = bestTile.links[i];
                long neighbourRef = link.refs;
                if (neighbourRef == 0 || neighbourRef == parentRef) continue;
                nav.GetTileAndPolyByRefUnsafe(neighbourRef, out var neighbourTile, out var neighbourPoly);
                if (!filter.PassFilter(neighbourRef, neighbourTile, neighbourPoly)) continue;

                Portal(bestTile, bestPoly, link, neighbourTile, neighbourPoly, out var va, out var vb);
                if (DtUtils.DistancePtSegSqr2D(center, va, vb, out _) > r2) continue;   // the portal is beyond the radius

                int nb = _node.TryGetValue(neighbourRef, out int known) ? known : -1;
                if (nb >= 0 && _closed[nb]) continue;
                RcVec3f entry = nb >= 0 ? _pos[nb] : new RcVec3f((va.X + vb.X) * 0.5f, (va.Y + vb.Y) * 0.5f, (va.Z + vb.Z) * 0.5f);
                float total = Cost[best] + filter.GetCost(_pos[best], entry, parentRef, parentTile, parentPoly,
                                                          bestRef, bestTile, bestPoly, neighbourRef, neighbourTile, neighbourPoly);
                if (nb >= 0)
                {
                    if (total >= Cost[nb]) continue;
                    Cost[nb] = total;
                    _total[nb] = total;
                    Parent[nb] = best;
                }
                else
                {
                    if (Count >= Capacity) { full = true; continue; }
                    nb = NewNode(neighbourRef, best, total, entry);
                }
                Push(total, nb);
            }
        }
        return !full;
    }

    /// <summary>A* heuristic scale — Detour's <c>H_SCALE</c> (a hair under 1 so the search stays admissible with the filter's costs).</summary>
    private const float HScale = 0.999f;

    /// <summary>
    /// ⭐ <c>CE-3427</c> — the A* from <paramref name="startRef"/> to <paramref name="endRef"/> by Detour's <c>FindPath</c> rules (a polygon's
    /// node at the midpoint of the portal it is first entered through; reaching the goal adds the cost from there to <paramref name="endPos"/>;
    /// heuristic = straight distance × <see cref="HScale"/>; a closed node reopens on a cheaper way), stopping at the goal or after
    /// <paramref name="maxIterations"/> expansions. Returns the node the corridor ends at — the goal, else the node nearest it (by heuristic,
    /// then cost) — and whether the goal was reached; −1 when nothing was searched.
    /// </summary>
    public int RunAStar(DtNavMesh nav, long startRef, RcVec3f startPos, long endRef, RcVec3f endPos, IDtQueryFilter filter,
                        int maxIterations, out bool reached, out bool capped)
    {
        _node.Clear();
        _heapCount = 0;
        Count = 0;
        reached = false; capped = false;
        float h0 = RcVec3f.Distance(startPos, endPos) * HScale;
        int start = NewNode(startRef, -1, 0f, startPos);
        _h[start] = h0; _total[start] = h0;
        Push(h0, start);
        int lastBest = start;
        int iterations = 0;
        while (_heapCount > 0)
        {
            var (c, best) = Pop();
            if (_closed[best] || c > _total[best]) continue;
            _closed[best] = true;
            if (Refs[best] == endRef) { reached = true; return best; }
            if (++iterations > maxIterations) { capped = true; break; }

            long bestRef = Refs[best];
            nav.GetTileAndPolyByRefUnsafe(bestRef, out var bestTile, out var bestPoly);
            long parentRef = Parent[best] >= 0 ? Refs[Parent[best]] : 0;
            DtMeshTile? parentTile = null; DtPoly? parentPoly = null;
            if (parentRef != 0) nav.GetTileAndPolyByRefUnsafe(parentRef, out parentTile, out parentPoly);

            for (int i = bestPoly.firstLink; i != DtDetour.DT_NULL_LINK; i = bestTile.links[i].next)
            {
                var link = bestTile.links[i];
                long neighbourRef = link.refs;
                if (neighbourRef == 0 || neighbourRef == parentRef) continue;
                nav.GetTileAndPolyByRefUnsafe(neighbourRef, out var neighbourTile, out var neighbourPoly);
                if (!filter.PassFilter(neighbourRef, neighbourTile, neighbourPoly)) continue;

                int nb = _node.TryGetValue(neighbourRef, out int known) ? known : -1;
                RcVec3f entry;
                if (nb >= 0) entry = _pos[nb];
                else
                {
                    Portal(bestTile, bestPoly, link, neighbourTile, neighbourPoly, out var va, out var vb);
                    entry = new RcVec3f((va.X + vb.X) * 0.5f, (va.Y + vb.Y) * 0.5f, (va.Z + vb.Z) * 0.5f);
                }
                float step = filter.GetCost(_pos[best], entry, parentRef, parentTile, parentPoly,
                                            bestRef, bestTile, bestPoly, neighbourRef, neighbourTile, neighbourPoly);
                float cost, heuristic;
                if (neighbourRef == endRef)
                {
                    float endCost = filter.GetCost(entry, endPos, bestRef, bestTile, bestPoly, neighbourRef, neighbourTile, neighbourPoly, 0, null, null);
                    cost = Cost[best] + step + endCost;
                    heuristic = 0f;
                }
                else
                {
                    cost = Cost[best] + step;
                    heuristic = RcVec3f.Distance(entry, endPos) * HScale;
                }
                float total = cost + heuristic;
                if (nb >= 0)
                {
                    if (total >= _total[nb]) continue;   // open or closed: only a cheaper way changes it (a closed node reopens)
                    _closed[nb] = false;
                }
                else
                {
                    if (Count >= Capacity) { capped = true; continue; }
                    nb = NewNode(neighbourRef, best, cost, entry);
                }
                Parent[nb] = best; Cost[nb] = cost; _h[nb] = heuristic; _total[nb] = total;
                Push(total, nb);
                if (heuristic < _h[lastBest] || (heuristic == _h[lastBest] && cost < Cost[lastBest])) lastBest = nb;
            }
        }
        return lastBest;
    }

    private int NewNode(long polyRef, int parent, float cost, RcVec3f pos)
    {
        int n = Count++;
        Refs[n] = polyRef; Parent[n] = parent; Cost[n] = cost; _total[n] = cost; _h[n] = 0f; _pos[n] = pos; _closed[n] = false;
        _node[polyRef] = n;
        return n;
    }

    // ⭐ Detour's GetPortalPoints for a link we already hold: the shared edge, clamped to the link's span at a tile border; an off-mesh
    //   connection's end is a point.
    private static void Portal(DtMeshTile fromTile, DtPoly fromPoly, in DtLink link, DtMeshTile toTile, DtPoly toPoly, out RcVec3f va, out RcVec3f vb)
    {
        var fv = fromTile.data.verts;
        if (fromPoly.GetPolyType() == OffMeshConnection)
        {
            int v = fromPoly.verts[link.edge] * 3;
            va = vb = new RcVec3f(fv[v], fv[v + 1], fv[v + 2]);
            return;
        }
        if (toPoly.GetPolyType() == OffMeshConnection)
        {
            var tv = toTile.data.verts;
            int v = toPoly.verts[0] * 3;
            va = vb = new RcVec3f(tv[v], tv[v + 1], tv[v + 2]);
            return;
        }
        int v0 = fromPoly.verts[link.edge] * 3, v1 = fromPoly.verts[(link.edge + 1) % fromPoly.vertCount] * 3;
        va = new RcVec3f(fv[v0], fv[v0 + 1], fv[v0 + 2]);
        vb = new RcVec3f(fv[v1], fv[v1 + 1], fv[v1 + 2]);
        if (link.side != 0xff && (link.bmin != 0 || link.bmax != 255))
        {
            const float s = 1f / 255f;
            float tmin = link.bmin * s, tmax = link.bmax * s;
            var a = va; var d = vb - va;
            va = new RcVec3f(a.X + d.X * tmin, a.Y + d.Y * tmin, a.Z + d.Z * tmin);
            vb = new RcVec3f(a.X + d.X * tmax, a.Y + d.Y * tmax, a.Z + d.Z * tmax);
        }
    }

    private void Push(float cost, int node)
    {
        if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);   // grows once on a big search, then reused
        int i = _heapCount++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_heap[p].Cost <= cost) break;
            _heap[i] = _heap[p];
            i = p;
        }
        _heap[i] = (cost, node);
    }

    private (float, int) Pop()
    {
        var top = _heap[0];
        var last = _heap[--_heapCount];
        int i = 0;
        while (true)
        {
            int l = 2 * i + 1;
            if (l >= _heapCount) break;
            int r = l + 1;
            int m = r < _heapCount && _heap[r].Cost < _heap[l].Cost ? r : l;
            if (_heap[m].Cost >= last.Cost) break;
            _heap[i] = _heap[m];
            i = m;
        }
        if (_heapCount > 0) _heap[i] = last;
        return top;
    }
}
