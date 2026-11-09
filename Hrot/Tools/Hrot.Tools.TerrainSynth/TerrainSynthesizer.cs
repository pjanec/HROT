using System.Diagnostics;
using System.Globalization;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Runs every generator in order and writes the complete input set into the output folder.</summary>
public static class TerrainSynthesizer
{
    private static string Secs(Stopwatch sw) => sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s";

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static int Run(SynthOptions o, TextWriter log)
    {
        var p = o.Resolve();
        string outDir = o.OutDir;
        Directory.CreateDirectory(outDir);
        var root = new Rng(o.Seed);
        var sw = new Stopwatch();

        sw.Restart();
        var heights = new HeightField(p, root);
        heights.Write(Path.Combine(outDir, "heights"));
        log.WriteLine($"heights: {Secs(sw)}, {N(heights.Cols)} x {N(heights.Rows)} posts at {heights.Spacing.ToString("R", CultureInfo.InvariantCulture)} m");

        sw.Restart();
        var roads = RoadNetwork.Generate(p, root);
        string roadsDir = Path.Combine(outDir, "roads");
        Directory.CreateDirectory(roadsDir);
        // a net left by an earlier run must not outlive a run that cannot rebuild it
        string netPath = Path.Combine(roadsDir, "roads.net.xml");
        if (File.Exists(netPath)) File.Delete(netPath);
        SumoWriter.WritePlain(roads, roadsDir);
        log.WriteLine($"roads: {Secs(sw)}, {N(roads.Nodes.Count)} nodes, {N(roads.Edges.Count)} edges, {N(roads.Villages.Count)} villages");
        // a missing or failing netconvert is a warning: the plain files stay
        SumoWriter.TryNetconvert(roadsDir, log);

        // the optional files of an earlier run must not outlive a run without them
        foreach (var stale in new[] { "water/water.geojson", "vegetation/scrub.geojson", "vegetation/woods.geojson" })
        {
            string staleFile = Path.Combine(outDir, stale);
            if (!o.Extras && File.Exists(staleFile)) File.Delete(staleFile);
        }

        WaterField? water = null;
        if (o.Extras)
        {
            sw.Restart();
            water = ExtrasGenerator.Water(p, root, roads);
            GeoJsonWriter.WriteAreas(Path.Combine(outDir, "water", "water.geojson"), water.Features);
            log.WriteLine($"water: {Secs(sw)}, {N(water.Features.Count)} polygons");
        }
        Func<double, double, bool>? blocked = water == null ? null : water.Near;

        sw.Restart();
        var (buildings, unassigned) = BuildingGenerator.Generate(p, root, heights, roads, new BuildingOptions { Noise = o.NoiseEnabled, Terraces = o.TerracesEnabled, Materials = o.MaterialsEnabled, Blocked = blocked });
        GeoJsonWriter.WriteBuildings(Path.Combine(outDir, "buildings", "buildings.geojson"), buildings);
        GeoJsonWriter.WriteUnassigned(Path.Combine(outDir, "buildings", "unassigned.geojson"), unassigned);
        log.WriteLine($"buildings: {Secs(sw)}, {N(buildings.Count)} buildings, {N(unassigned.Count)} unassigned openings");

        sw.Restart();
        var treeResult = TreeGenerator.GenerateDetailed(p, root, heights, roads, buildings, new TreeOptions { Blocked = blocked, WoodsAsPolygons = o.Extras });
        TreeGenerator.WriteCsv(Path.Combine(outDir, "trees", "trees.csv"), treeResult.Trees);
        log.WriteLine($"trees: {Secs(sw)}, {N(treeResult.Trees.Count)} trees" + (o.Extras ? " outside woods" : ""));

        if (o.Extras && water != null)
        {
            sw.Restart();
            var scrub = ExtrasGenerator.Scrub(p, root.Fork("extras.scrub"), roads, water, treeResult.Mask, buildings);
            GeoJsonWriter.WriteAreas(Path.Combine(outDir, "vegetation", "scrub.geojson"), scrub);
            GeoJsonWriter.WriteAreas(Path.Combine(outDir, "vegetation", "woods.geojson"), treeResult.Woods);
            log.WriteLine($"vegetation: {Secs(sw)}, {N(scrub.Count)} scrub polygons, {N(treeResult.Woods.Count)} woods polygons");
        }

        sw.Restart();
        GeoJsonWriter.WriteEmpty(Path.Combine(outDir, "walls", "walls.geojson"), "no walls or fences in this area");
        log.WriteLine($"walls: {Secs(sw)}, empty");

        sw.Restart();
        ManifestWriter.Write(outDir, p, o);
        log.WriteLine($"manifest: {Secs(sw)}, {outDir}");
        return 0;
    }
}
