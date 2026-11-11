using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ T-3 (CE-4430, docs/DESIGN_Terrain_Authoring.md §18i) — the ELEMENTS of a block recipe beyond houses and woods, each expanded through the kinds T-2 already writes (a house point, a wall / hedge line, a tree, a clutter prism, a surface) from the block's one
    /// stream, so a recipe is deterministic from its seed. Coordinates are block-LOCAL metres (x along the block's heading, y to its left); a part's label is <c>&lt;key&gt;/e&lt;n&gt;</c> (houses a row places keep the <c>&lt;key&gt;/&lt;i&gt;</c> run).
    /// <list type="bullet">
    ///   <item><c>lane</c> <c>{from,to,width,bend}</c> — a street / lane / trench strip: an <c>open</c> surface (there is no road surface — <c>surface: road</c> is retired, R-231; the road is the road GRAPH). The plan draws it grey.</item>
    ///   <item><c>row</c> <c>{from,to,templates,count,offset,sides,jitter,minSpacing,garden{material,depth,width}}</c> — houses along a line, both sides (alternating) or one, their fronts to the line, each with an optional hedge behind it.</item>
    ///   <item><c>grid</c> <c>{center,size,cols,rows,lane,templates,spacing,offset,jitter,garden}</c> — a street grid: lanes each way with a row of houses along each side of every street.</item>
    ///   <item><c>cluster</c> <c>{templates,count,center,radius,minSpacing}</c> — houses (barns, sheds) scattered in a small disc — the recipe's own <c>houses</c> block, anywhere in the block.</item>
    ///   <item><c>enclosure</c> <c>{center,size,material,height,gap,jitter}</c> — a hedge / fence / wall rectangle with an optional gate gap in its south side.</item>
    ///   <item><c>group</c> <c>{items:[{type,at|line|area,facing}]}</c> — clutter prisms (sandbags, barriers, cars, a booth): at explicit places, evenly along a line, or scattered in a rectangle.</item>
    ///   <item><c>orchard</c> <c>{center,rows,cols,spacing,jitter,angle}</c> — trees on a grid.</item>
    ///   <item><c>fields</c> <c>{center,size,cols,rows,gate,material}</c> — field polygons edged by hedge lines, with gates.</item>
    /// </list>
    /// </summary>
    public static partial class FeatureExpander
    {
        private static readonly string[] ElementTypes = { "lane", "row", "grid", "cluster", "enclosure", "group", "orchard", "fields" };

        internal static bool ElementIsKnown(string type) => Array.IndexOf(ElementTypes, type) >= 0;
        internal static string KnownElements => string.Join(", ", ElementTypes);

        /// <summary>Throws when <paramref name="el"/> misses what its <paramref name="type"/> needs (a typo is a parse error, not a silent empty block).</summary>
        internal static void ValidateElement(JsonObject el, string type, string where)
        {
            void Need(params string[] keys) { foreach (var k in keys) if (el[k] == null) throw new ArgumentException($"[Terrain] {where}: element '{type}' needs '{k}'."); }
            switch (type)
            {
                case "lane": Need("from", "to"); break;
                case "row": Need("from", "to", "templates", "count"); break;
                case "grid": Need("size", "templates"); break;
                case "cluster": Need("templates", "count", "radius"); break;
                case "enclosure": Need("size"); break;
                case "orchard": Need("rows", "cols", "spacing"); break;
                case "fields": Need("size"); break;
                case "group":
                    if (el["items"] is not JsonArray items || items.Count == 0) throw new ArgumentException($"[Terrain] {where}: element 'group' needs a non-empty 'items'.");
                    foreach (var it in items)
                    {
                        if (it is not JsonObject io) throw new ArgumentException($"[Terrain] {where}: a group item must be an object.");
                        ClutterTypes.Get((string?)io["type"], where + ".group");
                        if (io["at"] == null && io["line"] == null && io["area"] == null) throw new ArgumentException($"[Terrain] {where}: a group item needs 'at', 'line' or 'area'.");
                    }
                    break;
            }
        }

        private static float EN(JsonObject o, string k, float d) => o[k] is JsonNode n ? (float)n.GetValue<double>() : d;
        private static Vector2 EV(JsonNode? n, Vector2 d) => n is JsonArray a && a.Count >= 2 ? new Vector2((float)a[0]!.GetValue<double>(), (float)a[1]!.GetValue<double>()) : d;
        private static int ECount(ElementState st, JsonObject o, string k, int d)
        {
            if (o[k] == null) return d;
            var (lo, hi) = TerrainRecipe.Range(o[k], "element." + k);
            return lo + st.Rng.Next(hi - lo + 1);
        }
        private static List<string> ETemplates(JsonObject o) => ((JsonArray)o["templates"]!).Select(t => (string)t!).ToList();

        internal static void ExpandElement(ElementState st, JsonObject el)
        {
            string where = $"recipe '{st.Recipe.Id}'";
            switch ((string)el["type"]!)
            {
                case "lane": Lane(st, EV(el["from"], default), EV(el["to"], default), EN(el, "width", 4f), EN(el, "bend", 0f), where); break;
                case "row": Row(st, el, EV(el["from"], default), EV(el["to"], default), ECount(st, el, "count", 0), null); break;
                case "grid": Grid(st, el, where); break;
                case "cluster": Cluster(st, el); break;
                case "enclosure": Enclosure(st, el, where); break;
                case "group": Group(st, el, where); break;
                case "orchard": Orchard(st, el); break;
                case "fields": Fields(st, el, where); break;
            }
        }

        // ── lane ──────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Lane(ElementState st, Vector2 a, Vector2 b, float width, float bend, string where)
        {
            var d = b - a; float len = d.Length();
            if (len < 0.01f) return;
            var u = d / len; var n = new Vector2(-u.Y, u.X);
            var path = new List<Vector2> { a };
            if (bend > 0f) path.Add(a + d * (0.35f + 0.3f * st.Rng.NextFloat()) + n * ((st.Rng.NextFloat() * 2f - 1f) * bend));
            path.Add(b);
            var left = new List<Vector2>(); var right = new List<Vector2>();
            float half = width / 2f;
            for (int i = 0; i < path.Count; i++)
            {
                var nn = Vector2.Zero;
                if (i > 0) { var s = Vector2.Normalize(path[i] - path[i - 1]); nn += new Vector2(-s.Y, s.X); }
                if (i < path.Count - 1) { var s = Vector2.Normalize(path[i + 1] - path[i]); nn += new Vector2(-s.Y, s.X); }
                nn = Vector2.Normalize(nn);
                left.Add(st.W((path[i] + nn * half).X, (path[i] + nn * half).Y));
                right.Add(st.W((path[i] - nn * half).X, (path[i] - nn * half).Y));
            }
            right.Reverse();
            var ring = left.Concat(right).ToList();
            st.Features.Add(Area("open", st.NextLabel(), ring, null, where));
            st.Points.AddRange(ring);
        }

        // ── houses along a line ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>Houses along <paramref name="a"/>→<paramref name="b"/> (local), both sides alternating or one; <paramref name="reject"/> (local centre → true) refuses a place (a grid keeps houses off its cross streets).</summary>
        private static void Row(ElementState st, JsonObject el, Vector2 a, Vector2 b, int count, Func<Vector2, bool>? reject)
        {
            var d = b - a; float len = d.Length();
            if (len < 0.01f || count <= 0) return;
            var u = d / len; var n = new Vector2(-u.Y, u.X);
            var templates = ETemplates(el);
            string sides = (string?)el["sides"] ?? "both";
            float offset = EN(el, "offset", 9f), jitter = EN(el, "jitter", 1f), spacing = EN(el, "minSpacing", 2f);
            float theta = st.Heading + MathF.Atan2(u.Y, u.X) * 180f / MathF.PI;
            var garden = el["garden"] as JsonObject;
            bool both = sides == "both";
            int slots = both ? (count + 1) / 2 : count;
            for (int k = 0; k < count; k++)
            {
                float side = both ? (k % 2 == 0 ? 1f : -1f) : sides == "right" ? -1f : 1f;
                int slot = both ? k / 2 : k;
                string template = templates[st.Rng.Next(templates.Count)];
                var local = st.LocalOf(template);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    float t = (slot + 0.5f) / slots + (st.Rng.NextFloat() - 0.5f) * jitter * (1 + attempt) / len;
                    float lat = offset + (st.Rng.NextFloat() * 2f - 1f) * jitter * (1 + attempt);
                    var c = a + d * t + n * (side * lat);
                    if (reject != null && reject(c)) continue;
                    float rot = Round(theta + (side > 0 ? 0f : 180f));
                    if (!st.TryPlaceHouse(local, template, st.W(c.X, c.Y), rot, spacing, byCentre: true)) continue;
                    if (garden != null)
                    {
                        float depth = EN(garden, "depth", 9f), gw = EN(garden, "width", 12f);
                        var back = c + n * (side * depth);
                        var p0 = back - u * (gw / 2f); var p1 = back + u * (gw / 2f);
                        BoundaryLine(st, (string?)garden["material"] ?? "hedge", 0f, new[] { st.W(p0.X, p0.Y), st.W(p1.X, p1.Y) });
                    }
                    break;
                }
            }
        }

        private static void Grid(ElementState st, JsonObject el, string where)
        {
            var size = EV(el["size"], new Vector2(80, 60)); var centre = EV(el["center"], default);
            int cols = ECount(st, el, "cols", 2), rows = ECount(st, el, "rows", 2);
            float lane = EN(el, "lane", 4f), spacing = EN(el, "spacing", 16f), jitter = EN(el, "streetJitter", 1.5f);
            var xs = new List<float>(); var ys = new List<float>();
            for (int i = 0; i < cols; i++) xs.Add(centre.X - size.X / 2f + size.X * (i + 1) / (cols + 1) + (st.Rng.NextFloat() * 2f - 1f) * jitter);
            for (int j = 0; j < rows; j++) ys.Add(centre.Y - size.Y / 2f + size.Y * (j + 1) / (rows + 1) + (st.Rng.NextFloat() * 2f - 1f) * jitter);
            float keep = lane / 2f + 6f;
            foreach (float x in xs) Lane(st, new Vector2(x, centre.Y - size.Y / 2f), new Vector2(x, centre.Y + size.Y / 2f), lane, 0f, where);
            foreach (float y in ys) Lane(st, new Vector2(centre.X - size.X / 2f, y), new Vector2(centre.X + size.X / 2f, y), lane, 0f, where);
            foreach (float x in xs)
                Row(st, el, new Vector2(x, centre.Y - size.Y / 2f), new Vector2(x, centre.Y + size.Y / 2f), 2 * (int)(size.Y / spacing), c => ys.Any(y => MathF.Abs(c.Y - y) < keep) || xs.Any(x2 => x2 != x && MathF.Abs(c.X - x2) < keep));
            foreach (float y in ys)
                Row(st, el, new Vector2(centre.X - size.X / 2f, y), new Vector2(centre.X + size.X / 2f, y), 2 * (int)(size.X / spacing), c => xs.Any(x => MathF.Abs(c.X - x) < keep) || ys.Any(y2 => y2 != y && MathF.Abs(c.Y - y2) < keep));
        }

        private static void Cluster(ElementState st, JsonObject el)
        {
            var templates = ETemplates(el); var centre = EV(el["center"], default);
            float radius = EN(el, "radius", 15f), spacing = EN(el, "minSpacing", st.Recipe.MinSpacing);
            int want = ECount(st, el, "count", 1);
            for (int n = 0; n < want; n++)
            {
                string template = templates[st.Rng.Next(templates.Count)];
                var local = st.LocalOf(template);
                for (int a = 0; a < Attempts; a++)
                {
                    float ang = st.Rng.NextFloat() * MathF.Tau, dist = MathF.Sqrt(st.Rng.NextFloat()) * radius;
                    float rot = Round(st.Heading + 90f * st.Rng.Next(4));
                    var p = centre + new Vector2(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist);
                    if (st.TryPlaceHouse(local, template, st.W(p.X, p.Y), rot, spacing, byCentre: true)) break;
                }
            }
        }

        // ── lines: enclosures and hedgerows ───────────────────────────────────────────────────────────────────────

        /// <summary>A hedge / fence / wall line through the WORLD points <paramref name="pts"/>.</summary>
        private static void BoundaryLine(ElementState st, string material, float height, IReadOnlyList<Vector2> pts)
        {
            string label = st.NextLabel(), where = $"recipe '{st.Recipe.Id}'";
            st.Features.Add(material switch
            {
                "fence" => LineLabelled("fence", label, TerrainMaterialLibrary.DefaultFenceMaterial, height > 0 ? height : DefaultFenceHeight, 0f, pts, where),
                "wall" => LineLabelled("wall", label, TerrainMaterialLibrary.DefaultMaterial, height > 0 ? height : DefaultWallHeight, 0f, pts, where),
                _ => LineLabelled("wall", label, "hedge", height > 0 ? height : DefaultHedgeHeight, HedgeThickness, pts, where),
            });
            st.Points.AddRange(pts);
        }

        private static void Enclosure(ElementState st, JsonObject el, string where)
        {
            var c = EV(el["center"], default); var s = EV(el["size"], new Vector2(20, 14)); float gap = EN(el, "gap", 0f), j = EN(el, "jitter", 0f);
            string mat = (string?)el["material"] ?? "hedge"; float height = EN(el, "height", 0f);
            float hx = s.X / 2f, hy = s.Y / 2f;
            Vector2 Jit(float x, float y) => st.W(c.X + x + (j > 0 ? (st.Rng.NextFloat() * 2f - 1f) * j : 0f), c.Y + y + (j > 0 ? (st.Rng.NextFloat() * 2f - 1f) * j : 0f));
            var sw = Jit(-hx, -hy); var se = Jit(hx, -hy); var ne = Jit(hx, hy); var nw = Jit(-hx, hy);
            if (gap <= 0f) BoundaryLine(st, mat, height, new[] { sw, se, ne, nw, sw });
            else BoundaryLine(st, mat, height, new[] { st.W(c.X + gap / 2f, c.Y - hy), se, ne, nw, sw, st.W(c.X - gap / 2f, c.Y - hy) });
        }

        private static void Fields(ElementState st, JsonObject el, string where)
        {
            var c = EV(el["center"], default); var size = EV(el["size"], new Vector2(120, 90));
            int cols = ECount(st, el, "cols", 3), rows = ECount(st, el, "rows", 3);
            float gate = EN(el, "gate", 0.3f); string mat = (string?)el["material"] ?? "hedge";
            float x0 = c.X - size.X / 2f, y0 = c.Y - size.Y / 2f, x1 = c.X + size.X / 2f, y1 = c.Y + size.Y / 2f;
            // a hedge edge, with a gate gap (4 m) in it by chance — the gap is drawn whether or not it is taken, so the stream does not depend on the chance
            void Edge(Vector2 a, Vector2 b)
            {
                float roll = st.Rng.NextFloat(), pos = 0.3f + 0.4f * st.Rng.NextFloat();
                var d = b - a; float len = d.Length(); if (len < 6f) { BoundaryLine(st, mat, 0f, new[] { st.W(a.X, a.Y), st.W(b.X, b.Y) }); return; }
                if (roll >= gate) { BoundaryLine(st, mat, 0f, new[] { st.W(a.X, a.Y), st.W(b.X, b.Y) }); return; }
                var u = d / len; var m0 = a + d * pos - u * 2f; var m1 = a + d * pos + u * 2f;
                BoundaryLine(st, mat, 0f, new[] { st.W(a.X, a.Y), st.W(m0.X, m0.Y) });
                BoundaryLine(st, mat, 0f, new[] { st.W(m1.X, m1.Y), st.W(b.X, b.Y) });
            }
            var xs = new List<float> { x0 };
            for (int i = 1; i < cols; i++) xs.Add(x0 + size.X * i / cols + (st.Rng.NextFloat() * 2f - 1f) * size.X / cols * 0.2f);
            xs.Add(x1);
            Edge(new Vector2(x0, y0), new Vector2(x1, y0)); Edge(new Vector2(x1, y0), new Vector2(x1, y1));
            Edge(new Vector2(x1, y1), new Vector2(x0, y1)); Edge(new Vector2(x0, y1), new Vector2(x0, y0));
            for (int i = 1; i < cols; i++) Edge(new Vector2(xs[i], y0), new Vector2(xs[i], y1));
            for (int i = 0; i < cols; i++)
                for (int j = 1; j < rows; j++)
                {
                    float y = y0 + size.Y * j / rows + (st.Rng.NextFloat() * 2f - 1f) * size.Y / rows * 0.2f;
                    Edge(new Vector2(xs[i], y), new Vector2(xs[i + 1], y));
                }
        }

        // ── clutter groups and trees ──────────────────────────────────────────────────────────────────────────────

        private static void Group(ElementState st, JsonObject el, string where)
        {
            foreach (var node in (JsonArray)el["items"]!)
            {
                var it = (JsonObject)node!;
                var type = ClutterTypes.Get((string?)it["type"], where + ".group");
                string facing = (string?)it["facing"] ?? "along";
                var places = new List<(Vector2 P, float Deg)>();
                if (it["at"] is JsonArray at)
                    foreach (var p in at) { var pa = (JsonArray)p!; places.Add((new Vector2((float)pa[0]!.GetValue<double>(), (float)pa[1]!.GetValue<double>()), pa.Count > 2 ? (float)pa[2]!.GetValue<double>() : 0f)); }
                else if (it["line"] is JsonObject line)
                {
                    var a = EV(line["from"], default); var b = EV(line["to"], default);
                    int n = ECount(st, line, "count", 1);
                    var d = b - a; float deg = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI + (facing == "across" ? 90f : 0f);
                    for (int i = 0; i < n; i++) places.Add((a + d * ((i + 0.5f) / n), deg));
                }
                else if (it["area"] is JsonObject area)
                {
                    var c = EV(area["center"], default); var s = EV(area["size"], new Vector2(10, 10));
                    int n = ECount(st, area, "count", 1);
                    for (int i = 0; i < n; i++)
                    {
                        var p = c + new Vector2((st.Rng.NextFloat() - 0.5f) * s.X, (st.Rng.NextFloat() - 0.5f) * s.Y);
                        places.Add((p, facing == "random" ? st.Rng.NextFloat() * 360f : facing == "across" ? 90f : 0f));
                    }
                }
                foreach (var (p, deg) in places)
                {
                    var w = st.W(p.X, p.Y);
                    if ((st.Fit & TerrainBlock.AvoidWater) != 0 && InWater(st.Base, w, new[] { w })) continue;
                    st.Features.Add(Clutter(st.NextLabel(), type, w, Round(st.Heading + deg)));
                    st.Points.Add(w);
                }
            }
        }

        private static void Orchard(ElementState st, JsonObject el)
        {
            var c = EV(el["center"], default); int rows = ECount(st, el, "rows", 4), cols = ECount(st, el, "cols", 6);
            float sp = EN(el, "spacing", 6f), jit = EN(el, "jitter", 0.5f), ang = EN(el, "angle", 0f) * MathF.PI / 180f;
            float ca = MathF.Cos(ang), sa = MathF.Sin(ang);
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    float x = (i - (cols - 1) / 2f) * sp + (st.Rng.NextFloat() * 2f - 1f) * jit, y = (j - (rows - 1) / 2f) * sp + (st.Rng.NextFloat() * 2f - 1f) * jit;
                    var w = st.W(c.X + x * ca - y * sa, c.Y + x * sa + y * ca);
                    if ((st.Fit & TerrainBlock.AvoidWater) != 0 && InWater(st.Base, w, new[] { w })) continue;
                    st.Features.Add(Tree(st.NextLabel(), w));
                    st.Points.Add(w);
                }
        }
    }
}
