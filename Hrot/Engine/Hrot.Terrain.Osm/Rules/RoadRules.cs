using Clipper2Lib;

namespace Hrot.Terrain.Osm;

/// <summary>A road or railway as a line in local metres (preview and the road-facing door; the real road net comes from netconvert).</summary>
public sealed record RoadLine(string Class, PathD Points, long WayId, IReadOnlyDictionary<string, string> Tags);

public static class RoadRules
{
    private static readonly HashSet<string> NotRoads = new() { "proposed", "construction", "razed", "abandoned", "platform", "bus_stop", "corridor" };
    private static readonly HashSet<string> Railways = new() { "rail", "light_rail", "tram", "subway", "narrow_gauge", "monorail", "funicular" };

    public static string? ClassOf(IReadOnlyDictionary<string, string> tags)
    {
        if (tags.TryGetValue("highway", out var h) && !NotRoads.Contains(h)) return h;
        if (tags.TryGetValue("railway", out var r) && Railways.Contains(r)) return "rail";
        return null;
    }

    /// <summary>Every road / railway line, clipped to the bounds (a line leaving and re-entering gives two).</summary>
    public static List<RoadLine> PreviewLines(OsmModel model, AreaBounds b)
    {
        var list = new List<RoadLine>();
        foreach (var l in new GeometryAssembler(model).Lines(t => ClassOf(t) != null))
            foreach (var piece in Polygons.ClipLines(l.Points, b.MinX, b.MinY, b.MaxX, b.MaxY))
                if (piece.Count >= 2) list.Add(new RoadLine(ClassOf(l.Tags)!, piece, l.WayId, l.Tags));
        return list;
    }

    public static double LengthMetres(IEnumerable<RoadLine> lines)
    {
        double sum = 0;
        foreach (var l in lines) for (int i = 0; i + 1 < l.Points.Count; i++) sum += Math.Sqrt(Math.Pow(l.Points[i + 1].x - l.Points[i].x, 2) + Math.Pow(l.Points[i + 1].y - l.Points[i].y, 2));
        return sum;
    }

    /// <summary>The highways (unclipped — the margin counts) in a segment index, for "which road is nearest".</summary>
    public static SegmentIndex HighwayIndex(OsmModel model)
    {
        var idx = new SegmentIndex(50);
        foreach (var l in new GeometryAssembler(model).Lines(t => t.TryGetValue("highway", out var h) && !NotRoads.Contains(h)))
            for (int i = 0; i + 1 < l.Points.Count; i++) idx.Add(l.Points[i].x, l.Points[i].y, l.Points[i + 1].x, l.Points[i + 1].y, 0);
        return idx;
    }
}
