using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-2468 UF-4 (DESIGN_Minefields.md §9b, §9b.1 K3, K5) — the ground a known enemy at one point SEES within its range: a <see cref="CellM"/> grid over the circle, a
    /// bit per cell, derived from (point, range) through the AUTHORED terrain — <c>SegmentBlocked(eye, target, doors: null)</c>, eye <see cref="EyeM"/> over the ground at
    /// the point, target <see cref="TargetM"/> over the ground at the cell centre — so it is the SAME on every node and every load (K3). ⚠ A building destroyed after the
    /// record is not reflected (stated limitation; the record ages out within 600 s). Never stored in the record; built cold (≈ 4–5 ms for 150 m), immutable.
    /// </summary>
    public sealed class UnderFireSightGrid
    {
        public const float CellM = 5f, EyeM = 1.6f, TargetM = 1.0f;

        public Vector2 Origin { get; }
        public int Cells { get; }
        public float RangeM { get; }
        public Vector2 Point { get; }
        private readonly ulong[] _bits;

        private UnderFireSightGrid(Vector2 point, float range, Vector2 origin, int cells, ulong[] bits) { Point = point; RangeM = range; Origin = origin; Cells = cells; _bits = bits; }

        /// <summary>How many cells are seen (rails).</summary>
        public int SeenCount { get { int n = 0; foreach (var b in _bits) n += System.Numerics.BitOperations.PopCount(b); return n; } }

        /// <summary>Is <paramref name="p"/> inside the range AND in a seen cell? 0 B.</summary>
        public bool Covers(Vector2 p)
        {
            if (Vector2.DistanceSquared(p, Point) > RangeM * RangeM) return false;
            int cx = (int)MathF.Floor((p.X - Origin.X) / CellM), cy = (int)MathF.Floor((p.Y - Origin.Y) / CellM);
            if ((uint)cx >= (uint)Cells || (uint)cy >= (uint)Cells) return false;
            int i = cy * Cells + cx;
            return (_bits[i >> 6] & (1UL << (i & 63))) != 0;
        }

        /// <summary>⭐ K3 — the grid of a known enemy at <paramref name="point"/> covering <paramref name="rangeM"/>, on the authored terrain of <paramref name="world"/>.</summary>
        public static UnderFireSightGrid Build(TerrainWorld world, Vector2 point, float rangeM)
        {
            float range = MathF.Max(rangeM, CellM);
            int cells = (int)MathF.Ceiling(2f * range / CellM);
            var origin = point - new Vector2(cells * CellM * 0.5f);
            var bits = new ulong[(cells * cells + 63) >> 6];
            var eye = new Vector3(point, world.GroundHeightAt(point.X, point.Y) + EyeM);
            for (int cy = 0; cy < cells; cy++)
                for (int cx = 0; cx < cells; cx++)
                {
                    var c = origin + new Vector2((cx + 0.5f) * CellM, (cy + 0.5f) * CellM);
                    if (Vector2.DistanceSquared(c, point) > range * range) continue;
                    var target = new Vector3(c, world.GroundHeightAt(c.X, c.Y) + TargetM);
                    if (world.SegmentBlocked(eye, target, doors: null)) continue;   // ⭐ K3 — doors: null — the authored terrain, the same on every node
                    int i = cy * cells + cx;
                    bits[i >> 6] |= 1UL << (i & 63);
                }
            return new UnderFireSightGrid(point, range, origin, cells, bits);
        }
    }

    /// <summary>⭐ CE-2468 UF-5 (K4) — a mover's route cost under one view's places under fire, built per (mesh, side) in the SERIAL pre-pass (never on a path-query thread).
    /// The navigation layer implements it for each baked mesh and registers it on the <see cref="TerrainWorld"/> (<see cref="UnderFireSight.RegisterCostBuilder"/>).</summary>
    public interface IUnderFireCostBuilder
    {
        /// <summary>The mesh this builder prices (the filter of that mesh looks its map up by this token).</summary>
        object MeshToken { get; }
        /// <summary>Polygon → cost factor (&gt; 1) for side <paramref name="sidePlus1"/> − 1 under <paramref name="sight"/>; only polygons that are priced.</summary>
        IReadOnlyDictionary<long, float> Build(UnderFireSight sight, byte sidePlus1);
    }

    /// <summary>
    /// ⭐⭐ CE-2468 UF-4 / UF-5 (K3, K4, K5, K8, K12) — ONE view's places under fire as GROUND: every ACTIVE <see cref="UnderFireCause.Direct"/> entry of
    /// <see cref="UnderFireTable"/> with its <see cref="UnderFireSightGrid"/>, and the per-(mesh, side) route-cost maps built from them. Built by
    /// <see cref="UnderFireSightSystem"/> in the serial pre-pass when the table changes, published IMMUTABLE per world; readers (the danger solve, the route cost, the road
    /// router) call <see cref="Of"/>. ⛔ <see cref="UnderFireCause.Indirect"/> is NEVER priced (K8: shelling stays cross-or-hold) — it has no grid here.
    /// </summary>
    public sealed class UnderFireSight
    {
        public static readonly UnderFireSight Empty = new(UnderFireTable.Empty, Array.Empty<(byte, UnderFireSightGrid)>(), Array.Empty<Guid>());

        /// <summary>⭐ K4 — what a covered share costs: <c>1 + <see cref="CoveredExtraCost"/> × share</c> (a fully covered polygon ×8, §9b <c>UnderFireCost</c>).</summary>
        public const float CoveredExtraCost = 7f;

        public UnderFireTable Table { get; }
        private readonly (byte SidePlus1, UnderFireSightGrid Grid)[] _grids;
        private readonly Dictionary<(object Mesh, byte Side), IReadOnlyDictionary<long, float>> _costs = new();

        private readonly Guid[] _subjects;

        private UnderFireSight(UnderFireTable table, (byte, UnderFireSightGrid)[] grids, Guid[] subjects) { Table = table; _grids = grids; _subjects = subjects; }

        /// <summary>How many Direct grids the view holds (rails).</summary>
        public int GridCount => _grids.Length;

        /// <summary>The Direct grid <paramref name="index"/> and its side + 1.</summary>
        public (byte SidePlus1, UnderFireSightGrid Grid) Grid(int index) => _grids[index];

        /// <summary>⭐ UF-6 (K10) — the subject of grid <paramref name="index"/>'s record: the stable id of its <c>CoveredByFire</c> area.</summary>
        public Guid SubjectOf(int index) => _subjects[index];

        /// <summary>Is <paramref name="p"/> ground a known enemy of side <paramref name="sidePlus1"/> − 1 covers? 0 B.</summary>
        public bool Covered(byte sidePlus1, Vector2 p)
        {
            for (int i = 0; i < _grids.Length; i++) if (_grids[i].SidePlus1 == sidePlus1 && _grids[i].Grid.Covers(p)) return true;
            return false;
        }

        /// <summary>⭐ K4 — the share of <paramref name="samples"/> that is covered for the side, in [0, 1].</summary>
        public float CoveredShare(byte sidePlus1, ReadOnlySpan<Vector2> samples)
        {
            if (samples.Length == 0) return 0f;
            int n = 0;
            for (int i = 0; i < samples.Length; i++) if (Covered(sidePlus1, samples[i])) n++;
            return n / (float)samples.Length;
        }

        /// <summary>⭐ K4 / K12 — the factor of a stretch whose covered share is <paramref name="share"/>.</summary>
        public static float FactorOf(float share) => 1f + CoveredExtraCost * share;

        /// <summary>The route-cost map of <paramref name="mesh"/> for side <paramref name="sidePlus1"/> − 1, or null (nothing priced). 0 B.</summary>
        public IReadOnlyDictionary<long, float>? CostOf(object mesh, byte sidePlus1)
            => _grids.Length != 0 && _costs.TryGetValue((mesh, sidePlus1), out var m) ? m : null;

        /// <summary>Does side <paramref name="sidePlus1"/> − 1 hold any Direct grid (a builder skips the sides that do not)?</summary>
        public bool HasSide(byte sidePlus1)
        {
            for (int i = 0; i < _grids.Length; i++) if (_grids[i].SidePlus1 == sidePlus1) return true;
            return false;
        }

        // ── the published snapshot, per world ──────────────────────────────────────────────────────────────────
        private sealed class Box { public UnderFireSight Value = Empty; }
        private static readonly ConditionalWeakTable<EntityRepository, Box> Published = new();
        private static readonly ConditionalWeakTable<TerrainWorld, List<(object Slot, IUnderFireCostBuilder Builder)>> Builders = new();

        /// <summary>The snapshot published for <paramref name="view"/>'s world (Empty when none). 0 B.</summary>
        public static UnderFireSight Of(ISimulationView view)
            => view is EntityRepository repo && Published.TryGetValue(repo, out var box) ? System.Threading.Volatile.Read(ref box.Value) : Empty;

        /// <summary>⭐ UF-5 — a baked mesh's cost builder, in <paramref name="slot"/> (the navigation layer: one per layer); a re-bake REPLACES the builder of the same slot.</summary>
        public static void RegisterCostBuilder(TerrainWorld world, object slot, IUnderFireCostBuilder builder)
        {
            var list = Builders.GetValue(world, _ => new List<(object, IUnderFireCostBuilder)>());
            lock (list)
            {
                for (int i = list.Count - 1; i >= 0; i--) if (Equals(list[i].Slot, slot)) list.RemoveAt(i);
                list.Add((slot, builder));
                System.Threading.Interlocked.Increment(ref _registrations);
            }
        }

        internal static int BuilderCount(TerrainWorld world) { if (!Builders.TryGetValue(world, out var list)) return 0; lock (list) return list.Count + _registrations; }
        private static int _registrations;   // bumped by every registration: a re-bake that REPLACES a builder (same count) still rebuilds the maps

        /// <summary>Forget every cost builder of <paramref name="world"/> (a re-bake of every layer starts over).</summary>
        public static void ClearCostBuilders(TerrainWorld world) { if (Builders.TryGetValue(world, out var list)) lock (list) list.Clear(); }

        internal static UnderFireSight Make(UnderFireTable table, TerrainWorld? terrain, Dictionary<(Guid, byte, float), UnderFireSightGrid> cache)
        {
            var grids = new List<(byte, UnderFireSightGrid)>();
            var subjects = new List<Guid>();
            if (terrain != null)
                for (int i = 0; i < table.Count; i++)
                {
                    ref readonly var e = ref table[i];
                    if (e.Record.Cause != UnderFireCause.Direct) continue;   // ⭐ K8 — Indirect is not ground a route pays for
                    var key = (e.Subject, e.SidePlus1, e.Record.RangeM);
                    if (!cache.TryGetValue(key, out var g))
                        cache[key] = g = UnderFireSightGrid.Build(terrain, new Vector2(e.Record.Point.X, e.Record.Point.Y), e.Record.RangeM);
                    grids.Add((e.SidePlus1, g)); subjects.Add(e.Subject);
                }
            if (grids.Count == 0 && table.Count == 0) return Empty;
            var sight = new UnderFireSight(table, grids.ToArray(), subjects.ToArray());
            if (terrain != null && sight._grids.Length != 0 && Builders.TryGetValue(terrain, out var builders))
            {
                (object Slot, IUnderFireCostBuilder Builder)[] list;
                lock (builders) list = builders.ToArray();
                foreach (var (_, b) in list)
                    for (int s = 0; s < sight._grids.Length; s++)
                    {
                        byte side = sight._grids[s].SidePlus1;
                        if (sight._costs.ContainsKey((b.MeshToken, side))) continue;
                        sight._costs[(b.MeshToken, side)] = b.Build(sight, side);
                    }
            }
            return sight;
        }

        internal static void Publish(EntityRepository repo, UnderFireSight sight)
            => System.Threading.Volatile.Write(ref Published.GetValue(repo, _ => new Box()).Value, sight);
    }

    /// <summary>
    /// ⭐ CE-2468 UF-4 (K5) — builds and publishes <see cref="UnderFireSight"/> in the SERIAL pre-pass of every node that routes or senses (the Muscle; the editor), when the
    /// under-fire table changed (or the world was replaced — <c>WorldEpoch.Moved</c> clears the grid cache), otherwise nothing: 0 B a steady frame (R-2084). The grids are
    /// cached by (subject, side, range) across snapshots, so a record's new evidence that leaves its range builds nothing.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class UnderFireSightSystem : IEcsModuleSystem
    {
        [Fdp.Core.WorldDerived("the grids built, by (subject, side, range): derived from the authored terrain; cleared on a world move")]
        private readonly Dictionary<(Guid, byte, float), UnderFireSightGrid> _cache = new();
        [Fdp.Core.WorldDerived("the table the last snapshot was built from")]
        private UnderFireTable? _built;
        [Fdp.Core.WorldDerived("the terrain the last snapshot was built over")]
        private TerrainWorld? _terrain;
        [Fdp.Core.WorldDerived("the cost builders' count the last snapshot saw (a bake adds one)")]
        private int _builders = -1;
        private int _worldEpoch;

        /// <summary>How many snapshots were built (a rail reads it).</summary>
        public int Builds { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<UnderFireKnowledge>()) return;
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) { _cache.Clear(); _built = null; }
            var table = UnderFireTable.Of(view);
            var terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            int builders = terrain != null ? UnderFireSight.BuilderCount(terrain) : 0;
            if (ReferenceEquals(table, _built) && ReferenceEquals(terrain, _terrain) && builders == _builders) return;
            if (!ReferenceEquals(terrain, _terrain)) _cache.Clear();
            _built = table; _terrain = terrain; _builders = builders;
            UnderFireSight.Publish(repo, UnderFireSight.Make(table, terrain, _cache));
            Builds++;
        }
    }
}
