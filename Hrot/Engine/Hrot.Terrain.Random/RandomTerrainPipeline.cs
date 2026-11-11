using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.Import;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Random;

/// <summary>
/// ⭐ CE-4428 F-5 — what a preview is: the OSM wizard's own <see cref="OsmPreview"/> (so one wizard frame draws both), plus what the engine keeps to shuffle and to write: the options, the GROUND in memory and the
/// layout. Immutable.
/// </summary>
public sealed class RandomTerrainPreview
{
    internal RandomTerrainPreview(RandomTerrainOptions options, SynthGround ground, IReadOnlyList<LayoutBlock> layout, OsmPreview preview, RandomTerrainTimings timings, IReadOnlyList<RandomTerrainBlockInfo>? blocks = null)
    { Options = options; Ground = ground; Layout = layout; Preview = preview; Timings = timings; Blocks = blocks ?? Array.Empty<RandomTerrainBlockInfo>(); }

    /// <summary>⭐ Terrain authoring U-8 (ui, additive) — what the wizard's plan labels per block: its recipe, seed, extent (the expanded outline's box, terrain-local metres) and how many houses it made, in <see cref="Layout"/> order (a block that expanded to nothing is left out).</summary>
    public IReadOnlyList<RandomTerrainBlockInfo> Blocks { get; }

    public RandomTerrainOptions Options { get; }
    /// <summary>The records the wizard's frame draws: roads, water / woods / scrub / block outlines as areas, the houses as buildings, the counts, the budget verdict.</summary>
    public OsmPreview Preview { get; }
    /// <summary>The blocks the layout placed.</summary>
    public IReadOnlyList<LayoutBlock> Layout { get; }
    internal SynthGround Ground { get; }
    public RandomTerrainTimings Timings { get; }

    /// <summary>The height range of the ground (m).</summary>
    public (double Min, double Max) ReliefRange => (Ground.Profile.ZMin, Ground.Profile.ZMax);

    /// <summary>The relief for the 2-D plan: <paramref name="n"/> × <paramref name="n"/> heights (row 0 = the southern row), sampled from the generator's own height function.</summary>
    public float[] ReliefGrid(int n)
    {
        var p = Ground.Profile; var h = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                h[j * n + i] = Ground.Heights.Sample(p.MinX + (i + 0.5) * p.Width / n, p.MinY + (j + 0.5) * p.Height / n);
        return h;
    }

    /// <summary>The layout as a scenario DOM for the terrain called <paramref name="terrainName"/> (the positions are the terrain's local metres).</summary>
    public JsonObject ScenarioDom(string terrainName) => BlockLayout.ToScenarioDom(Layout, terrainName, Ground.Profile.MinX, Ground.Profile.MinY);
}

/// <summary>⭐ Terrain authoring U-8 (ui, additive) — one laid-out block as the preview's plan labels it (<see cref="RandomTerrainPreview.Blocks"/>).</summary>
public sealed record RandomTerrainBlockInfo(string Recipe, uint Seed, Vector2 Min, Vector2 Max, int Houses);

/// <summary>Seconds each engine stage took (diagnostics, the wizard's "Generating…" text, the budget rail).</summary>
public sealed record RandomTerrainTimings(double GroundSeconds, double LayoutSeconds, double PreviewSeconds);

/// <summary>What <see cref="RandomTerrainPipeline.OpenInEditorAsync"/> made.</summary>
public sealed record RandomTerrainOpenResult(string TerrainName, string TerrainFolder, string ScenarioFolder, string ScenarioFile, ImportReport? Import,
                                             double WriteGroundSeconds, double ImportSeconds, double ScenarioSeconds);

