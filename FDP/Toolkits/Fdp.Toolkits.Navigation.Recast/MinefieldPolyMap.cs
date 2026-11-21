using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>One navmesh polygon of a minefield: its field (an index into <see cref="MinefieldPolyMap.Fields"/>), the lane it lies in (−1 = the field proper), and the knowledge cells it covers.</summary>
public sealed class MinePoly
{
    public int Field;
    public int Lane = -1;
    /// <summary>The knowledge cells the polygon covers, each <c>row * <see cref="MinefieldKnowledge.MaxCols"/> + col</c> (precomputed once at the bake — KR-14).</summary>
    public int[] Cells = Array.Empty<int>();
}

/// <summary>
/// ⭐⭐ <c>CE-4624</c> MS-T3 (DESIGN_Minefields.md MR-3, MR-20, KR-10, KR-14) — the polygon → (field, the knowledge cells it covers, its lane) map of ONE baked mesh, built after the bake (the
/// <c>DoorPolys</c> precedent), read-only afterwards, and the price of a polygon for ONE side (<see cref="FactorFor"/>): the side's WORST cell over the polygon — known-mined ×<see cref="KnownMinedCost"/>,
/// suspected ×<see cref="SuspectedCost"/>, unknown / known-clear ×1 (KR-10: priced, never closed). A LANE polygon is open (×1) to the side that cleared it and to the sides its known-to mask names, priced
/// as the field for every other. 0 B: dictionary lookups and index loops over arrays.
/// </summary>
public sealed class MinefieldPolyMap
{
    public static readonly MinefieldPolyMap Empty = new(Array.Empty<TerrainMinefield>(), new Dictionary<long, MinePoly>());

    /// <summary>What a polygon the side knows to be mined costs, as a factor on its length — a detour of a few hundred metres beats a crossing (tuning).</summary>
    public const float KnownMinedCost = 200f;
    /// <summary>What a polygon the side suspects costs (tuning).</summary>
    public const float SuspectedCost = 20f;

    public IReadOnlyList<TerrainMinefield> Fields { get; }
    private readonly Dictionary<long, MinePoly> _polys;

    public MinefieldPolyMap(IReadOnlyList<TerrainMinefield> fields, Dictionary<long, MinePoly> polys) { Fields = fields; _polys = polys; }

    public int Count => _polys.Count;
    public int LanePolyCount { get { int n = 0; foreach (var p in _polys.Values) if (p.Lane >= 0) n++; return n; } }
    public bool TryGet(long polyRef, out MinePoly poly) => _polys.TryGetValue(polyRef, out poly!);

    /// <summary>The cost factor of <paramref name="p"/> for side <paramref name="sidePlus1"/> − 1 under <paramref name="knowledge"/> (1 = plain ground). Side 0 ⇒ no knowledge applied.</summary>
    public float FactorFor(MinePoly p, PlaceKnowledgeTable knowledge, byte sidePlus1)
    {
        if (sidePlus1 == 0) return 1f;
        int side = sidePlus1 - 1;
        var f = Fields[p.Field];
        if (p.Lane >= 0)
        {
            var l = f.Lanes[p.Lane];
            if (l.ClearedBySidePlus1 == sidePlus1 || (side < 16 && (l.KnownToSides & (1 << side)) != 0)) return 1f;
        }
        bool suspected = false;
        var cells = p.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            var k = knowledge.Knows(side, f.Field, cells[i] % MinefieldKnowledge.MaxCols, cells[i] / MinefieldKnowledge.MaxCols);
            if (k == MineCellKnowledge.KnownMined) return KnownMinedCost;
            if (k == MineCellKnowledge.Suspected) suspected = true;
        }
        return suspected ? SuspectedCost : 1f;
    }
}
