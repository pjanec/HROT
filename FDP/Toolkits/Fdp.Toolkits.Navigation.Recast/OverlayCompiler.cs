using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;

namespace Fdp.Toolkit.Terrain;

/// <summary>What one <see cref="OverlayCompiler.Compile"/> did.</summary>
public sealed record OverlayCompileResult(bool Wrote, string? Folder, string? ContentHash, string? BaseHash, int Blocks, int Houses, int Woods, int NavTiles, int NavtilesWritten, int NavtilesSeedHits);

/// <summary>
/// ⭐ O-4 (CE-4415, docs/DESIGN_Terrain_Authoring.md §18c) — compiles the scenario's authored terrain blocks and features into its terrain OVERLAY folder
/// (<c>&lt;scenarioDir&gt;/terrain-overlay/</c>: <c>overlay.json</c> + <c>overlay.world.geojson</c> + the navtiles the base's seed lacks). ONE function, called by the master's merge
/// and by the editor's own save; ⛔ nothing else expands a block. Reads the entities from the scenario JSON (a <c>TerrainBlock</c> / <c>TerrainFeature</c> object plus
/// <c>SimTransform.Position</c>), never from an ECS world. Written into <c>terrain-overlay.part/</c> and swapped in; no block or feature ⇒ a stale overlay folder is deleted.
/// Lives with the Recast bake because the compile bakes the merged world's navtiles.
/// </summary>
public static class OverlayCompiler
{
    public const string FolderName = "terrain-overlay";

    /// <param name="scenarioDom">the canonical scenario (<c>header.terrainName</c>, <c>entities</c>)</param>
    /// <param name="resolveTerrainDefinition">terrain name → the path of its <c>terrain.json</c>, or null</param>
    /// <param name="bakeNavtiles">false skips the navmesh bake (a terrain with no navmesh to speak of): the overlay then has no <c>navtiles/</c> and a node bakes its tiles at load</param>
    /// <param name="recipeRoots">where <c>&lt;id&gt;.recipe.json</c> is looked up; null = beside the terrain's assets tree, then the NAS</param>
    public static OverlayCompileResult Compile(JsonObject scenarioDom, string scenarioDir, Func<string, string?> resolveTerrainDefinition, bool bakeNavtiles = true, IEnumerable<string?>? recipeRoots = null)
    {
        string finalDir = Path.Combine(scenarioDir, FolderName), partDir = finalDir + ".part";
        if (Directory.Exists(partDir)) Directory.Delete(partDir, true);

        var blocks = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var feats = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        if (Prop(scenarioDom, "entities") is JsonObject entities)
            foreach (var (guid, node) in entities)
            {
                if (node is not JsonObject e) continue;
                // re-read through the bytes: a DOM built in memory may hold floats / uints, a parsed one holds doubles — the compile reads numbers one way
                if (e["TerrainBlock"] is JsonObject) blocks[guid] = Normalised(e);
                else if (e["TerrainFeature"] is JsonObject) feats[guid] = Normalised(e);
            }
        if (blocks.Count == 0 && feats.Count == 0)
        {
            if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);   // nothing authored: a stale overlay must not outlive its blocks
            return new OverlayCompileResult(false, null, null, null, 0, 0, 0, 0, 0, 0);
        }

        string terrainName = (string?)(Prop(Prop(scenarioDom, "header") as JsonObject, "terrainName"))
            ?? throw new InvalidOperationException($"[Terrain] '{scenarioDir}' has terrain blocks / features but its header names no terrain.");
        string definitionPath = resolveTerrainDefinition(terrainName)
            ?? throw new FileNotFoundException($"[Terrain] Terrain '{terrainName}' (named by '{scenarioDir}') was not found; its blocks cannot be compiled.");
        string terrainFolder = Path.GetDirectoryName(Path.GetFullPath(definitionPath))!;
        var definition = TerrainFolderWorld.ReadDefinition(definitionPath);
        var baseWorld = TerrainFolderWorld.ReadWorld(definitionPath, definition, terrainName)
            ?? throw new InvalidOperationException($"[Terrain] Terrain '{terrainName}' declares no world: blocks cannot be placed on it.");
        var assets = TerrainAssets.ForFolder(terrainFolder);
        var roots = recipeRoots ?? TerrainRecipe.RootsFor(terrainFolder);
        var roads = TerrainRoads.Load(definitionPath, definition);   // ⭐ T-5 — the SAME roads the live overlay reads (AlignRoad)