/// <summary>
/// ⭐⭐ CE-4428 F-5 (docs/DESIGN_Terrain_Authoring.md §18f) — the random-terrain wizard's engine, mirroring <see cref="OsmTerrainPipeline"/>: <see cref="Preview"/> → <see cref="ShuffleLayout"/> → <see cref="OpenInEditorAsync"/>.
/// The GROUND is the synth library (heights, roads, villages, water, woods and scrub — no buildings, no village houses); the HOUSES are recipe blocks a <see cref="BlockLayout"/> places — the same blocks an author places by
/// hand — expanded by the one <see cref="OverlayExpansion"/> for the preview's counts and outlines, and by the editor's live overlay once the scenario is open. Nothing is written twice.
/// </summary>
public sealed class RandomTerrainPipeline
{
    private readonly string _terrainsRoot;
    private readonly IReadOnlyList<string?> _recipeRoots;
    private readonly OsmBudget _budget;

    /// <param name="terrainsRoot">where terrains are imported (<c>assets/terrain</c>); the shared building templates and recipes are found beside it, as for any terrain</param>
    /// <param name="recipeRoots">where <c>&lt;id&gt;.recipe.json</c> is looked up; null = beside <paramref name="terrainsRoot"/> (<c>assets/terrain-recipes</c>), then the NAS</param>
    public RandomTerrainPipeline(string terrainsRoot, IEnumerable<string?>? recipeRoots = null, OsmBudget? budget = null)
    {
        _terrainsRoot = terrainsRoot ?? throw new ArgumentNullException(nameof(terrainsRoot));
        _recipeRoots = (recipeRoots ?? TerrainRecipe.RootsFor(Path.Combine(terrainsRoot, "_preview"))).ToList();
        _budget = budget ?? OsmBudget.Default;
    }

    /// <summary>The synth options a request becomes: GROUND ONLY (no buildings), the preset's villages and road scale, the relief's height range, the vegetation's trees, the water mode. Heights at 1 m (the importer's own grid).</summary>
    public static SynthOptions SynthOptionsOf(RandomTerrainOptions o, string outDir)
    {
        o.Validate();
        double km2 = o.SizeMetres * o.SizeMetres / 1e6;
        var (villagesPerKm2, roadScale) = RandomTerrainOptions.GroundOf(o.Preset);
        int villages = Math.Max(1, (int)Math.Round(villagesPerKm2 * km2));
        return new SynthOptions("full", o.GroundSeed, outDir, o.SizeMetres, 1.0, villages * 60, RandomTerrainOptions.TreesFor(o.Vegetation, km2),
            Extras: true, GroundOnly: true, ZRange: RandomTerrainOptions.ReliefMetres(o.Relief),
            WaterMode: o.Water switch { WaterLevel.None => "none", WaterLevel.Ponds => "ponds", _ => "river" }, RoadScale: roadScale,
            WoodsShare: o.Vegetation == VegetationLevel.None ? null : RandomTerrainOptions.WoodsShareOf(o.Vegetation));
    }

    /// <summary>Generates the ground and lays the blocks out over it; no files. The same options are the same preview.</summary>
    public RandomTerrainPreview Preview(RandomTerrainOptions options)
    {
        var sw = Stopwatch.StartNew();
        var ground = TerrainSynthesizer.Generate(SynthOptionsOf(options, ""));
        if (options.Vegetation == VegetationLevel.None)   // "no vegetation": no woods, no lone trees, no scrub (the synth cannot be told "no woods": a share of 0 means woods everywhere)
            ground = ground with { Scrub = Array.Empty<AreaFeature>(), Trees = new TreeResult(Array.Empty<(double, double, double)>(), Array.Empty<AreaFeature>(), null) };
        double groundSeconds = sw.Elapsed.TotalSeconds;
        return Compose(options, ground, groundSeconds);
    }

    /// <summary>A new layout (<paramref name="layoutSeed"/>) over the SAME ground, which is kept in memory: seconds, no generation.</summary>
    public RandomTerrainPreview ShuffleLayout(RandomTerrainPreview preview, ulong layoutSeed)
        => Compose(preview.Options with { LayoutSeed = layoutSeed }, preview.Ground, 0.0);

