using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>One block's expansion: its feature objects (GeoJSON, labelled <c>&lt;key&gt;/&lt;i&gt;</c>), the counts and the <c>overlay.json</c> summary row.</summary>
    public sealed record BlockExpansion(IReadOnlyList<JsonObject> Features, int Houses, int Woods, JsonObject Summary);

    /// <summary>
    /// ⭐ O-7 B (CE-4418, docs/DESIGN_Terrain_Authoring.md §18e-2 B-2) — the ONE per-entity expansion of a terrain block / feature and the overlay world document they go into. The scenario-save
    /// <c>OverlayCompiler</c> (entities read from the scenario JSON) and the editor's <c>LiveOverlay</c> (entities read from the ECS world) both call it, so a block expands the same way in both
    /// — ⛔ never a second copy. Pure and deterministic.
    /// </summary>
    public static class OverlayExpansion
    {
        /// <summary>Expands one block placed at <paramref name="at"/>; every part's label is prefixed by <paramref name="key"/> (the block entity's scenario guid).</summary>
        public static BlockExpansion Block(string key, string recipeId, uint seed, float headingDeg, byte fit, Vector2 at, TerrainWorld baseWorld, TerrainAssets assets, IEnumerable<string?> recipeRoots, TerrainRoads? roads = null)
        {
            var recipe = TerrainRecipe.Find(recipeId, recipeRoots);
            var x = FeatureExpander.Expand(recipe, seed, at, headingDeg, fit, baseWorld, key, assets, roads);
            var outline = new JsonArray();
            foreach (var p in Hull(x.Points)) outline.Add(new JsonArray(MathF.Round(p.X * 100f) / 100f, MathF.Round(p.Y * 100f) / 100f));
            var summary = new JsonObject { ["key"] = key, ["recipe"] = recipeId, ["seed"] = seed, ["houses"] = x.Houses, ["woods"] = x.Woods, ["clutter"] = 0, ["outline"] = outline };
            return new BlockExpansion(x.Features, x.Houses, x.Woods, summary);
        }

        /// <summary>
        /// ⭐ T-2 (CE-4429) — ONE placed <see cref="TerrainFeature"/> as the expansion reads it: its key (the entity's guid), kind, the per-kind text / numbers (<see cref="TerrainFeature"/> says what each means), the entity's position and — for
        /// the line and area kinds — the shape's points in WORLD coordinates (the entity's position plus the polyline's relative vertices). The compiler builds it from the scenario JSON and the live overlay from the ECS components:
        /// both feed <see cref="Feature"/>, so a kind expands the same way in both.
        /// </summary>
        public sealed record FeatureSpec(string Key, byte Kind, string Template, float HeadingDeg, float Pad, uint Seed, Vector2 At, IReadOnlyList<Vector2> Points);

        /// <summary>The parts of one feature (<c>&lt;key&gt;/&lt;i&gt;</c>) — a house, a wall, a wood, a bridge, a scatter's items … (<see cref="FeatureExpander.OfKind"/>).</summary>
        public static IReadOnlyList<JsonObject> Feature(FeatureSpec s, TerrainWorld baseWorld, TerrainAssets assets, IEnumerable<string?> recipeRoots)
            => FeatureExpander.OfKind(s.Kind, s.Key, s.Template, s.HeadingDeg, s.Pad, s.Seed, s.At, s.Points, baseWorld, assets, recipeRoots);

        /// <summary>One house feature (<c>&lt;key&gt;/0</c>).</summary>
        public static JsonObject House(string key, string template, float headingDeg, float pad, Vector2 at, TerrainWorld baseWorld, TerrainAssets assets)
            => FeatureExpander.House(baseWorld, assets, template, at, headingDeg, pad, key);

        /// <summary>Orders features by label (ordinal) — the overlay's canonical order.</summary>
        public static void SortByLabel(List<JsonObject> features) => features.Sort((a, b) => string.CompareOrdinal(Label(a), Label(b)));

        /// <summary>A feature's label (<c>properties.label</c>).</summary>
        public static string Label(JsonObject feature) => (string)((JsonObject)feature["properties"]!)["label"]!;

        /// <summary>
        /// The overlay's world document: a FeatureCollection declaring the BASE's bounds (grown only to hold a feature outside them) — the merged bounds are the union, and a tile's navmesh key covers the ground rectangle,
        /// so bounds that moved would give every base tile another key and the base's navtiles would never hit (measured: 0 of 610 with the features' own ±50 m margin). <paramref name="features"/> are
        /// CLONED into the document (a JSON node has one parent), so cached feature objects can be reused.
        /// </summary>
        public static JsonObject WorldDocument(TerrainWorld baseWorld, IReadOnlyList<JsonObject> features)
        {
            var bmin = baseWorld.BoundsMin; var bmax = baseWorld.BoundsMax;
            foreach (var feat in features) GrowBounds(((JsonObject)feat["geometry"]!)["coordinates"], ref bmin, ref bmax);
            var world = new JsonObject
            {
                ["type"] = "FeatureCollection",
                ["hrot"] = new JsonObject { ["schemaVersion"] = 1, ["groundZ"] = baseWorld.GroundZ, ["bounds"] = new JsonArray(bmin.X, bmin.Y, bmax.X, bmax.Y) },
            };
            var arr = new JsonArray();
            foreach (var f in features) arr.Add(f.DeepClone());
            world["features"] = arr;
            return world;
        }

        /// <summary>The ground rectangle a feature's geometry spans.</summary>
        public static (Vector2 Min, Vector2 Max) BoundsOf(JsonObject feature)
        {
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            GrowBounds(((JsonObject)feature["geometry"]!)["coordinates"], ref min, ref max);
            return (min, max);
        }

        private static void GrowBounds(JsonNode? coords, ref Vector2 min, ref Vector2 max)
        {
            if (coords is not JsonArray a || a.Count == 0) return;
            if (a[0] is JsonValue)
            {
                float x = a[0]!.GetValue<float>(), y = a[1]!.GetValue<float>();
                min = Vector2.Min(min, new Vector2(x, y)); max = Vector2.Max(max, new Vector2(x, y));
                return;
            }
            foreach (var c in a) GrowBounds(c, ref min, ref max);
        }

        /// <summary>The convex hull (Andrew's monotone chain), counter-clockwise, no repeated closing point.</summary>
        public static List<Vector2> Hull(IReadOnlyList<Vector2> pts)
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
}
