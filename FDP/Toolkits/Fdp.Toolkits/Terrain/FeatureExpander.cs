using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What one <see cref="FeatureExpander.Expand"/> made: the world-file features, their counts, and the points the block's outline is the hull of (local metres).</summary>
    public sealed record FeatureExpansion(IReadOnlyList<JsonObject> Features, int Houses, int Woods, IReadOnlyList<Vector2> Points, IReadOnlyList<Vector2[]>? Shapes = null)
    {
        /// <summary>⭐ Terrain authoring U-2 — the world polygons the placing ghost and the tile picture draw: every house footprint first (<see cref="Houses"/> of them, the recipe's houses and the elements' alike), then every wood ring — without parsing the GeoJSON back.</summary>
        public IReadOnlyList<Vector2[]> ShapesOrEmpty => Shapes ?? System.Array.Empty<Vector2[]>();
        /// <summary>⭐ T-3 part 2 — the templates the block's UNIQUE houses are (name → building-template JSON): the compiler writes them into the overlay's <c>buildings/</c>, the live overlay resolves them in memory.</summary>
        public IReadOnlyDictionary<string, string> Templates { get; init; } = new Dictionary<string, string>();
    }

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

        public static FeatureExpansion Expand(TerrainRecipe recipe, uint seed, Vector2 origin, float headingDeg, byte fit, TerrainWorld baseWorld, string keyPrefix, TerrainAssets? assets = null, TerrainRoads? roads = null, IUniqueBuildings? unique = null)
        {
            var rng = new Rng(seed);
            assets ??= TerrainAssets.None;
            // ⭐ T-5 — AlignRoad: the block turns to the nearest road edge within its radius (none in reach, or no road net ⇒ the heading as placed)
            if ((fit & TerrainBlock.AlignRoad) != 0 && roads != null && roads.TryAlign(origin, recipe.Radius, headingDeg, out float aligned)) headingDeg = aligned;
            float rad = headingDeg * MathF.PI / 180f, cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            var st = new ElementState { Recipe = recipe, Origin = origin, Heading = headingDeg, Cos = cos, Sin = sin, Fit = fit, Base = baseWorld, Assets = assets, Key = keyPrefix, Unique = unique };

            // the footprints this block must keep out of: the base's buildings (and solid building prisms), then what it has placed
            if ((fit & TerrainBlock.AvoidBuildings) != 0)
            {
                foreach (var b in baseWorld.Buildings) if (b.Footprint.Length > 0) st.Avoid.Add(Box(b.Footprint));
                foreach (var p in baseWorld.Prisms) if (p.Kind == TerrainPrismKind.Building && p.Panel < 0 && p.Building < 0) st.Avoid.Add((p.Min, p.Max));
            }

            int want = recipe.HousesMax == 0 ? 0 : recipe.HousesMin + rng.Next(recipe.HousesMax - recipe.HousesMin + 1);
            for (int n = 0; n < want; n++)
            {
                string template = recipe.Templates[rng.Next(recipe.Templates.Count)];
                var local = st.LocalOf(template);
                for (int a = 0; a < Attempts; a++)
                {
                    float ang = rng.NextFloat() * MathF.Tau, dist = MathF.Sqrt(rng.NextFloat()) * recipe.Radius;
                    float rot = Round(headingDeg + 90f * rng.Next(4));
                    var offset = new Vector2(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist);
                    var centre = new Vector2(Round(origin.X + offset.X * cos - offset.Y * sin), Round(origin.Y + offset.X * sin + offset.Y * cos));
                    if (st.TryPlaceHouse(local, template, centre, rot, recipe.MinSpacing)) break;
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
                st.Features.Add(Feature(new JsonObject { ["kind"] = "surface", ["surface"] = "forest", ["label"] = $"{keyPrefix}/w{woods}" },
                    new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(coords) }));
                st.Points.AddRange(ring);
                st.WoodShapes.Add(ring);
                woods++;
            }

            // ⭐ T-3 — the recipe's elements, in order, from the stream the houses and woods left (a recipe without any draws exactly as before)
            if (recipe.Elements.Count > 0)
            {
                st.Rng = rng;
                foreach (var el in recipe.Elements) ExpandElement(st, el);
            }
            var shapes = new List<Vector2[]>(st.HouseShapes.Count + st.WoodShapes.Count);
            shapes.AddRange(st.HouseShapes); shapes.AddRange(st.WoodShapes);
            return new FeatureExpansion(st.Features, st.Houses, woods, st.Points, shapes) { Templates = st.Templates };
        }

        /// <summary>⭐ T-3 — what one block expansion shares between its houses and its elements: the stream, the frame (origin, heading), the fit, and what is placed so far.</summary>
        internal sealed class ElementState
        {
            public Rng Rng;
            public TerrainRecipe Recipe = null!;
            public Vector2 Origin; public float Heading, Cos, Sin;
            public byte Fit;
            public TerrainWorld Base = null!;
            public TerrainAssets Assets = null!;
            public string Key = "";
            public readonly List<JsonObject> Features = new();
            public readonly List<Vector2> Points = new();
            public readonly List<Vector2[]> HouseShapes = new(), WoodShapes = new();
            public readonly List<(Vector2 Min, Vector2 Max)> Avoid = new();
            private readonly Dictionary<string, Vector2[]> _footprints = new(StringComparer.Ordinal);
            public int Houses, Parts;
            public IUniqueBuildings? Unique;
            public readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal);
            /// <summary>⭐ T-3 part 2 — the lane strips so far (world centre-line segments and half widths): a house keeps ≥ <see cref="LaneGap"/> off every one (the block's streets are not built over).</summary>
            public readonly List<(Vector2 A, Vector2 B, float Half)> Lanes = new();
            public const float LaneGap = 1f;

            public Vector2[] LocalOf(string template)
            {
                if (!_footprints.TryGetValue(template, out var local))
                    _footprints[template] = local = TerrainBuildingExpander.LocalFootprint(Assets.LoadTemplate(template, $"recipe '{Recipe.Id}'"), $"template '{template}'");
                return local;
            }

            /// <summary>Block-local metres → world (the block's origin and heading).</summary>
            public Vector2 W(float x, float y) => new(Round(Origin.X + x * Cos - y * Sin), Round(Origin.Y + x * Sin + y * Cos));

            public string NextLabel() => $"{Key}/e{Parts++}";

            /// <summary>Places a house at <paramref name="centre"/> (its origin corner, or its middle with <paramref name="byCentre"/>) turned <paramref name="rot"/> when the fit allows (water, buildings, <paramref name="spacing"/> to the others): its feature is added and true returned.</summary>
            public bool TryPlaceHouse(Vector2[] local, string template, Vector2 centre, float rot, float spacing, bool byCentre = false)
            {
                if (byCentre)   // a template's origin is its SW corner: put the footprint's MIDDLE at centre
                {
                    var mn = new Vector2(float.MaxValue); var mx = new Vector2(float.MinValue);
                    foreach (var q in local) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                    var mid = (mn + mx) / 2f; float rr = rot * MathF.PI / 180f, cr = MathF.Cos(rr), sr = MathF.Sin(rr);
                    centre = new Vector2(Round(centre.X - (mid.X * cr - mid.Y * sr)), Round(centre.Y - (mid.X * sr + mid.Y * cr)));
                }
                var world = Place(local, centre, rot);
                var box = Box(world);
                var padded = (new Vector2(box.Min.X - spacing * 0.5f, box.Min.Y - spacing * 0.5f), new Vector2(box.Max.X + spacing * 0.5f, box.Max.Y + spacing * 0.5f));
                if ((Fit & TerrainBlock.AvoidWater) != 0 && InWater(Base, centre, world)) return false;
                if ((Fit & TerrainBlock.AvoidBuildings) != 0 && Overlaps(Avoid, padded)) return false;
                foreach (var (la, lb, half) in Lanes) if (PolygonToSegment(world, la, lb) < half + LaneGap) return false;   // ⭐ T-3 part 2 — off every lane strip, by the footprint
                // placed: later houses keep out of this one too (its own box, unpadded — the later house pads itself)
                Avoid.Add(box);
                var props = new JsonObject { ["kind"] = "building", ["template"] = template, ["rotation"] = rot, ["label"] = $"{Key}/{Houses}", ["name"] = "", ["baseZ"] = BaseZ(Base, world) };
                if ((Fit & TerrainBlock.LevelPads) != 0) props["pad"] = Round(Base.GroundHeightAt(centre.X, centre.Y));
                Features.Add(FeatureExpander.Feature(props, new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(centre.X, centre.Y) }));
                Points.AddRange(world);
                HouseShapes.Add(world);
                Houses++;
                return true;
            }
        }

        /// <summary>⭐ Terrain authoring U-2 — the world footprint of ONE house of <paramref name="template"/> at <paramref name="at"/>, turned <paramref name="headingDeg"/> (what <see cref="House"/> places): the placing ghost's polygon.</summary>
        public static Vector2[] HouseFootprint(TerrainAssets assets, string template, Vector2 at, float headingDeg)
            => Place(TerrainBuildingExpander.LocalFootprint(assets.LoadTemplate(template, "house ghost"), $"template '{template}'"), at, headingDeg);

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

        /// <summary>The distance from the polygon <paramref name="poly"/> (a footprint) to the segment a→b; 0 when they cross or the segment starts inside it.</summary>
        internal static float PolygonToSegment(Vector2[] poly, Vector2 a, Vector2 b)
        {
            if (PolygonMath.Contains(poly, a) || PolygonMath.Contains(poly, b)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length];
                if (SegmentsCross(p, q, a, b)) return 0f;
                best = MathF.Min(best, MathF.Min(MathF.Min(PointSegment(p, a, b), PointSegment(q, a, b)), MathF.Min(PointSegment(a, p, q), PointSegment(b, p, q))));
            }
            return best;
        }

        private static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            static float O(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            return O(a, b, c) * O(a, b, d) < 0f && O(c, d, a) * O(c, d, b) < 0f;
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
        internal struct Rng
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
