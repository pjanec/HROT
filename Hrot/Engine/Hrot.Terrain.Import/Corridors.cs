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
        int offGround = 0;
        // ⭐⭐ CE-4694 ② — a TWO-WAY road is two SUMO edges (one per direction, between the same junctions): they become ONE corridor along their midline, so one profile
        //   levels the whole road. 📐 Two corridors on a cross-slope were levelled to two profiles and "the nearest road wins" left a cliff down the road's middle
        //   (0.6 m on Stechovice's west-bank road — a vehicle climbs 0.2 m). Paired only when the carriageways are ADJACENT (a divided highway's median is never levelled).
        var paired = PairTwoWay(edges);
        foreach (var e in edges)
        {
            if (e.Lanes.Count == 0) continue;
            if (e.OffGround) { offGround++; continue; }   // ⭐ V-15 (G-22) — a bridge or a tunnel levels nothing
            Vector2[] line = ReferenceLine(e);
            if (line.Length < 2) continue;
            float half = e.Lanes.Sum(l => l.Width) / 2f + RoadCorridors.Shoulder;
            if (paired.TryGetValue(e, out var other))
            {
                if (other == null) continue;   // the second edge of a pair: the first one's corridor covers it
                (line, half) = Merged(e, other);
            }
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
        if (offGround > 0)
            rep.Warnings.Add($"{offGround} road edges are a bridge or a tunnel (OSM bridge / tunnel / layer) — no corridor levelled under them (information, G-22)");
        if (cr.PostsUnderHouses > 0)
            rep.Warnings.Add($"{cr.PostsUnderHouses} road posts lie inside a house footprint — the road keeps its natural cross-slope there (information, D-3)");
        return locked;
    }

    /// <summary>An edge's reference line: the mean of its outer lanes' shapes (same point count), else its first lane.</summary>
    internal static Vector2[] ReferenceLine(Edge e)
    {
        var first = e.Lanes[0].Shape; var last = e.Lanes[^1].Shape;
        return first.Length == last.Length ? first.Select((p, i) => (p + last[i]) * 0.5f).ToArray() : first;
    }

    /// <summary>
    /// ⭐ CE-4694 — the two directions of a two-way road: per edge, its opposite (same junctions reversed, on the ground) when their reference lines run ADJACENT — the mean
    /// distance between them at most half their widths + 1 m —; the first edge of a pair maps to the second, the second to null. Pure.
    /// </summary>
    internal static Dictionary<Edge, Edge?> PairTwoWay(List<Edge> edges, Func<Edge, bool>? eligible = null)
    {
        eligible ??= e => !e.OffGround;   // the corridors pair the GROUND roads; ⭐ CE-5057 — the bridge stage pairs its bridged edges through the same rule
        var result = new Dictionary<Edge, Edge?>(ReferenceEqualityComparer.Instance);
        var byEnds = new Dictionary<(string, string), Edge>();
        foreach (var e in edges)
        {
            if (e.Lanes.Count == 0 || !eligible(e) || e.From.Length == 0 || e.To.Length == 0 || e.From == e.To) continue;
            if (byEnds.TryGetValue((e.To, e.From), out var o) && !result.ContainsKey(o))
            {
                var la = ReferenceLine(o); var lb = ReferenceLine(e);
                if (la.Length < 2 || lb.Length < 2) continue;
                float gap = MeanDistance(la, lb), widths = (o.Lanes.Sum(l => l.Width) + e.Lanes.Sum(l => l.Width)) / 2f;
                if (gap > widths + 1f) continue;   // a divided road: two corridors
                result[o] = e; result[e] = null;
            }
            else byEnds.TryAdd((e.From, e.To), e);
        }
        return result;
    }

    /// <summary>The corridor of a pair: its midline (each of <paramref name="a"/>'s points halfway to <paramref name="b"/>'s line) and a half-width covering both carriageways + the shoulder.</summary>
    internal static (Vector2[] Line, float Half) Merged(Edge a, Edge b)
    {
        var la = ReferenceLine(a); var lb = ReferenceLine(b);
        var mid = new Vector2[la.Length];
        for (int i = 0; i < la.Length; i++) mid[i] = (la[i] + Closest(lb, la[i])) * 0.5f;
        float gap = MeanDistance(la, lb);
        float half = gap / 2f + (a.Lanes.Sum(l => l.Width) + b.Lanes.Sum(l => l.Width)) / 4f + RoadCorridors.Shoulder;
        return (mid, half);
    }

    private static float MeanDistance(Vector2[] a, Vector2[] b)
    {
        float sum = 0f;
        foreach (var p in a) sum += Vector2.Distance(p, Closest(b, p));
        return sum / a.Length;
    }

    private static Vector2 Closest(Vector2[] line, Vector2 p)
    {
        var best = line[0]; float bd = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var d = line[i + 1] - line[i]; float l2 = d.LengthSquared();
            float t = l2 > 0f ? Math.Clamp(Vector2.Dot(p - line[i], d) / l2, 0f, 1f) : 0f;
            var q = line[i] + d * t; float dd = Vector2.DistanceSquared(p, q);
            if (dd < bd) { bd = dd; best = q; }
        }
        return best;
    }

    internal sealed record Lane(Vector2[] Shape, float Width);
    internal sealed record Edge(string Id, string From, string To, List<Lane> Lanes)
    {
        /// <summary>⭐ V-15 — a bridge or a tunnel (the OSM tag kept by netconvert's <c>--osm.extra-attributes</c>), or any edge on another LAYER than the ground's.</summary>
        public bool OffGround { get; set; }
        /// <summary>⭐ CE-5057 / CE-11063 (DESIGN_Terrain_OSM_Bridges D-1) — OSM <c>bridge</c> says yes.</summary>
        public bool Bridge { get; set; }
        /// <summary>⭐ CE-5057 — OSM <c>tunnel</c> says yes (never a deck).</summary>
        public bool Tunnel { get; set; }
        /// <summary>⭐ CE-5057 — OSM <c>layer</c> (0 when absent).</summary>
        public int Layer { get; set; }
    }

    /// <summary>⭐ V-15 — whether an OSM tag value says yes (<c>bridge=yes</c>, <c>=viaduct</c>, <c>tunnel=culvert</c> …): anything but empty / <c>no</c>.</summary>
    internal static bool Says(string? value) => !string.IsNullOrEmpty(value) && value != "no";
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
                case "param":
                    // ⭐ V-15 — the edge's kept OSM tags (after its lanes)
                    if (current == null) break;
                    string? key = r.GetAttribute("key"), value = r.GetAttribute("value");
                    if ((key is "bridge" or "tunnel" && Says(value)) || (key == "layer" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int layer) && layer != 0))
                        current.OffGround = true;
                    if (key == "bridge" && Says(value)) current.Bridge = true;   // ⭐ CE-5057 — kept apart: only a bridge gets a deck
                    if (key == "tunnel" && Says(value)) current.Tunnel = true;
                    if (key == "layer" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lv)) current.Layer = lv;
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
