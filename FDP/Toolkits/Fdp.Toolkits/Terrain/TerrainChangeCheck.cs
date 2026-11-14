using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>The kinds of finding a re-saved terrain can cause in a scenario that uses it (docs/DESIGN_Terrain_Authoring.md §18j).</summary>
    public enum TerrainFindingKind { StandsIn, RouteCrosses, AreaOverlaps, KeyLost }

    /// <summary>One finding: the scenario, the entity (guid and name), the kind, the part key it concerns, and the point (and the leg's far end for a route) it sits at.</summary>
    public sealed record TerrainFinding(string Scenario, string EntityGuid, string EntityName, TerrainFindingKind Kind, string PartKey, Vector2 At, Vector2? To = null, string? Detail = null);

    /// <summary>
    /// ⭐ T-8 (CE-4424, docs/DESIGN_Terrain_Authoring.md §18j J-1…J-3) — what a terrain overwrite would do to the scenarios that use the terrain. PURE: the OLD world, the NEW world and the scenario JSONs in, findings out — no world is loaded for a scenario, and nothing is
    /// written. ⭐ The parts are diffed BY KEY (a building's / wall's / prism's label, the occurrence of it; a water surface by its outline — surfaces carry no label): added, removed, or <b>moved</b> (the same key whose geometry differs by more than 1 cm). Then, per scenario:
    /// <list type="bullet">
    ///   <item><see cref="TerrainFindingKind.StandsIn"/> — an entity's <c>SimTransform</c> position inside an ADDED or MOVED blocking part (a building footprint, a wall / hedge / fence buffered by its thickness, a prism, water);</item>
    ///   <item><see cref="TerrainFindingKind.RouteCrosses"/> — a <c>RoutePlan</c> leg (the entity's position, then each waypoint) crossing such a part;</item>
    ///   <item><see cref="TerrainFindingKind.AreaOverlaps"/> — an <c>EditablePolyline</c> area intersecting an added, removed or moved part (informational);</item>
    ///   <item><see cref="TerrainFindingKind.KeyLost"/> — a <c>TerrainObjects</c> key, or a string property shaped <c>&lt;terrain&gt;/&lt;label&gt;…</c>, that the OLD world resolved and the NEW one does not (a door, or a building, as <see cref="BuildingDamages.IndexOfKey"/> resolves).</item>
    /// </list>
    /// The terrain's own source entities (<c>TerrainBlock</c>, <c>TerrainFeature</c>) are not units: they are the change.
    /// </summary>
    public static class TerrainChangeCheck
    {
        public const float MovedTolerance = 0.01f;

        /// <summary>One part of a world as the check sees it: its key, whether it blocks, and its plan outline.</summary>
        public sealed record Part(string Key, Vector2[] Outline, bool Blocking = true);

        public static IReadOnlyList<TerrainFinding> Check(TerrainWorld oldWorld, TerrainWorld newWorld, IEnumerable<(string Scenario, JsonObject Dom)> scenarios)
        {
            var oldParts = PartsOf(oldWorld); var newParts = PartsOf(newWorld);
            var added = new List<Part>(); var removed = new List<Part>(); var moved = new List<(Part Old, Part New)>();
            foreach (var (key, p) in newParts)
            {
                if (!oldParts.TryGetValue(key, out var o)) added.Add(p);
                else if (!Same(o.Outline, p.Outline)) moved.Add((o, p));
            }
            foreach (var (key, p) in oldParts) if (!newParts.ContainsKey(key)) removed.Add(p);

            var blocking = added.Concat(moved.Select(m => m.New)).Where(p => p.Blocking).ToList();                                  // where something now IS
            var touched = added.Concat(removed).Concat(moved.Select(m => m.Old)).Concat(moved.Select(m => m.New)).ToList();   // where something changed

            var findings = new List<TerrainFinding>();
            foreach (var (scenario, dom) in scenarios)
            {
                string terrain = (Prop(Prop(dom, "header") as JsonObject, "terrainName") as JsonValue)?.GetValue<string>() ?? newWorld.Name ?? "";
                if (Prop(dom, "entities") is JsonObject entities)
                    foreach (var (guid, node) in entities)
                    {
                        if (node is not JsonObject e) continue;
                        string name = (Prop(Prop(e, "EntityInfo") as JsonObject, "Name") as JsonValue)?.GetValue<string>() ?? "";
                        bool source = Prop(e, "TerrainBlock") != null || Prop(e, "TerrainFeature") != null;
                        var at = PositionOf(e);
                        var poly = Prop(e, "EditablePolyline") as JsonObject;

                        if (at is Vector2 p && !source && poly == null)
                            foreach (var part in blocking)
                                if (Inside(part.Outline, p)) findings.Add(new TerrainFinding(scenario, guid, name, TerrainFindingKind.StandsIn, part.Key, p));

                        if (at is Vector2 origin && Prop(e, "RoutePlan") is JsonObject plan && Prop(plan, "Waypoints") is JsonArray wps)
                        {
                            var pts = new List<Vector2> { origin };
                            foreach (var w in wps) if (w is JsonObject wo && Num(Prop(wo, "X")) is float x && Num(Prop(wo, "Y")) is float y) pts.Add(new Vector2(x, y));
                            if ((Prop(plan, "IsLoop") as JsonValue)?.TryGetValue(out bool loop) == true && loop && pts.Count > 2) pts.Add(pts[1]);
                            for (int i = 0; i + 1 < pts.Count; i++)
                                foreach (var part in blocking)
                                    if (SegmentHits(part.Outline, pts[i], pts[i + 1])) findings.Add(new TerrainFinding(scenario, guid, name, TerrainFindingKind.RouteCrosses, part.Key, pts[i], pts[i + 1]));
                        }

                        if (at is Vector2 o2 && poly != null && Prop(poly, "Points") is JsonArray ps && ps.Count >= 3)
                        {
                            var area = new List<Vector2>();
                            foreach (var pn in ps) if (pn is JsonObject po && Num(Prop(po, "X")) is float x && Num(Prop(po, "Y")) is float y) area.Add(o2 + new Vector2(x, y));
                            if (area.Count >= 3)
                                foreach (var part in touched.DistinctBy(t => t.Key))
                                    if (Overlap(area.ToArray(), part.Outline)) findings.Add(new TerrainFinding(scenario, guid, name, TerrainFindingKind.AreaOverlaps, part.Key, o2));
                        }

                        // a string property shaped "<terrain>/<label>…" on any component
                        foreach (var s in Strings(e, terrain)) if (Resolves(oldWorld, s) && !Resolves(newWorld, s)) findings.Add(new TerrainFinding(scenario, guid, name, TerrainFindingKind.KeyLost, s, at ?? default));
                    }

                // a door state / building damage the scenario saves
                if (Prop(dom, "TerrainObjects") is JsonObject objects)
                    foreach (var (key, _) in objects)
                        if (Resolves(oldWorld, key) && !Resolves(newWorld, key)) findings.Add(new TerrainFinding(scenario, "", "TerrainObjects", TerrainFindingKind.KeyLost, key, default));
            }
            return findings;
        }

        /// <summary>True when <paramref name="key"/> (a door key <c>&lt;terrain&gt;/&lt;label&gt;/&lt;door&gt;</c> or a building key <c>&lt;terrain&gt;/&lt;label&gt;</c>, with or without the terrain part) names something in <paramref name="world"/> — prefix-tolerant, as <see cref="BuildingDamages.IndexOfKey"/>.</summary>
        public static bool Resolves(TerrainWorld world, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var k in Candidates(world, key))
                if (world.DoorIndexOf(k) >= 0 || BuildingDamages.IndexOfKey(world, k) >= 0) return true;
            return false;
        }

        private static IEnumerable<string> Candidates(TerrainWorld world, string key)
        {
            yield return key;
            string name = world.Name ?? "";
            if (name.Length == 0) yield break;
            if (!key.StartsWith(name + "/", StringComparison.Ordinal)) yield return name + "/" + key;   // a bare label
            int slash = key.IndexOf('/');
            if (slash > 0) yield return name + "/" + key.Substring(slash + 1);                          // another terrain name in front
        }

        private static IEnumerable<string> Strings(JsonNode? n, string terrain)
        {
            if (terrain.Length == 0) yield break;
            switch (n)
            {
                case JsonValue v when v.TryGetValue(out string? s) && s != null && s.StartsWith(terrain + "/", StringComparison.Ordinal): yield return s; break;
                case JsonObject o: foreach (var (_, c) in o) foreach (var s in Strings(c, terrain)) yield return s; break;
                case JsonArray a: foreach (var c in a) foreach (var s in Strings(c, terrain)) yield return s; break;
            }
        }

        // ── the parts ─────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The blocking parts of <paramref name="w"/> by stable key: <c>b:&lt;label&gt;#i</c> buildings · <c>w:</c> free wall / hedge / fence panels (a rectangle buffered by the thickness) · <c>p:</c> free prisms · <c>s:&lt;type&gt;:&lt;outline hash&gt;</c> water (blocking) and woods / scrub (not blocking: an area can overlap them).</summary>
        public static IReadOnlyDictionary<string, Part> PartsOf(TerrainWorld w)
        {
            var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string kind, string? label, Vector2[] outline, bool blocks = true)
            {
                string stem = kind + ":" + label;
                seen.TryGetValue(stem, out int i); seen[stem] = i + 1;
                string key = stem + "#" + i;
                parts[key] = new Part(key, outline, blocks);
            }
            foreach (var b in w.Buildings) if (b.Footprint.Length >= 3) Add("b", b.Label, b.Footprint);
            foreach (var p in w.Panels)
            {
                if (p.Building >= 0) continue;
                var d = p.B - p.A; float len = d.Length(); if (len < 1e-4f) continue;
                var u = d / len; var n = new Vector2(-u.Y, u.X) * MathF.Max(p.Thickness, 0.05f) / 2f;
                Add("w", p.Label, new[] { p.A - n, p.B - n, p.B + n, p.A + n });
            }
            foreach (var p in w.Prisms) if (p.Building < 0 && p.Panel < 0 && p.Footprint.Length >= 3) Add("p", p.Label, p.Footprint);
            foreach (var s in w.Surfaces)
                if (s.Type != TerrainSurfaceType.Open && s.Polygon.Length >= 3)   // water blocks; a wood / scrub is an AREA a mission may overlap (informational), not an obstacle
                {
                    long h = 17; foreach (var v in s.Polygon) h = h * 31 + (long)MathF.Round(v.X * 100f) * 7919 + (long)MathF.Round(v.Y * 100f);
                    Add("s", s.Type.ToString().ToLowerInvariant() + ":" + h, s.Polygon, blocks: s.Type == TerrainSurfaceType.Water);
                }
            return parts;
        }

        private static bool Same(Vector2[] a, Vector2[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (Vector2.Distance(a[i], b[i]) > MovedTolerance) return false;
            return true;
        }

        // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────────

        private static bool Inside(Vector2[] poly, Vector2 p) => PolygonMath.Contains(poly, p);

        private static bool SegmentHits(Vector2[] poly, Vector2 a, Vector2 b)
        {
            if (Inside(poly, a) || Inside(poly, b)) return true;
            for (int i = 0; i < poly.Length; i++) if (Cross(poly[i], poly[(i + 1) % poly.Length], a, b)) return true;
            return false;
        }

        private static bool Overlap(Vector2[] a, Vector2[] b)
        {
            if (Inside(a, b[0]) || Inside(b, a[0])) return true;
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                    if (Cross(a[i], a[(i + 1) % a.Length], b[j], b[(j + 1) % b.Length])) return true;
            return false;
        }

        private static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            static float O(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            return O(a, b, c) * O(a, b, d) < 0f && O(c, d, a) * O(c, d, b) < 0f;
        }

        // ── the scenario JSON (the serialized shapes: SimTransform.Position [x,y,z], RoutePlan.Waypoints [{X,Y,Z}], EditablePolyline.Points [{X,Y}]) ──

        private static JsonNode? Prop(JsonNode? o, string name)
        {
            if (o is not JsonObject obj) return null;
            foreach (var (k, v) in obj) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        private static float? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? (float)d : null;

        private static Vector2? PositionOf(JsonObject e)
            => Prop(Prop(e, "SimTransform"), "Position") is JsonArray a && a.Count >= 2 && Num(a[0]) is float x && Num(a[1]) is float y ? new Vector2(x, y) : null;
    }
}
