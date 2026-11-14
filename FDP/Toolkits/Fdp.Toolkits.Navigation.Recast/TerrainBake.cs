using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;

namespace Fdp.Toolkit.Terrain;

/// <summary>What <em>Save terrain as</em> counted — the data mockup page 4 shows (DESIGN_Terrain_Authoring §18c-1 / §18d D-6).</summary>
/// <param name="Houses">Building features of the overlay (a block's houses, a placed house).</param>
/// <param name="Woods">Forest surface features.</param>
/// <param name="Trees">Lone-tree features.</param>
/// <param name="WallKm">Length of the wall / fence / hedge features, kilometres.</param>
/// <param name="Bridges">Distinct <c>"structure"</c> keys (slabs / ramps / prisms grouped into one collapsible).</param>
/// <param name="Blocks">The overlay's <c>blocks[]</c> (key, recipe, houses, woods).</param>
/// <param name="UnitsNotBaked">Entities of the source scenario that are neither terrain parts nor areas — they stay in the source only (E-4).</param>
/// <param name="AreasNotBaked">Entities carrying an area-like component (a component named <c>*Area</c> / <c>Zone*</c>) — heuristic, see <see cref="TerrainBake"/>.</param>
/// <param name="DamagedHousesNotBaked">Buildings with a <c>BuildingDamage</c> entry in the source's TerrainObjects — damage from a test run is not baked (E-6).</param>
/// <param name="TilesTotal">The navmesh tiles of the saved terrain (exact when the navtiles were baked, else an estimate from its bounds).</param>
/// <param name="TilesToBake">Tiles NOT in the saved navtiles (0 once they are baked at save); null when unknown.</param>
/// <param name="EstimateSeconds">Cold-bake estimate for <paramref name="TilesToBake"/> at <see cref="TerrainBake.ColdSecondsPerTile"/>; null when unknown.</param>
public sealed record TerrainSaveSummary(int Houses, int Woods, int Trees, double WallKm, int Bridges, IReadOnlyList<TerrainSaveSummary.BlockCount> Blocks,
    int UnitsNotBaked, int AreasNotBaked, int DamagedHousesNotBaked, int TilesTotal, int? TilesToBake, double? EstimateSeconds)
{
    public sealed record BlockCount(string Key, string Recipe, int Houses, int Woods);
}

/// <summary>The outcome of <see cref="TerrainBake.SaveAs"/>: the new terrain, its hash, the summary, and the NEW scenario (a DOM — the caller writes it where it wants).</summary>
public sealed record TerrainSaveResult(string Folder, string ContentHash, TerrainSaveSummary Summary, JsonObject Scenario);

/// <summary>
/// ⭐ O-5 (CE-4422, docs/DESIGN_Terrain_Authoring.md §18d) — <em>Save terrain as</em>: base terrain + the source scenario's compiled overlay → a NEW terrain folder. ⭐ No new serialiser:
/// a terrain's world file and an overlay's are one GeoJSON format read by one parser, so the saved world file is the base's features FOLLOWED BY the overlay's (bounds = the union) and every
/// other base file is copied byte for byte. <see cref="TerrainWorldMerge"/> stays the definition of "base + overlay"; the rail holds the saved folder to it.
/// <para>The folder carries its own source (E-1): <c>source/scenario/</c> (the block scenario, no overlay) and <c>source/base/</c> (the ground terrain, navtiles kept as seeds); <c>terrain.json</c> names both.
/// Written into <c>&lt;target&gt;.part</c> and swapped in after everything was read; a failure leaves the target untouched and no <c>.part</c>.</para>
/// <para>⚠ Heuristic: an "area" is an entity with a component whose name ends in <c>Area</c> or starts with <c>Zone</c> (the scenario format has no area marker); everything else that is not a terrain
/// block / feature is a unit.</para>
/// </summary>
public static class TerrainBake
{
    /// <summary>The cold navmesh-bake rate (DESIGN §18b: 536 s / 105 800 tiles, 4 cores).</summary>
    public const double ColdSecondsPerTile = 0.0051;

