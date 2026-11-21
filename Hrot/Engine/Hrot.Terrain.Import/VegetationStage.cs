using System.Globalization;
using System.Numerics;
using System.Xml;
using Fdp.Toolkit.Terrain;

namespace Hrot.Terrain.Import;

internal sealed record Wood(IReadOnlyList<(double X, double Y)> Ring, double Density);

internal sealed class VegetationResult
{
    public List<Wood> Woods { get; } = new();
    public List<(double X, double Y)> LoneTrees { get; } = new();
}

/// <summary>
/// ⭐ I-j: woods from <c>woods.geojson</c> POLYGONS with their density, else <c>trees.csv</c> through <see cref="TerrainWoods.Cluster"/> (the
/// importer's one clustering, roads left open); lone trees → tree points; scrub is not imported (no class yet, T-8) — reported.
/// </summary>
internal static class VegetationStage
{
    public static VegetationResult Run(InputSetReader set, double swX, double swY, double width, double height,
                                       IReadOnlyList<TerrainWoods.RoadSegment> roads, ImportReport rep)
    {
        var res = new VegetationResult();
        var trees = set.ReadTrees();
        if (set.Has(InputSetLayout.Woods))
        {
            foreach (var a in set.ReadAreas(InputSetLayout.Woods))
                res.Woods.Add(new Wood(a.Ring.Take(a.Ring.Count - 1).Select(p => (Math.Round(p.X - swX, 3), Math.Round(p.Y - swY, 3))).ToList(), a.Density ?? TerrainWoods.DefaultDensity));
            rep.WoodsPolygons = res.Woods.Count;
            foreach (var (x, y, _) in trees) AddLone(res, x - swX, y - swY, width, height);
        }
        else
        {
            var pts = new List<Vector2>(trees.Count);
            foreach (var (x, y, _) in trees)
            {
                var v = new Vector2((float)(x - swX), (float)(y - swY));
                if (v.X >= 0 && v.Y >= 0 && v.X <= width && v.Y <= height) pts.Add(v);
            }
            var r = TerrainWoods.Cluster(pts.ToArray(), Vector2.Zero, new Vector2((float)width, (float)height), roads);
            foreach (var wr in r.Woods)
                res.Woods.Add(new Wood(new (double, double)[] { (wr.Min.X, wr.Min.Y), (wr.Max.X, wr.Min.Y), (wr.Max.X, wr.Max.Y), (wr.Min.X, wr.Max.Y) }, wr.Density));
            foreach (int i in r.LoneTrees) res.LoneTrees.Add((pts[i].X, pts[i].Y));
            rep.WoodsFromPoints = res.Woods.Count;
        }
        rep.LoneTrees = res.LoneTrees.Count;
        rep.ScrubSkipped = set.Has(InputSetLayout.Scrub) ? set.ReadAreas(InputSetLayout.Scrub).Count : 0;
        if (rep.ScrubSkipped > 0) rep.Warnings.Add($"{rep.ScrubSkipped} scrub polygons not imported (no scrub class yet, UTM §9.6 T-8)");
        return res;
    }

    private static void AddLone(VegetationResult res, double x, double y, double w, double h)
    {
        if (x >= 0 && y >= 0 && x <= w && y <= h) res.LoneTrees.Add((Math.Round(x, 2), Math.Round(y, 2)));
    }
}

/// <summary>The lanes of a SUMO net as road segments (terrain-local, half the lane width) — what the woods leave open.</summary>
internal static class NetRoads
{
    public static List<TerrainWoods.RoadSegment> Read(string netPath, double anchorX, double anchorY)
    {
        var list = new List<TerrainWoods.RoadSegment>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var r = XmlReader.Create(netPath, settings);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Name != "lane") continue;
            string? shape = r.GetAttribute("shape");
            if (shape == null) continue;
            float half = float.TryParse(r.GetAttribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) ? wd / 2f : 1.6f;
            var pts = shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s =>
            {
                var c = s.Split(',');
                return new Vector2((float)(double.Parse(c[0], CultureInfo.InvariantCulture) - anchorX), (float)(double.Parse(c[1], CultureInfo.InvariantCulture) - anchorY));
            }).ToList();
            for (int i = 0; i + 1 < pts.Count; i++) list.Add(new TerrainWoods.RoadSegment(pts[i], pts[i + 1], half));
        }
        return list;
    }
}
