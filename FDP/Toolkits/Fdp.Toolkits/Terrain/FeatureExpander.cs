using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What one <see cref="FeatureExpander.Expand"/> made: the world-file features, their counts, and the points the block's outline is the hull of (local metres).</summary>
    public sealed record FeatureExpansion(IReadOnlyList<JsonObject> Features, int Houses, int Woods, IReadOnlyList<Vector2> Points);

    /// <summary>
    /// ⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18c) — a block recipe → GeoJSON-shaped world features. ⭐ PURE and DETERMINISTIC: its own xorshift generator (never <c>System.Random</c>),
    /// coordinates rounded to millimetres, so the same input is the same bytes on every machine. Houses are Point buildings (template + rotation + label <c>&lt;keyPrefix&gt;/&lt;i&gt;</c>), woods
    /// forest surface polygons (label <c>&lt;keyPrefix&gt;/w&lt;i&gt;</c>).
    /// <para>Fit: <see cref="TerrainBlock.AvoidBuildings"/> rejects a house whose footprint box (+ the recipe's spacing) meets a base building / building prism or an already placed house;
    /// <see cref="TerrainBlock.AvoidWater"/> rejects a house with a corner or centre in a base water surface and a wood centred in water; <see cref="TerrainBlock.LevelPads"/> writes
    /// <c>"pad"</c> = the base's ground height at the house centre. ⭐ T-5: <see cref="TerrainBlock.AlignRoad"/> turns the block's heading to the nearest road edge within the recipe's radius (<see cref="TerrainRoads.TryAlign"/>); with no road net passed, or none in reach, the heading is as placed.</para>
    /// </summary>
    public static partial class FeatureExpander
    {
        private const int Attempts = 40;

        public static FeatureExpansion Expand(TerrainRecipe recipe, uint seed, Vector2 origin, float headingDeg, byte fit, TerrainWorld baseWorld, string keyPrefix, TerrainAssets? assets = null, TerrainRoads? roads = null)
        {
            var rng = new Rng(seed);
            assets ??= TerrainAssets.None;
            // ⭐ T-5 — AlignRoad: the block turns to the nearest road edge within its radius (none in reach, or no road net ⇒ the heading as placed)
            if ((fit & TerrainBlock.AlignRoad) != 0 && roads != null && roads.TryAlign(origin, recipe.Radius, headingDeg, out float aligned)) headingDeg = aligned;
            float rad = headingDeg * MathF.PI / 180f, cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            var features = new List<JsonObject>();
            var points = new List<Vector2>();

            // the footprints this block must keep out of: the base's buildings (and solid building prisms), then what it has placed
            var avoid = new List<(Vector2 Min, Vector2 Max)>();
            if ((fit & TerrainBlock.AvoidBuildings) != 0)
            {
                foreach (var b in baseWorld.Buildings) if (b.Footprint.Length > 0) avoid.Add(Box(b.Footprint));
                foreach (var p in baseWorld.Prisms) if (p.Kind == TerrainPrismKind.Building && p.Panel < 0 && p.Building < 0) avoid.Add((p.Min, p.Max));
            }
            var footprints = new Dictionary<string, Vector2[]>(StringComparer.Ordinal);

            int houses = 0;
            int want = recipe.HousesMax == 0 ? 0 : recipe.HousesMin + rng.Next(recipe.HousesMax - recipe.HousesMin + 1);
            for (int n = 0; n < want; n++)
            {
                string template = recipe.Templates[rng.Next(recipe.Templates.Count)];
                if (!footprints.TryGetValue(template, out var local))
                    footprints[template] = local = TerrainBuildingExpander.LocalFootprint(assets.LoadTemplate(template, $"recipe '{recipe.Id}'"), $"template '{template}'");
                for (int a = 0; a < Attempts; a++)
                {
                    float ang = rng.NextFloat() * MathF.Tau, dist = MathF.Sqrt(rng.NextFloat()) * recipe.Radius;
                    float rot = Round(headingDeg + 90f * rng.Next(4));
                    var offset = new Vector2(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist);
                    var centre = new Vector2(Round(origin.X + offset.X * cos - offset.Y * sin), Round(origin.Y + offset.X * sin + offset.Y * cos));
                    var world = Place(local, centre, rot);
                    var box = Box(world);
                    var padded = (new Vector2(box.Min.X - recipe.MinSpacing * 0.5f, box.Min.Y - recipe.MinSpacing * 0.5f), new Vector2(box.Max.X + recipe.MinSpacing * 0.5f, box.Max.Y + recipe.MinSpacing * 0.5f));
                    if ((fit & TerrainBlock.AvoidWater) != 0 && InWater(baseWorld, centre, world)) continue;
                    if ((fit & TerrainBlock.AvoidBuildings) != 0 && Overlaps(avoid, padded)) continue;
                    // placed: later houses keep out of this one too (its own box, unpadded — the later house pads itself)
                    avoid.Add(box);
                    var props = new JsonObject { ["kind"] = "building", ["template"] = template, ["rotation"] = rot, ["label"] = $"{keyPrefix}/{houses}", ["name"] = "", ["baseZ"] = BaseZ(baseWorld, world) };
                    if ((fit & TerrainBlock.LevelPads) != 0) props["pad"] = Round(baseWorld.GroundHeightAt(centre.X, centre.Y));
                    features.Add(Feature(props, new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(centre.X, centre.Y) }));
                    points.AddRange(world);
                    houses++;
                    break;
                }
            }

            int woods = 0;
            int wantWoods = recipe.WoodsMax == 0 ? 0 : recipe.WoodsMin + rng.Next(recipe.WoodsMax - recipe.WoodsMin + 1);
            for (int n = 0; n < wantWoods; n++)
            {
                float ang = rng.NextFloat() * MathF.Tau, dist = MathF.Sqrt(rng.NextFloat()) * recipe.Radius;
                float size = recipe.WoodSizeMin + rng.NextFloat() * (recipe.WoodSizeMax - recipe.WoodSizeMin);
                var offset = new Vector2(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist);
                var centre = new Vector2(origin.X + offset.X * cos - offset.Y * sin, origin.Y + offset.X * sin + offset.Y * cos);
                // an irregular 8-gon around the centre (a convex ring, counter-clockwise)
                var ring = new Vector2[8];
                for (int k = 0; k < 8; k++)
                {
                    float r = size * (0.7f + 0.3f * rng.NextFloat()), t = k * MathF.PI / 4f;
                    ring[k] = new Vector2(Round(centre.X + r * MathF.Cos(t)), Round(centre.Y + r * MathF.Sin(t)));
                }
                if ((fit & TerrainBlock.AvoidWater) != 0 && InWater(baseWorld, centre, ring)) continue;
                var coords = new JsonArray();
                foreach (var p in ring) coords.Add(new JsonArray(p.X, p.Y));
                coords.Add(new JsonArray(ring[0].X, ring[0].Y));
                features.Add(Feature(new JsonObject { ["kind"] = "surface", ["surface"] = "forest", ["label"] = $"{keyPrefix}/w{woods}" },
                    new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(coords) }));
                points.AddRange(ring);
                woods++;
            }
            return new FeatureExpansion(features, houses, woods, points);
        }

        /// <summary>The Point-building feature of ONE placed house (a <see cref="TerrainFeature"/>): label <c>&lt;keyPrefix&gt;/0</c>.</summary>
        public static JsonObject House(TerrainWorld baseWorld, TerrainAssets assets, string template, Vector2 at, float headingDeg, float pad, string keyPrefix)
        {
            var local = TerrainBuildingExpander.LocalFootprint(assets.LoadTemplate(template, $"feature '{keyPrefix}'"), $"template '{template}'");
            var props = new JsonObject { ["kind"] = "building", ["template"] = template, ["rotation"] = Round(headingDeg), ["label"] = keyPrefix + "/0", ["name"] = "", ["baseZ"] = BaseZ(baseWorld, Place(local, at, headingDeg)) };
            if (pad != 0f) props["pad"] = Round(pad);
            return Feature(props, new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(Round(at.X), Round(at.Y)) });
        }

        /// <summary>The overlay world has no height grid of its own, so a house carries its base height explicitly: the lowest ground of the BASE under its footprint (TH-C's rule, evaluated on the base).</summary>
        private static float BaseZ(TerrainWorld baseWorld, Vector2[] footprint) => Round(baseWorld.Height?.LowestUnder(footprint) ?? baseWorld.GroundZ);

        private static JsonObject Feature(JsonObject props, JsonObject geometry) => new() { ["type"] = "Feature", ["properties"] = props, ["geometry"] = geometry };

        /// <summary>Millimetres: the bytes must not depend on float formatting of an unrounded value.</summary>
        private static float Round(float v) => MathF.Round(v * 1000f) / 1000f;

        private static Vector2[] Place(Vector2[] local, Vector2 at, float rotDeg)
        {
            float r = rotDeg * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
            var w = new Vector2[local.Length];
            for (int i = 0; i < w.Length; i++) w[i] = at + new Vector2(local[i].X * c - local[i].Y * s, local[i].X * s + local[i].Y * c);
            return w;
        }

        private static (Vector2 Min, Vector2 Max) Box(IReadOnlyList<Vector2> pts)
        {
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return (min, max);
        }

        private static bool Overlaps(List<(Vector2 Min, Vector2 Max)> boxes, (Vector2 Min, Vector2 Max) b)
        {
            foreach (var o in boxes)
                if (b.Min.X <= o.Max.X && b.Max.X >= o.Min.X && b.Min.Y <= o.Max.Y && b.Max.Y >= o.Min.Y) return true;
            return false;
        }

        private static bool InWater(TerrainWorld world, Vector2 centre, Vector2[] corners)
        {
            foreach (var s in world.Surfaces)
            {
                if (s.Type != TerrainSurfaceType.Water) continue;
                if (centre.X >= s.Min.X && centre.X <= s.Max.X && centre.Y >= s.Min.Y && centre.Y <= s.Max.Y && PolygonMath.Contains(s.Polygon, centre)) return true;
                foreach (var c in corners)
                    if (c.X >= s.Min.X && c.X <= s.Max.X && c.Y >= s.Min.Y && c.Y <= s.Max.Y && PolygonMath.Contains(s.Polygon, c)) return true;
            }
            return false;
        }

        /// <summary>xorshift64* seeded through splitmix64 — the one generator of the expander (⛔ never <c>System.Random</c>: its sequence is not a contract).</summary>
        private struct Rng
        {
            private ulong _s;
            public Rng(uint seed)
            {
                ulong z = seed + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
                _s = z == 0 ? 0x2545F4914F6CDD1DUL : z;
            }
            private ulong NextU64() { _s ^= _s >> 12; _s ^= _s << 25; _s ^= _s >> 27; return _s * 0x2545F4914F6CDD1DUL; }
            public float NextFloat() => (NextU64() >> 40) * (1f / (1 << 24));
            public int Next(int exclusiveMax) => exclusiveMax <= 1 ? 0 : (int)((NextU64() >> 33) % (uint)exclusiveMax);
        }
    }
}