    private RandomTerrainPreview Compose(RandomTerrainOptions options, SynthGround ground, double groundSeconds)
    {
        var sw = Stopwatch.StartNew();
        var radii = new Dictionary<string, float>(StringComparer.Ordinal);
        float RadiusOf(string recipe) => radii.TryGetValue(recipe, out var r) ? r : radii[recipe] = TerrainRecipe.Find(recipe, _recipeRoots).Radius;
        var layout = BlockLayout.Place(options.Preset, ground, options.LayoutSeed, RadiusOf);
        double layoutSeconds = sw.Elapsed.TotalSeconds;

        sw.Restart();
        var preview = BuildPreview(options, ground, layout, out var blocks);
        return new RandomTerrainPreview(options, ground, layout, preview, new RandomTerrainTimings(groundSeconds, layoutSeconds, sw.Elapsed.TotalSeconds), blocks);
    }

    // ── the preview: the OSM wizard's records ─────────────────────────────────────────────────────────────────────

    private OsmPreview BuildPreview(RandomTerrainOptions options, SynthGround g, IReadOnlyList<LayoutBlock> layout, out List<RandomTerrainBlockInfo> blockInfos)
    {
        blockInfos = new List<RandomTerrainBlockInfo>();
        var p = g.Profile;
        double cx = p.MinX, cy = p.MinY;   // the terrain's local metres: the south-west corner is 0,0 (what the importer makes)
        Vector2 Local(double x, double y) => new((float)(x - cx), (float)(y - cy));
        Vector2[] Ring(IReadOnlyList<(double X, double Y)> ring) => ring.Take(ring.Count > 1 && ring[0] == ring[^1] ? ring.Count - 1 : ring.Count).Select(v => Local(v.X, v.Y)).ToArray();

        var nodes = g.Roads.Nodes.ToDictionary(n => n.Id);
        var roads = g.Roads.Edges.Select(e => new PreviewRoad(e.Class.ToString().ToLowerInvariant(), new[] { Local(nodes[e.From].X, nodes[e.From].Y), Local(nodes[e.To].X, nodes[e.To].Y) })).ToList();

        var areas = new List<PreviewArea>();
        foreach (var w in g.Water?.Features ?? Array.Empty<AreaFeature>()) areas.Add(new PreviewArea("water", Ring(w.Ring)));
        foreach (var w in g.Trees.Woods) areas.Add(new PreviewArea("woods", Ring(w.Ring)));
        foreach (var s in g.Scrub) areas.Add(new PreviewArea("scrub", Ring(s.Ring)));
        double groundWoodsM2 = g.Trees.Woods.Sum(w => Math.Abs(SignedArea(w.Ring)));

        // the blocks, expanded by the one expander over a world that knows the ground's water (so AvoidWater fits as it will on the imported terrain)
        var world = BaseWorld(g);
        var assets = TerrainAssets.ForFolder(Path.Combine(_terrainsRoot, "_preview"));
        var features = new List<JsonObject>();
        int houses = 0, woods = 0;
        // ⭐ T-5 — the generator's roads, so the preview aligns a block as the compile / import will (AlignRoad)
        var blockRoads = TerrainRoads.FromEdges(roads.SelectMany(r => Enumerable.Range(0, Math.Max(0, r.Points.Length - 1)).Select(i => (r.Points[i], r.Points[i + 1]))));
        foreach (var b in layout)
        {
            var x = OverlayExpansion.Block(b.Guid.ToString("D"), b.Recipe, b.Seed, b.HeadingDeg, TerrainBlock.FitAll, new Vector2((float)(b.X - cx), (float)(b.Y - cy)), world, assets, _recipeRoots, blockRoads);
            features.AddRange(x.Features);
            houses += x.Houses; woods += x.Woods;
            var outline = (JsonArray)x.Summary["outline"]!;
            if (outline.Count >= 3)
            {
                var ring = outline.Select(pt => new Vector2((float)pt![0]!.GetValue<float>(), (float)pt[1]!.GetValue<float>())).ToArray();
                areas.Add(new PreviewArea("block", ring));
                var mn = new Vector2(float.MaxValue); var mx = new Vector2(float.MinValue);
                foreach (var q in ring) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                blockInfos.Add(new RandomTerrainBlockInfo(b.Recipe, b.Seed, mn, mx, x.Houses));
            }
        }
        var buildings = new List<PreviewBuilding>();
        double blockWoodsM2 = 0;
        if (features.Count > 0)
        {
            OverlayExpansion.SortByLabel(features);
            var overlay = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(world, features).ToJsonString(), "preview", assets);
            foreach (var b in overlay.Buildings)
                buildings.Add(new PreviewBuilding(b.Label, b.Footprint, Math.Max(1, b.Storeys), Enterable: !b.Solid));
            foreach (var s in overlay.Surfaces)
                if (s.Type == TerrainSurfaceType.Forest)
                {
                    areas.Add(new PreviewArea("woods", s.Polygon));
                    blockWoodsM2 += Math.Abs(SignedArea(s.Polygon));
                }
        }

