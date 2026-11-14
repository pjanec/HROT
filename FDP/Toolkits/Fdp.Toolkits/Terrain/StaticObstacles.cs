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
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
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
            => With(terrain, obstacles, null);

        /// <summary>
        /// ⭐⭐ <c>CE-4391</c> S-3 (UTM §8.2 B-10) — <paramref name="terrain"/> with <paramref name="obstacles"/> AND the live building damage laid over it, a NEW immutable world:
        /// <list type="bullet">
        /// <item>each wall panel that has a PASSAGE breach is re-expanded through <see cref="TerrainBuildingExpander.ExpandPanelPieces"/> with the passage (merged with any opening it
        /// overlaps) as an extra Gap opening — the one panel → prisms function, never a second one;</item>
        /// <item>a COLLAPSED building loses its prisms and walkables (its panels, slabs, ramps, the solid prism) and gains its rubble prism (<see cref="BuildingDamageTable.Rubble"/>'s shape,
        /// <c>Building = −1</c> here: its pieces are gone, so nothing must skip it);</item>
        /// <item>a HOLE changes nothing (sight and fire read it live; no walkable moves).</item>
        /// </list>
        /// ⛔ <see cref="TerrainWorld.Panels"/>, <see cref="TerrainWorld.Doors"/> and <see cref="TerrainWorld.Buildings"/> are the base's own lists, never removed or reordered, so every index
        /// held by an entity, a door key or a breach stays valid. After the bake <c>SurfaceZ</c>, <c>SurfacesAt</c> and <c>InsideFootprint</c> answer for the damaged world: the pieces are not there.
        /// </summary>
        public static TerrainWorld With(TerrainWorld terrain, IReadOnlyList<TerrainPrism> obstacles, BuildingDamageTable? damage)
            => With(terrain, obstacles, damage, null);

        /// <summary>
        /// ⭐⭐ <c>CE-4591</c> (P-4 part 2) — and the view's craters laid into the height grid (<see cref="CraterTable.Bake"/>): the derived world's <see cref="TerrainWorld.Height"/> holds them
        /// (the navmesh, the 3-D mesh and every reader without a view see them), its <see cref="TerrainWorld.BareHeight"/> the ground without them (the simulation's truth adds the
        /// view's own craters to it — D-1), its <see cref="TerrainWorld.HeightCraters"/> which they are (the navmesh's crater areas).
        /// </summary>
        public static TerrainWorld With(TerrainWorld terrain, IReadOnlyList<TerrainPrism> obstacles, BuildingDamageTable? damage, CraterTable? craters)
            => With(terrain, obstacles, damage, craters, null);

        /// <summary>⭐ <c>CE-4603</c> (B-6) — and the view's FELLED lone trunks left out of the pieces (the navmesh and the drawing follow; sight / fire / the hull skip them at query time already).</summary>
        public static TerrainWorld With(TerrainWorld terrain, IReadOnlyList<TerrainPrism> obstacles, BuildingDamageTable? damage, CraterTable? craters, FelledTrees? trees)
        {
            bool overlay = damage != null && damage.NavHash != 0;
            if (obstacles.Count == 0 && !overlay && craters == null && trees == null) return terrain;
            var prisms = new List<TerrainPrism>(terrain.Prisms.Count + obstacles.Count);
            var walkables = terrain.Walkables;
            IReadOnlyList<int> bakedCollapse = terrain.BakedCollapsed;
            var bakedPassages = new List<(Vector2 Min, Vector2 Max)>();   // ⭐ CE-4599 (V-10)
            if (!overlay) prisms.AddRange(terrain.Prisms);
            else
            {
                var collapsed = damage!.CollapsedBuildings;
                var byPanel = new Dictionary<int, List<WallBreach>>();
                foreach (var b in damage.Passages())
                {
                    if ((uint)b.Panel >= (uint)terrain.Panels.Count || damage.Collapsed(terrain.Panels[b.Panel].Building)) continue;
                    if (!byPanel.TryGetValue(b.Panel, out var l)) byPanel[b.Panel] = l = new List<WallBreach>();
                    l.Add(b);
                }
                foreach (var p in terrain.Prisms)
                {
                    if (p.Building >= 0 && damage.Collapsed(p.Building)) continue;          // the collapsed building's pieces
                    if (p.Panel >= 0 && byPanel.ContainsKey(p.Panel)) continue;             // re-expanded below
                    prisms.Add(p);
                }
                foreach (var (pi, passages) in byPanel)
                {
                    TerrainBuildingExpander.ExpandPanelPieces(terrain.Panels[pi], pi, WithGaps(terrain.Panels[pi], passages), prisms, "damage overlay");
                    var panel = terrain.Panels[pi];
                    float len = panel.Length;
                    if (len <= 1e-6f) continue;
                    var dir = (panel.B - panel.A) / len;
                    var pad = new Vector2(panel.Thickness * 0.5f + 1f);
                    foreach (var b in passages)
                    {
                        var a = panel.A + dir * b.From; var e = panel.A + dir * b.To;
                        bakedPassages.Add((Vector2.Min(a, e) - pad, Vector2.Max(a, e) + pad));
                    }
                }
                foreach (int c in collapsed)
                {
                    foreach (var r in BuildingDamageTable.PiecesOf(terrain, c))   // ⭐ CE-3438 — the mound clipped to land
                        prisms.Add(new TerrainPrism
                        {
                            Kind = r.Kind, Footprint = r.Footprint, BaseZ = r.BaseZ, TopZ = r.TopZ, Label = r.Label, Triangles = r.Triangles, Min = r.Min, Max = r.Max,
                            Material = r.Material, Building = -1,
                        });
                }
                if (collapsed.Count > 0)
                {
                    var kept = new List<TerrainWalkable>(terrain.Walkables.Count);
                    foreach (var w in terrain.Walkables) if (!(w.Building >= 0 && damage.Collapsed(w.Building))) kept.Add(w);
                    walkables = kept;
                    bakedCollapse = collapsed;
                }
            }
            if (trees != null) prisms.RemoveAll(p => trees.Felled(p));   // ⭐ CE-4603 — a bake, never a frame: allocation is fine here
            prisms.AddRange(obstacles);
            var min = terrain.BoundsMin; var max = terrain.BoundsMax;
            foreach (var o in obstacles) { min = Vector2.Min(min, o.Min); max = Vector2.Max(max, o.Max); }
            var derived = new TerrainWorld
            {
                Name      = terrain.Name,
                SourceFolder = terrain.SourceFolder,   // ⭐ CE-4393 — its untouched tiles still come from the importer's navtiles
                SeedFolders = terrain.SeedFolders,   // ⭐ O-2 — a merged world keeps its overlay's seed tiles too
                BoundsMin = min,
                BoundsMax = max,
                GroundZ   = terrain.GroundZ,
                Height    = craters != null && terrain.SimHeight is { } bare ? craters.Bake(bare) : terrain.Height,   // ⭐ CE-1034 H1; ⭐ CE-4591 — the craters baked in (a cache)
                BareHeight = craters != null ? terrain.SimHeight : terrain.BareHeight,
                HeightCraters = craters,
                Prisms    = prisms,
                Walkables = walkables,
                BakedCollapsed = bakedCollapse,
                BakedPassages = bakedPassages,
                Surfaces  = terrain.Surfaces,
                Panels    = terrain.Panels,
                Buildings = terrain.Buildings,
                Doors     = terrain.Doors,
                Materials = terrain.Materials,
                Breaches = terrain.Breaches,   // ⭐ CE-4391 S-1
            };
            if (terrain.Beyond is { } beyond) derived.AttachBeyond(beyond);   // ⭐ CE-4122 — the ground beyond the terrain is the base's, kept
            return derived;
        }

        /// <summary>The panel's openings with each passage added as a Gap, merged with every opening (and passage) it overlaps — ascending and non-overlapping, as the expander needs.</summary>
        internal static List<TerrainOpening> WithGaps(TerrainWallPanel panel, List<WallBreach> passages)   // ⭐ CE-4392 — shared with the drawing (DamagedBuildings): one gap merge
        {
            var spans = new List<(float At, float End, float Sill, float Head, TerrainOpening? Own)>();
            foreach (var o in panel.Openings) spans.Add((o.At, o.At + o.Width, o.SillZ, o.HeadZ, o));
            foreach (var b in passages) spans.Add((b.From, b.To, b.Bottom, b.Top, null));
            spans.Sort((x, y) => x.At.CompareTo(y.At));
            var result = new List<TerrainOpening>();
            var cur = spans[0];
            void Flush((float At, float End, float Sill, float Head, TerrainOpening? Own) s)
                => result.Add(s.Own ?? new TerrainOpening { Kind = TerrainOpeningKind.Gap, At = s.At, Width = s.End - s.At, SillZ = s.Sill, HeadZ = s.Head });
            for (int i = 1; i < spans.Count; i++)
            {
                var n = spans[i];
                if (n.At < cur.End - 1e-3f)   // overlap: one wider Gap holding both
                    cur = (cur.At, MathF.Max(cur.End, n.End), MathF.Min(cur.Sill, n.Sill), MathF.Max(cur.Head, n.Head), null);
                else { Flush(cur); cur = n; }
            }
            Flush(cur);
            return result;
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
