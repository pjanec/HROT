using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fdp.Toolkit.Terrain;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ THE terrain importer (docs/DESIGN_Terrain_Import_UTM.md §11, CE-4368): an input set → <c>assets/terrain/&lt;name&gt;/</c>. Heights at 1 m
/// (<see cref="HeightStage"/>), one template per enterable building (<see cref="BuildingTemplateGenerator"/>), woods and trees
/// (<see cref="VegetationStage"/>), the folder (<see cref="TerrainFolderWriter"/>). ⭐ I-m: all or nothing — written into a temp folder beside the
/// target and moved into place; Cancel or a failure leaves nothing.
/// </summary>
public sealed partial class TerrainImporter : ITerrainImporter
{
    private readonly string? _terrainsRoot, _scenariosRoot;

    /// <param name="terrainsRoot">default <c>&lt;repo&gt;/assets/terrain</c></param>
    /// <param name="scenariosRoot">default <c>&lt;repo&gt;/scenarios</c></param>
    public TerrainImporter(string? terrainsRoot = null, string? scenariosRoot = null)
    {
        _terrainsRoot = terrainsRoot; _scenariosRoot = scenariosRoot;
    }

    public string TerrainsRoot => _terrainsRoot ?? Path.Combine(Repo(), "assets", "terrain");
    public string ScenariosRoot => _scenariosRoot ?? Path.Combine(Repo(), "scenarios");

    /// <summary>
    /// ⭐ CE-4393 (UTM §12.1 N-3) — bakes the written terrain's navmesh into its <c>navtiles/</c> before the swap, so its first load bakes
    /// nothing; null ⇒ no "navmesh" stage (<c>--no-navmesh</c>; the library's own tests).
    /// </summary>
    public ITerrainNavmeshPrefill? NavmeshPrefill { get; init; }

    /// <summary>What the last <see cref="ImportAsync"/> did.</summary>
    public ImportReport? LastReport { get; private set; }

    private static string Repo() => Netconvert.RepoRoot() ?? throw new InvalidOperationException("HROT.sln was not found above the working directory — pass the terrains root explicitly");


    public Task<string> ImportAsync(string inputSetFolder, string terrainName, bool replace, IProgress<ImportProgress>? progress, CancellationToken cancellation)
    {
        if (!TerrainName.IsValid(terrainName)) throw new ArgumentException($"'{terrainName}' is not a terrain name (letters, digits, '-' and '_')", nameof(terrainName));
        string target = Path.Combine(TerrainsRoot, terrainName!);
        if (Directory.Exists(target) && !replace)
            throw new InvalidOperationException($"the terrain '{terrainName}' already exists ({target}) — pass replace to overwrite it");
        return Task.Run(() => Import(inputSetFolder, terrainName!, target, progress, cancellation), cancellation);
    }