        double woodsM2 = groundWoodsM2 + blockWoodsM2;
        double roadKm = Enum.GetValues<RoadClass>().Sum(c => g.Roads.LengthKm(c));
        var counts = new OsmCounts(houses, (int)Math.Round(houses * p.DoorsPerHouse), (int)Math.Round(houses * p.WindowsPerHouse), g.Trees.Trees.Count, woodsM2,
                                   g.Trees.Trees.Count + woodsM2 / 10000.0 * p.WoodDensityPerHa, roadKm, 0, g.Water?.Features.Count ?? 0);
        var (verdict, reason) = _budget.Evaluate(counts);

        var warnings = new List<string>();
        var notes = new List<PreviewNote>();
        var net = Netconvert.Locate();
        if (net.Path == null || net.Problem != null)
        {
            string text = "SUMO's netconvert was not found: the terrain will be imported WITHOUT a road net (" + (net.Problem ?? "not installed") + ").";
            warnings.Add(text); notes.Add(new PreviewNote(text, NoteSeverity.Warning, null));
        }
        if (layout.Count == 0 && options.Preset != TerrainPreset.Plain)
        {
            const string none = "no block fitted: the ground has no road with room beside it — try another seed or a larger size.";
            warnings.Add(none); notes.Add(new PreviewNote(none, NoteSeverity.Warning, null));
        }

