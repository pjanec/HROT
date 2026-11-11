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

    /// <param name="sourceScenarioDir">the scenario folder (its <c>scenario.json</c> holds the blocks; its overlay is compiled first)</param>
    /// <param name="targetFolder">the new terrain's folder; an existing one is replaced (W-4 re-save)</param>
    /// <param name="newName">the new terrain's name (terrain.json <c>name</c>, door-key prefix)</param>
    /// <param name="resolveTerrainDefinition">terrain name → its <c>terrain.json</c>; when it finds none and <c>&lt;target&gt;/source/base</c> is that terrain's kept ground (a re-save after the original is gone), that copy is the base</param>
    /// <param name="bakeNavtiles">bake the overlay's tiles at save (the base's are copied either way)</param>
    /// <param name="afterPartWritten">test seam: called with the <c>.part</c> folder once it is complete, before the swap (a throw there is a failed save)</param>
    public static TerrainSaveResult SaveAs(string sourceScenarioDir, string targetFolder, string newName, Func<string, string?> resolveTerrainDefinition,
        IEnumerable<string?>? recipeRoots = null, bool bakeNavtiles = true, Action<string>? afterPartWritten = null)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(new[] { '/', '\\' }) >= 0 || newName.Contains("..")) throw new ArgumentException($"'{newName}' is not a terrain name.", nameof(newName));
        string target = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), part = target + ".part";
        string scenarioFile = Path.Combine(sourceScenarioDir, "scenario.json");
        if (!File.Exists(scenarioFile)) throw new FileNotFoundException($"[Terrain] The source scenario '{sourceScenarioDir}' has no scenario.json.", scenarioFile);
        var dom = JsonNode.Parse(File.ReadAllText(scenarioFile)) as JsonObject ?? throw new InvalidOperationException($"[Terrain] '{scenarioFile}' is not a scenario object.");
        string baseName = (string?)Prop(Prop(dom, "header") as JsonObject, "terrainName") ?? throw new InvalidOperationException($"[Terrain] The source scenario '{sourceScenarioDir}' names no terrain.");

        string? Resolve(string name)
        {
            var d = resolveTerrainDefinition(name);
            if (d != null) return d;
            var kept = Path.Combine(target, "source", "base", "terrain.json");
            return File.Exists(kept) && SourceBaseNameOf(target) == name ? kept : null;
        }
        string baseDef = Resolve(baseName) ?? throw new FileNotFoundException($"[Terrain] Base terrain '{baseName}' (named by '{sourceScenarioDir}') was not found.");
        string baseFolder = Path.GetDirectoryName(Path.GetFullPath(baseDef))!;

        // ① the source compiled first: its overlay is current
        var compiled = OverlayCompiler.Compile(dom, sourceScenarioDir, Resolve, bakeNavtiles, recipeRoots);
        string overlayFolder = Path.Combine(sourceScenarioDir, OverlayCompiler.FolderName);

        if (Directory.Exists(part)) Directory.Delete(part, true);
        try
        {
            Directory.CreateDirectory(part);
            string baseHash = TerrainContentHash.OfTerrain(baseDef);
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
                if (Directory.Exists(overlayTiles)) CopyTree(overlayTiles, Path.Combine(part, NavTileCache.TerrainFolderName), _ => false);
                summaryParts.Read(overlayJson, JsonNode.Parse(File.ReadAllText(Path.Combine(overlayFolder, TerrainOverlayHeader.FileName)))!.AsObject());
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

    private static string? SourceBaseNameOf(string targetFolder)
    {
        try { return (string?)((JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(targetFolder, TerrainCatalog.DefinitionFileName)))!)["source"]?["base"]?["name"]; }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
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
