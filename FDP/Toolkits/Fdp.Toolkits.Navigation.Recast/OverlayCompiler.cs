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

        var features = new List<JsonObject>();
        var blockSummaries = new JsonArray();
        int houses = 0, woods = 0;
        foreach (var (guid, e) in blocks)
        {
            var tb = (JsonObject)e["TerrainBlock"]!;
            string recipeId = (string?)tb["Recipe"] ?? throw new InvalidOperationException($"[Terrain] TerrainBlock {guid} has no Recipe.");
            var recipe = TerrainRecipe.Find(recipeId, roots);
            uint seed = tb["Seed"] is JsonNode sn ? (uint)sn.GetValue<double>() : 0u;
            float heading = tb["HeadingDeg"] is JsonNode hn ? (float)hn.GetValue<double>() : 0f;
            byte fit = tb["Fit"] is JsonNode fn ? (byte)fn.GetValue<int>() : TerrainBlock.FitAll;
            var at = PositionOf(e, guid);
            var x = FeatureExpander.Expand(recipe, seed, at, heading, fit, baseWorld, guid, assets);
            features.AddRange(x.Features);
            houses += x.Houses; woods += x.Woods;
            var outline = new JsonArray();
            foreach (var p in Hull(x.Points)) outline.Add(new JsonArray(MathF.Round(p.X * 100f) / 100f, MathF.Round(p.Y * 100f) / 100f));
            blockSummaries.Add(new JsonObject { ["key"] = guid, ["recipe"] = recipeId, ["seed"] = seed, ["houses"] = x.Houses, ["woods"] = x.Woods, ["clutter"] = 0, ["outline"] = outline });
        }
        foreach (var (guid, e) in feats)
        {
            var tf = (JsonObject)e["TerrainFeature"]!;
            int kind = tf["Kind"] is JsonNode kn ? kn.GetValue<int>() : 0;
            if (kind != TerrainFeature.House) continue;   // v1: houses only
            string template = (string?)tf["Template"] ?? throw new InvalidOperationException($"[Terrain] TerrainFeature {guid} has no Template.");
            float heading = tf["HeadingDeg"] is JsonNode hn ? (float)hn.GetValue<double>() : 0f;
            float pad = tf["Pad"] is JsonNode pn ? (float)pn.GetValue<double>() : 0f;
            features.Add(FeatureExpander.House(baseWorld, assets, template, PositionOf(e, guid), heading, pad, guid));
            houses++;
        }
        features.Sort((a, b) => string.CompareOrdinal(Label(a), Label(b)));

        Directory.CreateDirectory(partDir);
        try
        {
            // ⭐ the overlay world declares the BASE's bounds (grown only to hold a feature outside them): the merged bounds are the union, and a tile's navmesh key covers the ground rectangle,
            //   so bounds that moved would give every base tile another key and the base's navtiles would never hit (measured: 0 of 610 with the features' own ±50 m margin)
            var bmin = baseWorld.BoundsMin; var bmax = baseWorld.BoundsMax;
            foreach (var feat in features) GrowBounds(((JsonObject)feat["geometry"]!)["coordinates"], ref bmin, ref bmax);
            var world = new JsonObject
            {
                ["type"] = "FeatureCollection",
                ["hrot"] = new JsonObject { ["schemaVersion"] = 1, ["groundZ"] = baseWorld.GroundZ, ["bounds"] = new JsonArray(bmin.X, bmin.Y, bmax.X, bmax.Y) },
            };
            var arr = new JsonArray(); foreach (var f in features) arr.Add(f);
            world["features"] = arr;
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

    private static void GrowBounds(JsonNode? coords, ref Vector2 min, ref Vector2 max)
    {
        if (coords is not JsonArray a || a.Count == 0) return;
        if (a[0] is JsonValue)
        {
            float x = (float)a[0]!.GetValue<float>(), y = (float)a[1]!.GetValue<float>();
            min = Vector2.Min(min, new Vector2(x, y)); max = Vector2.Max(max, new Vector2(x, y));
            return;
        }
        foreach (var c in a) GrowBounds(c, ref min, ref max);
    }

    /// <summary>A property by name, either casing (the writer's scenario files are camelCase, an older slice was PascalCase).</summary>
    private static JsonNode? Prop(JsonObject? o, string name)
    {
        if (o == null) return null;
        foreach (var (k, v) in o) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    private static JsonObject Normalised(JsonObject entity) => (JsonObject)JsonNode.Parse(entity.ToJsonString())!;

    private static string Label(JsonObject f) => (string)((JsonObject)f["properties"]!)["label"]!;

    private static Vector2 PositionOf(JsonObject entity, string guid)
    {
        if (entity["SimTransform"] is JsonObject t && t["Position"] is JsonArray p && p.Count >= 2)
            return new Vector2((float)p[0]!.GetValue<double>(), (float)p[1]!.GetValue<double>());
        throw new InvalidOperationException($"[Terrain] Entity {guid} carries a terrain block / feature but no SimTransform.Position.");
    }

    /// <summary>The convex hull (Andrew's monotone chain), counter-clockwise, no repeated closing point.</summary>
    internal static List<Vector2> Hull(IReadOnlyList<Vector2> pts)
    {
        var p = new List<Vector2>(pts);
        p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        if (p.Count < 3) return p;
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new List<Vector2>();
        foreach (var q in p) { while (h.Count >= 2 && Cross(h[^2], h[^1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
        int lower = h.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--) { while (h.Count >= lower && Cross(h[^2], h[^1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
        h.RemoveAt(h.Count - 1);
        return h;
    }
}
