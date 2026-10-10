using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ The terrain stand-in's answer to <see cref="IWorldQuery"/> (<c>docs/DESIGN_World_Query_Seam.md</c> §3.2): a
    /// <see cref="TerrainWorld"/> bound to one view's door states. Every answer is the terrain's own (identical to calling it
    /// directly); what this class adds is the engine-neutral shape — crossings say <see cref="TraceCrossing.ClosedBarrier"/> instead of
    /// handing callers the terrain's piece kinds (R-252).
    /// </summary>
    public sealed class TerrainWorldQuery : IWorldQuery, ITerrainRenderGeometry
    {
        public TerrainWorld World { get; }
        public DoorStates? Doors { get; }

        [ThreadStatic] private static TerrainWorldQuery? t_last;
        [ThreadStatic] private static List<TerrainWorld.FireCrossing>? t_fire;

        public TerrainWorldQuery(TerrainWorld world, DoorStates? doors = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Doors = doors;
        }

        /// <summary>The query for <paramref name="world"/> under <paramref name="doors"/> — this thread's previous one when both are the
        /// same objects (⭐ R-220: <see cref="DoorStates.Of(Fdp.Core.ISimulationView, TerrainWorld)"/> hands back the same table while no
        /// door changed, so a per-tick lookup allocates nothing).</summary>
        public static TerrainWorldQuery For(TerrainWorld world, DoorStates? doors = null)
        {
            var last = t_last;
            if (last != null && ReferenceEquals(last.World, world) && ReferenceEquals(last.Doors, doors)) return last;
            return t_last = new TerrainWorldQuery(world, doors);
        }

        public float GroundHeightAt(float x, float y) => World.GroundHeightAt(x, y);   // ⭐ CE-1034 H1 — the height grid, else the flat ground

        public float SurfaceZ(float x, float y, float zHint) => World.SurfaceZ(x, y, zHint);

        public IReadOnlyList<float> SurfacesAt(float x, float y, out int groundIndex) => World.SurfacesAt(x, y, out groundIndex);

        public float ResolveLevel(float x, float y, int level) => World.ResolveLevel(x, y, level);

        /// <summary>The stand-in's rule: not inside any piece's footprint (a building is solid at every height), then the surface near the hint.</summary>
        public bool TryStandAt(float x, float y, float zHint, out float z)
        {
            var p = new Vector2(x, y);
            foreach (var prism in World.Prisms)
            {
                if (p.X < prism.Min.X || p.Y < prism.Min.Y || p.X > prism.Max.X || p.Y > prism.Max.Y) continue;
                if (PolygonMath.Contains(prism.Footprint, p)) { z = default; return false; }
            }
            z = World.SurfaceZ(x, y, zHint);
            return true;
        }

        public bool SightBlocked(Vector3 from, Vector3 to) => World.SegmentBlocked(from, to, Doors);

        public void Trace(Vector3 from, Vector3 to, TracePurpose purpose, List<TraceCrossing> into)
        {
            into.Clear();
            switch (purpose)
            {
                case TracePurpose.Fire:
                {
                    var fire = t_fire ??= new List<TerrainWorld.FireCrossing>();   // ⭐ R-220 — one list per thread
                    World.QueryFire(from, to, fire, Doors);
                    for (int i = 0; i < fire.Count; i++)
                    {
                        var c = fire[i];
                        into.Add(new TraceCrossing(c.T, c.PathMetres, c.ResistanceMmRha, c.TopZ, IsClosedBarrier(c.Kind, c.Building),
                            c.Kind, c.Label, c.Material, c.Building, c.Storey));
                    }
                    break;
                }
                case TracePurpose.Sight:
                {
                    // ⚠ QuerySight is the terrain's DIAGNOSTIC form and allocates its answer; the per-frame question is SightBlocked
                    var r = World.QuerySight(from, to, Doors);
                    float length = Vector3.Distance(from, to);
                    foreach (var c in r.Crossed)
                        into.Add(new TraceCrossing(length > 0f ? c.Along / length : 0f, 0f, c.Transmittance, 0f, IsClosedBarrier(c.Kind, c.Building),
                            c.Kind, c.Label, c.Material, c.Building, c.Storey));
                    break;
                }
                default:
                    throw new NotSupportedException($"Trace({purpose}) is not built yet — DESIGN_World_Query_Seam.md slice Q3 (sound).");
            }
        }

        // ── ITerrainRenderGeometry (CE-1033 S1) ──

        object ITerrainRenderGeometry.Identity => World;

        /// <summary>The terrain mesh (the navmesh's own soup, unchanged), each triangle classed by its slope and its height over the ground.</summary>
        public void Build(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds)
        {
            TerrainWorldMesh.Build(World, out vertices, out indices);
            kinds = new TerrainSurfaceKind[indices.Length / 3];
            for (int t = 0; t < kinds.Length; t++)
            {
                var a = vertices[indices[3 * t]]; var b = vertices[indices[3 * t + 1]]; var c = vertices[indices[3 * t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                float len = n.Length();
                if (len < 1e-9f || MathF.Abs(n.Z) / len < 0.5f) { kinds[t] = TerrainSurfaceKind.Wall; continue; }
                var centre = (a + b + c) / 3f;
                kinds[t] = centre.Z > World.GroundHeightAt(centre.X, centre.Y) + 0.3f ? TerrainSurfaceKind.Roof : TerrainSurfaceKind.Ground;
            }
        }

        /// <summary>⭐ CE-1033 S4 — <see cref="Build"/>, with each triangle's material from the mesh's tags (a prism's material,
        /// <c>"forest"</c> ground) and the water surfaces appended as flat <see cref="TerrainSurfaceKind.Water"/> triangles at the
        /// lowest ground height round their edge (the soup leaves a hole there — water is not walkable).</summary>
        public void BuildTagged(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
        {
            Build(out var soupVerts, out var soupIndices, out var soupKinds);
            TerrainWorldMesh.Build(World, out _, out _, out var tags);
            var v = new List<Vector3>(soupVerts);
            var idx = new List<int>(soupIndices);
            var k = new List<TerrainSurfaceKind>(soupKinds);
            var m = new List<string?>(soupKinds.Length);
            for (int t = 0; t < soupKinds.Length; t++) m.Add(t < tags.Length ? tags[t].Material : null);

            foreach (var s in World.Surfaces)
            {
                if (s.Type != TerrainSurfaceType.Water || s.Polygon.Length < 3) continue;
                float level = float.MaxValue;
                foreach (var p in s.Polygon) level = MathF.Min(level, World.GroundHeightAt(p.X, p.Y));
                level -= 0.15f;   // a little below the bank
                int b = v.Count;
                foreach (var p in s.Polygon) v.Add(new Vector3(p, level));
                for (int i = 0; i + 2 < s.Triangles.Length; i += 3)
                {
                    int i0 = b + s.Triangles[i], i1 = b + s.Triangles[i + 1], i2 = b + s.Triangles[i + 2];
                    bool up = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]).Z >= 0f;
                    idx.Add(i0); idx.Add(up ? i1 : i2); idx.Add(up ? i2 : i1);
                    k.Add(TerrainSurfaceKind.Water);
                    m.Add("water");
                }
            }
            vertices = v.ToArray();
            indices = idx.ToArray();
            kinds = k.ToArray();
            materials = m.ToArray();
        }

        /// <summary>
        /// The stand-in's meaning of a closed barrier: a floor or stair (slab, ramp), a door leaf, or a wall panel of a building — the
        /// pieces that ENCLOSE a space, so a blast does not diffract round them (<c>DESIGN_Building_Interiors.md</c> W-7′). A free-standing
        /// wall or a solid block is not one.
        /// </summary>
        public static bool IsClosedBarrier(string kind, string? building)
            => kind is "slab" or "ramp" or "door" || (kind == "panel" && building != null);
    }
}
