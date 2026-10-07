using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ Buildings programme Stage 1 — the terrain diagnostics routes (📄 docs/DESIGN_Terrain_Combat_Tuning.md §4):
    /// <c>GET /terrain/levels</c>, <c>GET /terrain/query</c> (Sight) and <c>GET /doors</c>. They read the resident
    /// <see cref="TerrainWorld"/> singleton — immutable, swapped by reference at a terrain commit — and never recompute
    /// what a solver decides: the levels are <see cref="TerrainWorld.SurfacesAt(float, float, out int)"/>, the query is
    /// <see cref="TerrainWorld.QuerySight"/>.
    /// </summary>
    internal static class TerrainReport
    {
        private static TerrainWorld? Resident(EntityRepository? world)
            => world != null && world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;

        private static JsonObject NoTerrain() => new() { ["terrain"] = null, ["note"] = "no terrain world is resident on this node" };

        /// <summary>GET /terrain/levels?x=&amp;y= — the levels at a point, ground = level 0.</summary>
        public static JsonNode Levels(EntityRepository? world, float x, float y)
        {
            var t = Resident(world);
            if (t == null) return NoTerrain();
            var z = t.SurfacesAt(x, y, out int ground);
            var arr = new JsonArray();
            for (int i = 0; i < z.Length; i++)
                arr.Add(new JsonObject { ["level"] = i - ground, ["z"] = z[i], ["kind"] = i == ground ? "ground" : i < ground ? "below" : "above" });
            return new JsonObject { ["terrain"] = t.Name, ["x"] = x, ["y"] = y, ["levels"] = arr };
        }

        /// <summary>GET /terrain/query?from=x,y,z&amp;to=x,y,z&amp;purpose=sight — a dry-run trace with every crossed occluder.</summary>
        public static JsonNode Query(EntityRepository? world, Vector3 from, Vector3 to)
        {
            var t = Resident(world);
            if (t == null) return NoTerrain();
            var q = t.QuerySight(from, to);
            var crossed = new JsonArray();
            foreach (var c in q.Crossed)
                crossed.Add(new JsonObject
                {
                    ["along"] = c.Along, ["kind"] = c.Kind, ["label"] = c.Label, ["material"] = c.Material,
                    ["transmittance"] = c.Transmittance, ["building"] = c.Building, ["storey"] = c.Storey,
                });
            return new JsonObject
            {
                ["terrain"] = t.Name, ["purpose"] = "sight", ["transmittance"] = q.Transmittance,
                ["seesThrough"] = q.Transmittance >= TerrainWorld.SightThreshold, ["threshold"] = TerrainWorld.SightThreshold,
                ["length"] = Vector3.Distance(from, to), ["crossed"] = crossed,
                ["note"] = "Stage 1: a dry run. Perception still uses SegmentBlocked (any solid piece blocks) until Stage 3 switches it to this transmittance.",
            };
        }

        /// <summary>GET /doors — the doors the terrain defines (Stage 1: static definitions; door entities and live state land in Stage 5).</summary>
        public static JsonNode Doors(EntityRepository? world)
        {
            var t = Resident(world);
            if (t == null) return NoTerrain();
            var arr = new JsonArray();
            foreach (var d in t.Doors.OrderBy(d => d.Key, System.StringComparer.Ordinal))
            {
                var panel = t.Panels[d.Panel];
                arr.Add(new JsonObject
                {
                    ["key"] = d.Key, ["state"] = d.Initial.ToString(), ["source"] = "terrain (initial)",
                    ["x"] = d.Center.X, ["y"] = d.Center.Y, ["sillZ"] = d.SillZ,
                    ["building"] = panel.Building >= 0 ? t.Buildings[panel.Building].Label : null, ["storey"] = panel.Storey,
                    ["runtimeId"] = null,
                });
            }
            return new JsonObject { ["terrain"] = t.Name, ["count"] = arr.Count, ["doors"] = arr };
        }
    }
}
