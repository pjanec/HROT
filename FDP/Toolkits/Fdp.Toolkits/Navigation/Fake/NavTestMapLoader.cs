using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Loads a <see cref="NavTestMap"/> from a JSON file or string.
    ///
    /// JSON schema:
    /// <code>
    /// {
    ///   "min_altitude": 0.0,       // optional, default 0
    ///   "max_altitude": 5000.0,    // optional, default 5000
    ///   "no_fly_zones": [          // optional
    ///     { "min": [x,y,z], "max": [x,y,z] }
    ///   ],
    ///   "layers": [
    ///     {
    ///       "layer": "Infantry",   // NavLayerMask enum name or uint string
    ///       "polygons": [
    ///         {
    ///           "id": 0,
    ///           "vertices": [[x,y,z], ...],
    ///           "is_blocked": false,       // optional
    ///           "surface_type": "Generic"  // optional
    ///         }
    ///       ],
    ///       "adjacency": [[1,2],[0],[0]], // per-polygon neighbour index lists
    ///       "off_mesh_links": [           // optional
    ///         {
    ///           "from_polygon_id": 0,
    ///           "to_polygon_id": 3,
    ///           "start_pos": [x,y,z],
    ///           "end_pos":   [x,y,z],
    ///           "cost": 5.0,
    ///           "kind": "Jump"            // TraversalKind enum name
    ///         }
    ///       ]
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public static class NavTestMapLoader
    {
        /// <summary>Loads a map from a JSON string.</summary>
        public static NavTestMap FromJson(string json)
        {
            var root = JObject.Parse(json);
            var map  = new NavTestMap();

            map.MinAltitude = root["min_altitude"]?.Value<float>() ?? 0f;
            map.MaxAltitude = root["max_altitude"]?.Value<float>() ?? 5000f;

            // No-fly zones.
            var noFlyArray = root["no_fly_zones"] as JArray;
            if (noFlyArray != null)
            {
                var zones = new List<NoFlyVolume>();
                foreach (JObject zoneObj in noFlyArray)
                {
                    zones.Add(new NoFlyVolume
                    {
                        Bounds = new BoundingBox3D(
                            ParseVec3(zoneObj["min"]),
                            ParseVec3(zoneObj["max"]))
                    });
                }
                map.NoFlyZones = zones.ToArray();
            }

            // Layers.
            var layersArray = root["layers"] as JArray;
            if (layersArray != null)
            {
                var layers = new List<FakeNavLayer>();
                foreach (JObject layerObj in layersArray)
                {
                    layers.Add(ParseLayer(layerObj));
                }
                map.Layers = layers.ToArray();
            }

            return map;
        }

        /// <summary>Loads a map from a JSON file on disk.</summary>
        public static NavTestMap FromFile(string path)
            => FromJson(File.ReadAllText(path));

        // ── Private helpers ──────────────────────────────────────────────────────

        private static FakeNavLayer ParseLayer(JObject obj)
        {
            var layer = new FakeNavLayer();
            layer.Layer = ParseNavLayerMask(obj["layer"]?.Value<string>() ?? "Infantry");

            var polyArray = obj["polygons"] as JArray;
            if (polyArray != null)
            {
                var polys = new List<NavPolygon>();
                foreach (JObject pObj in polyArray)
                {
                    polys.Add(ParsePolygon(pObj));
                }
                layer.Polygons = polys.ToArray();
            }

            var adjArray = obj["adjacency"] as JArray;
            if (adjArray != null)
            {
                var adj = new List<int[]>();
                foreach (JArray innerArr in adjArray)
                {
                    var indices = new List<int>();
                    foreach (var token in innerArr)
                        indices.Add(token.Value<int>());
                    adj.Add(indices.ToArray());
                }
                layer.Adjacency = adj.ToArray();
            }

            var linksArray = obj["off_mesh_links"] as JArray;
            if (linksArray != null)
            {
                var links = new List<OffMeshLink>();
                foreach (JObject lObj in linksArray)
                {
                    links.Add(ParseOffMeshLink(lObj));
                }
                layer.OffMeshLinks = links.ToArray();
            }

            return layer;
        }

        private static NavPolygon ParsePolygon(JObject obj)
        {
            var poly = new NavPolygon();
            poly.Id = obj["id"]?.Value<int>() ?? 0;

            var vertsArray = obj["vertices"] as JArray;
            if (vertsArray != null)
            {
                var verts = new List<Vector3>();
                foreach (JArray v in vertsArray)
                    verts.Add(new Vector3(v[0].Value<float>(), v[1].Value<float>(), v[2].Value<float>()));
                poly.Vertices = verts.ToArray();
            }

            poly.IsBlocked      = obj["is_blocked"]?.Value<bool>() ?? false;
            poly.SurfaceType    = ParseSurfaceType(obj["surface_type"]?.Value<string>());
            // traversal_cost: parsed but not stored (NavPolygon has no traversal cost field)
            return poly;
        }

        private static OffMeshLink ParseOffMeshLink(JObject obj)
        {
            var link = new OffMeshLink();
            link.FromPolygonId = obj["from_polygon_id"]?.Value<int>() ?? 0;
            link.ToPolygonId   = obj["to_polygon_id"]?.Value<int>()   ?? 0;
            link.StartPos      = ParseVec3(obj["start_pos"]);
            link.EndPos        = ParseVec3(obj["end_pos"]);
            link.Cost          = obj["cost"]?.Value<float>() ?? 1f;
            link.Kind          = ParseTraversalKind(obj["kind"]?.Value<string>());
            return link;
        }

        private static uint ParseNavLayerMask(string? value)
        {
            if (string.IsNullOrEmpty(value)) return (uint)NavLayerMask.Infantry;
            if (Enum.TryParse<NavLayerMask>(value, ignoreCase: true, out var m)) return (uint)m;
            if (uint.TryParse(value, out uint raw)) return raw;
            return (uint)NavLayerMask.Infantry;
        }

        private static SurfaceType ParseSurfaceType(string? value)
        {
            if (string.IsNullOrEmpty(value)) return SurfaceType.Generic;
            if (Enum.TryParse<SurfaceType>(value, ignoreCase: true, out var s)) return s;
            return SurfaceType.Generic;
        }

        private static TraversalKind ParseTraversalKind(string? value)
        {
            if (string.IsNullOrEmpty(value)) return TraversalKind.Walk;
            if (Enum.TryParse<TraversalKind>(value, ignoreCase: true, out var k)) return k;
            return TraversalKind.Walk;
        }

        private static Vector3 ParseVec3(JToken? token)
        {
            if (token is JArray a && a.Count == 3)
                return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
            return Vector3.Zero;
        }
    }
}
