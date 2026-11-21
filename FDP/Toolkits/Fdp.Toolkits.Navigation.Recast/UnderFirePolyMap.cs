using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐⭐ CE-2468 UF-5 (DESIGN_Minefields.md §9b, §9b.1 K4, K8) — the route cost of ONE baked mesh under the places under fire: per side, polyRef → factor
/// <c>1 + 7 × covered share</c> (the polygon's vertices and centre sampled against the side's <see cref="UnderFireSightGrid"/>s — K4: its share, not its centre alone),
/// ⛔ <see cref="UnderFireCause.Direct"/> only (K8). Built by <see cref="UnderFireSight"/> in the SERIAL pre-pass when the table changes — never on a path-query thread —
/// and looked up by <see cref="DoorAwareQueryFilter.GetCost"/> in one dictionary lookup. Registered on the terrain world once per bake (the
/// <see cref="MinefieldPolyMap"/> shape: one per mesh), keyed by the mesh's door-poly map (the token every working copy of the layer's filter shares).
/// </summary>
public sealed class UnderFirePolyMap : IUnderFireCostBuilder
{
    private const int MaxPolyVerts = 6;   // Detour's DT_VERTS_PER_POLYGON (the loop guards a larger polygon anyway)
    private readonly DtNavMesh _mesh;

    public UnderFirePolyMap(DtNavMesh mesh, object meshToken) { _mesh = mesh; MeshToken = meshToken; }

    public object MeshToken { get; }

    public IReadOnlyDictionary<long, float> Build(UnderFireSight sight, byte sidePlus1)
    {
        var map = new Dictionary<long, float>();
        // the side's grid boxes: a polygon outside all of them costs nothing (most of the mesh)
        var boxes = new List<(Vector2 Lo, Vector2 Hi)>();
        for (int i = 0; i < sight.GridCount; i++)
        {
            var (side, g) = sight.Grid(i);
            if (side == sidePlus1) boxes.Add((g.Point - new Vector2(g.RangeM), g.Point + new Vector2(g.RangeM)));
        }
        if (boxes.Count == 0) return map;
        Span<Vector2> samples = stackalloc Vector2[MaxPolyVerts + 1];
        for (int t = 0; t < _mesh.GetMaxTiles(); t++)
        {
            var tile = _mesh.GetTile(t);
            if (tile?.data?.header == null) continue;
            var data = tile.data;
            long baseRef = _mesh.GetPolyRefBase(tile);
            for (int p = 0; p < data.header.polyCount; p++)
            {
                var poly = data.polys[p];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION || poly.vertCount < 3) continue;
                int n = 0;
                var c = Vector2.Zero;
                var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
                for (int j = 0; j < poly.vertCount && n < samples.Length - 1; j++)
                {
                    int v = poly.verts[j] * 3;
                    var q = new Vector2(data.verts[v], data.verts[v + 2]);   // engine x, y = Recast x, z
                    samples[n++] = q; c += q; lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q);
                }
                bool near = false;
                foreach (var (blo, bhi) in boxes) if (hi.X >= blo.X && lo.X <= bhi.X && hi.Y >= blo.Y && lo.Y <= bhi.Y) { near = true; break; }
                if (!near) continue;
                samples[n] = c / n;   // the centre, after the vertices
                n++;
                float share = sight.CoveredShare(sidePlus1, samples[..n]);
                if (share > 0f) map[baseRef | (long)p] = UnderFireSight.FactorOf(share);
            }
        }
        return map;
    }
}
