using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18c) — a block RECIPE (<c>assets/terrain-recipes/&lt;id&gt;.recipe.json</c>): what a <see cref="TerrainBlock"/> expands into.
    /// <code>{ "id":"hamlet", "houses": { "templates":["house-2f"], "count":[4,7], "radius":60, "minSpacing":6 }, "woods": { "count":[0,2], "size":[20,40] } }</code>
    /// Shipped data: the content hash covers the EXPANDED world, so a recipe edit reaches a scenario only when it is compiled again.
    /// </summary>
    public sealed record TerrainRecipe(string Id, IReadOnlyList<string> Templates, int HousesMin, int HousesMax, float Radius, float MinSpacing, int WoodsMin, int WoodsMax, float WoodSizeMin, float WoodSizeMax)
    {
        public const string Suffix = ".recipe.json";

        /// <summary>⭐ T-2 (CE-4429 G-4) — one kind of item a SCATTER places: <c>"clutter"</c> (<see cref="Type"/> a <see cref="ClutterTypes"/> id), <c>"tree"</c> or <c>"bush"</c> (a small scrub patch), with its draw weight.</summary>
        public sealed record ScatterItem(string Kind, string? Type, float Weight);

        /// <summary>The items a scatter of this recipe draws from (empty ⇒ the recipe cannot be scattered). <c>"scatter": { "density": 3, "items": [ { "kind": "clutter", "type": "car", "weight": 1 }, { "kind": "tree", "weight": 4 } ] }</c>.</summary>
        public IReadOnlyList<ScatterItem> Scatter { get; init; } = Array.Empty<ScatterItem>();

        /// <summary>Items per 100 m² of the scattered area when the feature gives none.</summary>
        public float ScatterDensity { get; init; } = 1f;

        public static TerrainRecipe Parse(string json, string where = "recipe")
        {
            JsonObject obj;
            try { obj = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException($"[Terrain] {where}: not a JSON object."); }
            catch (System.Text.Json.JsonException e) { throw new ArgumentException($"[Terrain] {where}: not valid JSON: {e.Message}", e); }
            string id = (string?)obj["id"] ?? throw new ArgumentException($"[Terrain] {where}: no 'id'.");
            var houses = obj["houses"] as JsonObject;
            var templates = new List<string>();
            int hMin = 0, hMax = 0; float radius = 50f, spacing = 5f;
            if (houses != null)
            {
                if (houses["templates"] is JsonArray ts) foreach (var t in ts) templates.Add((string?)t ?? throw new ArgumentException($"[Terrain] {where}: houses.templates must be strings."));
                (hMin, hMax) = Range(houses["count"], $"{where}.houses.count");
                radius = houses["radius"] is JsonNode r ? (float)r.GetValue<double>() : radius;
                spacing = houses["minSpacing"] is JsonNode sp ? (float)sp.GetValue<double>() : spacing;
                if (hMax > 0 && templates.Count == 0) throw new ArgumentException($"[Terrain] {where}: houses need at least one template.");
            }
            var woods = obj["woods"] as JsonObject;
            int wMin = 0, wMax = 0; float sMin = 15f, sMax = 30f;
            if (woods != null)
            {
                (wMin, wMax) = Range(woods["count"], $"{where}.woods.count");
                if (woods["size"] is JsonNode sz)
                {
                    var (a, b) = RangeF(sz, $"{where}.woods.size");
                    sMin = a; sMax = b;
                }
            }
            if (radius <= 0f) throw new ArgumentException($"[Terrain] {where}: houses.radius must be positive.");
            var items = new List<ScatterItem>();
            float density = 1f;
            if (obj["scatter"] is JsonObject sc)
            {
                if (sc["density"] is JsonNode dn) density = (float)dn.GetValue<double>();
                if (density <= 0f) throw new ArgumentException($"[Terrain] {where}: scatter.density must be positive.");
                if (sc["items"] is not JsonArray arr || arr.Count == 0) throw new ArgumentException($"[Terrain] {where}: scatter needs a non-empty 'items' array.");
                foreach (var node in arr)
                {
                    if (node is not JsonObject it) throw new ArgumentException($"[Terrain] {where}: a scatter item must be an object.");
                    string kind = (string?)it["kind"] ?? throw new ArgumentException($"[Terrain] {where}: a scatter item has no 'kind'.");
                    if (kind != "clutter" && kind != "tree" && kind != "bush") throw new ArgumentException($"[Terrain] {where}: scatter item kind '{kind}' is not clutter, tree or bush.");
                    string? type = (string?)it["type"];
                    if (kind == "clutter") ClutterTypes.Get(type, where + ".scatter");
                    float weight = it["weight"] is JsonNode wn ? (float)wn.GetValue<double>() : 1f;
                    if (weight <= 0f) throw new ArgumentException($"[Terrain] {where}: a scatter item's weight must be positive.");
                    items.Add(new ScatterItem(kind, type, weight));
                }
            }
            return new TerrainRecipe(id, templates, hMin, hMax, radius, spacing, wMin, wMax, sMin, sMax) { Scatter = items, ScatterDensity = density };
        }

        private static (int, int) Range(JsonNode? n, string where)
        {
            if (n is JsonValue v) { int c = v.GetValue<int>(); return (c, c); }
            if (n is JsonArray a && a.Count == 2) { int lo = a[0]!.GetValue<int>(), hi = a[1]!.GetValue<int>(); if (lo < 0 || hi < lo) throw new ArgumentException($"[Terrain] {where}: bad range."); return (lo, hi); }
            throw new ArgumentException($"[Terrain] {where}: expected a count or [min, max].");
        }

        private static (float, float) RangeF(JsonNode? n, string where)
        {
            if (n is JsonValue v) { float c = (float)v.GetValue<double>(); return (c, c); }
            if (n is JsonArray a && a.Count == 2) { float lo = (float)a[0]!.GetValue<double>(), hi = (float)a[1]!.GetValue<double>(); if (lo <= 0 || hi < lo) throw new ArgumentException($"[Terrain] {where}: bad range."); return (lo, hi); }
            throw new ArgumentException($"[Terrain] {where}: expected a size or [min, max].");
        }

        /// <summary>The recipe <paramref name="id"/> from the first of <paramref name="roots"/> that holds <c>&lt;id&gt;.recipe.json</c>; throws naming the roots when none does.</summary>
        public static TerrainRecipe Find(string id, IEnumerable<string?> roots)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(new[] { '/', '\\' }) >= 0 || id.Contains("..")) throw new ArgumentException($"[Terrain] Recipe id '{id}' is not a plain name.");
            var tried = new List<string>();
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var path = Path.Combine(root, id + Suffix);
                tried.Add(root);
                if (File.Exists(path)) return Parse(File.ReadAllText(path), path);
            }
            throw new FileNotFoundException($"[Terrain] Block recipe '{id}' was not found in [{string.Join(", ", tried)}].");
        }

        /// <summary>The recipe roots of a terrain folder: the <c>terrain-recipes</c> beside its <c>assets/terrain</c>, then the NAS's.</summary>
        public static IEnumerable<string?> RootsFor(string? terrainFolder)
        {
            if (terrainFolder != null)
            {
                var terrains = Path.GetDirectoryName(Path.GetFullPath(terrainFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var assets = terrains == null ? null : Path.GetDirectoryName(terrains);
                if (assets != null) yield return Path.Combine(assets, ContentLayout.TerrainRecipesDirectoryName);
            }
            string? nas = null;
            try { nas = ContentLayout.NasRoot; } catch (Exception) { }
            if (nas != null) yield return ContentLayout.TerrainRecipeAssets(nas);
        }
    }
}
