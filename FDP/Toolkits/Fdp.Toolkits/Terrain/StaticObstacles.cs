using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a (D11, R-243) — marks an entity of a static-obstacle TKB type and names its wall-library material.
    /// Such an entity is TERRAIN: every node bakes it into its own <see cref="TerrainWorld"/> (a box prism of that material), and
    /// its own collider is ignored by the bullet and sight paths so it is never counted twice. Derived from the TKB on every node
    /// (<c>ObstacleTkbTranslator</c>) ⇒ never saved, never on the wire. 📄 docs/DESIGN_Peek_And_Fire.md §9.
    /// </summary>
    [ComponentId(GlobalComponentIds.StaticObstacle)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct StaticObstacle
    {
        /// <summary>The wall-library material (<see cref="TerrainMaterialLibrary"/>).</summary>
        public FixedString32 Material;
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a O3 — a static obstacle's box: <see cref="Length"/> along its heading × <see cref="Width"/> ×
    /// <see cref="Height"/> (m). The TKB type's size, or a per-instance override a scenario authors (a longer sandbag wall); saved
    /// with the scenario. ⭐ <c>[PerInstanceValue]</c>: the creator publishes it (<c>EntityObstacleShape</c>, R-136) and a node does
    /// not promote the obstacle's ghost before it has arrived — so every node bakes the SAME box.
    /// </summary>
    [ComponentId(GlobalComponentIds.ObstacleShape)]
    [PerInstanceValue]
    public struct ObstacleShape
    {
        public float Length;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a — static obstacles as terrain: the one place an obstacle entity becomes a <see cref="TerrainPrism"/>, and
    /// the lifecycle participant id under which every node holds a new obstacle in <c>Constructing</c> until its bake commits (R-243).
    /// </summary>
    public static class TerrainObstacles
    {
        /// <summary>
        /// ⭐ The entity-lifecycle participant of the obstacle bake (R-243): an obstacle type requires an ack from it, which the node's
        /// obstacle watcher sends only after the rebuild that contains the obstacle has committed. A distinct number from every module
        /// id in use (they are small integers); stated once, here.
        /// </summary>
        public const int LifecycleModuleId = 0x7E41;

        /// <summary>The label every obstacle prism carries (<c>obstacle:&lt;entity index&gt;</c>), so a fire/sight report names it.</summary>
        public const string LabelPrefix = "obstacle:";

        /// <summary>
        /// ⭐ True when <paramref name="e"/> is a static obstacle — terrain, so the entity-collider paths (the bullet narrow phase, the
        /// sight / fragment occluder) must skip it: the terrain already holds it, with its material (R-243). Safe on a view that never
        /// registered the type.
        /// </summary>
        public static bool IsObstacle(ISimulationView view, Entity e)
            => !(view is EntityRepository repo && !repo.IsComponentTypeRegistered<StaticObstacle>()) && view.HasComponent<StaticObstacle>(e);

        /// <summary>Heading (rad, about +Z, counter-clockwise from +X) of a rotation — the obstacle's yaw.</summary>
        public static float Yaw(Quaternion q)
            => MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));

        /// <summary>
        /// The prism a static obstacle is in the terrain: its box footprint (length along the heading) from its base at
        /// <paramref name="position"/>.Z up <see cref="ObstacleShape.Height"/>, of <paramref name="material"/>. Counter-clockwise, as
        /// every terrain footprint.
        /// </summary>
        public static TerrainPrism PrismOf(Vector3 position, float yaw, in ObstacleShape shape, TerrainMaterial material, string? label = null)
        {
            float hl = MathF.Max(shape.Length, 0.01f) * 0.5f, hw = MathF.Max(shape.Width, 0.01f) * 0.5f;
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            Vector2 At(float lx, float ly) => new(position.X + lx * c - ly * s, position.Y + lx * s + ly * c);
            var footprint = new[] { At(-hl, -hw), At(hl, -hw), At(hl, hw), At(-hl, hw) };
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var p in footprint) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return new TerrainPrism
            {
                Kind      = TerrainPrismKind.Wall,
                Footprint = footprint,
                Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                BaseZ     = position.Z,
                TopZ      = position.Z + MathF.Max(shape.Height, 0.01f),
                Min       = min,
                Max       = max,
                Material  = material,
                Label     = label,
            };
        }

        /// <summary>
        /// <paramref name="terrain"/> with <paramref name="obstacles"/> added to its prisms — a NEW immutable world (R-218): every
        /// other part is shared by reference (they are immutable), the door leaves and indexes are rebuilt lazily by the new object.
        /// </summary>
        public static TerrainWorld With(TerrainWorld terrain, IReadOnlyList<TerrainPrism> obstacles)
        {
            if (obstacles.Count == 0) return terrain;
            var prisms = new List<TerrainPrism>(terrain.Prisms.Count + obstacles.Count);
            prisms.AddRange(terrain.Prisms);
            prisms.AddRange(obstacles);
            var min = terrain.BoundsMin; var max = terrain.BoundsMax;
            foreach (var o in obstacles) { min = Vector2.Min(min, o.Min); max = Vector2.Max(max, o.Max); }
            return new TerrainWorld
            {
                Name      = terrain.Name,
                BoundsMin = min,
                BoundsMax = max,
                GroundZ   = terrain.GroundZ,
                Prisms    = prisms,
                Walkables = terrain.Walkables,
                Surfaces  = terrain.Surfaces,
                Panels    = terrain.Panels,
                Buildings = terrain.Buildings,
                Doors     = terrain.Doors,
                Materials = terrain.Materials,
            };
        }

        /// <summary>
        /// The material <paramref name="name"/> names in <paramref name="library"/>; an unknown name reads as concrete (the library's
        /// default for a prism with none) — ⚠ logged by the caller, never thrown: an obstacle must not kill a node's terrain.
        /// </summary>
        public static TerrainMaterial MaterialOf(TerrainMaterialLibrary library, string name, out bool known)
        {
            known = library.TryGet(name, out var m);
            return known ? m : library.Get("concrete", "obstacle fallback");
        }
    }
}
