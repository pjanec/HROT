using System.Text.Json.Nodes;
using Fdp.Core;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ CE-3028 — the body of <c>GET /world/info</c>: geo origin, the perception and collider grids, the resident terrain
    /// and the navmesh, all read LIVE from the given world. A static builder so a test needs only a world, not the
    /// nine collaborators a <see cref="DebugApiService"/> takes.
    /// </summary>
    public static class WorldInfoReport
    {
        public static JsonObject Build(EntityRepository world, (double lat, double lon, double alt) origin)
        {
            var terrain = world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()
                ? world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()
                : null;

            return new JsonObject
            {
                ["geo"] = new JsonObject
                {
                    ["origin"] = new JsonObject
                    {
                        ["lat"] = origin.lat,
                        ["lon"] = origin.lon,
                        ["alt"] = origin.alt,
                    },
                },
                // The perception grid (sight queries) — the grid this key always described; CE-3018 places it over the terrain.
                ["spatialGrid"]  = GridJson(Fdp.Toolkit.Terrain.TerrainGridCoverage.PerceptionGrid(terrain),
                                            Fdp.Toolkit.Perception.PerceptionConstants.LocalGridWidth,
                                            Fdp.Toolkit.Perception.PerceptionConstants.LocalGridHeight),
                // The collider (vehicle avoidance) grid — rebased the same way.
                ["colliderGrid"] = GridJson(Fdp.Toolkit.Terrain.TerrainGridCoverage.ColliderGrid(terrain),
                                            global::CarKinem.Spatial.SpatialHashConstants.GridWidth,
                                            global::CarKinem.Spatial.SpatialHashConstants.GridHeight),
                ["terrain"] = terrain == null ? JsonValue.Create<object?>(null) : new JsonObject
                {
                    ["name"]      = terrain.Name,
                    ["bounds"]    = new JsonObject
                    {
                        ["minX"] = terrain.BoundsMin.X, ["minY"] = terrain.BoundsMin.Y,
                        ["maxX"] = terrain.BoundsMax.X, ["maxY"] = terrain.BoundsMax.Y,
                    },
                    ["groundZ"]   = terrain.GroundZ,
                    ["prisms"]    = terrain.Prisms.Count,
                    ["walkables"] = terrain.Walkables.Count,
                    ["surfaces"]  = terrain.Surfaces.Count,
                },
                ["navmesh"] = NavmeshJson(world),
            };
        }

        private static JsonObject GridJson(Fdp.Toolkit.Terrain.GridGeometry g, int width, int height) => new()
        {
            ["cellSize"] = g.CellSize,
            ["originX"]  = g.OriginX,
            ["originY"]  = g.OriginY,
            ["width"]    = width,
            ["height"]   = height,
            ["extent"]   = new JsonObject
            {
                ["minX"] = g.OriginX,
                ["maxX"] = g.OriginX + width * g.CellSize,
                ["minY"] = g.OriginY,
                ["maxY"] = g.OriginY + height * g.CellSize,
            },
        };

        /// <summary>The node's navmesh, or null when it composes no navigation solver. <c>baked</c> is false until a
        /// terrain's navmesh is published (until then the provider is the straight-line fallback).</summary>
        private static JsonNode? NavmeshJson(EntityRepository world)
        {
            if (!world.HasSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()) return null;
            var nav = world.GetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>();
            if (nav == null) return null;
            return new JsonObject
            {
                ["provider"] = nav is Fdp.Toolkit.Navigation.SwitchableNavmeshProvider sw
                    ? sw.Current.GetType().Name
                    : nav.GetType().Name,
                ["baked"]    = nav is Fdp.Toolkit.Navigation.SwitchableNavmeshProvider sw2 ? sw2.HasBakedMesh : (bool?)null,
                ["version"]  = nav.QueryVersion(),
            };
        }
    }
}
