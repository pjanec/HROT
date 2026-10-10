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
            if (t == null || Fdp.Toolkit.World.WorldQuery.Of(world!) is not { } q) return NoTerrain();
            var z = q.SurfacesAt(x, y, out int ground);   // ⭐ CE-1035 Q2 — the world query's answer, the one perception and spawning use
            var arr = new JsonArray();
            for (int i = 0; i < z.Count; i++)
                arr.Add(new JsonObject { ["level"] = i - ground, ["z"] = z[i], ["kind"] = i == ground ? "ground" : i < ground ? "below" : "above" });
            return new JsonObject { ["terrain"] = t.Name, ["x"] = x, ["y"] = y, ["levels"] = arr };
        }

        /// <summary>GET /terrain/query?from=x,y,z&amp;to=x,y,z&amp;purpose=sight — a dry-run trace with every crossed occluder.</summary>
        public static JsonNode Query(EntityRepository? world, Vector3 from, Vector3 to)
        {
            var t = Resident(world);
            if (t == null || Fdp.Toolkit.World.WorldQuery.Of(world!) is not { } wq) return NoTerrain();   // ⭐ CE-1035 Q2 — bound to this world's doors (R-219)
            var traced = new System.Collections.Generic.List<Fdp.Toolkit.World.TraceCrossing>();
            wq.Trace(from, to, Fdp.Toolkit.World.TracePurpose.Sight, traced);
            float length = Vector3.Distance(from, to), transmittance = 1f;
            var crossed = new JsonArray();
            foreach (var c in traced)
            {
                transmittance *= c.Loss;
                crossed.Add(new JsonObject
                {
                    ["along"] = c.T * length, ["kind"] = c.Kind, ["label"] = c.Label, ["material"] = c.Material,
                    ["transmittance"] = c.Loss, ["building"] = c.Building, ["storey"] = c.Storey,
                });
            }
            return new JsonObject
            {
                ["terrain"] = t.Name, ["purpose"] = "sight", ["transmittance"] = transmittance,
                ["seesThrough"] = !wq.SightBlocked(from, to), ["threshold"] = TerrainWorld.SightThreshold,
                ["length"] = Vector3.Distance(from, to), ["crossed"] = crossed,
                ["note"] = "A dry run of the rule perception uses: SegmentBlocked = transmittance < threshold (buildings Stage 3, sight).",
            };
        }

        /// <summary>
        /// GET /terrain/query?purpose=fire&amp;penetration=&amp;damage= — ⭐ Buildings §3d P2 (R-217): a dry run of what a round does in
        /// the terrain, step by step — the SAME <see cref="Fdp.Toolkit.Combat.TerrainPenetration.Cross"/> the bullets use, over the
        /// SAME <see cref="TerrainWorld.QueryFire(Vector3, Vector3)"/> crossings. penetration 0 = an unknown round (the engine fallback
        /// meets the terrain); damage 0 = the flat default.
        /// </summary>
        public static JsonNode QueryFire(EntityRepository? world, Vector3 from, Vector3 to, float penetration, float damage)
        {
            var t = Resident(world);
            if (t == null || Fdp.Toolkit.World.WorldQuery.Of(world!) is not { } wq) return NoTerrain();   // ⭐ CE-1035 Q2
            var fire = new System.Collections.Generic.List<Fdp.Toolkit.World.TraceCrossing>();
            float pen = penetration, dmg = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(damage);
            float length = Vector3.Distance(from, to);
            var crossed = new JsonArray();
            bool stopped = false; float? stopAlong = null;
            wq.Trace(from, to, Fdp.Toolkit.World.TracePurpose.Fire, fire);
            foreach (var c in fire)
            {
                float effective = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.TerrainPenetrationOrFallback(pen);
                float chance = Fdp.Toolkit.Combat.ArmorModel.PenetrationChance(effective, c.Loss);
                bool passes = Fdp.Toolkit.Combat.TerrainPenetration.Cross(c.Loss, ref dmg, ref pen);
                crossed.Add(new JsonObject
                {
                    ["along"] = c.T * length, ["kind"] = c.Kind, ["label"] = c.Label, ["material"] = c.Material,
                    ["pathMetres"] = c.PathMetres, ["resistanceMmRha"] = c.Loss, ["roundPenetrationMm"] = effective,
                    ["chance"] = chance, ["passes"] = passes, ["building"] = c.Building, ["storey"] = c.Storey,
                });
                if (!passes) { stopped = true; stopAlong = c.T * length; break; }
            }
            return new JsonObject
            {
                ["terrain"] = t.Name, ["purpose"] = "fire", ["penetration"] = penetration, ["length"] = length,
                ["stopped"] = stopped, ["stopAlong"] = stopAlong,
                ["arrivingDamage"] = stopped ? 0f : dmg, ["arrivingPenetration"] = stopped ? 0f : pen, ["crossed"] = crossed,
                ["note"] = "A dry run of the rule the bullets use (R-217): each crossing passes with ArmorModel.PenetrationChance(round, resistance); damage × chance; a known round's penetration − resistance; a crossing it cannot pass stops it.",
            };
        }

        /// <summary>
        /// GET /doors — the doors the terrain defines, their LIVE state (what sight and fire see) and, since Stage 5b, the door
        /// ENTITY that stands for each (<c>runtimeId</c>, its replicated <c>entityState</c>); null before the scenario load creates it.
        /// </summary>
        public static JsonNode Doors(EntityRepository? world)
        {
            var t = Resident(world);
            if (t == null) return NoTerrain();
            var arr = new JsonArray();
            var doors = world != null ? DoorStates.Of(world, t) : DoorStates.Authored(t);   // ⭐ R-219 — the doors as this world sees them
            foreach (var (d, i) in t.Doors.Select((d, i) => (d, i)).OrderBy(x => x.d.Key, System.StringComparer.Ordinal))
            {
                var panel = t.Panels[d.Panel];
                var entity = world != null ? Fdp.Toolkit.Terrain.TerrainObjects.Find(world, d.Key) : Entity.Null;
                bool hasEntity = entity != Entity.Null && world!.IsAlive(entity);
                arr.Add(new JsonObject
                {
                    ["key"] = d.Key, ["state"] = doors[i].ToString(), ["initial"] = d.Initial.ToString(),
                    ["source"] = hasEntity ? "the door entity's replicated DoorState" : "terrain (no door entity yet — as authored)",
                    ["x"] = d.Center.X, ["y"] = d.Center.Y, ["sillZ"] = d.SillZ,
                    ["building"] = panel.Building >= 0 ? t.Buildings[panel.Building].Label : null, ["storey"] = panel.Storey,
                    ["runtimeId"] = hasEntity && world!.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity)
                        ? world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value : null,
                    ["entityState"] = hasEntity && world!.HasComponent<Fdp.Toolkit.Terrain.DoorState>(entity)
                        ? world.GetComponentRO<Fdp.Toolkit.Terrain.DoorState>(entity).State.ToString() : null,
                });
            }
            return new JsonObject { ["terrain"] = t.Name, ["count"] = arr.Count, ["doors"] = arr };
        }
    }
}
