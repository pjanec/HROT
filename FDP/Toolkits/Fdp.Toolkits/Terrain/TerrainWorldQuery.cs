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
    public sealed class TerrainWorldQuery : IWorldQuery, ITerrainChunkedGeometry   // ⭐ CE-3423 — chunked (DESIGN_Map_3D_Mode §6ah)
    {
        public TerrainWorld World { get; }
        public DoorStates? Doors { get; }

        [ThreadStatic] private static TerrainWorldQuery? t_last;
        [ThreadStatic] private static List<TerrainWorld.FireCrossing>? t_fire;

        /// <summary>⭐ WQ-E Q3 — a piece whose material the library does not know stops this much sound (dB): concrete's.</summary>
        public const float DefaultSoundDb = 40f;

        /// <summary>⭐ AQ106 K — a solid piece crossed for at least this long (m) counts as two faces for sound, in and out.</summary>
        public const float SolidFacesMetres = 1f;

        /// <summary>⭐ <c>CE-3219</c> part 3 — the gas / smoke clouds of the same view (null = none): sight through one is multiplied by its
        /// transmittance (docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b). Fire and movement ignore them.</summary>
        public Fdp.Toolkit.Combat.CloudTable? Clouds { get; }

        public TerrainWorldQuery(TerrainWorld world, DoorStates? doors = null, Fdp.Toolkit.Combat.CloudTable? clouds = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Doors = doors;
            Clouds = clouds;
        }

        /// <summary>The query for <paramref name="world"/> under <paramref name="doors"/> — this thread's previous one when both are the
        /// same objects (⭐ R-220: <see cref="DoorStates.Of(Fdp.Core.ISimulationView, TerrainWorld)"/> hands back the same table while no
        /// door changed, so a per-tick lookup allocates nothing).</summary>
        public static TerrainWorldQuery For(TerrainWorld world, DoorStates? doors = null, Fdp.Toolkit.Combat.CloudTable? clouds = null)
        {
            var last = t_last;
            if (last != null && ReferenceEquals(last.World, world) && ReferenceEquals(last.Doors, doors) && ReferenceEquals(last.Clouds, clouds)) return last;
            return t_last = new TerrainWorldQuery(world, doors, clouds);
        }

        /// <summary>⭐ <c>CE-3219</c> part 3 — the query for <paramref name="world"/> as <paramref name="view"/> sees it: its doors (R-219) and
        /// its clouds. The one place both are read, so every sight caller gets the same answer.</summary>
        public static TerrainWorldQuery ForView(Fdp.ModuleHost.Abstractions.ISimulationView view, TerrainWorld world)
            => For(world, world.HasLiveState ? DoorStates.Of(view, world) : null, Fdp.Toolkit.Combat.AreaEffectClouds.Of(view));

        public float GroundHeightAt(float x, float y) => World.GroundHeightAt(x, y);   // ⭐ CE-1034 H1 — the height grid, else the flat ground

        public float SurfaceZ(float x, float y, float zHint) => World.SurfaceZ(x, y, zHint);

        public IReadOnlyList<float> SurfacesAt(float x, float y, out int groundIndex) => World.SurfacesAt(x, y, out groundIndex);

        public float ResolveLevel(float x, float y, int level) => World.ResolveLevel(x, y, level);

        /// <summary>The stand-in's rule: not inside any piece's footprint (a building is solid at every height), then the surface near the hint.</summary>
        public bool TryStandAt(float x, float y, float zHint, out float z)
        {
            if (World.InsideFootprint(x, y)) { z = default; return false; }   // ⭐ CE-3279 (a) — narrowed through the spatial index
            z = World.SurfaceZ(x, y, zHint);
            return true;
        }

        public bool SightBlocked(Vector3 from, Vector3 to)
        {
            // ⭐ CE-3219 part 3 — a cloud the line crosses multiplies in; with none crossed this is the terrain's own fast answer
            float cloud = Clouds?.Transmittance(from, to) ?? 1f;
            if (cloud >= 1f) return World.SegmentBlocked(from, to, Doors);
            if (cloud < TerrainWorld.SightThreshold) return true;
            return World.QuerySight(from, to, Doors).Transmittance * cloud < TerrainWorld.SightThreshold;
        }

        // ⭐ CE-3427 — the world query's cost per purpose (who asks is the caller's slot; the terrain primitives have their own)
        private static readonly int[] S_Trace =
        {
            Fdp.Toolkit.Diagnostics.QueryTimings.Slot("world.Trace.Sight"),
            Fdp.Toolkit.Diagnostics.QueryTimings.Slot("world.Trace.Fire"),
            Fdp.Toolkit.Diagnostics.QueryTimings.Slot("world.Trace.Sound"),
        };

        public void Trace(Vector3 from, Vector3 to, TracePurpose purpose, List<TraceCrossing> into)
        {
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            TraceCore(from, to, purpose, into);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Trace[(int)purpose < S_Trace.Length ? (int)purpose : 0], t0);
        }

        private void TraceCore(Vector3 from, Vector3 to, TracePurpose purpose, List<TraceCrossing> into)
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
                            c.Kind, c.Label, c.Material, c.Building, c.Storey) { EntryNormal = c.EntryNormal, ExitNormal = c.ExitNormal });   // ⭐ CE-1128
                    }
                    // ⭐ CE-4369 (S4, R-4190 T-6) — the woods: ONE crossing where the line first enters a wood, stopping a share of rounds
                    //   whatever their penetration (no resistance, not a barrier). Only the FIRE purpose — the round path; QueryFire's other
                    //   readers (blasts, sound, door breaching) are unchanged.
                    if (World.Woods is { } woods)
                    {
                        float stop = woods.FireStop(from, to, World, out float tWood);
                        if (stop > 0f)
                        {
                            int at = 0;
                            while (at < into.Count && into[at].T <= tWood) at++;
                            into.Insert(at, new TraceCrossing(tWood, 0f, 0f, 0f, false, TerrainWorld.WoodsKind, null, "forest", null, -1) { Stop = stop });
                        }
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
                    if (Clouds is { } clouds)   // ⭐ CE-3219 part 3 — each cloud crossed, where the line enters it
                    {
                        for (int i = 0; i < clouds.Count; i++)
                        {
                            var cl = clouds[i];
                            if (!Fdp.Toolkit.Combat.CloudTable.Crosses(in cl, from, to, out float along)) continue;
                            into.Add(new TraceCrossing(along, 0f, cl.Transmittance, cl.TopZ, false, "cloud", null, "cloud", null, 0) { Obscurant = true });   // CE-3249 WS-6
                        }
                        into.Sort(static (x, y) => x.T.CompareTo(y.T));
                    }
                    break;
                }
                case TracePurpose.Sound:
                {
                    // ⭐ WQ-E Q3 v1 (AQ104 E ②) — the straight line through what it crosses, each piece's material dB (materials.json
                    //   soundDb; a slab is the default material). ⛔ The ground (a hill) is no crossing: bass bends round it and off the
                    //   terrain hearing is distance only (Q95 B) — the loss over terrain is WQ-E's v2, not this
                    var fire = t_fire ??= new List<TerrainWorld.FireCrossing>();
                    World.QueryFire(from, to, fire, Doors);
                    for (int i = 0; i < fire.Count; i++)
                    {
                        var c = fire[i];
                        if (c.Kind == TerrainWorld.GroundKind) continue;
                        float db = World.Materials.TryGet(c.Material, out var m) ? m.SoundAttenuationDb : DefaultSoundDb;
                        into.Add(new TraceCrossing(c.T, c.PathMetres, db, c.TopZ, IsClosedBarrier(c.Kind, c.Building),
                            c.Kind, c.Label, c.Material, c.Building, c.Storey) { EntryNormal = c.EntryNormal, ExitNormal = c.ExitNormal });
                        // ⭐ AQ106 K — a SOLID piece the line runs through for a metre or more (a solid block, a thick berm) is two faces: the
                        //   sound goes in through one and out through the other (a thin wall panel is one)
                        if (c.Kind == "prism" && c.PathMetres >= SolidFacesMetres)
                            into.Add(new TraceCrossing(c.T, 0f, db, c.TopZ, IsClosedBarrier(c.Kind, c.Building),
                                c.Kind, c.Label, c.Material, c.Building, c.Storey) { EntryNormal = c.ExitNormal, ExitNormal = c.ExitNormal });
                    }
                    break;
                }
                default:
                    throw new NotSupportedException($"Trace({purpose}) is not built.");
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
                kinds[t] = Classify(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]]);
        }

        /// <summary>A triangle's kind by its slope and its height over the ground — ONE rule for the whole build and the chunks (CE-3423).</summary>
        private TerrainSurfaceKind Classify(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a);
            float len = n.Length();
            if (len < 1e-9f || MathF.Abs(n.Z) / len < 0.5f) return TerrainSurfaceKind.Wall;
            var centre = (a + b + c) / 3f;
            return centre.Z > World.GroundHeightAt(centre.X, centre.Y) + 0.3f ? TerrainSurfaceKind.Roof : TerrainSurfaceKind.Ground;
        }

        // ── ITerrainChunkedGeometry (CE-3423, DESIGN_Map_3D_Mode §6ah) ──

        /// <inheritdoc/>
        public TerrainChunkLayout Chunks => TerrainWorldMesh.ChunkLayout(World);

        /// <summary>⭐ CE-4392 — this view's building damage (the table on its doors), opaque to the map.</summary>
        object? ITerrainChunkedGeometry.Damage => Doors?.Damage;

        /// <inheritdoc/>
        void ITerrainChunkedGeometry.ChunksTouchedBy(object? before, object? after, List<int> into)
        {
            var buildings = new HashSet<int>();
            DamagedBuildings.Touched(World, before as BuildingDamageTable, after as BuildingDamageTable, buildings);
            var seen = new HashSet<int>();
            foreach (int b in buildings)
                foreach (int k in TerrainWorldMesh.ChunksOfBuilding(World, b))
                    if (seen.Add(k)) into.Add(k);
        }

        /// <inheritdoc/>
        public void BuildChunk(int cx, int cy, int level, TerrainChunkPart part, out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
            => BuildChunk(cx, cy, level, part, null, out vertices, out indices, out kinds, out materials);

        /// <summary>⭐ CE-4392 — the chunk under <paramref name="damage"/> (a <see cref="BuildingDamageTable"/>, or null): only the objects part differs.</summary>
        public void BuildChunk(int cx, int cy, int level, TerrainChunkPart part, object? damage, out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var g = new List<TerrainTriangleTag>();
            int water = 0;
            bool trees = part is TerrainChunkPart.ChunkTrees or TerrainChunkPart.ChunkCanopy;
            if (part == TerrainChunkPart.ChunkGround) TerrainWorldMesh.BuildChunkGround(World, cx, cy, level, v, t, g);
            else if (trees) TerrainWorldMesh.BuildChunkTrees(World, cx, cy, part == TerrainChunkPart.ChunkTrees, v, t, g);   // ⭐ CE-4377 §6ai
            else TerrainWorldMesh.BuildChunkObjects(World, cx, cy, damage as BuildingDamageTable, v, t, g, out water);
            vertices = v.ToArray(); indices = t.ToArray();
            int n = indices.Length / 3, solid = n - water;
            kinds = new TerrainSurfaceKind[n]; materials = new string?[n];
            for (int k = 0; k < n; k++)
            {
                if (k >= solid) { kinds[k] = TerrainSurfaceKind.Water; materials[k] = "water"; continue; }
                var tag = g[k];
                if (tag.Material == TerrainWorldMesh.SkirtMaterial) { kinds[k] = TerrainSurfaceKind.Ground; continue; }   // L-4: a skirt is ground, never a wall
                if (trees) { kinds[k] = TerrainSurfaceKind.Wall; materials[k] = tag.Material; continue; }                // T-4: a tree's colour is its material's
                kinds[k] = Classify(vertices[indices[3 * k]], vertices[indices[3 * k + 1]], vertices[indices[3 * k + 2]]);
                materials[k] = tag.Material;
            }
        }

        /// <inheritdoc/>
        public void BuildOutside(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
        {
            TerrainWorldMesh.BuildApron(World, out vertices, out indices);
            kinds = new TerrainSurfaceKind[indices.Length / 3];   // Ground (0)
            materials = new string?[kinds.Length];
            Array.Fill(materials, TerrainWorldMesh.ApronMaterial);
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

            // ⭐ the APRON (TerrainWorldMesh.BuildApron) — fake ground round the terrain, drawn only: the navmesh reads TerrainWorldMesh.Build, never this
            TerrainWorldMesh.BuildApron(World, out var apronVerts, out var apronIndices);
            int apronBase = v.Count;
            v.AddRange(apronVerts);
            for (int i = 0; i < apronIndices.Length; i += 3)
            {
                idx.Add(apronBase + apronIndices[i]); idx.Add(apronBase + apronIndices[i + 1]); idx.Add(apronBase + apronIndices[i + 2]);
                k.Add(TerrainSurfaceKind.Ground);
                m.Add(TerrainWorldMesh.ApronMaterial);
            }
            foreach (var s in World.Surfaces)
            {
                int before = idx.Count;
                TerrainWorldMesh.AppendWater(World, s, v, idx);   // ⭐ CE-3423 — the ONE water builder, shared with the chunks
                for (int i = before; i < idx.Count; i += 3) { k.Add(TerrainSurfaceKind.Water); m.Add("water"); }
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