    private string Import(string setFolder, string name, string target, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var rep = new ImportReport { TerrainName = name };
        LastReport = rep;
        var sw = Stopwatch.StartNew();
        void Stage(string s) { rep.StageSeconds[s] = sw.Elapsed.TotalSeconds; sw.Restart(); }

        var set = InputSetReader.Open(setFolder);
        var problems = InputSetChecks.Check(setFolder);
        if (problems.Count > 0) throw new InvalidDataException("the input set has problems: " + string.Join("; ", problems.Take(8)));
        Stage("read");
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(TerrainsRoot);
        string tmp = target + ".import-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(tmp);
        try
        {
            var m = set.Manifest;
            double swX = m.MinX, swY = m.MinY, width = m.MaxX - m.MinX, height = m.MaxY - m.MinY;
            string heightsFile = name + TerrainHeightGrid.StoreSuffix;

            // ⭐ CE-4403 (P-1, G-4) — the posts are resampled here and WRITTEN after the buildings: each house's pad is cut into them first
            var (posts, postCols, postRows) = HeightStage.Resample(set, rep, ct, progress);
            Stage("heights");

            // the net is copied UNCHANGED (D1) and placed by an anchor: terrain local = net − anchor, anchor = sumoAnchor + the corner (I-a)
            string? netFile = null, roadsFile = null;
            double ax = m.AnchorX + swX, ay = m.AnchorY + swY;
            List<TerrainWoods.RoadSegment> roads = new();
            if (set.RoadsNet is string net)
            {
                netFile = name + ".net.xml";
                File.Copy(net, Path.Combine(tmp, netFile));
                roads = NetRoads.Read(net, ax, ay);
                // ⭐ CE-6001 (§18k) — and the VEHICLES' road network from the same net, so a bridge (T-7a) has road nodes to snap to
                roadsFile = name + ".roads.json";
                File.WriteAllText(Path.Combine(tmp, roadsFile), NetRoadNetwork.Read(net, ax, ay).ToJsonString(), new System.Text.UTF8Encoding(false));
            }
            else rep.Warnings.Add("the input set has no roads/roads.net.xml — the terrain has no SUMO net");
            progress?.Report(new ImportProgress("roads", 1));
            Stage("roads");
            ct.ThrowIfCancellationRequested();

            var built = BuildingTemplateGenerator.Generate(set.ReadBuildings(), swX, swY, rep, ct);
            // ⭐⭐ CE-4403 (P-1; 📄 DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-2…G-4, R-4228) — every house stands on a PAD: the ground under it cut
            //   down to its own base (the lowest ground under it — no house moves), a skirt blending it into the slope; then the store is written
            Pads.Apply(posts, postCols, postRows, built, rep);
            HeightStage.Write(posts, postCols, postRows, Path.Combine(tmp, heightsFile), rep);
            posts = null!;
            progress?.Report(new ImportProgress("heights", 1));
            Stage("pads");
            foreach (var b in built.Where(b => !b.Solid)) TerrainFolderWriter.WriteTemplate(tmp, b.TemplateName!, b.TemplateJson!);
            rep.UnassignedSkipped = set.ReadUnassigned().Count;
            progress?.Report(new ImportProgress("buildings", 1));
            Stage("buildings");
            ct.ThrowIfCancellationRequested();

            var veg = VegetationStage.Run(set, swX, swY, width, height, roads, rep);
            var water = set.ReadAreas(InputSetLayout.Water)
                .Select(a => ((IReadOnlyList<(double X, double Y)>)a.Ring.Take(a.Ring.Count - 1).Select(p => (Math.Round(p.X - swX, 3), Math.Round(p.Y - swY, 3))).ToList(), a.Kind)).ToList();
            rep.WaterAreas = water.Count;
            var walls = set.ReadWalls().Select(wl => new WorldWall(
                wl.Line.Select(p => (Math.Round(p.X - swX, 3), Math.Round(p.Y - swY, 3))).ToList(), wl.Height, wl.Thickness,
                wl.Kind == "fence" ? "fence" : "wall", WallMaterial(wl.Kind))).ToList();
            rep.Walls = walls.Count;
            progress?.Report(new ImportProgress("vegetation", 1));
            Stage("vegetation");
            ct.ThrowIfCancellationRequested();

            string worldFile = name + ".world.geojson";
            TerrainFolderWriter.WriteWorld(Path.Combine(tmp, worldFile), Describe(set), width, height, heightsFile, water, veg.Woods, built, walls, veg.LoneTrees);
            TerrainFolderWriter.WriteTerrainJson(Path.Combine(tmp, "terrain.json"), name, worldFile, m.Frame, swX, swY, netFile, ax, ay, roadsFile);
            progress?.Report(new ImportProgress("write", 1));
            Stage("write");
            ct.ThrowIfCancellationRequested();

            // ⭐ CE-4393 — the whole navmesh into tmp/navtiles, BEFORE the swap: a cancelled or failed bake leaves no half-baked folder
            if (NavmeshPrefill != null)
            {
                var navProgress = progress == null ? null : new Progress1(f => progress.Report(new ImportProgress("navmesh", f)));
                var nav = NavmeshPrefill.Bake(tmp, navProgress, ct);
                rep.NavmeshTiles = nav.Tiles; rep.NavmeshBaked = nav.Baked; rep.NavmeshFiles = nav.Files; rep.NavmeshBytes = nav.Bytes;
                progress?.Report(new ImportProgress("navmesh", 1));
                Stage("navmesh");
                ct.ThrowIfCancellationRequested();
            }
            rep.OutputBytes = Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

            Swap(tmp, target);
            return target;
        }
        catch
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>An <see cref="IProgress{T}"/> that reports on the CALLING thread (the bake's workers), never posting to a context.</summary>
    private sealed class Progress1 : IProgress<double>
    {
        private readonly Action<double> _report;
        public Progress1(Action<double> report) => _report = report;
        public void Report(double value) => _report(value);
    }

    private static void Swap(string tmp, string target)
    {
        if (!Directory.Exists(target)) { Move(tmp, target); return; }
        string old = target + ".old-" + Guid.NewGuid().ToString("N")[..8];
        Move(target, old);
        try { Move(tmp, target); }
        catch { Move(old, target); throw; }
        try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Windows: a folder whose files were JUST written (or are open in a viewer) is briefly held by the virus scanner / indexer, and renaming it
    // fails with "access denied" — measured as a flaky import on the user's machine; a few short retries ride it out
    private static void Move(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Move(from, to); return; }
            catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < 20 && Directory.Exists(from) && !Directory.Exists(to))
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    /// <summary>Wall kinds of the input set (stone / mud / fence / hedge, and the OSM names) → the material library's names.</summary>
    public static string WallMaterial(string kind) => kind switch
    {
        "fence" => "fence-wood", "hedge" => "hedge", "mud" => "earth-berm",
        "retaining_wall" or "city_wall" => "concrete",
        _ => "brick",   // stone, wall
    };

    private static string Describe(InputSetReader set)
    {
        string text = $"Imported by hrot-terrain-import (CE-4368) from the input set '{set.Manifest.Name}'. Local metres = the set's metres minus the south-west corner of its bounds ({set.Manifest.MinX:0.##}, {set.Manifest.MinY:0.##}); X east, Y north, Z up.";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(set.PathOf(InputSetLayout.Manifest)));
            if (doc.RootElement.TryGetProperty("source", out var src) && src.TryGetProperty("attribution", out var at) && at.GetString() is string a) text += " " + a + ".";
            else if (doc.RootElement.TryGetProperty("generator", out var g) && g.TryGetProperty("name", out var gn)) text += $" Generated by {gn.GetString()}.";
        }
        catch (JsonException) { }
        return text;
    }

