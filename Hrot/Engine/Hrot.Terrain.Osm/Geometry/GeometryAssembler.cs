using Clipper2Lib;

namespace Hrot.Terrain.Osm;

/// <summary>A tagged area of OSM — a closed way or a multipolygon relation — as polygons in local metres. <see cref="Id"/> is <c>w&lt;id&gt;</c> or <c>r&lt;id&gt;</c>.</summary>
public sealed record AreaShape(string Id, IReadOnlyDictionary<string, string> Tags, IReadOnlyList<PolyRegion> Regions);

/// <summary>A tagged way as a line in local metres (open or closed), with its node ids.</summary>
public sealed record LineShape(long WayId, IReadOnlyDictionary<string, string> Tags, PathD Points, long[] NodeIds);

/// <summary>⭐ Closed ways + multipolygon relations → polygons in local metres (rings joined end to end, inner rings matched to their outer ring).</summary>
public sealed class GeometryAssembler
{
    private readonly OsmModel _m;
    /// <summary>Multipolygons skipped because a ring did not close, or a node was missing from the extract.</summary>
    public int BrokenRings { get; private set; }

    public GeometryAssembler(OsmModel model) => _m = model;

    public IEnumerable<LineShape> Lines(Func<IReadOnlyDictionary<string, string>, bool> accept)
    {
        foreach (var w in _m.Ways)
        {
            if (!accept(w.Tags)) continue;
            var pts = new PathD();
            var ids = new List<long>();
            foreach (var n in w.Nodes)
                if (_m.Nodes.TryGetValue(n, out var p)) { pts.Add(new PointD(p.X, p.Y)); ids.Add(n); }
            if (pts.Count >= 2) yield return new LineShape(w.Id, w.Tags, pts, ids.ToArray());
        }
    }

    public IEnumerable<AreaShape> Areas(Func<IReadOnlyDictionary<string, string>, bool> accept)
    {
        var relationOuterWays = new HashSet<long>();
        var fromRelations = new List<AreaShape>();
        foreach (var rel in _m.Relations)
        {
            if (!accept(rel.Tags)) continue;
            if (!OsmTags.Is(rel.Tags, "type", "multipolygon") && !OsmTags.Is(rel.Tags, "type", "boundary")) continue;
            var shape = Multipolygon(rel);
            if (shape == null) continue;
            foreach (var mem in rel.Members) if (mem.Type == 'w') relationOuterWays.Add(mem.Ref);
            fromRelations.Add(shape);
        }
        foreach (var w in _m.Ways)
        {
            if (!w.IsClosed || !accept(w.Tags) || relationOuterWays.Contains(w.Id)) continue;
            var ring = Ring(w.Nodes);
            if (ring == null) continue;
            yield return new AreaShape("w" + w.Id, w.Tags, new[] { ring });
        }
        foreach (var s in fromRelations) yield return s;
    }

    private PolyRegion? Ring(long[] nodeIds)
    {
        var pts = new List<PointD>(nodeIds.Length);
        var ids = new List<long>(nodeIds.Length);
        foreach (var n in nodeIds)
        {
            if (!_m.Nodes.TryGetValue(n, out var p)) { BrokenRings++; return null; }
            pts.Add(new PointD(p.X, p.Y));
            ids.Add(n);
        }
        var (ring, cleanIds) = Polygons.Clean(pts, ids.ToArray());
        if (ring.Count < 3 || Polygons.Area(ring) < 1e-6) return null;
        if (Polygons.SignedArea(ring) < 0) { ring.Reverse(); Array.Reverse(cleanIds!); }
        return new PolyRegion(ring, cleanIds);
    }

    private AreaShape? Multipolygon(OsmRelation rel)
    {
        var outerWays = new List<long[]>();
        var innerWays = new List<long[]>();
        foreach (var mem in rel.Members)
        {
            if (mem.Type != 'w' || !_m.WayById.TryGetValue(mem.Ref, out var way)) continue;
            if (mem.Role == "inner") innerWays.Add(way.Nodes); else if (mem.Role == "outer" || mem.Role == "") outerWays.Add(way.Nodes);
        }
        var outers = JoinRings(outerWays);
        var inners = JoinRings(innerWays);
        var regions = new List<PolyRegion>();
        foreach (var o in outers) if (Ring(o) is PolyRegion r) regions.Add(r);
        if (regions.Count == 0) return null;
        foreach (var i in inners)
        {
            var hole = Ring(i);
            if (hole == null) continue;
            foreach (var r in regions)
                if (Polygons.Contains(r.Outer, hole.Outer[0].x, hole.Outer[0].y)) { r.Holes.Add(Polygons.Cw(hole.Outer)); break; }
        }
        // a region with holes no longer follows its node ids point for point only if the outer ring is untouched — it is, holes are separate
        return new AreaShape("r" + rel.Id, rel.Tags, regions);
    }

    /// <summary>Joins ways end to end into closed rings (a way may have to be reversed); rings that do not close are counted in <see cref="BrokenRings"/>.</summary>
    public List<long[]> JoinRings(IReadOnlyList<long[]> ways)
    {
        var rings = new List<long[]>();
        var used = new bool[ways.Count];
        var byEnd = new Dictionary<long, List<int>>();
        for (int i = 0; i < ways.Count; i++)
        {
            if (ways[i].Length < 2) { used[i] = true; continue; }
            foreach (var e in new[] { ways[i][0], ways[i][^1] })
            {
                if (!byEnd.TryGetValue(e, out var l)) byEnd[e] = l = new List<int>();
                if (!l.Contains(i)) l.Add(i);
            }
        }
        for (int s = 0; s < ways.Count; s++)
        {
            if (used[s]) continue;
            used[s] = true;
            var chain = new List<long>(ways[s]);
            while (chain[0] != chain[^1])
            {
                int next = -1;
                if (byEnd.TryGetValue(chain[^1], out var cands))
                    foreach (var c in cands) if (!used[c]) { next = c; break; }
                if (next < 0) break;
                used[next] = true;
                var w = ways[next];
                if (w[0] == chain[^1]) for (int k = 1; k < w.Length; k++) chain.Add(w[k]);
                else for (int k = w.Length - 2; k >= 0; k--) chain.Add(w[k]);
            }
            if (chain.Count >= 4 && chain[0] == chain[^1]) rings.Add(chain.ToArray()); else BrokenRings++;
        }
        return rings;
    }
}
