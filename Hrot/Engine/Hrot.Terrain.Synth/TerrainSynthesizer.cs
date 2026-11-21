using System.Diagnostics;
using System.Globalization;

namespace Hrot.Tools.TerrainSynth;

/// <summary>
/// ⭐ CE-4428 (F-1, F-2) — everything the synth generated, IN MEMORY: what <see cref="TerrainSynthesizer.Generate"/> makes and <see cref="TerrainSynthesizer.Write"/> writes. The random-terrain pipeline keeps one of
/// these to preview, to lay blocks out over and to shuffle the layout without generating the ground again.
/// </summary>
public sealed record SynthGround(
    SynthProfile Profile, HeightField Heights, RoadNetwork Roads, WaterField? Water,
    IReadOnlyList<Building> Buildings, IReadOnlyList<Opening> Unassigned, TreeResult Trees, IReadOnlyList<AreaFeature> Scrub);

/// <summary>Runs every generator in order and writes the complete input set into the output folder.</summary>
public static class TerrainSynthesizer
{
    private static string Secs(Stopwatch sw) => sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s";

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// ⭐ The generators, in the order they always ran (the random streams are forked by name, so the order is part of the output), with nothing written: heights, roads, water (extras), buildings (none when
    /// <see cref="SynthOptions.GroundOnly"/>), trees and woods, scrub (extras). Pure: the same options give the same ground.
    /// </summary>
    public static SynthGround Generate(SynthOptions o, TextWriter? log = null)
    {
        log ??= TextWriter.Null;
        var p = o.Resolve();
        var root = new Rng(o.Seed);
        var sw = Stopwatch.StartNew();

        var heights = new HeightField(p, root);
        log.WriteLine($"heights: {Secs(sw)}, {N(heights.Cols)} x {N(heights.Rows)} posts at {heights.Spacing.ToString("R", CultureInfo.InvariantCulture)} m");

        sw.Restart();
        var roads = RoadNetwork.Generate(p, root);
        log.WriteLine($"roads: {Secs(sw)}, {N(roads.Nodes.Count)} nodes, {N(roads.Edges.Count)} edges, {N(roads.Villages.Count)} villages");

        WaterField? water = null;
        if (o.Extras)
        {
            sw.Restart();
            water = ExtrasGenerator.Water(p, root, roads, o.WaterMode);
            log.WriteLine($"water: {Secs(sw)}, {N(water.Features.Count)} polygons");
        }
        Func<double, double, bool>? blocked = water == null ? null : water.Near;

        sw.Restart();
        IReadOnlyList<Building> buildings = Array.Empty<Building>();
        IReadOnlyList<Opening> unassigned = Array.Empty<Opening>();
        if (!o.GroundOnly)
            (buildings, unassigned) = BuildingGenerator.Generate(p, root, heights, roads, new BuildingOptions { Noise = o.NoiseEnabled, Terraces = o.TerracesEnabled, Materials = o.MaterialsEnabled, Blocked = blocked });
        log.WriteLine($"buildings: {Secs(sw)}, {N(buildings.Count)} buildings, {N(unassigned.Count)} unassigned openings");

        sw.Restart();
        var treeResult = TreeGenerator.GenerateDetailed(p, root, heights, roads, buildings, new TreeOptions { Blocked = blocked, WoodsAsPolygons = o.Extras });
        log.WriteLine($"trees: {Secs(sw)}, {N(treeResult.Trees.Count)} trees" + (o.Extras ? " outside woods" : ""));

        IReadOnlyList<AreaFeature> scrub = Array.Empty<AreaFeature>();
        if (o.Extras && water != null)
        {
            sw.Restart();
            scrub = ExtrasGenerator.Scrub(p, root.Fork("extras.scrub"), roads, water, treeResult.Mask, buildings);
            log.WriteLine($"vegetation: {Secs(sw)}, {N(scrub.Count)} scrub polygons, {N(treeResult.Woods.Count)} woods polygons");
        }
        return new SynthGround(p, heights, roads, water, buildings, unassigned, treeResult, scrub);
    }

    /// <summary>Writes the input set of <paramref name="g"/> into <paramref name="outDir"/> (heights, the road plain files and — when SUMO is installed — the net, water, buildings, trees, vegetation, walls, manifest).</summary>
    public static void Write(SynthGround g, SynthOptions o, TextWriter log)
    {
        string outDir = o.OutDir;
        Directory.CreateDirectory(outDir);
        var sw = Stopwatch.StartNew();
        g.Heights.Write(Path.Combine(outDir, "heights"));

        string roadsDir = Path.Combine(outDir, "roads");
        Directory.CreateDirectory(roadsDir);
        // a net left by an earlier run must not outlive a run that cannot rebuild it
        string netPath = Path.Combine(roadsDir, "roads.net.xml");
        if (File.Exists(netPath)) File.Delete(netPath);
        SumoWriter.WritePlain(g.Roads, roadsDir);
        // a missing or failing netconvert is a warning: the plain files stay
        // ⭐ AQ93 E2b — netconvert writes a FLAT net: give it OUR heights (the grid just written), the one shared step the OSM import takes too
        if (SumoWriter.TryNetconvert(roadsDir, log))
        {
            sw.Restart();
            var e = NetElevation.Apply(netPath, outDir);
            log.WriteLine($"net heights: {Secs(sw)}, {e.Shapes} shapes, {e.PointsBefore} -> {e.PointsAfter} points, {e.BytesBefore} -> {e.BytesAfter} bytes");
        }

        // the optional files of an earlier run must not outlive a run without them
        foreach (var stale in new[] { "water/water.geojson", "vegetation/scrub.geojson", "vegetation/woods.geojson" })
        {
            string staleFile = Path.Combine(outDir, stale);
            if (!o.Extras && File.Exists(staleFile)) File.Delete(staleFile);
        }
        if (o.Extras && g.Water != null) GeoJsonWriter.WriteAreas(Path.Combine(outDir, "water", "water.geojson"), g.Water.Features);

        GeoJsonWriter.WriteBuildings(Path.Combine(outDir, "buildings", "buildings.geojson"), g.Buildings);
        GeoJsonWriter.WriteUnassigned(Path.Combine(outDir, "buildings", "unassigned.geojson"), g.Unassigned);
        TreeGenerator.WriteCsv(Path.Combine(outDir, "trees", "trees.csv"), g.Trees.Trees);

        if (o.Extras && g.Water != null)
        {
            GeoJsonWriter.WriteAreas(Path.Combine(outDir, "vegetation", "scrub.geojson"), g.Scrub);
            GeoJsonWriter.WriteAreas(Path.Combine(outDir, "vegetation", "woods.geojson"), g.Trees.Woods);
        }
        sw.Restart();
        GeoJsonWriter.WriteEmpty(Path.Combine(outDir, "walls", "walls.geojson"), "no walls or fences in this area");
        log.WriteLine($"walls: {Secs(sw)}, empty");

        sw.Restart();
        ManifestWriter.Write(outDir, g.Profile, o);
        log.WriteLine($"manifest: {Secs(sw)}, {outDir}");
    }

    public static int Run(SynthOptions o, TextWriter log)
    {
        Write(Generate(o, log), o, log);
        return 0;
    }
}
