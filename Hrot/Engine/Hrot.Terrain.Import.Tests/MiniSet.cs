using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Import.Tests;

/// <summary>A tiny hand-made input set (written through the shared writers) for single-rule rails.</summary>
public sealed class MiniSet
{
    public double MinX = -100, MinY = -100, MaxX = 100, MaxY = 100;
    public double AnchorX, AnchorY;
    public bool Net;
    /// <summary>⭐ CE-4403 — a net of the rail's own (written when <see cref="Net"/> is set), in input coordinates; null = the one-lane default.</summary>
    public string? NetXml;
    public Func<double, double, float> Ground = (x, y) => (float)(10 + 0.01 * x);
    public List<Building> Buildings = new();
    public List<(double X, double Y, double Z)> Trees = new();
    public List<WallFeature> Walls = new();
    public List<AreaFeature>? Woods, Water, Scrub;
    public List<Opening> Unassigned = new();

    /// <summary>A rectangle building, counter-clockwise and closed; storeys of 3 m on <paramref name="baseZ"/>.</summary>
    public static Building Rect(string id, double x0, double y0, double x1, double y1, int storeys = 1, bool enterable = true, string roof = "flat",
                                double? ridgeRise = null, double baseZ = 10, IReadOnlyList<Opening>? openings = null, string? material = null)
    {
        var ring = new List<(double, double)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0) };
        var sz = Enumerable.Range(0, storeys).Select(i => baseZ + 3.0 * i).ToList();
        double height = 3.0 * storeys;
        return new Building(id, ring, baseZ, height, roof, roof == "pitched" ? baseZ + height + (ridgeRise ?? 2.5) : null, sz, openings ?? new List<Opening>(), material, enterable);
    }

    public string Write(string dir)
    {
        Directory.CreateDirectory(dir);
        var frame = new UtmFrame(33, false, 500000, 5430000);
        int cols = (int)(MaxX - MinX) + 1, rows = (int)(MaxY - MinY) + 1;
        using (var hw = new HeightsWriter(Path.Combine(dir, "heights"), new HeightsInfo(MinX, MinY, 1.0, cols, rows, frame)))
        {
            var row = new float[cols];
            for (int r = 0; r < rows; r++)
            {
                double y = MinY + (rows - 1 - r);
                for (int c = 0; c < cols; c++) row[c] = Ground(MinX + c, y);
                hw.WriteRow(row);
            }
        }
        GeoJsonWriter.WriteBuildings(Path.Combine(dir, InputSetLayout.Buildings), Buildings);
        GeoJsonWriter.WriteUnassigned(Path.Combine(dir, InputSetLayout.Unassigned), Unassigned);
        TreesCsv.Write(Path.Combine(dir, InputSetLayout.Trees), Trees);
        GeoJsonWriter.WriteWalls(Path.Combine(dir, InputSetLayout.Walls), Walls, "none");
        if (Woods != null) GeoJsonWriter.WriteAreas(Path.Combine(dir, InputSetLayout.Woods), Woods);
        if (Water != null) GeoJsonWriter.WriteAreas(Path.Combine(dir, InputSetLayout.Water), Water);
        if (Scrub != null) GeoJsonWriter.WriteAreas(Path.Combine(dir, InputSetLayout.Scrub), Scrub);
        if (Net)
        {
            Directory.CreateDirectory(Path.Combine(dir, "roads"));
            File.WriteAllText(Path.Combine(dir, InputSetLayout.RoadsNet),
                NetXml ?? "<net version=\"1.20\"><location netOffset=\"0,0\"/><edge id=\"e\"><lane id=\"e_0\" index=\"0\" width=\"3.2\" shape=\"-100,0 100,0\"/></edge></net>\n");
        }
        Manifest.Write(dir, new ManifestHead("mini", frame, MinX, MinY, MaxX, MaxY, AnchorX, AnchorY), "generator", w => w.WriteString("name", "mini"));
        var problems = InputSetChecks.Check(dir);
        if (problems.Count > 0) throw new InvalidOperationException("the mini set is not a valid input set: " + string.Join("; ", problems));
        return dir;
    }
}