        return new OsmPreview
        {
            Frame = new UtmFrame(p.UtmZone, !p.NorthernHemisphere, p.E0, p.N0),
            Bounds = new[] { 0.0, 0.0, p.Width, p.Height },
            Counts = counts, Verdict = verdict, VerdictReason = reason, Warnings = warnings,
            Roads = roads, Buildings = buildings, Areas = areas, Lines = Array.Empty<PreviewLine>(),
            Attribution = $"Generated by hrot-terrain-synth · ground seed {options.GroundSeed}, layout seed {options.LayoutSeed}",
            Trees = g.Trees.Trees.Select(t => Local(t.X, t.Y)).ToList(),
            Notes = notes,
        };
    }

    /// <summary>The ground as a terrain WORLD for the expander: its bounds (local metres) and its water, nothing else — what <c>AvoidWater</c> needs.</summary>
    private static TerrainWorld BaseWorld(SynthGround g)
    {
        var p = g.Profile;
        var feats = new JsonArray();
        foreach (var w in g.Water?.Features ?? Array.Empty<AreaFeature>())
        {
            var ring = new JsonArray();
            foreach (var v in w.Ring) ring.Add(new JsonArray(Math.Round(v.X - p.MinX, 2), Math.Round(v.Y - p.MinY, 2)));
            feats.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["properties"] = new JsonObject { ["kind"] = "surface", ["surface"] = "water" },
                ["geometry"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) },
            });
        }
        var doc = new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["hrot"] = new JsonObject { ["schemaVersion"] = 1, ["groundZ"] = 0, ["bounds"] = new JsonArray(0.0, 0.0, p.Width, p.Height) },
            ["features"] = feats,
        };
        return TerrainWorldParser.Parse(doc.ToJsonString(), "preview");
    }

    private static double SignedArea(IReadOnlyList<(double X, double Y)> r)
    {
        double a = 0;
        for (int i = 0; i < r.Count; i++) { var u = r[i]; var v = r[(i + 1) % r.Count]; a += u.X * v.Y - v.X * u.Y; }
        return a / 2;
    }

    private static double SignedArea(IReadOnlyList<Vector2> r)
    {
        double a = 0;
        for (int i = 0; i < r.Count; i++) { var u = r[i]; var v = r[(i + 1) % r.Count]; a += (double)u.X * v.Y - (double)v.X * u.Y; }
        return a / 2;
    }

    // ── writing ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the GROUND's input set (the §10 format: heights, roads — the net when SUMO is installed —, water, trees, woods, scrub, no buildings) into <paramref name="folder"/>. The same preview is the same bytes.
    /// </summary>
    public string WriteGroundInputSet(RandomTerrainPreview preview, string folder, TextWriter? log = null)
    {
        var o = SynthOptionsOf(preview.Options, folder);
        TerrainSynthesizer.Write(preview.Ground, o, log ?? TextWriter.Null);
        return folder;
    }

    /// <summary>
    /// ⭐ F-6 — imports the ground (<see cref="TerrainImporter"/>, navmesh prefill included when <paramref name="navmeshPrefill"/> is given) as the terrain <paramref name="terrainName"/>, then writes
    /// <c>&lt;scenariosRoot&gt;/&lt;terrainName&gt;/scenario.json</c> — a header on that terrain and the layout's <c>TerrainBlock</c> entities — and returns both folders. ⛔ Never overwrites: a terrain or scenario of that
    /// name that exists is refused. The editor opens the scenario and its live overlay expands the blocks (O-7 B).
    /// </summary>
    public async Task<RandomTerrainOpenResult> OpenInEditorAsync(RandomTerrainPreview preview, string terrainName, string scenariosRoot, string? terrainsRoot = null,
                                                                 ITerrainNavmeshPrefill? navmeshPrefill = null, IProgress<ImportProgress>? progress = null, CancellationToken cancellation = default)
    {
        terrainsRoot ??= _terrainsRoot;
        string scenarioFolder = Path.Combine(scenariosRoot, terrainName);
        string scenarioFile = Path.Combine(scenarioFolder, "scenario.json");
        if (File.Exists(scenarioFile)) throw new InvalidOperationException($"a scenario named '{terrainName}' already exists ({scenarioFile})");

        string setFolder = Path.Combine(Path.GetTempPath(), "random-terrain-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            var sw = Stopwatch.StartNew();
            WriteGroundInputSet(preview, setFolder);
            double writeSeconds = sw.Elapsed.TotalSeconds;

            sw.Restart();
            var importer = new TerrainImporter(terrainsRoot, scenariosRoot) { NavmeshPrefill = navmeshPrefill };
            string terrainFolder = await importer.ImportAsync(setFolder, terrainName, replace: false, progress, cancellation).ConfigureAwait(false);
            double importSeconds = sw.Elapsed.TotalSeconds;

            sw.Restart();
            Directory.CreateDirectory(scenarioFolder);
            File.WriteAllText(scenarioFile, BlockLayout.Serialize(preview.ScenarioDom(terrainName)));
            return new RandomTerrainOpenResult(terrainName, terrainFolder, scenarioFolder, scenarioFile, importer.LastReport, writeSeconds, importSeconds, sw.Elapsed.TotalSeconds);
        }
        finally
        {
            try { if (Directory.Exists(setFolder)) Directory.Delete(setFolder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
