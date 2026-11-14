using System.Globalization;
using System.Numerics;
using System.Xml;
using Fdp.Toolkit.Terrain;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐⭐ <c>CE-4403</c> (P-1 part 2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4b G-21…G-25) — the importer's road corridors: every non-internal EDGE of the
/// SUMO net becomes a <see cref="RoadCorridor"/> (its reference line — the mean of its outer lanes' shapes, else its first lane —, half its lanes' width + the
/// shoulder, its junctions' ground heights at both ends), every junction outline a flat junction; <see cref="RoadCorridors.Level"/> levels them into the posts.
/// Terrain-local = net − anchor.
/// </summary>
internal static class Corridors
{
    public static bool[] Apply(float[] z, int cols, int rows, string netPath, double ax, double ay, IReadOnlyList<BuiltBuilding> built, ImportReport rep)
    {
        var natural = z;   // read before levelling: the junction heights are the natural ground there
        float Ground(Vector2 p)
        {
            int c = Math.Clamp((int)MathF.Round(p.X), 0, cols - 1), r = Math.Clamp((int)MathF.Round(p.Y), 0, rows - 1);
            return natural[(long)r * cols + c];
        }
        var (edges, junctions) = Read(netPath, ax, ay);
        var junctionAt = junctions.ToDictionary(j => j.Id, j => j.At);
        var corridors = new List<RoadCorridor>(edges.Count);
        foreach (var e in edges)
        {
            if (e.Lanes.Count == 0) continue;
            var first = e.Lanes[0].Shape; var last = e.Lanes[^1].Shape;
            Vector2[] line = first.Length == last.Length ? first.Select((p, i) => (p + last[i]) * 0.5f).ToArray() : first;
            if (line.Length < 2) continue;
            float half = e.Lanes.Sum(l => l.Width) / 2f + RoadCorridors.Shoulder;
            float z0 = Ground(junctionAt.TryGetValue(e.From, out var a) ? a : line[0]);
            float z1 = Ground(junctionAt.TryGetValue(e.To, out var b) ? b : line[^1]);
            corridors.Add(new RoadCorridor(line, half, z0, z1));
        }
        var flat = junctions.Where(j => j.Shape.Length >= 3).Select(j => (j.Shape, Ground(j.At))).ToList();
        var houses = built.Where(b => b.Ring.Count >= 3).Select(b => b.Ring.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray()).ToList();
        var cr = new CorridorReport();
        // levelled IN PLACE: copy first so the junction heights above keep reading the natural ground
        var copy = (float[])z.Clone();
        natural = copy;
        var locked = RoadCorridors.Level(z, cols, rows, Vector2.Zero, 1f, corridors, flat, houses, cr);
        rep.RoadCorridors = cr.Corridors; rep.RoadJunctions = cr.Junctions; rep.RoadPostsLevelled = cr.PostsLevelled;
        rep.RoadPostsKeptNatural = cr.PostsKeptNatural; rep.RoadPostsUnderHouses = cr.PostsUnderHouses;
        if (cr.PostsUnderHouses > 0)
            rep.Warnings.Add($"{cr.PostsUnderHouses} road posts lie inside a house footprint — the road keeps its natural cross-slope there (information, D-3)");
        return locked;
    }

    internal sealed record Lane(Vector2[] Shape, float Width);
    internal sealed record Edge(string Id, string From, string To, List<Lane> Lanes);
    internal sealed record Junction(string Id, Vector2 At, Vector2[] Shape);

    internal static (List<Edge> Edges, List<Junction> Junctions) Read(string netPath, double ax, double ay)
    {
        var edges = new List<Edge>(); var junctions = new List<Junction>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var r = XmlReader.Create(netPath, settings);
        Edge? current = null;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            switch (r.Name)
            {
                case "edge":
                    current = null;
                    if (r.GetAttribute("function") is { } fn && fn != "normal") break;   // internal, walkingarea, crossing
                    current = new Edge(r.GetAttribute("id") ?? "", r.GetAttribute("from") ?? "", r.GetAttribute("to") ?? "", new List<Lane>());
                    edges.Add(current);
                    break;
                case "lane":
                    if (current == null) break;
                    if (r.GetAttribute("shape") is not { } shape) break;
                    float w = float.TryParse(r.GetAttribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) ? wd : 3.2f;
                    current.Lanes.Add(new Lane(Points(shape, ax, ay), w));
                    break;
                case "junction":
                    current = null;
                    if (r.GetAttribute("type") == "internal") break;
                    var at = new Vector2((float)(D(r.GetAttribute("x")) - ax), (float)(D(r.GetAttribute("y")) - ay));
                    junctions.Add(new Junction(r.GetAttribute("id") ?? "", at, r.GetAttribute("shape") is { } js ? Points(js, ax, ay) : Array.Empty<Vector2>()));
                    break;
            }
        }
        return (edges, junctions);
    }

    private static Vector2[] Points(string shape, double ax, double ay)
        => shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s =>
        {
            var c = s.Split(',');
            return new Vector2((float)(D(c[0]) - ax), (float)(D(c[1]) - ay));
        }).ToArray();

    private static double D(string? s) => double.Parse(s ?? "0", CultureInfo.InvariantCulture);
}
