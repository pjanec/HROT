using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐ CE-4141 (AQ108 D25) — what the scenario picker's side pane shows about a scenario beyond its name: facts READ FROM <c>scenario.json</c> — the header's description, terrain and
/// subsystem, and how many entities the file holds. Only what the file has: an absent field is <see langword="null"/>, never a placeholder (no objectives / triggers section exists in the
/// file today). Read lazily on first ask and cached per path, re-checked against the file's write time at most every <see cref="RecheckMilliseconds"/> — never parsed per frame.
/// </summary>
public static class ScenarioPaneFacts
{
    /// <summary>How long a cached answer is trusted before the file's write time is looked at again (ms).</summary>
    public const int RecheckMilliseconds = 2000;

    /// <summary>The facts of one scenario file.</summary>
    public sealed record Facts(string? Description, string? TerrainName, string? SubsystemType, int EntityCount,
        IReadOnlyList<Fdp.Toolkit.Scenario.ScenarioParamDecl>? Params = null, bool UsesVariants = false);   // ⭐ AQ108 D24 — what the run setup offers: the header's params and whether the file has any variant content

    private sealed record Cached(DateTime Written, long CheckedAt, object? Value);
    private static readonly Dictionary<string, Cached> Cache = new(StringComparer.Ordinal);

    /// <summary>How many times a file was actually parsed — a probe for the rails.</summary>
    public static int Parses { get; private set; }

    /// <summary>The facts of <paramref name="scenarioJsonPath"/>; <see langword="null"/> when the file is missing or unreadable.</summary>
    public static Facts? Get(string scenarioJsonPath, long? nowMs = null) => ReadCached<Facts>(scenarioJsonPath, nowMs, Read);

    /// <summary>The ONE cache: a reader's answer per file path, re-checked against the file's write time at most every <see cref="RecheckMilliseconds"/> (scenario.json and terrain.json both go through it).</summary>
    private static T? ReadCached<T>(string path, long? nowMs, Func<string, T?> read) where T : class
    {
        if (string.IsNullOrEmpty(path)) return null;
        long now = nowMs ?? Environment.TickCount64;
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var hit) && now - hit.CheckedAt < RecheckMilliseconds) return hit.Value as T;
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(path); } catch (Exception) { return null; }
            if (hit != null && hit.Written == written) { Cache[path] = hit with { CheckedAt = now }; return hit.Value as T; }
            var value = read(path);
            Cache[path] = new Cached(written, now, value);
            return value;
        }
    }

    // ── ⭐ B-9 N1 pane — a TERRAIN's facts (docs/UX/DESIGN_New_Scenario_And_Recipe.md §2) ─────────────────────────────

    /// <summary>What <c>terrain.json</c> says about a terrain: its name, geo origin (null when it states none) and road networks. Only what the file has.</summary>
    public sealed record TerrainFacts(string? Name, double? Lat, double? Lon, double? Alt, IReadOnlyList<string> RoadNetworks, bool HasWorld);

    /// <summary>The facts of the terrain whose definition file is <paramref name="terrainJsonPath"/> (parsed by the ONE <c>TerrainDefinitionParser</c>); <see langword="null"/> when missing or malformed. Cached like scenario facts.</summary>
    public static TerrainFacts? GetTerrain(string terrainJsonPath, long? nowMs = null) => ReadCached<TerrainFacts>(terrainJsonPath, nowMs, ReadTerrain);

    private static TerrainFacts? ReadTerrain(string path)
    {
        try
        {
            Parses++;
            var def = Fdp.Toolkit.Terrain.TerrainDefinitionParser.Parse(File.ReadAllText(path));
            var o = def.Origin;
            return new TerrainFacts(string.IsNullOrEmpty(def.Name) ? null : def.Name, o?.Lat, o?.Lon, o?.Alt, def.RoadNetworks, !string.IsNullOrEmpty(def.World));
        }
        catch (Exception) { return null; }
    }

    /// <summary>The still of a terrain: <c>preview.jpg</c> (else <c>.png</c>) beside <paramref name="terrainJsonPath"/> — the SAME rule and reader as a scenario's (<see cref="PreviewOf"/>).</summary>
    public static string? TerrainPreviewOf(string terrainJsonPath) => PreviewOf(terrainJsonPath);

    /// <summary>
    /// The scenario recipes whose header names <paramref name="terrainName"/> as their terrain — the SAME lister New Scenario reads (<c>RecipeFiles.ScenarioRecipes</c>),
    /// each recipe's header through <see cref="Get"/> (cached). Recipe names, sorted.
    /// </summary>
    public static IReadOnlyList<string> RecipesOn(string terrainName)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(terrainName)) return result;
        foreach (var recipe in Recipes.RecipeFiles.ScenarioRecipes())
        {
            var file = Path.Combine(AssetRoots.ScenariosRecipesRoot, recipe, Fdp.Toolkit.Orchestration.ContentLayout.ScenarioFileName);
            if (Get(file)?.TerrainName is { } t && string.Equals(t, terrainName, StringComparison.OrdinalIgnoreCase)) result.Add(recipe);
        }
        return result;
    }

    private static Facts? Read(string path)
    {
        try
        {
            Parses++;
            using var stream = File.OpenRead(path);
            if (System.Text.Json.Nodes.JsonNode.Parse(stream) is not System.Text.Json.Nodes.JsonObject root) return null;
            string? desc = null, terrain = null, subsystem = null;
            if ((root["header"] ?? root["Header"]) is System.Text.Json.Nodes.JsonObject h)
            {
                desc = Text(h, "description"); terrain = Text(h, "terrainName"); subsystem = Text(h, "subsystemType");
            }
            int entities = (root["entities"] ?? root["Entities"]) is System.Text.Json.Nodes.JsonObject e ? e.Count : 0;
            // ⭐ AQ108 D24 — the run setup's facts, read with the ONE header reader and the ONE variant test (the same ones the load uses)
            var decls = Fdp.Toolkit.Scenario.ScenarioHeaderJson.Read(root)?.Params;
            return new Facts(desc, terrain, subsystem, entities, decls, Fdp.Toolkit.Scenario.ScenarioVariants.Uses(root));
        }
        catch (Exception) { return null; }   // unreadable ⇒ no facts, the pane shows the name alone
    }

    private static string? Text(System.Text.Json.Nodes.JsonObject o, string name)
        => o[name] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    /// <summary>The preview file names of a scenario folder, in the order they are tried: ⭐ CE-4271 <c>preview.jpg</c> (a generated 320 × 180 JPEG, §2j) first, then <c>preview.png</c> (a hand-made one).</summary>
    public static readonly string[] PreviewFileNames = { "preview.jpg", "preview.png" };

    /// <summary>The preview picture of a scenario folder (<c>preview.jpg</c>, else <c>preview.png</c>, beside <c>scenario.json</c>) when one exists; else <see langword="null"/>.
    /// ⭐ The ONE reader — the pane (here) and the picker's tile (<c>AssetPickerLauncher</c>) both call it.</summary>
    public static string? PreviewOf(string scenarioJsonPath)
    {
        if (string.IsNullOrEmpty(scenarioJsonPath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(scenarioJsonPath);
            if (dir == null) return null;
            foreach (var name in PreviewFileNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }
        catch (Exception) { return null; }
    }
}
