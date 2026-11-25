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
        public const float DefaultBridgeWidth = 4.0f, BridgeDeckHeight = DeckGeometry.AuthoredDeckHeight, BridgeRampLength = DeckGeometry.AuthoredRampLength;
        public const int MaxScatterItems = 2000;
        public const float DefaultRowSpacing = 8f;
        public const int DefaultGroupCount = 10;

        /// <summary>
        /// The parts of ONE feature of <paramref name="kind"/>. <paramref name="points"/> are WORLD coordinates (the entity's position plus its polyline's relative vertices) for the line and area kinds; <paramref name="at"/> is the
        /// entity's position (a house, a tree, a clutter item). <paramref name="template"/>, <paramref name="pad"/> and <paramref name="seed"/> mean what <see cref="TerrainFeature"/> says per kind.
        /// </summary>
        public static IReadOnlyList<JsonObject> OfKind(byte kind, string key, string template, float headingDeg, float pad, uint seed, Vector2 at, IReadOnlyList<Vector2> points,
                                                       TerrainWorld baseWorld, TerrainAssets assets, IEnumerable<string?> recipeRoots, string? target = null, IUniqueBuildings? unique = null)
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
                case TerrainFeature.Lane:
                    return new[] { Area("open", key + "/0", points, null, where) };
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
                case TerrainFeature.Row:
                    return Row(key, points, pad > 0f ? pad : DefaultRowSpacing, baseWorld, where);
                case TerrainFeature.Group:
                    return Group(key, points, pad > 0f ? (int)pad : DefaultGroupCount, seed, baseWorld, where);
                case TerrainFeature.Ground when DitchPresets.Of(template) is { } preset:
                    return new[] { Ditch(key + "/0", template, pad > 0f ? pad : preset.Depth, preset.Width, seed, at, headingDeg, points, where) };   // ⭐ CE-4593 (P-6)
                case TerrainFeature.Ground:
                    return new[] { Ground(key + "/0", template, pad, seed, points, where) };
                case TerrainFeature.Edit:
                    return Edit(key, template, target ?? "", baseWorld, where, at, unique);   // ⭐ CE-5025 BE-1, CE-5029 BE-3
                default:
                    throw new ArgumentException($"[Terrain] {where}: unknown feature kind {kind}.");
            }
        }

        /// <summary>True for the kinds that carry a shape (a polyline beside the component): they are the line and area kinds.</summary>
        public static bool NeedsShape(byte kind) => kind is TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge or TerrainFeature.Wood or TerrainFeature.Scrub or TerrainFeature.Water or TerrainFeature.Lane
                                                       or TerrainFeature.Bridge or TerrainFeature.Scatter or TerrainFeature.Ground or TerrainFeature.Row or TerrainFeature.Group;

        /// <summary>True when <paramref name="kind"/> is one this expander knows (the others are ignored by a caller, as before).</summary>
        public static bool IsKnown(byte kind) => kind is >= TerrainFeature.House and <= TerrainFeature.Edit;

        /// <summary>The draw order of an edit's levelled ground: above every authored shape (a removed footprint is levelled whatever was drawn there before).</summary>
        public const uint EditGroundOrder = uint.MaxValue;

        /// <summary>
        /// ⭐⭐ <c>CE-5025</c> BE-1 (📄 docs/DESIGN_Terrain_Base_Edits.md D-2, D-3) — an EDIT of the baked-in building <paramref name="target"/>. <c>remove</c>: a <c>"remove"</c> part (the merge drops the
        /// building — its record with its parts, or its solid block) and a <c>level</c> ground part over its footprint at its floor height (🔒 user: "removed building leaves bare flattened
        /// ground"; no longer an island once it is gone). A target the base does not have: the remove part alone (the merge warns), no ground change.
        /// </summary>
        private static IReadOnlyList<JsonObject> Edit(string key, string action, string target, TerrainWorld baseWorld, string where, Vector2 at = default, IUniqueBuildings? unique = null)
        {
            // ⭐ CE-5029 BE-3 (D-6, D-7) — MAKE ENTERABLE / ADD DOOR: the base building is removed and the generator's house stands on its footprint (≥ 1 door; "door": at the
            //   edit's own position). ⛔ Without a generator, or when no house fits, NOTHING is removed (the user keeps the building) and the host is told.
            if (action is "enterable" or "door")
            {
                if (target.Length == 0) throw new ArgumentException($"[Terrain] {where}: an edit names no building (Target).");
                if (!BaseFootprint(baseWorld, target, out var fp, out float floor) ) { Fdp.Core.Logging.FdpLog<TerrainRecipe>.Warn($"[Terrain] {where}: building '{target}' is not in the base — nothing to {action}."); return Array.Empty<JsonObject>(); }
                if (unique == null) { Fdp.Core.Logging.FdpLog<TerrainRecipe>.Warn($"[Terrain] {where}: this host has no house generator — '{target}' stays as it is."); return Array.Empty<JsonObject>(); }
                var house = unique.FromFootprint(fp, floor, BaseHeight(baseWorld, target, floor), action == "door" ? at : null);
                if (house == null) { Fdp.Core.Logging.FdpLog<TerrainRecipe>.Warn($"[Terrain] {where}: no enterable house fits '{target}' (no stair) — it stays as it is."); return Array.Empty<JsonObject>(); }
                var template = (JsonObject)JsonNode.Parse(house.TemplateJson)!;
                template["name"] = key + "/1";
                var remove = Feature(new JsonObject { ["kind"] = "remove", ["target"] = target, ["label"] = key + "/0" },
                                     new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(Ring(fp)) });
                var building = Feature(new JsonObject { ["kind"] = "building", ["label"] = key + "/1", ["baseZ"] = floor, ["building"] = template },
                                       new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(house.Origin.X, house.Origin.Y) });
                return new[] { remove, Ground(key + "/2", "level", floor, EditGroundOrder, fp, where), building };
            }
            if (target.Length == 0) throw new ArgumentException($"[Terrain] {where}: an edit names no building (Target).");
            if (action.Length == 0 || action == "remove")
            {
                // the remove part covers the footprint, so the live bake's dirty rectangle is the building's (a part's box is its geometry's)
                bool found = BaseFootprint(baseWorld, target, out var footprint, out float floorZ);
                var geometry = found ? new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(Ring(footprint)) } : new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(0.0, 0.0) };
                var remove = Feature(new JsonObject { ["kind"] = "remove", ["target"] = target, ["label"] = key + "/0" }, geometry);
                if (!found) return new[] { remove };
                return new[] { remove, Ground(key + "/1", "level", floorZ, EditGroundOrder, footprint, where) };
            }
            throw new ArgumentException($"[Terrain] {where}: unknown building edit '{action}' (remove, enterable, door).");
        }

        /// <summary>
        /// ⭐ <c>CE-5030</c> BE-4 (D-5, the terrain pick) — the label of the BAKED building at <paramref name="at"/> (world metres): a building record whose footprint holds the point
        /// (a placed overlay house is not baked — its label carries a <c>/</c>), else a solid block prism's; null when none stands there.
        /// </summary>
        public static string? BaseBuildingAt(TerrainWorld world, Vector2 at)
        {
            foreach (var b in world.Buildings)
                if (b.Footprint.Length >= 3 && !b.Label.Contains('/') && PolygonMath.Contains(b.Footprint, at)) return b.Label;
            foreach (var p in world.Prisms)
                if (p.Kind == TerrainPrismKind.Building && p.Building < 0 && p.Panel < 0 && p.Label is { } l && !l.Contains('/')
                    && at.X >= p.Min.X && at.X <= p.Max.X && at.Y >= p.Min.Y && at.Y <= p.Max.Y && PolygonMath.Contains(p.Footprint, at)) return l;
            return null;
        }

        /// <summary>⭐ CE-5029 — how tall the base building <paramref name="label"/> stands above its floor: its pieces' highest top (a record's walls, or a solid block).</summary>
        public static float BaseHeight(TerrainWorld world, string label, float floorZ)
        {
            int b = -1;
            for (int i = 0; i < world.Buildings.Count; i++) if (world.Buildings[i].Label == label) { b = i; break; }
            float top = floorZ;
            foreach (var p in world.Prisms)
                if ((b >= 0 && p.Building == b) || (b < 0 && p.Building < 0 && p.Panel < 0 && p.Kind == TerrainPrismKind.Building && p.Label == label)) top = MathF.Max(top, p.TopZ);
            return MathF.Max(top - floorZ, 3f);
        }

        /// <summary>The footprint and floor height of the base building <paramref name="label"/>: a building record, else a solid block prism of that label (its outer ring).</summary>
        public static bool BaseFootprint(TerrainWorld world, string label, out Vector2[] footprint, out float floorZ)
        {
            foreach (var b in world.Buildings)
                if (b.Label == label && b.Footprint.Length >= 3) { footprint = b.Footprint; floorZ = b.BaseZ; return true; }
            foreach (var p in world.Prisms)
                if (p.Kind == TerrainPrismKind.Building && p.Building < 0 && p.Panel < 0 && p.Label == label && p.Footprint.Length >= 3) { footprint = p.Footprint; floorZ = p.BaseZ; return true; }
            footprint = Array.Empty<Vector2>(); floorZ = 0f;
            return false;
        }

        // ── lines ─────────────────────────────────────────────────────────────────────────────────────────────────

        private static JsonObject Line(string partKind, string key, string material, float height, float thickness, IReadOnlyList<Vector2> points, string where)
            => LineLabelled(partKind, key + "/0", material, height, thickness, points, where);

        private static JsonObject LineLabelled(string partKind, string label, string material, float height, float thickness, IReadOnlyList<Vector2> points, string where)
        {
            if (points.Count < 2) throw new ArgumentException($"[Terrain] {where}: a line feature needs at least two points (got {points.Count}).");
            var props = new JsonObject { ["kind"] = partKind, ["material"] = material, ["height"] = Round(height), ["label"] = label };
            if (thickness > 0f) props["thickness"] = Round(thickness);
            var coords = new JsonArray();
            foreach (var p in points) coords.Add(new JsonArray(Round(p.X), Round(p.Y)));
            return Feature(props, new JsonObject { ["type"] = "LineString", ["coordinates"] = coords });
        }

        // ── areas ─────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ CE-4592 (P-5, G-14) — a ground shape's part: <c>{"kind":"ground","mode":…,"value":…,"order":…}</c> over its polygon.</summary>
        private static JsonObject Ground(string label, string mode, float value, uint order, IReadOnlyList<Vector2> points, string where)
        {
            if (points.Count < 3) throw new ArgumentException($"[Terrain] {where}: a ground shape needs at least three points (got {points.Count}).");
            GroundShapes.ModeOf(mode, where);   // an unknown mode fails here, at the compile, naming the feature
            var props = new JsonObject { ["kind"] = "ground", ["mode"] = mode.Length == 0 ? "raise" : mode, ["value"] = Round(value), ["order"] = order, ["label"] = label };
            return Feature(props, new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(Ring(points)) });
        }

        /// <summary>⭐ CE-4593 (P-6, G-26, G-36) — a ditch's part: its line (a ditch) or a foxhole's rectangle at its click and heading, with its depth and width.</summary>
        private static JsonObject Ditch(string label, string mode, float depth, float width, uint order, Vector2 at, float headingDeg, IReadOnlyList<Vector2> points, string where)
        {
            var props = new JsonObject { ["kind"] = "ground", ["mode"] = mode, ["value"] = Round(depth), ["width"] = Round(width), ["order"] = order, ["label"] = label };
            if (mode == "foxhole")
            {
                float a = headingDeg * MathF.PI / 180f;
                var d = new Vector2(MathF.Sin(a), MathF.Cos(a)) * (DitchPresets.FoxholeLength * 0.5f);   // heading 0 = north
                var line = new[] { at - d, at + d };
                var coords = new JsonArray(); foreach (var p in line) coords.Add(new JsonArray(Round(p.X), Round(p.Y)));
                return Feature(props, new JsonObject { ["type"] = "LineString", ["coordinates"] = coords });
            }
            if (points.Count < 2) throw new ArgumentException($"[Terrain] {where}: a ditch needs at least two points (got {points.Count}).");
            var c2 = new JsonArray(); foreach (var p in points) c2.Add(new JsonArray(Round(p.X), Round(p.Y)));
            return Feature(props, new JsonObject { ["type"] = "LineString", ["coordinates"] = c2 });
        }

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
            // ⭐ T-3 — the type and the turn ride along (the parser ignores them): an Unpack of a block makes a Clutter feature of the same type from them
            var props = new JsonObject { ["kind"] = "prism", ["material"] = type.Material, ["height"] = Round(type.Height), ["label"] = label, ["clutter"] = type.Id, ["heading"] = Round(headingDeg) };
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
            if ((b - a).Length() < 1f) throw new ArgumentException($"[Terrain] {where}: a bridge's two points are the same place.");
            // ⭐ CE-4692 BL-4 — the ONE deck geometry (DeckGeometry), shared with the laid bridge's bake; this only writes its parts as features
            JsonArray Ring(DeckGeometry.Part q)
            {
                var ring = new JsonArray();
                for (int i = 0; i < 4; i++) ring.Add(new JsonArray(Round(q[i].At.X), Round(q[i].At.Y), Round(q[i].Above)));
                ring.Add(new JsonArray(Round(q[0].At.X), Round(q[0].At.Y), Round(q[0].Above)));
                return ring;
            }
            JsonObject Part(string kind, string label, JsonArray ring)
                => Feature(new JsonObject { ["kind"] = kind, ["zRef"] = "ground", ["structure"] = key, ["label"] = label },
                           new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) });
            var q = DeckGeometry.Build(a, b, width, BridgeDeckHeight, BridgeRampLength);
            return new[] { Part("slab", key + "/0", Ring(q[0])), Part("ramp", key + "/1", Ring(q[1])), Part("ramp", key + "/2", Ring(q[2])) };
        }

        // ── row and group of lone trees (CE-5007) ─────────────────────────────────────────────────────────────────

        /// <summary>⭐ CE-5007 — lone trees every <paramref name="spacing"/> metres along the polyline (<see cref="LinePlacesSpaced"/>), the first half a spacing in; labels <c>&lt;key&gt;/&lt;i&gt;</c>.</summary>
        private static IReadOnlyList<JsonObject> Row(string key, IReadOnlyList<Vector2> points, float spacing, TerrainWorld baseWorld, string where)
        {
            if (points.Count < 2) throw new ArgumentException($"[Terrain] {where}: a row needs a line of at least two points.");
            var parts = new List<JsonObject>();
            foreach (var (p, _) in LinePlacesSpaced(points, spacing, MaxScatterItems))
            {
                var at = new Vector2(Round(p.X), Round(p.Y));
                if (!InWater(baseWorld, at, new[] { at })) parts.Add(Tree($"{key}/{parts.Count}", at));   // out of the base's water, like the scatter and the group
            }
            return parts;
        }

        /// <summary>⭐ CE-5007 — <paramref name="count"/> lone trees inside the polygon, out of the base's water, from <paramref name="seed"/> (the scatter's <see cref="AreaSampler"/>); a place not found is skipped.</summary>
        private static IReadOnlyList<JsonObject> Group(string key, IReadOnlyList<Vector2> points, int count, uint seed, TerrainWorld baseWorld, string where)
        {
            if (points.Count < 3) throw new ArgumentException($"[Terrain] {where}: a group needs an area of at least three points.");
            var sampler = new AreaSampler(points);
            var rng = new Rng(seed);
            var parts = new List<JsonObject>();
            for (int n = 0, want = Math.Min(count, MaxScatterItems); n < want; n++)
                if (sampler.Next(ref rng, baseWorld) is Vector2 at) parts.Add(Tree($"{key}/{parts.Count}", at));
            return parts;
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
            var sampler = new AreaSampler(points);
            float area = sampler.Area;
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
                Vector2? place = sampler.Next(ref rng, baseWorld);
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
