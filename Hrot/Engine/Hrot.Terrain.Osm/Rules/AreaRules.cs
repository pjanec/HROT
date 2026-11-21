using Clipper2Lib;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

/// <summary>A woods, scrub or water polygon as the rules decided it: a hole-free counter-clockwise ring (open) with its kind or density.</summary>
public sealed record AreaDraft(PathD Ring, string? Kind, double? Density)
{
    public double Area => Polygons.Area(Ring);

    public AreaFeature ToFeature()
    {
        var ring = new List<(double, double)>(Ring.Count + 1);
        foreach (var p in Ring) ring.Add((Math.Round(p.x, 2), Math.Round(p.y, 2)));
        ring.Add(ring[0]);
        return new AreaFeature(ring, Kind, Density);
    }
}

public sealed class VegetationResult
{
    public List<AreaDraft> Woods { get; } = new();
    public List<AreaDraft> Scrub { get; } = new();
    /// <summary>Lone <c>natural=tree</c> nodes and the trees of <c>tree_row</c> lines (one every 10 m) — what <c>trees.csv</c> holds.</summary>
    public List<(double X, double Y)> Trees { get; } = new();
    public int LoneTrees { get; set; }
    public int RowTrees { get; set; }
    /// <summary>The tree_row lines (clipped), for the preview.</summary>
    public List<PathD> Rows { get; } = new();
    public double WoodsAreaM2 => Woods.Sum(w => w.Area);
    public double EstimatedWoodsTrees => Woods.Sum(w => w.Area * (w.Density ?? 0) / 10000.0);
    public int FilledHoles { get; set; }
}

/// <summary>⭐ §5.6 / F-3: woods are POLYGONS with a density (never invented tree points); scrub polygons; lone trees and tree rows make trees.csv.</summary>
public static class VegetationRules
{
    public const double DefaultDensityPerHa = 115, RowSpacing = 10, MinPart = 12;

    public static VegetationResult Apply(OsmModel model, AreaBounds bounds)
    {
        var res = new VegetationResult();
        var asm = new GeometryAssembler(model);
        int filled = 0;
        var woods = asm.Areas(t => OsmTags.Is(t, "landuse", "forest") || OsmTags.Is(t, "natural", "wood")).ToList();
        foreach (var part in Polygons.ToParts(woods.SelectMany(s => s.Regions), bounds, MinPart, ref filled))
            res.Woods.Add(new AreaDraft(Polygons.Simplify(part, 0.25), null, DefaultDensityPerHa));
        var scrub = asm.Areas(t => OsmTags.Is(t, "natural", "scrub")).ToList();
        foreach (var part in Polygons.ToParts(scrub.SelectMany(s => s.Regions), bounds, MinPart, ref filled))
            res.Scrub.Add(new AreaDraft(Polygons.Simplify(part, 0.25), "scrub", null));
        res.FilledHoles = filled;

        foreach (var (id, tags) in model.NodeTags.OrderBy(k => k.Key))
        {
            if (!OsmTags.Is(tags, "natural", "tree") || !model.Nodes.TryGetValue(id, out var p) || !bounds.Contains(p.X, p.Y)) continue;
            res.Trees.Add((p.X, p.Y));
            res.LoneTrees++;
        }
        foreach (var line in asm.Lines(t => OsmTags.Is(t, "natural", "tree_row")))
            foreach (var piece in Polygons.ClipLines(line.Points, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY))
            {
                res.Rows.Add(piece);
                double carry = 0;   // distance walked since the last tree; the first tree stands on the line's start
                bool first = true;
                for (int i = 0; i + 1 < piece.Count; i++)
                {
                    double dx = piece[i + 1].x - piece[i].x, dy = piece[i + 1].y - piece[i].y, l = Math.Sqrt(dx * dx + dy * dy);
                    if (l < 1e-9) continue;
                    double at = first ? 0 : RowSpacing - carry;
                    for (; at <= l + 0.05; at += RowSpacing) { res.Trees.Add((piece[i].x + dx / l * at, piece[i].y + dy / l * at)); res.RowTrees++; first = false; }
                    carry = l - (at - RowSpacing);
                    first = false;
                }
            }
        return res;
    }
}

