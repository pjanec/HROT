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
/// <para>One instance per thread (the provider keeps it <see cref="ThreadStaticAttribute">thread-static</see>): not thread-safe.</para>
/// </summary>
internal sealed class CircleSearch
{
    private const int OffMeshConnection = 1;   // DT_POLYTYPE_OFFMESH_CONNECTION

    public readonly int Capacity;
    public readonly long[] Refs;        // node → polygon ref
    public readonly int[] Parent;       // node → its parent node, −1 at the start
    public readonly float[] Cost;       // node → the best cost found
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
            if (_closed[best] || c > Cost[best]) continue;   // a stale entry (the lazy decrease-key)
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

    private int NewNode(long polyRef, int parent, float cost, RcVec3f pos)
    {
        int n = Count++;
        Refs[n] = polyRef; Parent[n] = parent; Cost[n] = cost; _pos[n] = pos; _closed[n] = false;
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
