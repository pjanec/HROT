using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a solid terrain prism is — drives only the default draw style and label.</summary>
    public enum TerrainPrismKind : byte { Building = 0, Wall = 1 }

    /// <summary>What a walkable terrain surface with its own Z is.</summary>
    public enum TerrainWalkableKind : byte { Slab = 0, Ramp = 1 }

    /// <summary>The ground cover of a flat surface area.</summary>
    public enum TerrainSurfaceType : byte { Open = 0, Road = 1, Forest = 2, Water = 3 }

    /// <summary>
    /// A SOLID extruded polygon — a building or a wall: blocks movement and sight from <see cref="BaseZ"/>
    /// to <see cref="TopZ"/>; its roof is walkable at <see cref="TopZ"/>.
    /// </summary>
    public sealed class TerrainPrism
    {
        public TerrainPrismKind Kind { get; init; }
        /// <summary>Footprint, counter-clockwise, no repeated closing point (engine X east / Y north).</summary>
        public Vector2[] Footprint { get; init; } = Array.Empty<Vector2>();
        public float BaseZ { get; init; }
        public float TopZ { get; init; }
        /// <summary>Storeys, for the label only in v1 (a prism is solid).</summary>
        public int Floors { get; init; }
        public string? Label { get; init; }
        /// <summary>Footprint triangulated once at parse (index triples into <see cref="Footprint"/>).</summary>
        public int[] Triangles { get; init; } = Array.Empty<int>();
        public Vector2 Min { get; init; }
        public Vector2 Max { get; init; }
        public float Height => TopZ - BaseZ;
        /// <summary>⭐ Stage 1 — what it is made of (§3c); null only for a prism built in code without one (read as concrete).</summary>
        public TerrainMaterial? Material { get; init; }
        /// <summary>⭐ Stage 1 — the <see cref="TerrainWallPanel"/> this prism is a piece of (index into
        /// <see cref="TerrainWorld.Panels"/>), −1 for a solid building/wall that is not a panel.</summary>
        public int Panel { get; init; } = -1;
    }

    /// <summary>
    /// A walkable polygon with its own per-vertex Z — a floor slab (multi-level) or a ramp linking levels.
    /// Thin: it blocks a sight line that crosses it, and it is a surface an entity can stand on.
    /// </summary>
    public sealed class TerrainWalkable
    {
        public TerrainWalkableKind Kind { get; init; }
        /// <summary>Outline, counter-clockwise in plan view, each vertex carrying its Z.</summary>
        public Vector3[] Vertices { get; init; } = Array.Empty<Vector3>();
        public int[] Triangles { get; init; } = Array.Empty<int>();
        public Vector2 Min { get; init; }
        public Vector2 Max { get; init; }
        public float MinZ { get; init; }
        public float MaxZ { get; init; }
    }

    /// <summary>A flat ground-cover area at ground level (road, open, forest, water).</summary>
    public sealed class TerrainSurface
    {
        public TerrainSurfaceType Type { get; init; }
        public Vector2[] Polygon { get; init; } = Array.Empty<Vector2>();
        public int[] Triangles { get; init; } = Array.Empty<int>();
        public Vector2 Min { get; init; }
        public Vector2 Max { get; init; }
    }

    /// <summary>
    /// ⭐⭐⭐ THE TERRAIN WORLD — the one in-memory model a terrain's world file is parsed into, on every
    /// ECS node. Navigation (the navmesh is built from it), movement (the surface Z), perception (sight lines)
    /// and the 2D map all read THIS, so they cannot disagree.
    ///
    /// <para>⭐ Engine space throughout: X east, Y north, Z up, local metres.</para>
    /// <para>⛔ Never persisted — <c>[DataPolicy(NoScenario | NoReplay)]</c>: it is re-derived from the
    /// named terrain asset on every load, like <see cref="TerrainDefinition"/>.</para>
    /// 📄 docs/DESIGN_Terrain_World.md §2–§4.
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.TerrainWorld)]
    public sealed class TerrainWorld
    {
        /// <summary>How far above its current Z an entity may step onto a surface (a kerb, a ramp start).</summary>
        public const float StepHeight = 0.6f;

        /// <summary>The terrain's catalog name (e.g. <c>test-town</c>), stamped at load; null for a world built in code.
        /// ⭐ CE-3028 — so a reader of the singleton (<c>GET /world/info</c>) can name what is resident.</summary>
        public string? Name { get; init; }
        public Vector2 BoundsMin { get; init; }
        public Vector2 BoundsMax { get; init; }
        public float GroundZ { get; init; }
        public IReadOnlyList<TerrainPrism> Prisms { get; init; } = Array.Empty<TerrainPrism>();
        public IReadOnlyList<TerrainWalkable> Walkables { get; init; } = Array.Empty<TerrainWalkable>();
        public IReadOnlyList<TerrainSurface> Surfaces { get; init; } = Array.Empty<TerrainSurface>();

        /// <summary>⭐ Stage 1 — every wall/fence panel with its openings and material; each is ALSO expanded into
        /// <see cref="Prisms"/> (its solid pieces), so consumers that only know prisms see doorways and windows as gaps.
        /// 📄 docs/DESIGN_Building_Interiors.md §2, §3a, §3c.</summary>
        public IReadOnlyList<TerrainWallPanel> Panels { get; init; } = Array.Empty<TerrainWallPanel>();
        /// <summary>⭐ Stage 1 — the placed buildings (template, position, storey floors).</summary>
        public IReadOnlyList<TerrainBuilding> Buildings { get; init; } = Array.Empty<TerrainBuilding>();
        /// <summary>⭐ Stage 1 — the doors the terrain defines, by terrain-object key (§3b); door ENTITIES come with Stage 5.</summary>
        public IReadOnlyList<TerrainDoorDef> Doors { get; init; } = Array.Empty<TerrainDoorDef>();
        /// <summary>⭐ Stage 1 — the material library this world was parsed with (shared + terrain overrides).</summary>
        public TerrainMaterialLibrary Materials { get; init; } = TerrainMaterialLibrary.Shared;

        /// <summary>
        /// ⭐ The Z an entity at (<paramref name="x"/>, <paramref name="y"/>) stands on — the ground, a roof or
        /// a floor. <paramref name="zHint"/> is the entity's current Z: the highest surface no more than
        /// <see cref="StepHeight"/> above it wins, so a unit on the second deck of a garage stays on that deck
        /// and a unit walking under it stays on the ground. When every candidate is above the reach (a unit
        /// spawned at Z=0 inside a building footprint), the LOWEST candidate wins.
        /// </summary>
        public float SurfaceZ(float x, float y, float zHint)
        {
            var p = new Vector2(x, y);
            float best = float.NegativeInfinity;
            float lowest = float.PositiveInfinity;
            float reach = zHint + StepHeight;
            bool insideSolid = false;

            foreach (var prism in Prisms)
            {
                if (!InBox(p, prism.Min, prism.Max) || !PolygonMath.Contains(prism.Footprint, p)) continue;
                insideSolid = true;
                Consider(prism.TopZ, reach, ref best, ref lowest);
            }

            foreach (var w in Walkables)
            {
                if (!InBox(p, w.Min, w.Max)) continue;
                for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
                {
                    float? z = PolygonMath.HeightOnTriangle(x, y,
                        w.Vertices[w.Triangles[t]], w.Vertices[w.Triangles[t + 1]], w.Vertices[w.Triangles[t + 2]]);
                    if (z.HasValue) { Consider(z.Value, reach, ref best, ref lowest); break; }
                }
            }

            // The ground is a candidate unless the point is inside a solid prism (you cannot stand under a
            // building's footprint at ground level — it is solid in v1).
            if (!insideSolid) Consider(GroundZ, reach, ref best, ref lowest);

            if (!float.IsNegativeInfinity(best)) return best;
            return float.IsPositiveInfinity(lowest) ? GroundZ : lowest;
        }

        /// <summary>Surfaces closer than this merge into one LEVEL (a ramp foot meeting the ground is not a second level).</summary>
        public const float LevelMergeDistance = 0.3f;

        /// <summary>
        /// ⭐ <c>CE-1017</c> S2 — the LEVELS at (<paramref name="x"/>, <paramref name="y"/>): every surface there, ascending,
        /// merged within <see cref="LevelMergeDistance"/>. 🔒 Level 0 is ALWAYS <see cref="GroundZ"/> — in the open AND inside
        /// a building footprint (the roof is an explicit +n); <paramref name="groundIndex"/> is its index in the result, so
        /// level <c>n</c> is <c>result[groundIndex + n]</c>. A surface within the merge distance of the ground merges INTO
        /// level 0; any other cluster stands at its HIGHEST member (so an entity never lands inside the thicker one).
        /// 📄 docs/DESIGN_Add_Entity_Picker.md §2b–§2c (D6b rev 4/5).
        /// </summary>
        public float[] SurfacesAt(float x, float y, out int groundIndex)
        {
            var p = new Vector2(x, y);
            var below = new List<float>();
            var above = new List<float>();
            void Add(float z)
            {
                if (z < GroundZ - LevelMergeDistance) below.Add(z);
                else if (z > GroundZ + LevelMergeDistance) above.Add(z);
                // else: merges into the ground level
            }

            foreach (var prism in Prisms)
                if (InBox(p, prism.Min, prism.Max) && PolygonMath.Contains(prism.Footprint, p)) Add(prism.TopZ);

            foreach (var w in Walkables)
            {
                if (!InBox(p, w.Min, w.Max)) continue;
                for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
                {
                    float? z = PolygonMath.HeightOnTriangle(x, y,
                        w.Vertices[w.Triangles[t]], w.Vertices[w.Triangles[t + 1]], w.Vertices[w.Triangles[t + 2]]);
                    if (z.HasValue) { Add(z.Value); break; }
                }
            }

            var levels = new List<float>(below.Count + above.Count + 1);
            MergeAscending(below, levels);
            groundIndex = levels.Count;
            levels.Add(GroundZ);
            MergeAscending(above, levels);
            return levels.ToArray();
        }

        /// <inheritdoc cref="SurfacesAt(float, float, out int)"/>
        public float[] SurfacesAt(float x, float y) => SurfacesAt(x, y, out _);

        /// <summary>
        /// ⭐ <c>CE-1017</c> S2 — the Z of <paramref name="level"/> at (<paramref name="x"/>, <paramref name="y"/>): 0 = the
        /// ground, +n = the n-th level above, −n = the n-th below; past the top ⇒ the HIGHEST, past the bottom ⇒ the LOWEST.
        /// The one function the creating node, the placement ghost and "move to level" share.
        /// </summary>
        public float ResolveLevel(float x, float y, int level)
        {
            var levels = SurfacesAt(x, y, out int ground);
            long i = (long)ground + level;
            if (i < 0) i = 0;
            if (i >= levels.Length) i = levels.Length - 1;
            return levels[i];
        }

        private static void MergeAscending(List<float> zs, List<float> into)
        {
            zs.Sort();
            int start = 0;
            for (int i = 1; i <= zs.Count; i++)
            {
                if (i < zs.Count && zs[i] - zs[start] <= LevelMergeDistance) continue;
                if (i > start) into.Add(zs[i - 1]);   // the cluster's highest member
                start = i;
            }
        }

        /// <summary>
        /// ⭐ True when the straight sight line <paramref name="from"/>→<paramref name="to"/> is blocked by the
        /// terrain: a prism it passes through within the prism's height, a slab/ramp it crosses, or the ground
        /// it dips under. Dynamic obstacles (entities) are not part of the world — the LOS strategy adds them.
        /// </summary>
        public bool SegmentBlocked(Vector3 from, Vector3 to)
        {
            // Under the ground at either end means a malformed query, not an occluder — ignore; a line that
            // dips below the flat ground between two points above it is impossible, so no ground test needed
            // until a heightfield exists.
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            var segMin = Vector2.Min(a, b);
            var segMax = Vector2.Max(a, b);

            foreach (var prism in Prisms)
            {
                if (!BoxesOverlap(segMin, segMax, prism.Min, prism.Max)) continue;
                foreach (var (t0, t1) in PolygonMath.InsideIntervals(prism.Footprint, a, b))
                {
                    float z0 = from.Z + ((to.Z - from.Z) * t0);
                    float z1 = from.Z + ((to.Z - from.Z) * t1);
                    if (MathF.Min(z0, z1) < prism.TopZ && MathF.Max(z0, z1) > prism.BaseZ) return true;
                }
            }

            foreach (var w in Walkables)
            {
                if (!BoxesOverlap(segMin, segMax, w.Min, w.Max)) continue;
                for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
                {
                    if (PolygonMath.SegmentCrossesTriangle(from, to,
                            w.Vertices[w.Triangles[t]], w.Vertices[w.Triangles[t + 1]], w.Vertices[w.Triangles[t + 2]]))
                        return true;
                }
            }
            return false;
        }

        /// <summary>⭐ Stage 1 — a line SEES THROUGH when its sight transmittance is at least this (§3c M3, v1).</summary>
        public const float SightThreshold = 0.5f;

        /// <summary>One occluder a trace crossed.</summary>
        public readonly record struct Crossing(float Along, string Kind, string? Label, string? Material, float Transmittance,
            string? Building, int Storey);

        /// <summary>A trace's answer: the product of the crossed transmittances, and the crossings in order along the line.</summary>
        public readonly record struct TraceResult(float Transmittance, IReadOnlyList<Crossing> Crossed);

        /// <summary>
        /// ⭐ Buildings programme Stage 1 — the SIGHT trace with its evidence (📄 docs/DESIGN_Building_Interiors.md §3a "one
        /// query, a solver per purpose", §3c M3): every solid piece the line passes within its height, at its material's
        /// sight transmittance; every slab/ramp it crosses, opaque. Transmittance multiplies along the line.
        /// <para>⚠ Stage 1 serves it for diagnostics (<c>GET /terrain/query</c>); perception still asks
        /// <see cref="SegmentBlocked"/> until Stage 3 makes that <c>QuerySight(...) &lt; threshold</c>.</para>
        /// </summary>
        public TraceResult QuerySight(Vector3 from, Vector3 to)
        {
            var crossed = new List<Crossing>();
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            var segMin = Vector2.Min(a, b);
            var segMax = Vector2.Max(a, b);
            float length = Vector3.Distance(from, to);

            foreach (var prism in Prisms)
            {
                if (!BoxesOverlap(segMin, segMax, prism.Min, prism.Max)) continue;
                foreach (var (t0, t1) in PolygonMath.InsideIntervals(prism.Footprint, a, b))
                {
                    float z0 = from.Z + ((to.Z - from.Z) * t0);
                    float z1 = from.Z + ((to.Z - from.Z) * t1);
                    if (!(MathF.Min(z0, z1) < prism.TopZ && MathF.Max(z0, z1) > prism.BaseZ)) continue;
                    var panel = prism.Panel >= 0 && prism.Panel < Panels.Count ? Panels[prism.Panel] : null;
                    string? building = panel != null && panel.Building >= 0 && panel.Building < Buildings.Count ? Buildings[panel.Building].Label : null;
                    crossed.Add(new Crossing(t0 * length, panel != null ? "panel" : "prism", prism.Label,
                        prism.Material?.Name ?? TerrainMaterialLibrary.DefaultMaterial, prism.Material?.SightTransmittance ?? 0f,
                        building, panel?.Storey ?? -1));
                    break;   // one entry per piece
                }
            }

            foreach (var w in Walkables)
            {
                if (!BoxesOverlap(segMin, segMax, w.Min, w.Max)) continue;
                for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
                {
                    var p0 = w.Vertices[w.Triangles[t]]; var p1 = w.Vertices[w.Triangles[t + 1]]; var p2 = w.Vertices[w.Triangles[t + 2]];
                    if (!PolygonMath.SegmentCrossesTriangle(from, to, p0, p1, p2)) continue;
                    var n = Vector3.Cross(p1 - p0, p2 - p0);
                    float denom = Vector3.Dot(n, to - from);
                    float along = MathF.Abs(denom) < 1e-9f ? 0f : Vector3.Dot(n, p0 - from) / denom * length;
                    crossed.Add(new Crossing(along, w.Kind == TerrainWalkableKind.Ramp ? "ramp" : "slab", null, null, 0f, null, -1));
                    break;
                }
            }

            crossed.Sort((x, y) => x.Along.CompareTo(y.Along));
            float transmittance = 1f;
            foreach (var c in crossed) transmittance *= c.Transmittance;
            return new TraceResult(transmittance, crossed);
        }

        /// <summary>The surface type at a point (the last-listed surface wins on overlap), Open by default.</summary>
        public TerrainSurfaceType SurfaceTypeAt(float x, float y)
        {
            var p = new Vector2(x, y);
            var result = TerrainSurfaceType.Open;
            foreach (var s in Surfaces)
                if (InBox(p, s.Min, s.Max) && PolygonMath.Contains(s.Polygon, p)) result = s.Type;
            return result;
        }

        private static void Consider(float z, float reach, ref float best, ref float lowest)
        {
            if (z <= reach && z > best) best = z;
            if (z < lowest) lowest = z;
        }

        private static bool InBox(Vector2 p, Vector2 min, Vector2 max)
            => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        private static bool BoxesOverlap(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
            => aMin.X <= bMax.X && aMax.X >= bMin.X && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y;
    }
}
