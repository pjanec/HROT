using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ T-2 (CE-4429, docs/DESIGN_Terrain_Authoring.md §18g) — the kinds of a <see cref="TerrainFeature"/> beyond the house, as parts the ONE world parser already reads: <c>wall</c> / <c>fence</c> (a hedge is a wall of the
    /// <c>hedge</c> material), <c>surface</c> forest / scrub / water, <c>tree</c>, <c>prism</c> (clutter), <c>slab</c> + <c>ramp</c> under one <c>"structure"</c> (a bridge) and the area SCATTER. Pure and deterministic like the rest of
    /// the expander: coordinates in millimetres, a scatter draws from its own seed. A part's label is <c>&lt;key&gt;/&lt;i&gt;</c>.
    /// </summary>
    public static partial class FeatureExpander
    {
        public const float DefaultWallHeight = 2.0f, DefaultFenceHeight = 1.6f, DefaultHedgeHeight = 1.5f, HedgeThickness = 0.8f;
        public const float DefaultBridgeWidth = 4.0f, BridgeDeckHeight = 1.5f, BridgeRampLength = 6.0f;
        public const int MaxScatterItems = 2000;

        /// <summary>
        /// The parts of ONE feature of <paramref name="kind"/>. <paramref name="points"/> are WORLD coordinates (the entity's position plus its polyline's relative vertices) for the line and area kinds; <paramref name="at"/> is the
        /// entity's position (a house, a tree, a clutter item). <paramref name="template"/>, <paramref name="pad"/> and <paramref name="seed"/> mean what <see cref="TerrainFeature"/> says per kind.
        /// </summary>
        public static IReadOnlyList<JsonObject> OfKind(byte kind, string key, string template, float headingDeg, float pad, uint seed, Vector2 at, IReadOnlyList<Vector2> points,
                                                       TerrainWorld baseWorld, TerrainAssets assets, IEnumerable<string?> recipeRoots)
        {
            string where = $"feature '{key}'";
            switch (kind)
            {
                case TerrainFeature.House:
                    return new[] { House(baseWorld, assets, template, at, headingDeg, pad, key) };
                case TerrainFeature.Wall:
                    return new[] { Line("wall", key, template.Length > 0 ? template : TerrainMaterialLibrary.DefaultMaterial, pad > 0 ? pad : DefaultWallHeight, 0f, points, where) };
                case TerrainFeature.Fence:
                    return new[] { Line("fence", key, template.Length > 0 ? template : TerrainMaterialLibrary.DefaultFenceMaterial, pad > 0 ? pad : DefaultFenceHeight, 0f, points, where) };
                case TerrainFeature.Hedge:
                    return new[] { Line("wall", key, template.Length > 0 ? template : "hedge", pad > 0 ? pad : DefaultHedgeHeight, HedgeThickness, points, where) };
                case TerrainFeature.Wood:
                    return new[] { Area("forest", key + "/0", points, pad > 0 ? pad : (float?)null, where) };
                case TerrainFeature.Scrub:
                    return new[] { Area("scrub", key + "/0", points, null, where) };
                case TerrainFeature.Water:
                    return new[] { Area("water", key + "/0", points, null, where) };
                case TerrainFeature.Tree:
                    return new[] { Tree(key + "/0", at) };
                case TerrainFeature.Clutter:
                    return new[] { Clutter(key + "/0", ClutterTypes.Get(template, where), at, headingDeg) };
                case TerrainFeature.Bridge:
                    return Bridge(key, points, pad > 0 ? pad : DefaultBridgeWidth, where);
                case TerrainFeature.Scatter:
                    return Scatter(key, TerrainRecipe.Find(template, recipeRoots), seed, pad, points, baseWorld, where);
                default:
                    throw new ArgumentException($"[Terrain] {where}: unknown feature kind {kind}.");
            }
        }

        /// <summary>True for the kinds that carry a shape (a polyline beside the component): they are the line and area kinds.</summary>
        public static bool NeedsShape(byte kind) => kind is TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge or TerrainFeature.Wood or TerrainFeature.Scrub or TerrainFeature.Water
                                                       or TerrainFeature.Bridge or TerrainFeature.Scatter;

        /// <summary>True when <paramref name="kind"/> is one this expander knows (the others are ignored by a caller, as before).</summary>
        public static bool IsKnown(byte kind) => kind is >= TerrainFeature.House and <= TerrainFeature.Scatter;

        // ── lines ─────────────────────────────────────────────────────────────────────────────────────────────────

        private static JsonObject Line(string partKind, string key, string material, float height, float thickness, IReadOnlyList<Vector2> points, string where)
        {
            if (points.Count < 2) throw new ArgumentException($"[Terrain] {where}: a line feature needs at least two points (got {points.Count}).");
            var props = new JsonObject { ["kind"] = partKind, ["material"] = material, ["height"] = Round(height), ["label"] = key + "/0" };
            if (thickness > 0f) props["thickness"] = Round(thickness);
            var coords = new JsonArray();
            foreach (var p in points) coords.Add(new JsonArray(Round(p.X), Round(p.Y)));
            return Feature(props, new JsonObject { ["type"] = "LineString", ["coordinates"] = coords });
        }

        // ── areas ─────────────────────────────────────────────────────────────────────────────────────────────────

        private static JsonObject Area(string surface, string label, IReadOnlyList<Vector2> points, float? density, string where)
        {
            if (points.Count < 3) throw new ArgumentException($"[Terrain] {where}: an area feature needs at least three points (got {points.Count}).");
            var props = new JsonObject { ["kind"] = "surface", ["surface"] = surface, ["label"] = label };
            if (density is float d) props["density"] = Round(d);
            return Feature(props, new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(Ring(points)) });
        }

        /// <summary>A closed GeoJSON ring, counter-clockwise (the parser's winding), rounded to millimetres.</summary>
        private static JsonArray Ring(IReadOnlyList<Vector2> points)
        {
            var ring = points.Select(p => new Vector2(Round(p.X), Round(p.Y))).ToList();
            if (ring.Count > 1 && ring[0] == ring[^1]) ring.RemoveAt(ring.Count - 1);
            if (PolygonMath.SignedArea2(ring) < 0f) ring.Reverse();
            var coords = new JsonArray();
            foreach (var p in ring) coords.Add(new JsonArray(p.X, p.Y));
            coords.Add(new JsonArray(ring[0].X, ring[0].Y));
            return coords;
        }

        // ── points ────────────────────────────────────────────────────────────────────────────────────────────────

        private static JsonObject Tree(string label, Vector2 at)
            => Feature(new JsonObject { ["kind"] = "tree", ["label"] = label }, new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(Round(at.X), Round(at.Y)) });

        private static JsonObject Clutter(string label, ClutterTypes.Type type, Vector2 at, float headingDeg)
        {
            float rad = headingDeg * MathF.PI / 180f, c = MathF.Cos(rad), s = MathF.Sin(rad), hl = type.Length / 2f, hw = type.Width / 2f;
            Vector2 P(float lx, float ly) => new(at.X + lx * c - ly * s, at.Y + lx * s + ly * c);
            var ring = new[] { P(-hl, -hw), P(hl, -hw), P(hl, hw), P(-hl, hw) };
            var props = new JsonObject { ["kind"] = "prism", ["material"] = type.Material, ["height"] = Round(type.Height), ["label"] = label };
            return Feature(props, new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(Ring(ring)) });
        }

        // ── bridge ────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A bridge from the first to the last point (the banks): a deck SLAB <see cref="BridgeDeckHeight"/> above the ground under it, <paramref name="width"/> wide, and a RAMP at each end
        /// (<see cref="BridgeRampLength"/> long, its outer foot buried at the ground), all under ONE <c>"structure": "&lt;key&gt;"</c> — a collapsible building with no panels (O-2). The ground under the deck may be water: the deck is
        /// walkable, the water cuts the ground only.
        /// </summary>
        private static IReadOnlyList<JsonObject> Bridge(string key, IReadOnlyList<Vector2> points, float width, string where)
        {
            if (points.Count < 2) throw new ArgumentException($"[Terrain] {where}: a bridge needs two points (the banks).");
            var a = points[0]; var b = points[^1];
            var d = b - a; float len = d.Length();
            if (len < 1f) throw new ArgumentException($"[Terrain] {where}: a bridge's two points are the same place.");
            var u = d / len; var n = new Vector2(-u.Y, u.X); float hw = width / 2f;
            JsonArray Quad(Vector2 p0, float z0, Vector2 p1, float z1)   // p0 → p1 along the bridge; z0 / z1 are the heights above the ground at each end
            {
                var corners = new[] { (p0 - n * hw, z0), (p1 - n * hw, z1), (p1 + n * hw, z1), (p0 + n * hw, z0) };
                var ring = new JsonArray();
                foreach (var (p, z) in corners) ring.Add(new JsonArray(Round(p.X), Round(p.Y), Round(z)));
                ring.Add(new JsonArray(Round(corners[0].Item1.X), Round(corners[0].Item1.Y), Round(corners[0].Item2)));
                return ring;
            }
            JsonObject Part(string kind, string label, JsonArray ring)
                => Feature(new JsonObject { ["kind"] = kind, ["zRef"] = "ground", ["structure"] = key, ["label"] = label },
                           new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) });
            var deck = Part("slab", key + "/0", Quad(a, BridgeDeckHeight, b, BridgeDeckHeight));
            var rampA = Part("ramp", key + "/1", Quad(a - u * BridgeRampLength, 0f, a, BridgeDeckHeight));
            var rampB = Part("ramp", key + "/2", Quad(b, BridgeDeckHeight, b + u * BridgeRampLength, 0f));
            return new[] { deck, rampA, rampB };
        }

        // ── scatter ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ G-4 — an AREA scattered with a recipe's items, ONE entity (R-4199 B-2): <c>area / 100 · density</c> items (the feature's <paramref name="pad"/> when given, else the recipe's), each a clutter prism, a tree or a bush
        /// (a small scrub patch) drawn by weight and placed inside the polygon (and out of the base's water), from the feature's seed — the same inputs are the same bytes. Capped at <see cref="MaxScatterItems"/>.
        /// </summary>
        private static IReadOnlyList<JsonObject> Scatter(string key, TerrainRecipe recipe, uint seed, float pad, IReadOnlyList<Vector2> points, TerrainWorld baseWorld, string where)
        {
            if (recipe.Scatter.Count == 0) throw new ArgumentException($"[Terrain] {where}: recipe '{recipe.Id}' has no 'scatter' section.");
            if (points.Count < 3) throw new ArgumentException($"[Terrain] {where}: a scatter needs an area of at least three points.");
            var poly = points.ToList();
            if (poly.Count > 1 && poly[0] == poly[^1]) poly.RemoveAt(poly.Count - 1);
            var (min, max) = Box(poly);
            float area = MathF.Abs(PolygonMath.SignedArea2(poly)) / 2f;
            float density = pad > 0f ? pad : recipe.ScatterDensity;
            int want = (int)MathF.Min(MaxScatterItems, MathF.Round(area / 100f * density));
            float total = recipe.Scatter.Sum(i => i.Weight);
            var rng = new Rng(seed);
            var features = new List<JsonObject>();
            for (int n = 0; n < want; n++)
            {
                float t = rng.NextFloat() * total;
                var item = recipe.Scatter[^1];
                foreach (var it in recipe.Scatter) { if ((t -= it.Weight) < 0f) { item = it; break; } }
                // a place inside the area and out of the water; the draws are made whether or not it is found, so the sequence does not depend on the base
                Vector2? place = null;
                for (int a = 0; a < Attempts; a++)
                {
                    var p = new Vector2(Round(min.X + rng.NextFloat() * (max.X - min.X)), Round(min.Y + rng.NextFloat() * (max.Y - min.Y)));
                    if (place == null && PolygonMath.Contains(poly, p) && !InWater(baseWorld, p, new[] { p })) place = p;
                }
                float heading = Round(rng.NextFloat() * 360f), bush = 0.6f + rng.NextFloat() * 0.6f;
                if (place is not Vector2 at) continue;
                string label = $"{key}/{features.Count}";
                switch (item.Kind)
                {
                    case "clutter": features.Add(Clutter(label, ClutterTypes.Get(item.Type, where), at, heading)); break;
                    case "tree": features.Add(Tree(label, at)); break;
                    default:   // a bush: a small scrub octagon
                    {
                        var ring = new Vector2[8];
                        for (int k = 0; k < 8; k++) { float ang = k * MathF.PI / 4f; ring[k] = new Vector2(at.X + bush * MathF.Cos(ang), at.Y + bush * MathF.Sin(ang)); }
                        features.Add(Area("scrub", label, ring, null, where));
                        break;
                    }
                }
            }
            return features;
        }
    }
}
