using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings programme Stage 1 — a building TEMPLATE placed by an INSTANCE becomes ordinary primitives at parse
    /// (📄 docs/DESIGN_Building_Interiors.md §3a): each wall a <see cref="TerrainWallPanel"/> expanded into thin prisms
    /// around its openings, each upper storey a floor slab, each stair a ramp, the roof a slab. 🔒 <i>"Solid building is
    /// just special case of a generic building"</i> — <c>"solid": true</c> yields today's prism.
    /// <para>Template coordinates are LOCAL metres; the instance rotates them by <c>rotation</c> degrees counter-clockwise
    /// (engine yaw: 0 = east, 90 = north) about the template origin, then moves them to <c>position</c>.</para>
    /// </summary>
    internal static class TerrainBuildingExpander
    {
        internal sealed class Sink
        {
            public readonly List<TerrainPrism> Prisms = new();
            public readonly List<TerrainWalkable> Walkables = new();
            public readonly List<TerrainWallPanel> Panels = new();
            public readonly List<TerrainDoorDef> Doors = new();
            public readonly List<TerrainBuilding> Buildings = new();
        }

        internal readonly record struct Placement(
            Vector2 Position, float RotationDegrees, float? BaseZ, string Label, string? TemplateName,
            IReadOnlyDictionary<string, TerrainDoorState> DoorOverrides);

        /// <summary>Default sill/head (m above the storey floor) per opening kind; a gap is full height.</summary>
        public const float DoorHead = 2.1f, WindowSill = 0.9f, WindowHead = 2.1f;

        /// <param name="groundUnder">⭐ CE-1034 H1 (TH-C) — the lowest ground under a world-space footprint; a placement with no
        /// explicit base stands on it, and the ground storey needs no floor slab when it stands on the ground.</param>
        public static void Expand(JsonObject template, Placement at, Func<IReadOnlyList<Vector2>, float> groundUnder, TerrainMaterialLibrary materials,
            string? terrainName, Sink sink, string where)
        {
            float rad = at.RotationDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            Vector2 W(Vector2 local) => at.Position + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);

            string defaultMaterial = (string?)template["material"] ?? TerrainMaterialLibrary.DefaultMaterial;
            int buildingIndex = sink.Buildings.Count;

            // ── solid: today's prism, the special case ─────────────────────────────────────────────────
            if (template["solid"]?.GetValue<bool>() == true)
            {
                var fp = Ccw(ReadRing(template["footprint"], where + ".footprint").Select(W).ToArray());
                float height = ReadFloat(template["height"], where + ".height");
                float solidBase = at.BaseZ ?? groundUnder(fp);
                var prism = TerrainWorldParser.MakePrismForExpander(TerrainPrismKind.Building, fp, solidBase, solidBase + height,
                    template["floors"]?.GetValue<int>() ?? 0, at.Label, where);
                sink.Prisms.Add(Clone(prism, materials.Get(defaultMaterial, where), -1));
                sink.Buildings.Add(new TerrainBuilding
                {
                    Label = at.Label, Template = at.TemplateName, Position = at.Position, RotationDegrees = at.RotationDegrees,
                    BaseZ = solidBase, StoreyZ = new[] { solidBase, solidBase + height }, Footprint = fp, Solid = true,
                });
                return;
            }

            if (template["storeys"] is not JsonArray storeys || storeys.Count == 0)
                throw new ArgumentException($"Terrain world {where}: a building template needs 'storeys' (or \"solid\": true).");

            // ── footprint: explicit, else the bounding box of the ground storey's walls ───────────────
            Vector2[] localFootprint = template["footprint"] is JsonNode fpn
                ? ReadRing(fpn, where + ".footprint")
                : WallBox((JsonObject)storeys[0]!, where);
            var footprint = Ccw(localFootprint.Select(W).ToArray());
            float ground = groundUnder(footprint);   // ⭐ TH-C — the lowest ground under it
            float baseZ = at.BaseZ ?? ground;

            // ── storey floors ──────────────────────────────────────────────────────────────────────────
            var storeyZ = new List<float> { baseZ };
            foreach (var (s, i) in storeys.Select((s, i) => (s, i)))
            {
                if (s is not JsonObject so) throw new ArgumentException($"Terrain world {where}.storeys[{i}] is not an object.");
                storeyZ.Add(storeyZ[^1] + ReadFloat(so["height"], $"{where}.storeys[{i}].height"));
            }

            for (int k = 0; k < storeys.Count; k++)
            {
                var so = (JsonObject)storeys[k]!;
                string sw = $"{where}.storeys[{k}]";
                float floor = storeyZ[k], ceiling = storeyZ[k + 1];

                // walls → panels → prisms
                if (so["walls"] is JsonArray walls)
                    for (int w = 0; w < walls.Count; w++)
                    {
                        var wo = walls[w] as JsonObject ?? throw new ArgumentException($"Terrain world {sw}.walls[{w}] is not an object.");
                        string ww = $"{sw}.walls[{w}]";
                        var a = W(ReadPoint(wo["from"], ww + ".from"));
                        var b = W(ReadPoint(wo["to"], ww + ".to"));
                        float thickness = wo["thickness"] is JsonNode t ? ReadFloat(t, ww + ".thickness") : 0.3f;
                        var material = materials.Get((string?)wo["material"] ?? defaultMaterial, ww);
                        AddPanel(sink, a, b, thickness, floor, ceiling, material, wo["openings"] as JsonArray, buildingIndex, k,
                            at.Label, terrainName, at.DoorOverrides, ww);
                    }

                // the floor of every upper storey (the ground storey stands on the ground unless the building is raised)
                if (k > 0 || MathF.Abs(baseZ - ground) > TerrainWorld.LevelMergeDistance)
                {
                    var rings = so["floor"] is JsonNode fl ? ReadRings(fl, sw + ".floor") : new List<Vector2[]> { localFootprint };
                    foreach (var r in rings)
                        sink.Walkables.Add(TerrainWorldParser.MakeWalkableForExpander(TerrainWalkableKind.Slab,
                            r.Select(p => { var q = W(p); return new Vector3(q.X, q.Y, floor); }).ToList(), null, sw + ".floor"));
                }

                // stairs → ramps from this storey's floor to the target storey's floor
                if (so["stairs"] is JsonArray stairs)
                    for (int j = 0; j < stairs.Count; j++)
                    {
                        var st = stairs[j] as JsonObject ?? throw new ArgumentException($"Terrain world {sw}.stairs[{j}] is not an object.");
                        string stw = $"{sw}.stairs[{j}]";
                        var from = W(ReadPoint(st["from"], stw + ".from"));
                        var to = W(ReadPoint(st["to"], stw + ".to"));
                        float width = st["width"] is JsonNode wn ? ReadFloat(wn, stw + ".width") : 1.0f;
                        int toStorey = st["toStorey"]?.GetValue<int>() ?? k + 1;
                        if (toStorey <= k || toStorey > storeys.Count)
                            throw new ArgumentException($"Terrain world {stw}: toStorey {toStorey} must be above storey {k} and at most {storeys.Count}.");
                        var dir = to - from;
                        if (dir.LengthSquared() < 1e-6f) throw new ArgumentException($"Terrain world {stw}: from and to coincide.");
                        var n = Vector2.Normalize(new Vector2(-dir.Y, dir.X)) * (width / 2f);
                        float z0 = floor, z1 = storeyZ[toStorey];
                        sink.Walkables.Add(TerrainWorldParser.MakeWalkableForExpander(TerrainWalkableKind.Ramp, new List<Vector3>
                        {
                            new(from.X - n.X, from.Y - n.Y, z0), new(to.X - n.X, to.Y - n.Y, z1),
                            new(to.X + n.X, to.Y + n.Y, z1), new(from.X + n.X, from.Y + n.Y, z0),
                        }, null, stw));
                    }
            }

            // roof
            string roof = (string?)template["roof"] ?? "flat";
            if (roof == "flat")
                sink.Walkables.Add(TerrainWorldParser.MakeWalkableForExpander(TerrainWalkableKind.Slab,
                    footprint.Select(p => new Vector3(p.X, p.Y, storeyZ[^1])).ToList(), null, where + ".roof"));
            else if (roof != "none")
                throw new ArgumentException($"Terrain world {where}: unknown roof '{roof}' (flat, none).");

            sink.Buildings.Add(new TerrainBuilding
            {
                Label = at.Label, Template = at.TemplateName, Position = at.Position, RotationDegrees = at.RotationDegrees,
                BaseZ = baseZ, StoreyZ = storeyZ, Footprint = footprint, Solid = false,
            });
        }

        /// <summary>
        /// One wall panel A→B, floor..ceiling, with openings; its solid pieces become thin prisms (between openings: full
        /// height; under a window's sill and over any opening's head: the remaining strip).
        /// </summary>
        public static void AddPanel(Sink sink, Vector2 a, Vector2 b, float thickness, float floor, float ceiling,
            TerrainMaterial material, JsonArray? openingsJson, int building, int storey, string? label, string? terrainName,
            IReadOnlyDictionary<string, TerrainDoorState>? doorOverrides, string where)
        {
            float length = Vector2.Distance(a, b);
            if (length < 1e-3f) throw new ArgumentException($"Terrain world {where}: wall endpoints coincide.");
            float height = ceiling - floor;
            if (height <= 0f) throw new ArgumentException($"Terrain world {where}: height must be positive.");

            var openings = new List<TerrainOpening>();
            var doorIds = new List<(int Index, string Id, TerrainDoorState State)>();
            if (openingsJson != null)
                for (int i = 0; i < openingsJson.Count; i++)
                {
                    var o = openingsJson[i] as JsonObject ?? throw new ArgumentException($"Terrain world {where}.openings[{i}] is not an object.");
                    string ow = $"{where}.openings[{i}]";
                    string kindText = (string?)o["kind"] ?? throw new ArgumentException($"Terrain world {ow}: an opening needs 'kind' (door, window, gap).");
                    var kind = kindText switch
                    {
                        "door" => TerrainOpeningKind.Door, "window" => TerrainOpeningKind.Window, "gap" => TerrainOpeningKind.Gap,
                        _ => throw new ArgumentException($"Terrain world {ow}: unknown opening kind '{kindText}' (door, window, gap)."),
                    };
                    float atAlong = ReadFloat(o["at"], ow + ".at");
                    float width = ReadFloat(o["width"], ow + ".width");
                    float sill = o["sillZ"] is JsonNode sn ? ReadFloat(sn, ow + ".sillZ") : kind == TerrainOpeningKind.Window ? WindowSill : 0f;
                    float head = o["headZ"] is JsonNode hn ? ReadFloat(hn, ow + ".headZ")
                        : kind == TerrainOpeningKind.Gap ? height : kind == TerrainOpeningKind.Window ? WindowHead : DoorHead;
                    if (width <= 0f || atAlong < 0f || atAlong + width > length + 1e-3f)
                        throw new ArgumentException($"Terrain world {ow}: the opening [{F(atAlong)}, {F(atAlong + width)}] m does not fit the {F(length)} m wall.");
                    if (sill < 0f || head <= sill || head > height + 1e-3f)
                        throw new ArgumentException($"Terrain world {ow}: sillZ {F(sill)} / headZ {F(head)} must satisfy 0 ≤ sill < head ≤ storey height {F(height)}.");
                    string? doorId = (string?)o["doorId"];
                    if (doorId != null && kind != TerrainOpeningKind.Door)
                        throw new ArgumentException($"Terrain world {ow}: only a door may carry a doorId.");
                    var state = ParseDoorState((string?)o["initial"], ow);
                    if (doorId != null && doorOverrides != null && doorOverrides.TryGetValue(doorId, out var over)) state = over;
                    string? key = doorId == null ? null : DoorKey(terrainName, label, doorId);
                    openings.Add(new TerrainOpening
                    {
                        Kind = kind, At = atAlong, Width = width, SillZ = floor + sill, HeadZ = floor + head, DoorKey = key,
                    });
                    if (doorId != null) doorIds.Add((openings.Count - 1, doorId, state));
                }

            openings.Sort((x, y) => x.At.CompareTo(y.At));
            for (int i = 1; i < openings.Count; i++)
                if (openings[i].At < openings[i - 1].At + openings[i - 1].Width - 1e-3f)
                    throw new ArgumentException($"Terrain world {where}: openings at {F(openings[i - 1].At)} m and {F(openings[i].At)} m overlap.");

            int panelIndex = sink.Panels.Count;
            sink.Panels.Add(new TerrainWallPanel
            {
                A = a, B = b, Thickness = thickness, BaseZ = floor, TopZ = ceiling, Material = material,
                Openings = openings, Building = building, Storey = storey, Label = label,
            });

            var dir = (b - a) / length;
            Vector2 P(float along) => a + dir * along;
            void Piece(float s0, float s1, float z0, float z1)
            {
                if (s1 - s0 < 1e-3f || z1 - z0 < 1e-3f) return;
                var prism = TerrainWorldParser.MakePrismForExpander(TerrainPrismKind.Wall,
                    TerrainWorldParser.WallQuadForExpander(P(s0), P(s1), thickness), z0, z1, 0, label, where);
                sink.Prisms.Add(Clone(prism, material, panelIndex));
            }

            float cursor = 0f;
            foreach (var op in openings)
            {
                Piece(cursor, op.At, floor, ceiling);
                Piece(op.At, op.At + op.Width, floor, op.SillZ);     // under a window's sill
                Piece(op.At, op.At + op.Width, op.HeadZ, ceiling);   // over the head
                cursor = op.At + op.Width;
            }
            Piece(cursor, length, floor, ceiling);

            foreach (var (idx, id, state) in doorIds)
            {
                var op = openings.FindIndex(x => x.DoorKey == DoorKey(terrainName, label, id));
                var o = openings[op];
                if (sink.Doors.Any(d => d.Key == o.DoorKey))
                    throw new ArgumentException($"Terrain world {where}: door '{o.DoorKey}' is defined twice.");
                sink.Doors.Add(new TerrainDoorDef
                {
                    Key = o.DoorKey!, Panel = panelIndex, Opening = op, Initial = state,
                    Center = P(o.At + o.Width / 2f), SillZ = o.SillZ,
                });
            }
        }

        /// <summary>§3b K2 — <c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>; a world built without a name drops the first part.</summary>
        public static string DoorKey(string? terrain, string? building, string doorId)
            => string.Join('/', new[] { terrain, building, doorId }.Where(p => !string.IsNullOrEmpty(p)));

        public static TerrainDoorState ParseDoorState(string? text, string where) => text switch
        {
            null or "open" => TerrainDoorState.Open,
            "closed" => TerrainDoorState.Closed,
            "locked" => TerrainDoorState.Locked,
            "destroyed" => TerrainDoorState.Destroyed,
            _ => throw new ArgumentException($"Terrain world {where}: unknown door state '{text}' (open, closed, locked, destroyed)."),
        };

        private static TerrainPrism Clone(TerrainPrism p, TerrainMaterial material, int panel) => new()
        {
            Kind = p.Kind, Footprint = p.Footprint, BaseZ = p.BaseZ, TopZ = p.TopZ, Floors = p.Floors, Label = p.Label,
            Triangles = p.Triangles, Min = p.Min, Max = p.Max, Material = material, Panel = panel,
        };

        private static Vector2[] WallBox(JsonObject storey0, string where)
        {
            if (storey0["walls"] is not JsonArray walls || walls.Count == 0)
                throw new ArgumentException($"Terrain world {where}: no 'footprint' and no ground-storey walls to derive one from.");
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var w in walls)
                foreach (var key in new[] { "from", "to" })
                {
                    var p = ReadPoint((w as JsonObject)?[key], where + ".walls." + key);
                    min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                }
            return new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) };
        }

        private static Vector2[] Ccw(Vector2[] ring)
        {
            if (PolygonMath.SignedArea2(ring) < 0f) Array.Reverse(ring);
            return ring;
        }

        private static List<Vector2[]> ReadRings(JsonNode node, string where)
        {
            // one ring [[x,y],...] or a list of rings [[[x,y],...], ...]
            if (node is JsonArray arr && arr.Count > 0 && arr[0] is JsonArray first && first.Count > 0 && first[0] is JsonArray)
                return arr.Select((r, i) => ReadRing(r, $"{where}[{i}]")).ToList();
            return new List<Vector2[]> { ReadRing(node, where) };
        }

        private static Vector2[] ReadRing(JsonNode? node, string where)
        {
            if (node is not JsonArray arr || arr.Count < 3)
                throw new ArgumentException($"Terrain world {where}: expected a ring of at least 3 [x, y] points.");
            var pts = arr.Select((p, i) => ReadPoint(p, $"{where}[{i}]")).ToList();
            if (pts.Count > 3 && Vector2.Distance(pts[0], pts[^1]) < 1e-4f) pts.RemoveAt(pts.Count - 1);   // closing point optional
            return pts.ToArray();
        }

        private static Vector2 ReadPoint(JsonNode? node, string where)
        {
            if (node is not JsonArray a || a.Count < 2)
                throw new ArgumentException($"Terrain world {where}: expected [x, y].");
            return new Vector2(ReadFloat(a[0], where), ReadFloat(a[1], where));
        }

        private static float ReadFloat(JsonNode? node, string where)
        {
            if (node == null) throw new ArgumentException($"Terrain world {where} is missing.");
            try { return (float)node.GetValue<double>(); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                throw new ArgumentException($"Terrain world {where} must be a number.", ex);
            }
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
