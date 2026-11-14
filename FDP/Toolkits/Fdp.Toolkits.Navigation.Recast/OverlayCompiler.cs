using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core.Logging;
using Fdp.Toolkit.Navigation.Recast;

namespace Fdp.Toolkit.Terrain;

/// <summary>What one <see cref="OverlayCompiler.Compile"/> did.</summary>
public sealed record OverlayCompileResult(bool Wrote, string? Folder, string? ContentHash, string? BaseHash, int Blocks, int Houses, int Woods, int NavTiles, int NavtilesWritten, int NavtilesSeedHits, int Bridges = 0, int BridgeRoads = 0);

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
    /// <summary>
    /// ⭐ <c>CE-4592</c> (P-5; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4a G-16) — and, when the overlay CHANGES THE GROUND (a ground shape, a padded house) on a terrain with a SUMO net,
    /// a RE-ELEVATED net copy written into the overlay (the bridge net when there is one, else the base's) through the caller's <paramref name="reElevateNet"/> (FDP cannot see
    /// <c>Hrot.Terrain.InputSet.NetElevation</c>) — so SUMO's cars drive the shaped ground after save + reload (V-25: not while editing). Null ⇒ the base net, as before.
    /// </summary>
    public static OverlayCompileResult Compile(JsonObject scenarioDom, string scenarioDir, Func<string, string?> resolveTerrainDefinition, bool bakeNavtiles = true, IEnumerable<string?>? recipeRoots = null,
        IUniqueBuildings? unique = null, IBridgeNet? bridgeNet = null, Action<string, Func<double, double, double>>? reElevateNet = null)
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
        var bridgeEnds = new List<(string Key, Vector2 A, Vector2 B)>();   // ⭐ T-7a — each Bridge feature's two banks
        var generated = new SortedDictionary<string, string>(StringComparer.Ordinal);   // ⭐ T-3 part 2 — the blocks' generated templates
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
            var x = OverlayExpansion.Block(guid, recipeId, seed, heading, fit, PositionOf(e, guid), baseWorld, assets, roots, roads, unique);
            features.AddRange(x.Features);
            foreach (var (tn, tj) in x.Templates) generated[tn] = tj;
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
            if (kind == TerrainFeature.Bridge && points.Length >= 2) bridgeEnds.Add((guid, points[0], points[^1]));
        }
        OverlayExpansion.SortByLabel(features);

        Directory.CreateDirectory(partDir);
        try
        {
            var world = OverlayExpansion.WorldDocument(baseWorld, features);
            string worldName = TerrainOverlayHeader.DefaultWorldFileName;
            File.WriteAllText(Path.Combine(partDir, worldName), world.ToJsonString());
            if (generated.Count > 0)
            {
                // ⭐ T-3 part 2 — written into the overlay's own buildings/: OfOverlay hashes buildings/*, and ReadOverlay resolves a template there before the shared ones
                string bdir = Path.Combine(partDir, TerrainAssets.BuildingsFolderName); Directory.CreateDirectory(bdir);
                foreach (var (tn, tj) in generated) File.WriteAllText(Path.Combine(bdir, tn + TerrainAssets.TemplateSuffix), tj);
            }

            // ⭐ T-7a (CE-4423, §18k) — the bridges' ROAD: ends snapped to the base's road nodes; the segments go into the overlay's roads file, the edges into an overlay SUMO net
            var plans = BridgeRoads.Plan(bridgeEnds, roads);
            var bridgeRows = new JsonArray();
            string? sumoNetName = null; int snapped = 0;
            foreach (var p in plans) if (p.Snapped) snapped++;
            if (plans.Count > 0 && snapped < plans.Count)
                FdpLog<TerrainRoads>.Warn("[Terrain] {0} of {1} bridge(s) have no road node of '{2}' within {3} m of both ends: a deck, not road.", plans.Count - snapped, plans.Count, terrainName, BridgeRoads.SnapMetres);
            IReadOnlyList<IReadOnlyList<string>>? netEdges = null;
            if (snapped > 0)
            {
                File.WriteAllText(Path.Combine(partDir, TerrainOverlayHeader.RoadsFileName), BridgeRoads.RoadsJson(plans));
                if (definition.SumoNet is { } declared)
                {
                    if (bridgeNet == null) FdpLog<TerrainRoads>.Warn("[Terrain] '{0}' has a SUMO net but no netconvert was given: the overlay's bridges are road for the vehicles' net only.", terrainName);
                    else
                    {
                        string netName = Path.GetFileName(declared.File);
                        var reqs = new List<BridgeNetRequest>();
                        foreach (var p in plans) if (p.Snapped) reqs.Add(new BridgeNetRequest(p.Key, p.PosA, p.PosB));
                        netEdges = bridgeNet.Build(definition, terrainFolder, reqs, Path.Combine(partDir, netName), out var warning);
                        if (netEdges == null) FdpLog<TerrainRoads>.Warn("[Terrain] No SUMO net for the overlay's bridges (the vehicles' net still has them): {0}", warning ?? "netconvert failed");
                        else sumoNetName = netName;
                    }
                }
                int k = 0;
                foreach (var p in plans)
                {
                    var row = new JsonObject { ["key"] = p.Key, ["snapped"] = p.Snapped };
                    if (p.Snapped)
                    {
                        row["segments"] = new JsonArray(p.Segment);
                        var ids = new JsonArray();
                        if (netEdges != null) foreach (var id in netEdges[k]) ids.Add(id);
                        row["sumoEdges"] = ids; k++;
                    }
                    bridgeRows.Add(row);
                }
            }
            else foreach (var p in plans) bridgeRows.Add(new JsonObject { ["key"] = p.Key, ["snapped"] = false });

            // ⭐ CE-4592 (G-16) — the ground the overlay changes reaches SUMO: the net re-elevated over the merged heights, kept in the overlay
            if (reElevateNet != null && definition.SumoNet is { } sumo && ChangesTheGround(features))
            {
                string netName = Path.GetFileName(sumo.File), netPath = Path.Combine(partDir, netName);
                if (sumoNetName == null && File.Exists(Path.Combine(terrainFolder, sumo.File))) File.Copy(Path.Combine(terrainFolder, sumo.File), netPath, overwrite: true);
                if (File.Exists(netPath))
                {
                    var merged = TerrainWorldMerge.Merge(baseWorld, TerrainWorldParser.Parse(world.ToJsonString(), terrainName + "/overlay", assets));
                    if (merged.Height is { } shaped && !ReferenceEquals(shaped, baseWorld.Height))
                    {
                        reElevateNet(netPath, (x, y) => shaped.Sample((float)(x - sumo.AnchorX), (float)(y - sumo.AnchorY)));
                        sumoNetName = netName;
                    }
                }
            }

            string baseHash = TerrainContentHash.OfTerrain(definitionPath);
            string contentHash = TerrainContentHash.OfOverlay(partDir, baseHash);
            var header = new JsonObject
            {
                ["format"] = TerrainOverlayHeader.CurrentFormat, ["contentHash"] = contentHash,
                ["base"] = new JsonObject { ["name"] = terrainName, ["contentHash"] = baseHash },
                ["scenario"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(scenarioDir)), ["world"] = worldName, ["blocks"] = blockSummaries,
            };
            if (bridgeRows.Count > 0) header["bridges"] = bridgeRows;
            if (snapped > 0) header["hasRoads"] = true;
            if (sumoNetName != null) header["sumoNet"] = sumoNetName;
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
            return new OverlayCompileResult(true, finalDir, contentHash, baseHash, blocks.Count, houses, woods, tiles, written, hits, plans.Count, snapped);
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

    /// <summary>⭐ CE-4592 — true when a compiled part changes the ground: a ground shape, or a house standing on a pad.</summary>
    private static bool ChangesTheGround(List<JsonObject> features)
    {
        foreach (var f in features)
            if (f["properties"] is JsonObject p && ((string?)p["kind"] == "ground" || ((string?)p["kind"] == "building" && p["pad"] is not null))) return true;
        return false;
    }
}