        var features = new List<JsonObject>();
        var blockSummaries = new JsonArray();
        int houses = 0, woods = 0;
        foreach (var (guid, e) in blocks)
        {
            var tb = (JsonObject)e["TerrainBlock"]!;
            string recipeId = (string?)tb["Recipe"] ?? throw new InvalidOperationException($"[Terrain] TerrainBlock {guid} has no Recipe.");
            uint seed = tb["Seed"] is JsonNode sn ? (uint)sn.GetValue<double>() : 0u;
            float heading = tb["HeadingDeg"] is JsonNode hn ? (float)hn.GetValue<double>() : 0f;
            byte fit = tb["Fit"] is JsonNode fn ? (byte)fn.GetValue<int>() : TerrainBlock.FitAll;
            // ⭐ O-7 B — the per-block expansion is shared with the editor's live overlay (OverlayExpansion): ONE code path, two callers
            var x = OverlayExpansion.Block(guid, recipeId, seed, heading, fit, PositionOf(e, guid), baseWorld, assets, roots, roads);
            features.AddRange(x.Features);
            houses += x.Houses; woods += x.Woods;
            blockSummaries.Add(x.Summary);
        }
        foreach (var (guid, e) in feats)
        {
            var tf = (JsonObject)e["TerrainFeature"]!;
            int kindValue = tf["Kind"] is JsonNode kn ? kn.GetValue<int>() : 0;
            if (kindValue < 0 || kindValue > byte.MaxValue || !FeatureExpander.IsKnown((byte)kindValue)) continue;   // 0 and the unknown are ignored
            byte kind = (byte)kindValue;
            string template = (string?)tf["Template"] ?? "";
            if (kind == TerrainFeature.House && template.Length == 0) throw new InvalidOperationException($"[Terrain] TerrainFeature {guid} has no Template.");
            float heading = tf["HeadingDeg"] is JsonNode hn ? (float)hn.GetValue<double>() : 0f;
            float pad = tf["Pad"] is JsonNode pn ? (float)pn.GetValue<double>() : 0f;
            uint seed = tf["Seed"] is JsonNode sn ? (uint)sn.GetValue<double>() : 0u;
            var at = PositionOf(e, guid);
            var points = FeatureExpander.NeedsShape(kind) ? PolylineOf(e, guid, at) : Array.Empty<Vector2>();
            // ⭐ T-2 — ONE expansion entry for every kind, the live overlay's too (OverlayExpansion.Feature)
            var parts = OverlayExpansion.Feature(new OverlayExpansion.FeatureSpec(guid, kind, template, heading, pad, seed, at, points), baseWorld, assets, roots);
            features.AddRange(parts);
            if (kind == TerrainFeature.House) houses += parts.Count;
        }
        OverlayExpansion.SortByLabel(features);

        Directory.CreateDirectory(partDir);
        try
        {
            var world = OverlayExpansion.WorldDocument(baseWorld, features);
            string worldName = TerrainOverlayHeader.DefaultWorldFileName;
            File.WriteAllText(Path.Combine(partDir, worldName), world.ToJsonString());

            string baseHash = TerrainContentHash.OfTerrain(definitionPath);
            string contentHash = TerrainContentHash.OfOverlay(partDir, baseHash);
            var header = new JsonObject
            {
                ["format"] = TerrainOverlayHeader.CurrentFormat, ["contentHash"] = contentHash,
                ["base"] = new JsonObject { ["name"] = terrainName, ["contentHash"] = baseHash },
                ["scenario"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(scenarioDir)), ["world"] = worldName, ["blocks"] = blockSummaries,
            };
            File.WriteAllText(Path.Combine(partDir, TerrainOverlayHeader.FileName), header.ToJsonString());

            int tiles = 0, written = 0, hits = 0;
            if (bakeNavtiles)
            {
                // the merged world as a node will load it; the prefill cache writes only the tiles neither the base's seed nor the part already holds
                var overlay = TerrainFolderWorld.ReadOverlay(partDir, terrainName, TerrainAssets.RecipesSibling(terrainFolder));
                var merged = TerrainWorldMerge.Merge(baseWorld, overlay.World);
                var factory = new RecastNavmeshFactory { TileCache = NavTileCache.ForPrefill(Path.Combine(partDir, NavTileCache.TerrainFolderName)), SeedPrefill = true };
                factory.Build(merged);
                var st = factory.LastStats;
                tiles = st.Tiles; written = st.Baked; hits = st.FromCache;
            }

            if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
            Directory.Move(partDir, finalDir);
            return new OverlayCompileResult(true, finalDir, contentHash, baseHash, blocks.Count, houses, woods, tiles, written, hits);
        }
        catch
        {
            try { Directory.Delete(partDir, true); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>A property by name, either casing (the writer's scenario files are camelCase, an older slice was PascalCase).</summary>
    private static JsonNode? Prop(JsonObject? o, string name)
    {
        if (o == null) return null;
        foreach (var (k, v) in o) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    private static JsonObject Normalised(JsonObject entity) => (JsonObject)JsonNode.Parse(entity.ToJsonString())!;

    private static Vector2 PositionOf(JsonObject entity, string guid)
    {
        if (entity["SimTransform"] is JsonObject t && t["Position"] is JsonArray p && p.Count >= 2)
            return new Vector2((float)p[0]!.GetValue<double>(), (float)p[1]!.GetValue<double>());
        throw new InvalidOperationException($"[Terrain] Entity {guid} carries a terrain block / feature but no SimTransform.Position.");
    }

    /// <summary>The shape of a line / area feature: the entity's <c>EditablePolyline.Points</c> (<c>{ "X", "Y" }</c>, RELATIVE to the entity's position — the <c>EditablePolylineTranslator</c>'s shape) as WORLD points.</summary>
    private static Vector2[] PolylineOf(JsonObject entity, string guid, Vector2 origin)
    {
        if (Prop(entity, "EditablePolyline") is not JsonObject poly || poly["Points"] is not JsonArray pts)
            throw new InvalidOperationException($"[Terrain] Entity {guid} is a line / area terrain feature but carries no EditablePolyline.");
        var list = new List<Vector2>(pts.Count);
        foreach (var node in pts)
        {
            if (node is not JsonObject p) continue;
            list.Add(new Vector2(origin.X + (float)(p["X"]?.GetValue<double>() ?? 0.0), origin.Y + (float)(p["Y"]?.GetValue<double>() ?? 0.0)));
        }
        return list.ToArray();
    }
}
