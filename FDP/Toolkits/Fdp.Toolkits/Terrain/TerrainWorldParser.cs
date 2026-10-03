using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Parses a terrain WORLD file — a GeoJSON <c>FeatureCollection</c> in the engine's local metres
    /// (X east, Y north, optional Z up) — into a <see cref="TerrainWorld"/>.
    ///
    /// <para><b>Feature kinds</b> (<c>properties.kind</c>):</para>
    /// <list type="table">
    ///   <item><term><c>building</c></term><description>Polygon → solid prism <c>baseZ</c>..<c>baseZ+height</c>; <c>floors</c>, <c>label</c> optional</description></item>
    ///   <item><term><c>wall</c></term><description>LineString + <c>thickness</c> → one thin prism per segment</description></item>
    ///   <item><term><c>slab</c></term><description>Polygon with Z per vertex (or <c>z</c>) → walkable floor</description></item>
    ///   <item><term><c>ramp</c></term><description>Polygon with Z per vertex → sloped walkable link</description></item>
    ///   <item><term><c>surface</c></term><description>Polygon + <c>surface</c> = road / open / forest / water</description></item>
    /// </list>
    ///
    /// <para>⛔ <b>Fails loudly</b> on anything it does not understand — an unknown kind, a polygon with
    /// holes, a missing height — rather than silently dropping a wall a unit would then walk through
    /// (the <c>BP-526</c> precedent for the definition parser).</para>
    /// <para>⭐ GeoJSON's WGS84 rule is deliberately NOT followed: coordinates are local metres
    /// (user, 2026-10-03: <i>"small numbers over large ones"</i>).</para>
    /// 📄 docs/DESIGN_Terrain_World.md §2.
    /// </summary>
    public static class TerrainWorldParser
    {
        /// <summary>The <c>hrot.schemaVersion</c> this build writes and understands.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Margin added around the features when the file declares no bounds.</summary>
        public const float DefaultBoundsMargin = 50f;

        public static TerrainWorld Parse(string geojson) => Parse(geojson, name: null);

        /// <summary>Parses <paramref name="geojson"/> and stamps the terrain's catalog <paramref name="name"/> on it.</summary>
        public static TerrainWorld Parse(string geojson, string? name)
        {
            if (string.IsNullOrWhiteSpace(geojson))
                throw new ArgumentException("Terrain world file is empty.", nameof(geojson));

            JsonNode? root;
            try { root = JsonNode.Parse(geojson); }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Terrain world file is not valid JSON: {ex.Message}", nameof(geojson), ex);
            }

            if (root is not JsonObject obj || (string?)obj["type"] != "FeatureCollection")
                throw new ArgumentException("Terrain world root must be a GeoJSON FeatureCollection.", nameof(geojson));

            var meta = obj["hrot"] as JsonObject;
            int version = meta?["schemaVersion"]?.GetValue<int>() ?? CurrentSchemaVersion;
            if (version <= 0 || version > CurrentSchemaVersion)
                throw new ArgumentException(
                    $"Terrain world schemaVersion {version} is not supported by this build (understands 1..{CurrentSchemaVersion}).",
                    nameof(geojson));

            float groundZ = meta?["groundZ"] is JsonNode gz ? ReadFloat(gz, "hrot.groundZ") : 0f;

            var prisms = new List<TerrainPrism>();
            var walkables = new List<TerrainWalkable>();
            var surfaces = new List<TerrainSurface>();

            if (obj["features"] is not JsonArray features)
                throw new ArgumentException("Terrain world has no 'features' array.", nameof(geojson));

            int index = 0;
            foreach (var featureNode in features)
            {
                string where = $"feature #{index++}";
                if (featureNode is not JsonObject feature)
                    throw new ArgumentException($"Terrain world {where} is not an object.");

                var props = feature["properties"] as JsonObject
                    ?? throw new ArgumentException($"Terrain world {where} has no 'properties'.");
                string kind = (string?)props["kind"]
                    ?? throw new ArgumentException($"Terrain world {where} has no 'properties.kind'.");
                var geometry = feature["geometry"] as JsonObject
                    ?? throw new ArgumentException($"Terrain world {where} ({kind}) has no 'geometry'.");
                string geomType = (string?)geometry["type"] ?? string.Empty;
                string? label = (string?)props["label"] ?? (string?)props["name"];
                where = label != null ? $"{where} '{label}'" : where;

                switch (kind)
                {
                    case "building":
                    {
                        float baseZ = props["baseZ"] is JsonNode b ? ReadFloat(b, where + ".baseZ") : groundZ;
                        float height = RequireFloat(props, "height", where);
                        int floors = props["floors"]?.GetValue<int>() ?? 0;
                        foreach (var ring in OuterRings(geometry, geomType, where))
                            prisms.Add(MakePrism(TerrainPrismKind.Building, Flatten(ring), baseZ, baseZ + height, floors, label, where));
                        break;
                    }
                    case "wall":
                    {
                        if (geomType != "LineString")
                            throw new ArgumentException($"Terrain world {where}: a wall must be a LineString (got '{geomType}').");
                        float baseZ = props["baseZ"] is JsonNode b ? ReadFloat(b, where + ".baseZ") : groundZ;
                        float height = RequireFloat(props, "height", where);
                        float thickness = props["thickness"] is JsonNode th ? ReadFloat(th, where + ".thickness") : 0.3f;
                        var line = ReadPositions(geometry["coordinates"], where);
                        for (int i = 0; i + 1 < line.Count; i++)
                            prisms.Add(MakePrism(TerrainPrismKind.Wall,
                                WallQuad(new Vector2(line[i].X, line[i].Y), new Vector2(line[i + 1].X, line[i + 1].Y), thickness),
                                baseZ, baseZ + height, 0, label, where));
                        break;
                    }
                    case "slab":
                    case "ramp":
                    {
                        float? flatZ = props["z"] is JsonNode z ? ReadFloat(z, where + ".z") : null;
                        foreach (var ring in OuterRings(geometry, geomType, where))
                            walkables.Add(MakeWalkable(kind == "ramp" ? TerrainWalkableKind.Ramp : TerrainWalkableKind.Slab,
                                ring, flatZ, where));
                        break;
                    }
                    case "surface":
                    {
                        string surface = (string?)props["surface"]
                            ?? throw new ArgumentException($"Terrain world {where}: a surface needs 'properties.surface'.");
                        var type = surface switch
                        {
                            "road" => TerrainSurfaceType.Road,
                            "open" => TerrainSurfaceType.Open,
                            "forest" => TerrainSurfaceType.Forest,
                            "water" => TerrainSurfaceType.Water,
                            _ => throw new ArgumentException(
                                $"Terrain world {where}: unknown surface '{surface}' (road, open, forest, water)."),
                        };
                        foreach (var ring in OuterRings(geometry, geomType, where))
                        {
                            var poly = Flatten(ring);
                            var (min, max) = Box(poly);
                            surfaces.Add(new TerrainSurface
                            {
                                Type = type, Polygon = poly, Triangles = PolygonMath.Triangulate(poly), Min = min, Max = max,
                            });
                        }
                        break;
                    }
                    default:
                        throw new ArgumentException(
                            $"Terrain world {where}: unknown kind '{kind}' (building, wall, slab, ramp, surface).");
                }
            }

            Vector2 bMin, bMax;
            if (meta?["bounds"] is JsonArray bounds && bounds.Count == 4)
            {
                bMin = new Vector2(ReadFloat(bounds[0]!, "hrot.bounds"), ReadFloat(bounds[1]!, "hrot.bounds"));
                bMax = new Vector2(ReadFloat(bounds[2]!, "hrot.bounds"), ReadFloat(bounds[3]!, "hrot.bounds"));
            }
            else
            {
                bMin = new Vector2(float.MaxValue);
                bMax = new Vector2(float.MinValue);
                foreach (var p in prisms) { bMin = Vector2.Min(bMin, p.Min); bMax = Vector2.Max(bMax, p.Max); }
                foreach (var w in walkables) { bMin = Vector2.Min(bMin, w.Min); bMax = Vector2.Max(bMax, w.Max); }
                foreach (var s in surfaces) { bMin = Vector2.Min(bMin, s.Min); bMax = Vector2.Max(bMax, s.Max); }
                if (bMin.X > bMax.X) { bMin = Vector2.Zero; bMax = Vector2.Zero; }
                bMin -= new Vector2(DefaultBoundsMargin);
                bMax += new Vector2(DefaultBoundsMargin);
            }

            return new TerrainWorld
            {
                Name = name,
                BoundsMin = bMin,
                BoundsMax = bMax,
                GroundZ = groundZ,
                Prisms = prisms,
                Walkables = walkables,
                Surfaces = surfaces,
            };
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────

        private static TerrainPrism MakePrism(TerrainPrismKind kind, Vector2[] footprint, float baseZ, float topZ,
            int floors, string? label, string where)
        {
            if (topZ <= baseZ)
                throw new ArgumentException($"Terrain world {where}: height must be positive.");
            var tris = PolygonMath.Triangulate(footprint);
            if (tris.Length == 0)
                throw new ArgumentException($"Terrain world {where}: footprint is degenerate (fewer than 3 distinct points or zero area).");
            var (min, max) = Box(footprint);
            return new TerrainPrism
            {
                Kind = kind, Footprint = footprint, BaseZ = baseZ, TopZ = topZ, Floors = floors, Label = label,
                Triangles = tris, Min = min, Max = max,
            };
        }

        private static TerrainWalkable MakeWalkable(TerrainWalkableKind kind, List<Vector3> ring, float? flatZ, string where)
        {
            var verts = new Vector3[ring.Count];
            for (int i = 0; i < ring.Count; i++)
                verts[i] = flatZ.HasValue ? new Vector3(ring[i].X, ring[i].Y, flatZ.Value) : ring[i];
            var plan = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++) plan[i] = new Vector2(verts[i].X, verts[i].Y);
            if (PolygonMath.SignedArea2(plan) < 0f)
            {
                Array.Reverse(verts);
                Array.Reverse(plan);
            }
            var tris = PolygonMath.Triangulate(plan);
            if (tris.Length == 0)
                throw new ArgumentException($"Terrain world {where}: walkable outline is degenerate.");
            var (min, max) = Box(plan);
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var v in verts) { minZ = MathF.Min(minZ, v.Z); maxZ = MathF.Max(maxZ, v.Z); }
            return new TerrainWalkable
            {
                Kind = kind, Vertices = verts, Triangles = tris, Min = min, Max = max, MinZ = minZ, MaxZ = maxZ,
            };
        }

        private static IEnumerable<List<Vector3>> OuterRings(JsonObject geometry, string geomType, string where)
        {
            switch (geomType)
            {
                case "Polygon":
                    yield return OuterRing(geometry["coordinates"] as JsonArray, where);
                    break;
                case "MultiPolygon":
                    if (geometry["coordinates"] is not JsonArray polys)
                        throw new ArgumentException($"Terrain world {where}: MultiPolygon has no coordinates.");
                    foreach (var poly in polys)
                        yield return OuterRing(poly as JsonArray, where);
                    break;
                default:
                    throw new ArgumentException($"Terrain world {where}: expected a Polygon or MultiPolygon (got '{geomType}').");
            }
        }

        private static List<Vector3> OuterRing(JsonArray? rings, string where)
        {
            if (rings == null || rings.Count == 0)
                throw new ArgumentException($"Terrain world {where}: polygon has no rings.");
            if (rings.Count > 1)
                throw new ArgumentException(
                    $"Terrain world {where}: polygons with holes are not supported in v1 — split the shape instead.");
            var ring = ReadPositions(rings[0], where);
            if (ring.Count > 1 && ring[0] == ring[^1]) ring.RemoveAt(ring.Count - 1);   // GeoJSON closes rings
            if (ring.Count < 3)
                throw new ArgumentException($"Terrain world {where}: a polygon ring needs at least 3 distinct points.");
            return ring;
        }

        private static List<Vector3> ReadPositions(JsonNode? node, string where)
        {
            if (node is not JsonArray arr)
                throw new ArgumentException($"Terrain world {where}: coordinates must be an array of positions.");
            var list = new List<Vector3>(arr.Count);
            foreach (var p in arr)
            {
                if (p is not JsonArray pos || pos.Count < 2)
                    throw new ArgumentException($"Terrain world {where}: a position must be [x, y] or [x, y, z].");
                float x = ReadFloat(pos[0]!, where), y = ReadFloat(pos[1]!, where);
                float z = pos.Count > 2 ? ReadFloat(pos[2]!, where) : 0f;
                list.Add(new Vector3(x, y, z));
            }
            return list;
        }

        /// <summary>Plan-view footprint, made counter-clockwise.</summary>
        private static Vector2[] Flatten(List<Vector3> ring)
        {
            var poly = new Vector2[ring.Count];
            for (int i = 0; i < ring.Count; i++) poly[i] = new Vector2(ring[i].X, ring[i].Y);
            if (PolygonMath.SignedArea2(poly) < 0f) Array.Reverse(poly);
            return poly;
        }

        private static Vector2[] WallQuad(Vector2 a, Vector2 b, float thickness)
        {
            var d = b - a;
            float len = d.Length();
            if (len < 1e-4f) return new[] { a, a, a };
            var n = new Vector2(-d.Y, d.X) / len * (thickness * 0.5f);
            return new[] { a - n, b - n, b + n, a + n };   // counter-clockwise
        }

        private static (Vector2 Min, Vector2 Max) Box(IReadOnlyList<Vector2> poly)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return (min, max);
        }

        private static float RequireFloat(JsonObject props, string name, string where)
            => props[name] is JsonNode n
                ? ReadFloat(n, $"{where}.{name}")
                : throw new ArgumentException($"Terrain world {where}: '{name}' is required.");

        private static float ReadFloat(JsonNode node, string where)
        {
            try { return (float)node.GetValue<double>(); }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                throw new ArgumentException(
                    $"Terrain world {where}: expected a number, got '{node.ToJsonString()}'.", ex);
            }
        }
    }
}