    /// <summary>
    /// ⭐ T-8 (CE-4424, docs/DESIGN_Terrain_Authoring.md §18j) — what saving <paramref name="sourceScenarioDir"/> over <paramref name="targetFolder"/> would do to the scenarios that use the terrain <paramref name="newName"/>: the OLD world is the target as it is, the NEW world is the same
    /// merge <see cref="SaveAs"/> writes (the base's world file plus the compiled overlay's features, parsed under the new name) built IN MEMORY — ⭐ nothing of the target is written (the source scenario's own derived <c>terrain-overlay/</c> is compiled first, as SaveAs does) —
    /// and <see cref="TerrainChangeCheck"/> runs over every scenario <see cref="TerrainUsers.Of"/> finds under <paramref name="scenarioRoots"/>. A target that does not exist yet has no users: nothing to find.
    /// </summary>
    public static IReadOnlyList<TerrainFinding> CheckOverwrite(string sourceScenarioDir, string targetFolder, string newName, Func<string, string?> resolveTerrainDefinition, IEnumerable<string> scenarioRoots,
        IEnumerable<string?>? recipeRoots = null, IUniqueBuildings? unique = null)
    {
        string target = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string oldDef = Path.Combine(target, TerrainCatalog.DefinitionFileName);
        if (!File.Exists(oldDef)) return Array.Empty<TerrainFinding>();
        var users = TerrainUsers.Of(newName, scenarioRoots).Where(d => !string.Equals(Path.GetFullPath(d), Path.GetFullPath(sourceScenarioDir), StringComparison.OrdinalIgnoreCase)).ToList();
        if (users.Count == 0) return Array.Empty<TerrainFinding>();

        var oldWorld = TerrainFolderWorld.ReadWorld(oldDef, TerrainFolderWorld.ReadDefinition(oldDef), newName);
        string scenarioFile = Path.Combine(sourceScenarioDir, "scenario.json");
        if (!File.Exists(scenarioFile)) throw new FileNotFoundException($"[Terrain] The source scenario '{sourceScenarioDir}' has no scenario.json.", scenarioFile);
        var dom = JsonNode.Parse(File.ReadAllText(scenarioFile)) as JsonObject ?? throw new InvalidOperationException($"[Terrain] '{scenarioFile}' is not a scenario object.");
        string baseName = (string?)Prop(Prop(dom, "header") as JsonObject, "terrainName") ?? throw new InvalidOperationException($"[Terrain] The source scenario '{sourceScenarioDir}' names no terrain.");
        string? Resolve(string name) => ResolveBase(name, target, resolveTerrainDefinition);
        string baseDef = Resolve(baseName) ?? throw new FileNotFoundException($"[Terrain] Base terrain '{baseName}' (named by '{sourceScenarioDir}') was not found.");
        string baseFolder = Path.GetDirectoryName(Path.GetFullPath(baseDef))!;
        var definition = TerrainFolderWorld.ReadDefinition(baseDef);
        if (oldWorld == null || string.IsNullOrEmpty(definition.World)) return Array.Empty<TerrainFinding>();

        var compiled = OverlayCompiler.Compile(dom, sourceScenarioDir, Resolve, bakeNavtiles: false, recipeRoots, unique);
        string worldPath = Path.IsPathRooted(definition.World) ? definition.World : Path.GetFullPath(Path.Combine(baseFolder, definition.World));
        var worldJson = JsonNode.Parse(File.ReadAllText(worldPath)) as JsonObject ?? throw new InvalidOperationException($"[Terrain] World file '{worldPath}' is not a GeoJSON object.");
        var assets = TerrainAssets.ForFolder(baseFolder);
        if (compiled.Wrote)
        {
            string overlayFolder = Path.Combine(sourceScenarioDir, OverlayCompiler.FolderName);
            var header = TerrainOverlayHeader.Parse(File.ReadAllText(Path.Combine(overlayFolder, TerrainOverlayHeader.FileName)), overlayFolder);
            var overlayJson = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(overlayFolder, header.World)))!;
            var features = worldJson["features"] as JsonArray ?? throw new InvalidOperationException($"[Terrain] World file '{worldPath}' has no features array.");
            foreach (var f in (JsonArray)overlayJson["features"]!) features.Add(f!.DeepClone());
            var overlayBase = TerrainFolderWorld.ReadWorld(baseDef, definition, baseName)!;
            var merged = TerrainWorldMerge.Merge(overlayBase, TerrainFolderWorld.ReadOverlay(overlayFolder, baseName, TerrainAssets.RecipesSibling(baseFolder)).World);
            var hrot = worldJson["hrot"] as JsonObject ?? (JsonObject)(worldJson["hrot"] = new JsonObject { ["schemaVersion"] = 1 });
            hrot["bounds"] = new JsonArray(merged.BoundsMin.X, merged.BoundsMin.Y, merged.BoundsMax.X, merged.BoundsMax.Y);
            // the overlay's own templates (a block's generated houses) resolve in memory, as they will from the saved folder's buildings/
            string overlayBuildings = Path.Combine(overlayFolder, TerrainAssets.BuildingsFolderName);
            if (Directory.Exists(overlayBuildings))
            {
                var inline = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in Directory.EnumerateFiles(overlayBuildings, "*" + TerrainAssets.TemplateSuffix)) inline[Path.GetFileName(file)[..^TerrainAssets.TemplateSuffix.Length]] = File.ReadAllText(file);
                assets = assets.WithInlineTemplates(inline);
            }
        }
        var newWorld = TerrainWorldParser.Parse(worldJson.ToJsonString(), newName, assets);

        var scenarios = new List<(string, JsonObject)>();
        foreach (var d in users)
            if (JsonNode.Parse(File.ReadAllText(Path.Combine(d, "scenario.json"))) is JsonObject sd) scenarios.Add((d, sd));
        return TerrainChangeCheck.Check(oldWorld, newWorld, scenarios);
    }

    /// <param name="sourceScenarioDir">the scenario folder (its <c>scenario.json</c> holds the blocks; its overlay is compiled first)</param>
    /// <param name="targetFolder">the new terrain's folder; an existing one is replaced (W-4 re-save)</param>
    /// <param name="newName">the new terrain's name (terrain.json <c>name</c>, door-key prefix)</param>
    /// <param name="resolveTerrainDefinition">terrain name → its <c>terrain.json</c>; when it finds none and <c>&lt;target&gt;/source/base</c> is that terrain's kept ground (a re-save after the original is gone), that copy is the base</param>
    /// <param name="bakeNavtiles">bake the overlay's tiles at save (the base's are copied either way)</param>
    /// <param name="bridgeNet">⭐ CE-6001 (§18k) — the host's builder of the bridges' SUMO net (<c>Hrot.Traffic.BridgeNetBuilder</c>), the SAME one the live overlay's compile gets; null ⇒ the bridges are road for the vehicles' net only (and only if the base declares one)</param>
    /// <param name="unique">⭐ T-3 part 2 — the host's generator of unique houses (null ⇒ the shipped templates), the same the editor's live overlay uses</param>
    /// <param name="afterPartWritten">test seam: called with the <c>.part</c> folder once it is complete, before the swap (a throw there is a failed save)</param>
    public static TerrainSaveResult SaveAs(string sourceScenarioDir, string targetFolder, string newName, Func<string, string?> resolveTerrainDefinition,
        IEnumerable<string?>? recipeRoots = null, bool bakeNavtiles = true, Action<string>? afterPartWritten = null, IUniqueBuildings? unique = null, IBridgeNet? bridgeNet = null,
        Action<string, Func<double, double, double>>? reElevateNet = null)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(new[] { '/', '\\' }) >= 0 || newName.Contains("..")) throw new ArgumentException($"'{newName}' is not a terrain name.", nameof(newName));
        string target = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), part = target + ".part";
        string scenarioFile = Path.Combine(sourceScenarioDir, "scenario.json");
        if (!File.Exists(scenarioFile)) throw new FileNotFoundException($"[Terrain] The source scenario '{sourceScenarioDir}' has no scenario.json.", scenarioFile);
        var dom = JsonNode.Parse(File.ReadAllText(scenarioFile)) as JsonObject ?? throw new InvalidOperationException($"[Terrain] '{scenarioFile}' is not a scenario object.");
        string baseName = (string?)Prop(Prop(dom, "header") as JsonObject, "terrainName") ?? throw new InvalidOperationException($"[Terrain] The source scenario '{sourceScenarioDir}' names no terrain.");

        string? Resolve(string name) => ResolveBase(name, target, resolveTerrainDefinition);
        string baseDef = Resolve(baseName) ?? throw new FileNotFoundException($"[Terrain] Base terrain '{baseName}' (named by '{sourceScenarioDir}') was not found.");
        string baseFolder = Path.GetDirectoryName(Path.GetFullPath(baseDef))!;

        // ① the source compiled first: its overlay is current
        var compiled = OverlayCompiler.Compile(dom, sourceScenarioDir, Resolve, bakeNavtiles, recipeRoots, unique, bridgeNet, reElevateNet);   // ⭐ CE-4592 (G-16)
        string overlayFolder = Path.Combine(sourceScenarioDir, OverlayCompiler.FolderName);

        if (Directory.Exists(part)) Directory.Delete(part, true);
        try
        {
            Directory.CreateDirectory(part);
            string baseHash = TerrainContentHash.OfTerrain(baseDef);
            // ⭐ E-3 (T-8 follow-up) — a source saved over its OWN base: the ground it keeps gets a name of its own, so reopening the source loads the bare ground (the catalogue resolves the new name through the kept-ground fallback) and the
            //   live overlay adds the blocks once, instead of the saved terrain (blocks and all) under the same name
            bool selfSave = string.Equals(baseFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), target, StringComparison.OrdinalIgnoreCase);
            string? keptName = selfSave ? KeptGroundName(baseName, target, resolveTerrainDefinition) : null;
            // a re-save from the ORIGINAL source (it still names T) once the ground was renamed: the base resolved to the kept copy under its OLD name — the rename is re-applied under the kept name it already has
            if (!selfSave && string.Equals(baseFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.Combine(target, "source", "base"), StringComparison.OrdinalIgnoreCase)
                && ((string?)((JsonObject)JsonNode.Parse(File.ReadAllText(baseDef))!)["name"]) is { } currentKept && TerrainCatalog.SourceBaseOfOf(target) != null)
                keptName = currentKept;   // also from the kept source itself (it names the ground): the `of` it recorded is kept, so the same inputs are the same terrain
            var definition = TerrainFolderWorld.ReadDefinition(baseDef);
            var baseWorld = TerrainFolderWorld.ReadWorld(baseDef, definition, baseName)
                ?? throw new InvalidOperationException($"[Terrain] Base terrain '{baseName}' declares no world.");
            var summaryParts = new SummaryParts();

            // ② the base folder, byte for byte (not its own source/, not its hash cache)
            CopyTree(baseFolder, part, rel => rel == "source" || rel.StartsWith("source/", StringComparison.Ordinal) || rel == TerrainContentHash.CacheFileName);

            // ③ the world file: the base's features, then the overlay's; bounds = the union
            TerrainWorld? overlayWorld = null;
            if (compiled.Wrote)
            {
                var overlay = TerrainFolderWorld.ReadOverlay(overlayFolder, baseName, TerrainAssets.RecipesSibling(baseFolder));
                overlayWorld = overlay.World;
                var merged = TerrainWorldMerge.Merge(baseWorld, overlayWorld);
                string worldPath = Path.Combine(part, definition.World);
                var worldJson = JsonNode.Parse(File.ReadAllText(worldPath)) as JsonObject ?? throw new InvalidOperationException($"[Terrain] World file '{worldPath}' is not a GeoJSON object.");
                var overlayJson = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(overlayFolder, overlay.Header.World)))!;
                var features = worldJson["features"] as JsonArray ?? throw new InvalidOperationException($"[Terrain] World file '{worldPath}' has no features array.");
                foreach (var f in (JsonArray)overlayJson["features"]!) features.Add(f!.DeepClone());
                var hrot = worldJson["hrot"] as JsonObject ?? (JsonObject)(worldJson["hrot"] = new JsonObject { ["schemaVersion"] = 1 });
                hrot["bounds"] = new JsonArray(merged.BoundsMin.X, merged.BoundsMin.Y, merged.BoundsMax.X, merged.BoundsMax.Y);
                File.WriteAllText(worldPath, worldJson.ToJsonString());

                // templates the overlay places: the saved folder is self-contained (its own buildings/), whatever shared tree it lands under
                var baseAssets = TerrainAssets.ForFolder(baseFolder);
                foreach (var f in (JsonArray)overlayJson["features"]!)
                    if ((string?)f!["properties"]?["template"] is { } t && !File.Exists(Path.Combine(part, TerrainAssets.BuildingsFolderName, t + TerrainAssets.TemplateSuffix)))
                        if (baseAssets.Templates?.Invoke(t) is { } text)
                        {
                            Directory.CreateDirectory(Path.Combine(part, TerrainAssets.BuildingsFolderName));
                            File.WriteAllText(Path.Combine(part, TerrainAssets.BuildingsFolderName, t + TerrainAssets.TemplateSuffix), text);
                        }
                string overlayBuildings = Path.Combine(overlayFolder, TerrainAssets.BuildingsFolderName);
                if (Directory.Exists(overlayBuildings)) CopyTree(overlayBuildings, Path.Combine(part, TerrainAssets.BuildingsFolderName), _ => false);

                // ④ navtiles: the base's (copied above) + the overlay's
                string overlayTiles = Path.Combine(overlayFolder, NavTileCache.TerrainFolderName);
                if (Directory.Exists(overlayTiles))
                {
                    // ⭐ T-6 (CE-3435) — a tile is <navtiles>/<layer>/<x>_<z>/<x>_<z>_<hash>.navtile: where the overlay re-baked a square (it holds a tile at that layer + x + z) the BASE's tile of the same square is stale (its hash can no longer match) — dropped, not copied beside it
                    foreach (var layerDir in Directory.GetDirectories(overlayTiles))
                        foreach (var squareDir in Directory.GetDirectories(layerDir))
                        {
                            if (Directory.GetFiles(squareDir, "*.navtile").Length == 0) continue;
                            string stale = Path.Combine(part, NavTileCache.TerrainFolderName, Path.GetFileName(layerDir), Path.GetFileName(squareDir));
                            if (Directory.Exists(stale)) Directory.Delete(stale, true);
                        }
                    CopyTree(overlayTiles, Path.Combine(part, NavTileCache.TerrainFolderName), _ => false);
                }
                summaryParts.Read(overlayJson, JsonNode.Parse(File.ReadAllText(Path.Combine(overlayFolder, TerrainOverlayHeader.FileName)))!.AsObject());

                // ④b ⭐ CE-6001 (§18k T-7a, "a bridge is road in BOTH nets", for every SAVED terrain): the overlay's bridge segments are appended to the saved road network (base segment indices stay),
                //   and the overlay's SUMO net — the base net plus the bridge edges — is the saved terrain's net
                if (overlay.Header.HasRoads && definition.RoadNetworks.Count > 0)
                {
                    string roadsPath = Path.Combine(part, definition.RoadNetworks[0]);
                    var roadsJson = JsonNode.Parse(File.ReadAllText(roadsPath)) as JsonObject;
                    var bridgeRoads = JsonNode.Parse(File.ReadAllText(Path.Combine(overlayFolder, TerrainOverlayHeader.RoadsFileName))) as JsonObject;
                    if (roadsJson?["segments"] is JsonArray all && bridgeRoads?["segments"] is JsonArray extra)
                    {
                        foreach (var seg in extra) all.Add(seg!.DeepClone());
                        File.WriteAllText(roadsPath, roadsJson.ToJsonString());
                    }
                }
                if (overlay.Header.SumoNet is { Length: > 0 } overlayNet && definition.SumoNet is { } baseNet && File.Exists(Path.Combine(overlayFolder, overlayNet)))
                    File.Copy(Path.Combine(overlayFolder, overlayNet), Path.Combine(part, baseNet.File), overwrite: true);

                // ④c ⭐⭐ CE-4597 (P-3, G-20) — the saved terrain keeps every PAD: the merged (padded) heights are WRITTEN over the copied base store, and the SUMO net
                //   re-elevated from them (the caller's NetElevation, forced — V-23: over the bridge net when the overlay had one). ⛔ It copied the base's store byte for byte,
                //   so a saved terrain lost every pad (measured: CopyTree).
                if (merged.Height is { } padded && !ReferenceEquals(padded, baseWorld.Height) && (string?)hrot["heightGrid"] is { } heightFile)
                {
                    string heightPath = Path.Combine(part, heightFile);
                    if (heightFile.EndsWith(TerrainHeightGrid.StoreSuffix, StringComparison.OrdinalIgnoreCase))
                        using (var o = File.Create(heightPath)) padded.WriteStore(o);
                    else
                        using (var w = new StreamWriter(heightPath)) padded.WriteAsciiGrid(w);
                    if (reElevateNet != null && definition.SumoNet is { } sumo && File.Exists(Path.Combine(part, sumo.File)))
                        reElevateNet(Path.Combine(part, sumo.File), (x, y) => padded.Sample((float)(x - sumo.AnchorX), (float)(y - sumo.AnchorY)));
                }
            }

            // ⑤ terrain.json: the new name + where it came from
            string defPath = Path.Combine(part, TerrainCatalog.DefinitionFileName);
            var defJson = (JsonObject)JsonNode.Parse(File.ReadAllText(defPath))!;
            defJson["name"] = newName;
            defJson["source"] = new JsonObject
            {
                ["scenario"] = "source/scenario",
                ["base"] = new JsonObject { ["name"] = baseName, ["contentHash"] = baseHash },
            };
            File.WriteAllText(defPath, defJson.ToJsonString());

            // ⑥ the source: the block scenario (no overlay) and the ground terrain (navtiles kept as seeds)
            CopyTree(sourceScenarioDir, Path.Combine(part, "source", "scenario"), rel => TopFolder(rel) is OverlayCompiler.FolderName or OverlayCompiler.FolderName + ".part" or ".autosave" or ".slices");
            CopyTree(baseFolder, Path.Combine(part, "source", "base"), rel => rel == "source" || rel.StartsWith("source/", StringComparison.Ordinal) || rel == TerrainContentHash.CacheFileName);

            if (keptName != null) RenameKeptGround(part, defPath, baseName, keptName, selfSave ? baseName : TerrainCatalog.SourceBaseOfOf(target) ?? baseName);
            string hash = TerrainContentHash.OfTerrain(defPath);
            var scenario = NewScenario(dom, baseName, newName);
            var summary = summaryParts.Finish(dom, scenario, compiled, baseWorld, overlayWorld);
            afterPartWritten?.Invoke(part);

            // ⑦ swap — everything was read before this point
            string old = target + ".old";
            if (Directory.Exists(old)) Directory.Delete(old, true);
            bool had = Directory.Exists(target);
            if (had) Directory.Move(target, old);
            try { Directory.CreateDirectory(Path.GetDirectoryName(target)!); Directory.Move(part, target); }
            catch { if (had && !Directory.Exists(target)) Directory.Move(old, target); throw; }
            if (had) Directory.Delete(old, true);
            return new TerrainSaveResult(target, hash, summary, scenario);
        }
        catch
        {
            try { if (Directory.Exists(part)) Directory.Delete(part, true); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>⭐ E-3 — the name of the ground a self-save keeps: <c>&lt;T&gt;-ground</c>, else <c>&lt;T&gt;-ground-2</c>, <c>-3</c> … — the first no terrain of the catalogue (real or kept) and no kept ground of the target already has. Deterministic.</summary>
    internal static string KeptGroundName(string terrainName, string target, Func<string, string?> resolve)
    {
        for (int n = 1; ; n++)
        {
            string candidate = n == 1 ? terrainName + "-ground" : terrainName + "-ground-" + n;
            if (resolve(candidate) == null && TerrainCatalog.KeptGroundOf(target, candidate) == null) return candidate;
        }
    }

    /// <summary>
    /// ⭐ E-3 — writes <paramref name="keptName"/> into the part's kept ground (<c>source/base/terrain.json</c> <c>name</c>), into <c>terrain.json</c>'s <c>source.base</c> (<c>name</c>; <c>of</c> = the old name; <c>contentHash</c> = the RENAMED copy's — a rename
    /// moves <see cref="TerrainContentHash.OfTerrain"/>, which hashes <c>terrain.json</c>) and into the copied source scenario (header <c>terrainName</c>; the <c>TerrainObjects</c> keys <c>&lt;old&gt;/…</c> → <c>&lt;kept&gt;/…</c>).
    /// </summary>
    private static void RenameKeptGround(string part, string defPath, string oldName, string keptName, string of)
    {
        string keptDef = Path.Combine(part, "source", "base", TerrainCatalog.DefinitionFileName);
        var kept = (JsonObject)JsonNode.Parse(File.ReadAllText(keptDef))!;
        kept["name"] = keptName;
        File.WriteAllText(keptDef, kept.ToJsonString());

        var def = (JsonObject)JsonNode.Parse(File.ReadAllText(defPath))!;
        def["source"] = new JsonObject
        {
            ["scenario"] = "source/scenario",
            ["base"] = new JsonObject { ["name"] = keptName, ["of"] = of, ["contentHash"] = TerrainContentHash.OfTerrain(keptDef) },
        };
        File.WriteAllText(defPath, def.ToJsonString());

        string scenarioFile = Path.Combine(part, "source", "scenario", "scenario.json");
        var dom = (JsonObject)JsonNode.Parse(File.ReadAllText(scenarioFile))!;
        if (Prop(dom, "header") is JsonObject header)
        {
            string tnKey = header.Select(kv => kv.Key).First(k => k.Equals("terrainName", StringComparison.OrdinalIgnoreCase));
            header[tnKey] = keptName;
        }
        if (TerrainObjectsSection.Of(dom) is { } section)
        {
            var entries = section.Select(kv => (kv.Key, kv.Value)).ToList();
            section.Clear();
            foreach (var (key, node) in entries)
                section[key.StartsWith(oldName + "/", StringComparison.Ordinal) ? keptName + "/" + key.Substring(oldName.Length + 1) : key] = node;
        }
        File.WriteAllText(scenarioFile, dom.ToJsonString());
    }

    /// <summary>
    /// The new scenario on the saved terrain (W-3, E-5): the source's header with <c>terrainName</c> = <paramref name="newName"/>, the door states re-prefixed <c>&lt;base&gt;/</c> → <c>&lt;new&gt;/</c> — and nothing else
    /// (no blocks, features, units or areas; damage stays in the source, E-6).
    /// </summary>
    internal static JsonObject NewScenario(JsonObject source, string baseName, string newName)
    {
        var result = new JsonObject();
        if (source["$meta"] is JsonNode meta) result["$meta"] = meta.DeepClone();
        var header = Prop(source, "header") as JsonObject;
        string headerKey = source.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("header", StringComparison.OrdinalIgnoreCase)) ?? "Header";
        var h = header == null ? new JsonObject() : (JsonObject)header.DeepClone();
        string tnKey = h.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("terrainName", StringComparison.OrdinalIgnoreCase)) ?? "TerrainName";
        h[tnKey] = newName;
        result[headerKey] = h;
        string entitiesKey = source.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("entities", StringComparison.OrdinalIgnoreCase)) ?? "Entities";
        result[entitiesKey] = new JsonObject();
        if (TerrainObjectsSection.Of(source) is { } section)
        {
            var doors = new JsonObject();
            foreach (var (key, node) in section)
            {
                if (node is not JsonObject entry || TerrainObjectsSection.IsBuildingDamageEntry(entry) || entry["door"] == null) continue;   // damage stays in the source (E-6)
                string k = key.StartsWith(baseName + "/", StringComparison.Ordinal) ? newName + "/" + key.Substring(baseName.Length + 1) : key;
                doors[k] = entry.DeepClone();
            }
            if (doors.Count > 0) result[TerrainObjectsSection.Name] = doors;
        }
        return result;
    }

    private sealed class SummaryParts
    {
        private int _houses, _woods, _trees;
        private double _wallMetres;
        private readonly HashSet<string> _bridges = new(StringComparer.Ordinal);
        private readonly List<TerrainSaveSummary.BlockCount> _blocks = new();

        public void Read(JsonObject overlayWorld, JsonObject header)
        {
            foreach (var f in (JsonArray)overlayWorld["features"]!)
            {
                var props = f!["properties"] as JsonObject; var geo = f["geometry"] as JsonObject;
                string kind = (string?)props?["kind"] ?? "";
                string surface = (string?)props?["surface"] ?? "";
                switch (kind)
                {
                    case "building": _houses++; break;
                    case "tree": _trees++; break;
                    case "surface" when surface == "forest": _woods++; break;
                    case "wall":
                    case "fence":
                        if (geo?["coordinates"] is JsonArray line)
                            for (int i = 0; i + 1 < line.Count; i++)
                            {
                                var a = (JsonArray)line[i]!; var b = (JsonArray)line[i + 1]!;
                                _wallMetres += Vector2.Distance(new Vector2(a[0]!.GetValue<float>(), a[1]!.GetValue<float>()), new Vector2(b[0]!.GetValue<float>(), b[1]!.GetValue<float>()));
                            }
                        break;
                }
                if ((string?)props?["structure"] is { Length: > 0 } s) _bridges.Add(s);
            }
            if (header["blocks"] is JsonArray blocks)
                foreach (var b in blocks)
                    _blocks.Add(new TerrainSaveSummary.BlockCount((string)b!["key"]!, (string)b["recipe"]!, (int)b["houses"]!, (int)b["woods"]!));
        }

        public TerrainSaveSummary Finish(JsonObject sourceDom, JsonObject scenario, OverlayCompileResult compiled, TerrainWorld baseWorld, TerrainWorld? overlayWorld)
        {
            int units = 0, areas = 0;
            if (Prop(sourceDom, "entities") is JsonObject entities)
                foreach (var (_, node) in entities)
                {
                    if (node is not JsonObject e || e["TerrainBlock"] != null || e["TerrainFeature"] != null) continue;
                    if (e.Any(kv => kv.Key.EndsWith("Area", StringComparison.Ordinal) || kv.Key.StartsWith("Zone", StringComparison.Ordinal))) areas++; else units++;
                }
            int damaged = TerrainObjectsSection.Of(sourceDom) is { } section ? section.Count(kv => TerrainObjectsSection.IsBuildingDamageEntry(kv.Value)) : 0;
            int tiles = compiled.NavTiles, toBake = compiled.Wrote && compiled.NavTiles > 0 ? 0 : -1;
            if (tiles == 0)   // not baked at save: an estimate from the bounds (two layers over the tile grid)
            {
                var w = overlayWorld != null ? TerrainWorldMerge.Merge(baseWorld, overlayWorld) : baseWorld;
                float tm = RecastNavmeshBaker.DefaultTileMetres;
                tiles = 2 * Math.Max(1, (int)MathF.Ceiling((w.BoundsMax.X - w.BoundsMin.X) / tm)) * Math.Max(1, (int)MathF.Ceiling((w.BoundsMax.Y - w.BoundsMin.Y) / tm));
            }
            int? tb = toBake == 0 ? 0 : null;
            return new TerrainSaveSummary(_houses, _woods, _trees, Math.Round(_wallMetres / 1000.0, 3), _bridges.Count, _blocks, units, areas, damaged, tiles, tb, tb * ColdSecondsPerTile);
        }
    }

    /// <summary>
    /// ⭐ THE base resolver of a save (CheckOverwrite and SaveAs): the catalogue's answer (<paramref name="resolve"/> — which already falls back to a kept ground, <see cref="TerrainCatalog.KeptGroundOf"/>), except that when that answer IS the
    /// terrain being overwritten and the target keeps its own ground under that name, the KEPT ground is the base — a re-save of a source that was saved over its own base (E-3) must never stack its blocks on the result of the
    /// last save. A name the catalogue does not know falls back to the target's kept ground (a target outside the catalogue's roots).
    /// </summary>
    private static string? ResolveBase(string name, string target, Func<string, string?> resolve)
    {
        string? d = resolve(name);
        if (d != null)
        {
            string folder = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(d))!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(folder, target, StringComparison.OrdinalIgnoreCase) && TerrainCatalog.KeptGroundOf(target, name) is { } kept ? kept : d;
        }
        return TerrainCatalog.KeptGroundOf(target, name);
    }

    private static string TopFolder(string rel) { int i = rel.IndexOf('/'); return i < 0 ? rel : rel.Substring(0, i); }

    private static JsonNode? Prop(JsonObject? o, string name)
    {
        if (o == null) return null;
        foreach (var (k, v) in o) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    /// <summary>Copies every file under <paramref name="from"/> to <paramref name="to"/> (existing files are overwritten), skipping the relative paths <paramref name="skip"/> names (a folder's whole subtree through its first segment test).</summary>
    private static void CopyTree(string from, string to, Func<string, bool> skip)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file).Replace('\\', '/');
            if (skip(rel) || skip(TopFolder(rel))) continue;
            string dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}

/// <summary>⭐ O-5 — which scenarios name a terrain (the "Used by 3 scenarios: …" line of the overwrite warning, mockup 4).</summary>
public static class TerrainUsers
{
    /// <summary>The scenario folders under <paramref name="scenarioRoots"/> whose <c>scenario.json</c> header names <paramref name="terrainName"/> (a scenario's own <c>source/</c>, <c>.autosave</c> and overlay folders are not scenarios), sorted.</summary>
    public static IReadOnlyList<string> Of(string terrainName, IEnumerable<string> scenarioRoots)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var root in scenarioRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "scenario.json", SearchOption.AllDirectories))
            {
                var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => p is ".autosave" or ".slices" or "source" or OverlayCompiler.FolderName)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.Name.Equals("header", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
                            foreach (var h in p.Value.EnumerateObject())
                                if (h.Name.Equals("terrainName", StringComparison.OrdinalIgnoreCase) && h.Value.ValueKind == JsonValueKind.String && h.Value.GetString() == terrainName)
                                    found.Add(Path.GetDirectoryName(file)!);
                }
                catch (JsonException) { }   // not a scenario
            }
        }
        return found.ToList();
    }
}