    [GeneratedRegex("\"terrainName\"\\s*:\\s*\"([^\"]*)\"")]
    private static partial Regex TerrainNameKey();

    /// <summary>
    /// The scenarios (folder names under the scenarios root) whose <c>scenario.json</c> header names this terrain — <c>header.terrainName</c>,
    /// which the scenario serializer writes first; any depth under the scenarios root (<c>ScenarioHeaderDto.TerrainName</c>), so the first 64 KB are enough.
    /// </summary>
    public IReadOnlyList<string> ScenariosUsing(string terrainName)
    {
        var list = new List<string>();
        if (!Directory.Exists(ScenariosRoot)) return list;
        // ⭐ CE-4360 (Q-S2) — every scenario folder at any depth, named by its leaf; ordinal by name (as the top-level listing was)
        foreach (var folder in Fdp.Toolkit.Orchestration.ScenarioFolderIndex.Enumerate(ScenariosRoot).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var dir = folder.Directory;
            string file = Path.Combine(dir, "scenario.json");
            if (!File.Exists(file)) continue;
            var buf = new char[65536];
            int n;
            using (var r = new StreamReader(file)) n = r.Read(buf, 0, buf.Length);
            var mt = TerrainNameKey().Match(new string(buf, 0, n));
            if (mt.Success && string.Equals(mt.Groups[1].Value, terrainName, StringComparison.Ordinal)) list.Add(Path.GetFileName(dir));
        }
        return list;
    }
}
