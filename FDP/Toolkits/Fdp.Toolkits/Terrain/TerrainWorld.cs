using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a solid terrain prism is — drives only the default draw style and label.</summary>
    public enum TerrainPrismKind : byte { Building = 0, Wall = 1 }

    /// <summary>What a walkable terrain surface with its own Z is.</summary>
    public enum TerrainWalkableKind : byte { Slab = 0, Ramp = 1 }

    /// <summary>The ground cover of a flat surface area.</summary>
    /// <summary>A ground surface's type. ⛔ <c>Road = 1</c> retired with CE-3128 (R-231): roads are the terrain's road graph.</summary>
    public enum TerrainSurfaceType : byte { Open = 0, Forest = 2, Water = 3 }

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

    /// <summary>A flat ground-cover area at ground level (open, forest, water — roads are the road graph, CE-3128).</summary>
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

        /// <summary>⭐ CE-1034 H1 (TH-B) — the ground's relief, or null for the temporary flat ground at <see cref="GroundZ"/> (R-248).</summary>
        public TerrainHeightGrid? Height { get; init; }

        /// <summary>⭐ CE-1034 H1 — the ground's height at (x, y): the height grid where there is one, else <see cref="GroundZ"/>. Every
        /// question about "the ground" goes through this — never read <see cref="GroundZ"/> as the ground (R-248).</summary>
        public float GroundHeightAt(float x, float y) => Height?.Sample(x, y) ?? GroundZ;
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

        // ── ⭐ Stage 5 (doors) — the door LEAVES and their live state ─────────────────────────────────────────────

        /// <summary>The material a closed door leaf is (sight 0; a wooden door — a rifle round goes through).</summary>
        public const string DoorMaterial = "door-wood";
        /// <summary>A door leaf's thickness (m), centred in its wall.</summary>
        public const float DoorLeafThickness = 0.05f;

        /// <summary>
        /// ⭐ Stage 5 (📄 docs/DESIGN_Building_Interiors.md §3a, §3j) — one closing leaf per door (index-aligned with <see cref="Doors"/>):
        /// a thin piece across the doorway, from its sill to its head. ⛔ NOT in <see cref="Prisms"/> — the navmesh is baked with every
        /// door OPEN (§3a), so nothing that reads prisms ever sees a leaf; the sight and fire queries add the leaves of doors that are
        /// not open (in the caller's <see cref="DoorStates"/>).
        /// </summary>
        public IReadOnlyList<TerrainPrism> DoorLeaves => _doorLeaves.Value;

        /// <summary>⭐ R-220 — the most vertices any prism or door leaf has: sizes the queries' stack scratch, computed once.</summary>
        private int MaxFootprintVertices => _maxFootprintVertices >= 0 ? _maxFootprintVertices : (_maxFootprintVertices = ComputeMaxFootprintVertices());
        private int _maxFootprintVertices = -1;
        private int ComputeMaxFootprintVertices()
        {
            int m = 4;   // a door leaf
            for (int i = 0; i < Prisms.Count; i++) m = Math.Max(m, Prisms[i].Footprint.Length);
            return m;
        }

        /// <summary>The index of the door whose terrain-object key is <paramref name="key"/>, or −1.</summary>
        public int DoorIndexOf(string key) => _doorIndex.Value.TryGetValue(key, out int i) ? i : -1;

        private Lazy<Dictionary<string, int>> _doorIndex => _doorIndexLazy ??= new Lazy<Dictionary<string, int>>(() =>
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Doors.Count; i++) d[Doors[i].Key] = i;
            return d;
        });
        private Lazy<Dictionary<string, int>>? _doorIndexLazy;

        /// <summary>
        /// A door blocks when it is closed or locked (an open or destroyed door is a gap). ⭐ R-219: the state comes from the CALLER's
        /// <paramref name="doors"/> — the table built from the view it runs on; with none, the door is as the terrain authored it.
        /// ⛔ <see cref="TerrainWorld"/> holds NO live state: it is shared by reference into every background snapshot.
        /// </summary>
        private bool DoorBlocks(int index, DoorStates? doors)
            => (doors != null ? doors[index] : Doors[index].Initial) is TerrainDoorState.Closed or TerrainDoorState.Locked;

        private Lazy<IReadOnlyList<TerrainPrism>> _doorLeaves => _doorLeavesLazy ??= new Lazy<IReadOnlyList<TerrainPrism>>(BuildDoorLeaves);
        private Lazy<IReadOnlyList<TerrainPrism>>? _doorLeavesLazy;

        private IReadOnlyList<TerrainPrism> BuildDoorLeaves()
        {
            var leaves = new List<TerrainPrism>(Doors.Count);
            Materials.TryGet(DoorMaterial, out var material);
            foreach (var d in Doors)
            {
                var panel = Panels[d.Panel];
                var o = panel.Openings[d.Opening];
                var dir = panel.B - panel.A;
                float len = dir.Length();
                dir = len > 0f ? dir / len : Vector2.UnitX;
                var n = new Vector2(-dir.Y, dir.X) * (DoorLeafThickness * 0.5f);
                var p0 = panel.A + dir * o.At;
                var p1 = panel.A + dir * (o.At + o.Width);
                var fp = new[] { p0 - n, p1 - n, p1 + n, p0 + n };
                leaves.Add(new TerrainPrism
                {
                    Kind = TerrainPrismKind.Wall, Footprint = fp, BaseZ = o.SillZ, TopZ = o.HeadZ, Label = d.Key,
                    Min = Vector2.Min(Vector2.Min(fp[0], fp[1]), Vector2.Min(fp[2], fp[3])),
                    Max = Vector2.Max(Vector2.Max(fp[0], fp[1]), Vector2.Max(fp[2], fp[3])),
                    Material = material, Panel = d.Panel, Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                });
            }
            return leaves;
        }

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
                // ⭐ Buildings 5d-2 (live, bt-doors) — a piece that STARTS above reach (a door's lintel at 2.1 m, a window's head)
                //   is OVERHEAD: you stand under it, so it does not take the ground away. ⛔ It did, and an agent walking through a
                //   doorway under an upper floor came out at that floor's height (3 m).
                // ⭐ CE-3111 (live, bt-doors with real 0.9 m doors) — a WALL PANEL (Panel ≥ 0) never takes the ground away: it is thin,
                //   nobody stands inside it, and a mover turning through a narrow doorway clips a jamb by centimetres. ⛔ It did: the
                //   wall's top (3 m) became the only surface, and the agent's Z hint then held it on the upper floor's slab. The same
                //   rule as the ground mesh (TerrainWorldMesh: a panel keeps its ground). Its top is still a candidate surface.
                if (prism.BaseZ <= reach && prism.Panel < 0) insideSolid = true;
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
            float ground = GroundHeightAt(x, y);   // ⭐ CE-1034 H1 — the ground HERE, not one flat height
            if (!insideSolid) Consider(ground, reach, ref best, ref lowest);

            if (!float.IsNegativeInfinity(best)) return best;
            return float.IsPositiveInfinity(lowest) ? ground : lowest;
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
            float ground = GroundHeightAt(x, y);   // ⭐ CE-1034 H1 — level 0 is the ground at (x, y)
            var p = new Vector2(x, y);
            var below = new List<float>();
            var above = new List<float>();
            void Add(float z)
            {
                if (z < ground - LevelMergeDistance) below.Add(z);
                else if (z > ground + LevelMergeDistance) above.Add(z);
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
            levels.Add(ground);
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
        /// ⭐ CE-1035 Q0 (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-D) — an optional spatial index that narrows the pieces and
        /// walkables a segment query examines. Null (the default) = every piece, exactly as before. It only ever NARROWS: the
        /// per-piece maths below is unchanged, so an index that returns a superset of the true crossings, in ascending order,
        /// gives identical answers. Set once after the world is built; a derived world (<see cref="StaticObstacles"/>) has none.
        /// </summary>
        public ITerrainSpatialIndex? SpatialIndex { get; set; }

        /// <summary>The pieces (prisms, then door leaves when doors exist) and walkables one segment query visits.</summary>
        private ref struct CandidateSet
        {
            private readonly int[]? _pieces;
            private readonly int[]? _walkables;
            public readonly int PieceCount;
            public readonly int WalkableCount;

            public CandidateSet(TerrainWorld world, Vector3 from, Vector3 to, int pieceTotal)
            {
                var index = world.SpatialIndex;
                if (index == null)
                {
                    _pieces = null; _walkables = null;
                    PieceCount = pieceTotal; WalkableCount = world.Walkables.Count;
                    return;
                }
                _pieces = System.Buffers.ArrayPool<int>.Shared.Rent(Math.Max(1, pieceTotal));   // ⭐ R-220 — pooled, no allocation once warm
                _walkables = System.Buffers.ArrayPool<int>.Shared.Rent(Math.Max(1, world.Walkables.Count));
                index.Candidates(from, to, pieceTotal, _pieces, out int pc, _walkables, out int wc);
                PieceCount = pc; WalkableCount = wc;
            }

            public readonly int Piece(int k) => _pieces == null ? k : _pieces[k];
            public readonly int Walkable(int k) => _walkables == null ? k : _walkables[k];

            public readonly void Dispose()
            {
                if (_pieces != null) System.Buffers.ArrayPool<int>.Shared.Return(_pieces);
                if (_walkables != null) System.Buffers.ArrayPool<int>.Shared.Return(_walkables);
            }
        }

        /// <summary>
        /// ⭐ True when the straight sight line <paramref name="from"/>→<paramref name="to"/> is blocked by the
        /// terrain: its SIGHT TRANSMITTANCE is below <see cref="SightThreshold"/> — the product of the materials of the
        /// solid pieces it passes within their height (concrete/brick 0, chain-link 0.85, hedge 0.3, §3c M3), with any
        /// slab/ramp it crosses opaque. Dynamic obstacles (entities) are not part of the world — the LOS strategy adds them.
        /// <para>⭐ Buildings programme Stage 3 (sight half): the SAME rule <see cref="QuerySight"/> reports, so
        /// <c>GET /terrain/query</c> and perception agree. A prism with no material reads as opaque (concrete), so every
        /// world built before materials behaves exactly as before.</para>
        /// </summary>
        /// <param name="doors">⭐ R-219 — the door states of the caller's view (<see cref="DoorStates.Of(Fdp.Core.ISimulationView, TerrainWorld)"/>); null = as authored.</param>
        public bool SegmentBlocked(Vector3 from, Vector3 to, DoorStates? doors = null)
        {
            float transmittance = 1f;
            // ⭐ CE-1034 H2 (TH-D) — a HILL blocks sight: the ground trace over the height grid (opaque). Without a grid the ground is
            //   flat and no line between two points above it can dip under it. Under the ground at either end = malformed, ignored.
            if (Height != null && Height.Crosses(from, to)) return true;
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            var segMin = Vector2.Min(a, b);
            var segMax = Vector2.Max(a, b);

            // ⭐ Stage 5 — the prisms, then the leaves of doors that are shut (a closed/locked door is a panel; open = a gap)
            var leaves = Doors.Count > 0 ? DoorLeaves : Array.Empty<TerrainPrism>();
            int maxV = MaxFootprintVertices;
            Span<float> ts = maxV + 2 <= 256 ? stackalloc float[maxV + 2] : new float[maxV + 2];
            Span<(float T0, float T1)> iv = maxV + 1 <= 256 ? stackalloc (float, float)[maxV + 1] : new (float, float)[maxV + 1];
            using var cand = new CandidateSet(this, from, to, Prisms.Count + leaves.Count);   // ⭐ CE-1035 Q0 — every piece, or the index's candidates
            for (int k = 0; k < cand.PieceCount; k++)
            {
                int pi = cand.Piece(k);
                if (pi >= Prisms.Count && !DoorBlocks(pi - Prisms.Count, doors)) continue;
                var prism = pi < Prisms.Count ? Prisms[pi] : leaves[pi - Prisms.Count];
                if (!BoxesOverlap(segMin, segMax, prism.Min, prism.Max)) continue;
                foreach (var (t0, t1) in iv.Slice(0, PolygonMath.InsideIntervals(prism.Footprint, a, b, ts, iv)))   // ⭐ R-220 — no allocation
                {
                    float z0 = from.Z + ((to.Z - from.Z) * t0);
                    float z1 = from.Z + ((to.Z - from.Z) * t1);
                    if (MathF.Min(z0, z1) < prism.TopZ && MathF.Max(z0, z1) > prism.BaseZ)
                    {
                        transmittance *= prism.Material?.SightTransmittance ?? 0f;
                        if (transmittance < SightThreshold) return true;
                        break;   // one crossing per piece — as QuerySight counts it
                    }
                }
            }

            for (int kw = 0; kw < cand.WalkableCount; kw++)   // ⭐ R-220 — an index loop: no interface enumerator
            {
                var w = Walkables[cand.Walkable(kw)];
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

        /// <summary>⭐ CE-1034 H2 — the crossing kind and material name a hill in the way reports (descriptive).</summary>
        public const string GroundKind = "ground", GroundMaterial = "earth";

        /// <summary>⭐ CE-1034 H2 — the resistance a hill presents to a round or a fragment: it stops anything.</summary>
        public const float GroundResistanceMmRha = 1_000_000f;

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
        /// <param name="doors">⭐ R-219 — the door states of the caller's view; null = as authored.</param>
        /// <remarks>⚠ R-220 — allocates its answer (the crossing list): a DIAGNOSTIC trace, not a per-tick query. Perception
        /// asks <see cref="SegmentBlocked"/>, which allocates nothing.</remarks>
        public TraceResult QuerySight(Vector3 from, Vector3 to, DoorStates? doors = null)
        {
            var crossed = new List<Crossing>();
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            var segMin = Vector2.Min(a, b);
            var segMax = Vector2.Max(a, b);
            float length = Vector3.Distance(from, to);
            // ⭐ CE-1034 H2 — a hill in the way is an opaque crossing where the line enters the ground
            if (Height != null && Height.Crosses(from, to, out float groundT, out _))
                crossed.Add(new Crossing(groundT * length, GroundKind, null, GroundMaterial, 0f, null, -1));

            // ⭐ Stage 5 — the prisms, then the leaves of doors that are shut (a closed/locked door is a panel; open = a gap)
            var leaves = Doors.Count > 0 ? DoorLeaves : Array.Empty<TerrainPrism>();
            int maxV = MaxFootprintVertices;
            Span<float> ts = maxV + 2 <= 256 ? stackalloc float[maxV + 2] : new float[maxV + 2];
            Span<(float T0, float T1)> iv = maxV + 1 <= 256 ? stackalloc (float, float)[maxV + 1] : new (float, float)[maxV + 1];
            using var cand = new CandidateSet(this, from, to, Prisms.Count + leaves.Count);   // ⭐ CE-1035 Q0 — every piece, or the index's candidates
            for (int k = 0; k < cand.PieceCount; k++)
            {
                int pi = cand.Piece(k);
                if (pi >= Prisms.Count && !DoorBlocks(pi - Prisms.Count, doors)) continue;
                var prism = pi < Prisms.Count ? Prisms[pi] : leaves[pi - Prisms.Count];
                if (!BoxesOverlap(segMin, segMax, prism.Min, prism.Max)) continue;
                foreach (var (t0, t1) in iv.Slice(0, PolygonMath.InsideIntervals(prism.Footprint, a, b, ts, iv)))   // ⭐ R-220 — no allocation
                {
                    float z0 = from.Z + ((to.Z - from.Z) * t0);
                    float z1 = from.Z + ((to.Z - from.Z) * t1);
                    if (!(MathF.Min(z0, z1) < prism.TopZ && MathF.Max(z0, z1) > prism.BaseZ)) continue;
                    var panel = prism.Panel >= 0 && prism.Panel < Panels.Count ? Panels[prism.Panel] : null;
                    string? building = panel != null && panel.Building >= 0 && panel.Building < Buildings.Count ? Buildings[panel.Building].Label : null;
                    crossed.Add(new Crossing(t0 * length, pi >= Prisms.Count ? "door" : panel != null ? "panel" : "prism", prism.Label,
                        prism.Material?.Name ?? TerrainMaterialLibrary.DefaultMaterial, prism.Material?.SightTransmittance ?? 0f,
                        building, panel?.Storey ?? -1));
                    break;   // one entry per piece
                }
            }

            for (int kw = 0; kw < cand.WalkableCount; kw++)   // ⭐ R-220 — an index loop: no interface enumerator
            {
                var w = Walkables[cand.Walkable(kw)];
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

        /// <summary>⭐ §3d P2 — the thickness (m) a floor slab or stair ramp presents to a round, of the default material (concrete):
        /// a walkable is a surface with no thickness of its own. 0.2 m of concrete = 300 mm RHA with the starter table.</summary>
        public const float SlabThicknessMetres = 0.2f;

        /// <summary>
        /// One piece a FIRE trace crossed: where the round enters it (<see cref="T"/>, 0..1 along the segment), the length of its
        /// path inside (m), and the ballistic resistance that path presents (mm RHA = the material's per-metre value × the path).
        /// </summary>
        public readonly record struct FireCrossing(float T, float PathMetres, float ResistanceMmRha, string Kind, string? Label,
            string Material, string? Building, int Storey)
        {
            /// <summary>⭐ <c>CE-1032</c> (W-7′) — the top of the piece crossed (a wall's top, a slab's level): how tall an obstacle a
            /// blast wave diffracts over.</summary>
            public float TopZ { get; init; }
        }

        /// <summary>
        /// ⭐⭐ Buildings §3d P2 (R-217) — the FIRE purpose of the one terrain query (§3a "one query, a solver per purpose"): every
        /// solid piece the segment passes within its height, with the length of the path INSIDE it (so a wall crossed obliquely
        /// resists more than its thickness — the same rule as armour), and every slab/ramp it passes through as
        /// <see cref="SlabThicknessMetres"/> of the default material. A piece with no material is the default material
        /// (concrete — a solid building stops any round). Ordered along the line. ⛔ The ground is not an occluder (flat ground;
        /// a shot line never dips below it between two points above it). What a round DOES with this is the combat rule
        /// (<c>TerrainPenetration</c>), not the terrain's.
        /// </summary>
        public IReadOnlyList<FireCrossing> QueryFire(Vector3 from, Vector3 to, DoorStates? doors = null)
        {
            var list = new List<FireCrossing>();
            QueryFire(from, to, list, doors);
            return list;
        }

        /// <inheritdoc cref="QueryFire(Vector3, Vector3, DoorStates?)"/>
        /// <param name="into">Cleared, then filled — a caller tracing every round each tick reuses one list.</param>
        public void QueryFire(Vector3 from, Vector3 to, List<FireCrossing> into, DoorStates? doors = null)
        {
            into.Clear();
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            var segMin = Vector2.Min(a, b);
            var segMax = Vector2.Max(a, b);
            float length = Vector3.Distance(from, to);
            if (length <= 0f) return;
            float dz = to.Z - from.Z;
            Materials.TryGet(TerrainMaterialLibrary.DefaultMaterial, out var fallback);
            float fallbackPerMetre = fallback?.ResistanceMmRhaPerMetre ?? 1500f;
            // ⭐ CE-1034 H2 — a hill in the way stops any round: one crossing where the line enters the ground, its TopZ the crest
            //   (a blast diffracts over the hill like over a wall — AreaEffect.TerrainShadow)
            if (Height != null && Height.Crosses(from, to, out float groundT, out float crest))
                into.Add(new FireCrossing(groundT, (1f - groundT) * length, GroundResistanceMmRha, GroundKind, null, GroundMaterial, null, -1) { TopZ = crest });

            // ⭐ Stage 5 — the prisms, then the leaves of doors that are shut (a closed/locked door is a panel; open = a gap)
            var leaves = Doors.Count > 0 ? DoorLeaves : Array.Empty<TerrainPrism>();
            int maxV = MaxFootprintVertices;
            Span<float> ts = maxV + 2 <= 256 ? stackalloc float[maxV + 2] : new float[maxV + 2];
            Span<(float T0, float T1)> iv = maxV + 1 <= 256 ? stackalloc (float, float)[maxV + 1] : new (float, float)[maxV + 1];
            using var cand = new CandidateSet(this, from, to, Prisms.Count + leaves.Count);   // ⭐ CE-1035 Q0 — every piece, or the index's candidates
            for (int k = 0; k < cand.PieceCount; k++)
            {
                int pi = cand.Piece(k);
                if (pi >= Prisms.Count && !DoorBlocks(pi - Prisms.Count, doors)) continue;
                var prism = pi < Prisms.Count ? Prisms[pi] : leaves[pi - Prisms.Count];
                if (!BoxesOverlap(segMin, segMax, prism.Min, prism.Max)) continue;
                foreach (var (t0, t1) in iv.Slice(0, PolygonMath.InsideIntervals(prism.Footprint, a, b, ts, iv)))   // ⭐ R-220 — no allocation
                {
                    // the part of [t0, t1] where the line is also within the piece's height
                    float lo = t0, hi = t1;
                    if (MathF.Abs(dz) < 1e-6f)
                    {
                        if (!(from.Z > prism.BaseZ && from.Z < prism.TopZ)) continue;
                    }
                    else
                    {
                        float ta = (prism.BaseZ - from.Z) / dz, tb = (prism.TopZ - from.Z) / dz;
                        lo = MathF.Max(lo, MathF.Min(ta, tb));
                        hi = MathF.Min(hi, MathF.Max(ta, tb));
                    }
                    if (hi <= lo) continue;
                    var panel = prism.Panel >= 0 && prism.Panel < Panels.Count ? Panels[prism.Panel] : null;
                    string? building = panel != null && panel.Building >= 0 && panel.Building < Buildings.Count ? Buildings[panel.Building].Label : null;
                    float path = (hi - lo) * length;
                    float perMetre = prism.Material?.ResistanceMmRhaPerMetre ?? fallbackPerMetre;
                    into.Add(new FireCrossing(lo, path, perMetre * path, pi >= Prisms.Count ? "door" : panel != null ? "panel" : "prism", prism.Label,
                        prism.Material?.Name ?? TerrainMaterialLibrary.DefaultMaterial, building, panel?.Storey ?? -1) { TopZ = prism.TopZ });
                }
            }

            for (int kw = 0; kw < cand.WalkableCount; kw++)   // ⭐ R-220 — an index loop: no interface enumerator
            {
                var w = Walkables[cand.Walkable(kw)];
                if (!BoxesOverlap(segMin, segMax, w.Min, w.Max)) continue;
                for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
                {
                    var p0 = w.Vertices[w.Triangles[t]]; var p1 = w.Vertices[w.Triangles[t + 1]]; var p2 = w.Vertices[w.Triangles[t + 2]];
                    if (!PolygonMath.SegmentCrossesTriangle(from, to, p0, p1, p2)) continue;
                    var n = Vector3.Cross(p1 - p0, p2 - p0);
                    float denom = Vector3.Dot(n, to - from);
                    float along = MathF.Abs(denom) < 1e-9f ? 0f : Vector3.Dot(n, p0 - from) / denom;
                    into.Add(new FireCrossing(along, SlabThicknessMetres, fallbackPerMetre * SlabThicknessMetres,
                        w.Kind == TerrainWalkableKind.Ramp ? "ramp" : "slab", null, TerrainMaterialLibrary.DefaultMaterial, null, -1)
                        { TopZ = MathF.Max(p0.Z, MathF.Max(p1.Z, p2.Z)) });
                    break;
                }
            }

            into.Sort(ByT);   // a cached static comparison — no delegate per call
        }

        private static readonly Comparison<FireCrossing> ByT = (x, y) => x.T.CompareTo(y.T);

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
