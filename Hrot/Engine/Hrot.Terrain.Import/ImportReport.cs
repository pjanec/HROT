namespace Hrot.Terrain.Import;

/// <summary>What an import did — counts, warnings and the seconds each stage took (the CLI prints it; tests assert on it).</summary>
public sealed class ImportReport
{
    public string TerrainName { get; set; } = "";
    public Dictionary<string, double> StageSeconds { get; } = new();
    public List<string> Warnings { get; } = new();

    public int HeightCols { get; set; }
    public int HeightRows { get; set; }
    public long HeightPostsFilled { get; set; }

    public int BuildingsEnterable { get; set; }
    /// <summary>Solid prisms: not enterable in the input (R-4193) plus the ones whose stairs did not fit.</summary>
    public int BuildingsSolid { get; set; }
    public int SolidBecauseNoStair { get; set; }
    public int RoofCaps { get; set; }
    public int SharedWallsMerged { get; set; }

    public int OpeningsPlaced { get; set; }
    public int OpeningsDroppedOffWall { get; set; }
    public int OpeningsDroppedOnPartyWall { get; set; }
    public int OpeningsDroppedOverlap { get; set; }
    public int OpeningsDroppedOther { get; set; }
    public int OpeningsDropped => OpeningsDroppedOffWall + OpeningsDroppedOnPartyWall + OpeningsDroppedOverlap + OpeningsDroppedOther;
    public int Stairs { get; set; }

    public int WoodsPolygons { get; set; }
    public int WoodsFromPoints { get; set; }
    public int LoneTrees { get; set; }
    public int WaterAreas { get; set; }
    public int Walls { get; set; }
    public int ScrubSkipped { get; set; }
    public int UnassignedSkipped { get; set; }

    public long OutputBytes { get; set; }
    public double TotalSeconds => StageSeconds.Values.Sum();
}

public static class ImportReportText
{
    public static void WriteTo(this ImportReport r, TextWriter w, int maxWarnings = 10)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        w.WriteLine(string.Create(inv, $"terrain '{r.TerrainName}': heights {r.HeightCols} x {r.HeightRows} posts at 1 m ({r.HeightPostsFilled} NODATA filled)"));
        w.WriteLine(string.Create(inv, $"buildings: {r.BuildingsEnterable} enterable, {r.BuildingsSolid} solid ({r.SolidBecauseNoStair} for want of a stair), {r.RoofCaps} roof caps, {r.SharedWallsMerged} shared walls merged, {r.Stairs} stairs"));
        w.WriteLine(string.Create(inv, $"openings: {r.OpeningsPlaced} placed, {r.OpeningsDropped} dropped (off wall {r.OpeningsDroppedOffWall}, party wall {r.OpeningsDroppedOnPartyWall}, overlap {r.OpeningsDroppedOverlap}, other {r.OpeningsDroppedOther}); {r.UnassignedSkipped} unassigned points ignored"));
        w.WriteLine(string.Create(inv, $"vegetation: {r.WoodsPolygons} wood polygons + {r.WoodsFromPoints} clustered from points, {r.LoneTrees} lone trees, {r.WaterAreas} water areas, {r.Walls} walls, {r.ScrubSkipped} scrub skipped"));
        string stages = string.Join(", ", r.StageSeconds.Select(kv => string.Create(inv, $"{kv.Key} {kv.Value:0.0}")));
        w.WriteLine(string.Create(inv, $"output {r.OutputBytes / 1e6:0.0} MB; seconds per stage: {stages} (total {r.TotalSeconds:0.0})"));
        foreach (var warn in r.Warnings.Take(maxWarnings)) w.WriteLine("  - " + warn);
        if (r.Warnings.Count > maxWarnings) w.WriteLine($"  … and {r.Warnings.Count - maxWarnings} more warnings");
    }
}