/// <summary>A wall, fence or hedge line (clipped to the bounds) with its OSM <c>barrier</c> value.</summary>
public sealed record WallDraft(PathD Line, string Barrier, double Height, double Thickness, string Kind)
{
    public WallFeature ToFeature() => new(Line.Select(p => (Math.Round(p.x, 2), Math.Round(p.y, 2))).ToList(), Height, Thickness, Kind);
}

/// <summary>⭐ §5.6 / §14.1: <c>barrier=wall|fence|hedge|retaining_wall|city_wall</c> → walls.geojson, the height by type.</summary>
public static class BarrierRules
{
    // barrier → (height, thickness, kind in walls.geojson)
    public static readonly IReadOnlyDictionary<string, (double Height, double Thickness, string Kind)> Types = new Dictionary<string, (double, double, string)>
    {
        ["wall"] = (2.0, 0.4, "stone"),
        ["fence"] = (1.5, 0.1, "fence"),
        ["hedge"] = (1.5, 0.8, "hedge"),
        ["retaining_wall"] = (2.0, 0.5, "stone"),
        ["city_wall"] = (6.0, 2.0, "stone"),
    };

    public static List<WallDraft> Apply(OsmModel model, AreaBounds bounds)
    {
        var list = new List<WallDraft>();
        foreach (var line in new GeometryAssembler(model).Lines(t => t.TryGetValue("barrier", out var b) && Types.ContainsKey(b)))
        {
            string barrier = line.Tags["barrier"];
            var (h, th, kind) = Types[barrier];
            foreach (var piece in Polygons.ClipLines(line.Points, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY))
                if (piece.Count >= 2 && Length(piece) >= 0.5) list.Add(new WallDraft(piece, barrier, h, th, kind));
        }
        return list;
    }

    private static double Length(PathD p)
    {
        double s = 0;
        for (int i = 0; i + 1 < p.Count; i++) s += Math.Sqrt(Math.Pow(p[i + 1].x - p[i].x, 2) + Math.Pow(p[i + 1].y - p[i].y, 2));
        return s;
    }
}

/// <summary>⭐ §5.6 / F-5: water polygons as they are; <c>waterway=river|canal</c> LINES buffered by <c>width</c>, else 10 / 8 m; streams and ditches are no water.</summary>
public static class WaterRules
{
    public const double MinPart = 12;

    public static List<AreaDraft> Apply(OsmModel model, AreaBounds bounds, ref int filledHoles)
    {
        var asm = new GeometryAssembler(model);
        var byKind = new SortedDictionary<string, List<PolyRegion>>(StringComparer.Ordinal);
        void Add(string kind, IEnumerable<PolyRegion> regions)
        {
            if (!byKind.TryGetValue(kind, out var l)) byKind[kind] = l = new();
            l.AddRange(regions);
        }
        foreach (var shape in asm.Areas(t => OsmTags.Is(t, "natural", "water") || OsmTags.Is(t, "waterway", "riverbank") || OsmTags.Is(t, "landuse", "reservoir")))
        {
            string kind = OsmTags.Get(shape.Tags, "water") switch { "river" or "canal" or "stream" => "river", "pond" => "pond", _ => OsmTags.Is(shape.Tags, "waterway", "riverbank") ? "river" : "lake" };
            Add(kind, shape.Regions);
        }
        foreach (var line in asm.Lines(t => OsmTags.Is(t, "waterway", "river") || OsmTags.Is(t, "waterway", "canal")))
        {
            double width = BuildingRules.ParseNumber(OsmTags.Get(line.Tags, "width")) is double w && w > 0 ? w : (line.Tags["waterway"] == "canal" ? 8.0 : 10.0);
            Add("river", Polygons.Buffer(line.Points, width));
        }
        var result = new List<AreaDraft>();
        foreach (var (kind, regions) in byKind)
        {
            // flatten to hole-free rings first so the union is a plain NonZero union of counter-clockwise rings
            var rings = new List<PathD>();
            foreach (var r in regions)
                foreach (var clipped in Polygons.ClipToRect(r, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY))
                    rings.AddRange(Polygons.SplitHoles(clipped, 0.5, ref filledHoles));
            foreach (var part in Polygons.ToParts(Polygons.Union(rings), bounds, MinPart, ref filledHoles))
                result.Add(new AreaDraft(Polygons.Simplify(part, 0.25), kind, null));
        }
        return result;
    }
}
