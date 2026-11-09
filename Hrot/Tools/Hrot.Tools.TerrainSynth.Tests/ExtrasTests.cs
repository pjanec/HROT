using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

// --extras: water, scrub and woods polygons; trees.csv then holds only the trees outside woods
public class ExtrasTests : IDisposable
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "x" + Guid.NewGuid().ToString("N"));

    public void Dispose() { }

    private static string Run(string name, params string[] args)
    {
        string dir = Path.Combine(Root, name);
        var o = SynthOptions.Parse(args.Concat(new[] { "--out", dir }).ToArray());
        Assert.Equal(0, TerrainSynthesizer.Run(o, new StringWriter()));
        return dir;
    }

    private static readonly Lazy<string> FullDir = new(() => Run("full", "--preset", "full", "--seed", "1", "--height-spacing", "8", "--extras"));
    private static readonly Lazy<string> TinyDir = new(() => Run("tiny", "--preset", "tiny", "--seed", "1", "--height-spacing", "4", "--extras"));

    private sealed record Poly(string? Kind, double? Density, List<(double X, double Y)> Ring, int RingCount);

    private static List<Poly> Load(string dir, string file)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, file))).RootElement;
        var list = new List<Poly>();
        foreach (var f in doc.GetProperty("features").EnumerateArray())
        {
            var g = f.GetProperty("geometry");
            Assert.Equal("Polygon", g.GetProperty("type").GetString());
            var rings = g.GetProperty("coordinates").EnumerateArray().ToList();
            var ring = rings[0].EnumerateArray().Select(c => (c[0].GetDouble(), c[1].GetDouble())).ToList();
            var props = f.GetProperty("properties");
            string? kind = props.TryGetProperty("kind", out var k) ? k.GetString() : null;
            double? density = props.TryGetProperty("density", out var d) ? d.GetDouble() : null;
            list.Add(new Poly(kind, density, ring, rings.Count));
        }
        return list;
    }

    private static List<(double X, double Y)> Open(List<(double X, double Y)> closed) => closed.Take(closed.Count - 1).ToList();

    private static double Area(List<(double X, double Y)> v)
    {
        double a = 0;
        for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
        return a / 2;
    }

    private static bool Inside(List<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    private static double PointSeg(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
        return Math.Sqrt(Math.Pow(a.X + t * dx - px, 2) + Math.Pow(a.Y + t * dy - py, 2));
    }

    private static bool Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        double O((double X, double Y) p, (double X, double Y) q, (double X, double Y) s) => (q.X - p.X) * (s.Y - p.Y) - (q.Y - p.Y) * (s.X - p.X);
        return O(a, b, c) * O(a, b, d) < -1e-9 && O(c, d, a) * O(c, d, b) < -1e-9;
    }

    // true when the two polygons overlap (an edge crosses, or one contains the other)
    private static bool Overlap(List<(double X, double Y)> a, List<(double X, double Y)> b)
    {
        if (a.Max(p => p.X) < b.Min(p => p.X) || b.Max(p => p.X) < a.Min(p => p.X) || a.Max(p => p.Y) < b.Min(p => p.Y) || b.Max(p => p.Y) < a.Min(p => p.Y)) return false;
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
                if (Cross(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
        return Inside(b, a[0].X, a[0].Y) || Inside(a, b[0].X, b[0].Y);
    }

    private static List<List<(double X, double Y)>> Footprints(string dir) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "buildings/buildings.geojson"))).RootElement.GetProperty("features").EnumerateArray()
            .Select(f => Open(f.GetProperty("geometry").GetProperty("coordinates")[0].EnumerateArray().Select(c => (c[0].GetDouble(), c[1].GetDouble())).ToList())).ToList();

    private static List<(double X, double Y)> Trees(string dir) =>
        File.ReadLines(Path.Combine(dir, "trees/trees.csv")).Skip(1).Select(l => l.Split(',')).Select(f => (double.Parse(f[0], CultureInfo.InvariantCulture), double.Parse(f[1], CultureInfo.InvariantCulture))).ToList();

    // ---- tests ----

    [Fact]
    public void ExtrasOff_WritesNoExtraFiles()
    {
        string dir = Run("off", "--preset", "tiny", "--height-spacing", "8");
        Assert.False(File.Exists(Path.Combine(dir, "water/water.geojson")));
        Assert.False(File.Exists(Path.Combine(dir, "vegetation/scrub.geojson")));
        Assert.False(File.Exists(Path.Combine(dir, "vegetation/woods.geojson")));
        Assert.DoesNotContain("extras", File.ReadAllText(Path.Combine(dir, "manifest.json")));
    }

    [Fact]
    public void ExtrasOn_ListsThreeFilesInTheManifestWithFeatureCounts()
    {
        string dir = FullDir.Value;
        var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))).RootElement;
        Assert.True(m.GetProperty("generator").GetProperty("overrides").GetProperty("extras").GetBoolean());
        var files = m.GetProperty("files");
        foreach (var f in new[] { "water/water.geojson", "vegetation/scrub.geojson", "vegetation/woods.geojson" })
        {
            var e = files.GetProperty(f);
            Assert.Equal(Load(dir, f).Count, e.GetProperty("features").GetInt32());
            string text = File.ReadAllText(Path.Combine(dir, f));
            Assert.DoesNotContain("\n", text);
            var root = JsonDocument.Parse(text).RootElement;
            Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());
            Assert.Contains("compass degrees", root.GetProperty("hrotInput").GetProperty("heading").GetString());
        }
    }

    [Fact]
    public void AllPolygons_AreClosedCounterClockwiseAndWithoutHoles()
    {
        string dir = FullDir.Value;
        foreach (var f in new[] { "water/water.geojson", "vegetation/scrub.geojson", "vegetation/woods.geojson" })
            foreach (var p in Load(dir, f))
            {
                Assert.Equal(1, p.RingCount);
                Assert.True(p.Ring.Count >= 4);
                Assert.Equal(p.Ring[0], p.Ring[^1]);
                Assert.True(Area(Open(p.Ring)) > 0, $"{f} polygon not counter-clockwise");
            }
    }

    [Fact]
    public void Water_IsOnePondAndOneRiverStrip()
    {
        string dir = FullDir.Value;
        var water = Load(dir, "water/water.geojson");
        Assert.Equal(new[] { "pond", "river" }, water.Select(w => w.Kind).OrderBy(k => k, StringComparer.Ordinal));
        var pond = Open(water.Single(w => w.Kind == "pond").Ring);
        Assert.InRange(Area(pond), 2000.0, 8000.0);

        var river = Open(water.Single(w => w.Kind == "river").Ring);
        double perimeter = 0;
        for (int i = 0; i < river.Count; i++) { var a = river[i]; var b = river[(i + 1) % river.Count]; perimeter += Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)); }
        double half = perimeter / 2, area = Area(river);
        double width = (half - Math.Sqrt(Math.Max(0, half * half - 4 * area))) / 2;
        Assert.InRange(width, 14.0, 31.0);
        // it crosses the whole area from one side to the other
        Assert.True(river.Max(p => p.Y) - river.Min(p => p.Y) > 0.95 * 5500 || river.Max(p => p.X) - river.Min(p => p.X) > 0.95 * 5500);
        Console.WriteLine($"EXTRAS pondArea={Area(pond):F0} riverWidth={width:F1} riverLength~{half - width:F0}");
    }

    [Fact]
    public void NoBuildingOrTree_OnWater()
    {
        string dir = FullDir.Value;
        var water = Load(dir, "water/water.geojson").Select(w => Open(w.Ring)).ToList();
        foreach (var fp in Footprints(dir))
            foreach (var w in water)
                Assert.False(Overlap(fp, w), "a building overlaps water");
        foreach (var (x, y) in Trees(dir))
            foreach (var w in water)
                Assert.False(Inside(w, x, y), "a tree stands on water");
    }

    [Fact]
    public void Scrub_ThreeToEight_OutsideWoodsVillagesWaterAndBuildings()
    {
        string dir = FullDir.Value;
        var scrub = Load(dir, "vegetation/scrub.geojson");
        Assert.InRange(scrub.Count, 3, 8);
        Assert.All(scrub, s => Assert.Equal("scrub", s.Kind));
        var woods = Load(dir, "vegetation/woods.geojson").Select(w => Open(w.Ring)).ToList();
        var water = Load(dir, "water/water.geojson").Select(w => Open(w.Ring)).ToList();
        var footprints = Footprints(dir);
        foreach (var s in scrub)
        {
            var ring = Open(s.Ring);
            foreach (var w in woods) Assert.False(Overlap(ring, w), "scrub overlaps woods");
            foreach (var w in water) Assert.False(Overlap(ring, w), "scrub overlaps water");
            foreach (var f in footprints) Assert.False(Overlap(ring, f), "scrub overlaps a building");
            double cx = ring.Average(p => p.X), cy = ring.Average(p => p.Y);
            foreach (var v in FullVillages(dir))
                Assert.True(Math.Sqrt(Math.Pow(v.X - cx, 2) + Math.Pow(v.Y - cy, 2)) > v.R, "scrub inside a village");
        }
    }

    // villages from the roads: the generator is deterministic, so rebuild the network for the same seed
    private static List<(double X, double Y, double R)> FullVillages(string dir)
    {
        var p = SynthOptions.Parse(new[] { "--preset", "full", "--height-spacing", "8" }).Resolve();
        return RoadNetwork.Generate(p, new Rng(1)).Villages.Select(v => (v.X, v.Y, v.Radius)).ToList();
    }

    [Fact]
    public void Woods_PolygonsCarryDensity_AvoidBuildingsAndWater_AndCoverAboutHalf()
    {
        string dir = FullDir.Value;
        var woods = Load(dir, "vegetation/woods.geojson");
        Assert.True(woods.Count >= 5);
        Assert.All(woods, w => Assert.InRange(w.Density!.Value, 90.0, 150.0));
        var rings = woods.Select(w => Open(w.Ring)).ToList();
        double areaShare = rings.Sum(r => Area(r)) / (5500.0 * 5500.0);
        Assert.InRange(areaShare, 0.30, 0.50);
        var water = Load(dir, "water/water.geojson").Select(w => Open(w.Ring)).ToList();
        foreach (var f in Footprints(dir))
            foreach (var w in rings)
                Assert.False(Overlap(f, w), "woods overlap a building");
        foreach (var wa in water)
            foreach (var w in rings)
                Assert.False(Overlap(wa, w), "woods overlap water");
        // pieces may touch along a split line but not cross
        for (int i = 0; i < rings.Count; i++)
            for (int j = i + 1; j < rings.Count; j++)
            {
                if (rings[i].Max(p => p.X) < rings[j].Min(p => p.X) || rings[j].Max(p => p.X) < rings[i].Min(p => p.X)) continue;
                for (int a = 0; a < rings[i].Count; a++)
                    for (int b = 0; b < rings[j].Count; b++)
                        Assert.False(Cross(rings[i][a], rings[i][(a + 1) % rings[i].Count], rings[j][b], rings[j][(b + 1) % rings[j].Count]), "woods pieces cross");
            }
        Console.WriteLine($"EXTRAS water={Load(dir, "water/water.geojson").Count} scrub={Load(dir, "vegetation/scrub.geojson").Count} woods={woods.Count} woodsAreaShare={areaShare:F3} density={woods[0].Density}");
    }

    [Fact]
    public void TreesCsv_HoldsOnlyTreesOutsideWoods()
    {
        string dir = FullDir.Value;
        var woods = Load(dir, "vegetation/woods.geojson").Select(w => Open(w.Ring)).ToList();
        var trees = Trees(dir);
        // only the lone trees: a few percent of the full count, far below the 176 000 of the woods-included set
        Assert.InRange(trees.Count, 4000, 25000);
        foreach (var (x, y) in trees)
            foreach (var w in woods)
            {
                if (x < w.Min(p => p.X) || x > w.Max(p => p.X) || y < w.Min(p => p.Y) || y > w.Max(p => p.Y)) continue;
                Assert.False(Inside(w, x, y), $"tree at {x},{y} inside a woods polygon");
            }
    }

    [Fact]
    public void BuildingCount_StaysNearTheTarget_WithExtras()
    {
        Assert.InRange(Footprints(FullDir.Value).Count, 1620, 1980);
    }

    [Fact]
    public void Tiny_WithExtras_WritesTheFiles_AndIsDeterministic()
    {
        string a = TinyDir.Value;
        Assert.Equal(2, Load(a, "water/water.geojson").Count);
        Assert.True(File.Exists(Path.Combine(a, "vegetation/scrub.geojson")));
        Assert.True(File.Exists(Path.Combine(a, "vegetation/woods.geojson")));
        string b = Run("tiny2", "--preset", "tiny", "--seed", "1", "--height-spacing", "4", "--extras");
        foreach (var f in new[] { "water/water.geojson", "vegetation/scrub.geojson", "vegetation/woods.geojson", "trees/trees.csv", "buildings/buildings.geojson" })
            Assert.Equal(Sha(Path.Combine(a, f)), Sha(Path.Combine(b, f)));
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    [Fact]
    public void Options_ExtrasIsAFlag()
    {
        Assert.False(SynthOptions.Parse(new[] { "--preset", "full" }).Extras);
        Assert.True(SynthOptions.Parse(new[] { "--preset", "full", "--extras" }).Extras);
        Assert.True(SynthOptions.Parse(new[] { "--extras", "--preset", "tiny" }).Extras);
    }
}
