using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4591</c> (P-4, G-9) — a crater's size class: the navmesh area it gets (both are closed to every vehicle, D-2) and its cover delay (G-35).</summary>
    public enum CraterKind : byte
    {
        Small = 1,
        Large = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7, D-1, V-10) — one crater: a bowl of <see cref="Depth"/> m at
    /// <see cref="Centre"/> rising to a rim <see cref="Rim"/> m above the ground at <see cref="Radius"/>, falling back to the ground at
    /// <see cref="Radius"/> × <see cref="RimOuterFactor"/>. <see cref="BornAt"/> is the sim time it was made (G-35: its rim is cover only after
    /// <c>coverAfterSeconds</c>). The profile uses + − × ÷ √ only (V-10) — the same bits on every node.
    /// </summary>
    public readonly record struct Crater(Vector2 Centre, float Radius, float Depth, float Rim, SimInstant BornAt, CraterKind Kind)
    {
        /// <summary>Where the rim meets the untouched ground, as a multiple of <see cref="Radius"/>.</summary>
        public const float RimOuterFactor = 1.4f;

        /// <summary>The disc the crater changes the ground in.</summary>
        public float OuterRadius => Radius * RimOuterFactor;

        /// <summary>
        /// True when (<paramref name="x"/>, <paramref name="y"/>) is inside the changed disc; <paramref name="offset"/> is then the change to the ground there
        /// (negative in the bowl, positive on the rim). The bowl is a parabola from −<see cref="Depth"/> at the centre to +<see cref="Rim"/> at
        /// <see cref="Radius"/>; the rim falls linearly to 0 at <see cref="OuterRadius"/>.
        /// </summary>
        public bool TryOffset(float x, float y, out float offset)
        {
            float dx = x - Centre.X, dy = y - Centre.Y;
            float r2 = dx * dx + dy * dy;
            float outer = OuterRadius;
            if (r2 >= outer * outer || Radius <= 0f) { offset = 0f; return false; }
            float rr = Radius * Radius;
            if (r2 < rr) { offset = -Depth + (Depth + Rim) * (r2 / rr); return true; }
            float r = MathF.Sqrt(r2);
            offset = Rim * (1f - (r - Radius) / (outer - Radius));
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7, V-7) — the craters ONE deciding node made in ONE ground cell
    /// (<see cref="CellSize"/> m): a runtime entity that node owns, recorded by bytes (unmanaged), replicated to every node (<c>dtGroundDamage</c>), read by
    /// every node through <see cref="CraterTable"/> (the union of every such entity, content-sorted — so two Muscles' craters combine the same whatever order
    /// they arrive in). Past <see cref="Capacity"/> the owner opens another entity for the same cell. The <c>BuildingDamage</c> shape (B-2), proven on breaches.
    /// <para>⛔ <c>NoScenario</c>: never saved as an entity (a save section carries the craters, like the breaches).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GroundDamage)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct GroundDamage : ICellRecord<Crater>
    {
        int ICellRecord<Crater>.CellX { get => CellX; set => CellX = value; }
        int ICellRecord<Crater>.CellY { get => CellY; set => CellY = value; }
        readonly int ICellRecord<Crater>.ItemCount => Count;
        readonly bool ICellRecord<Crater>.IsFull => Count >= Capacity;
        readonly Crater ICellRecord<Crater>.Item(int i) => Get(i);
        bool ICellRecord<Crater>.TryAdd(in Crater item, out bool full) => GroundDamages.Add(ref this, in item, out full);

        /// <summary>How many craters one entity holds.</summary>
        public const int Capacity = 16;

        /// <summary>The side (m) of the ground cell one entity covers — the cell of a crater's centre.</summary>
        public const float CellSize = 64f;

        public int CellX;
        public int CellY;
        /// <summary>Craters in use.</summary>
        public byte Count;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Radius[Capacity];
        public fixed float Depth[Capacity];
        public fixed float Rim[Capacity];
        /// <summary>Each crater's birth, seconds of sim time (a fixed buffer holds no <see cref="SimInstant"/>; <see cref="Get"/> types it).</summary>
        public fixed double BornAt[Capacity];
        public fixed byte Kind[Capacity];

        /// <summary>Crater <paramref name="i"/> (0 ≤ i &lt; <see cref="Count"/>).</summary>
        public readonly Crater Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new Crater(new Vector2(X[i], Y[i]), Radius[i], Depth[i], Rim[i], (SimInstant)BornAt[i], (CraterKind)Kind[i]);
        }

        /// <summary>Overwrites slot <paramref name="i"/> (does not change <see cref="Count"/>).</summary>
        public void Set(int i, in Crater c)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            X[i] = c.Centre.X; Y[i] = c.Centre.Y; Radius[i] = c.Radius; Depth[i] = c.Depth; Rim[i] = c.Rim; BornAt[i] = c.BornAt; Kind[i] = (byte)c.Kind;
        }

        /// <summary>This entity's craters as a fresh array (a test / diagnostics read; ⛔ never per frame).</summary>
        public readonly Crater[] ToArray()
        {
            var a = new Crater[Count];
            for (int i = 0; i < a.Length; i++) a[i] = Get(i);
            return a;
        }
    }

    /// <summary>⭐ <c>CE-4591</c> — the writer-side rules of a <see cref="GroundDamage"/>: its cell, and adding a crater.</summary>
    public static class GroundDamages
    {
        /// <summary>⭐ The TKB type of a ground-damage entity (bare: the craters travel on <see cref="GroundDamage"/> / <c>dtGroundDamage</c>).</summary>
        public const long TkbType = 7204;

        /// <summary>The cell of a point.</summary>
        public static (int X, int Y) CellOf(Vector2 p) => ((int)MathF.Floor(p.X / GroundDamage.CellSize), (int)MathF.Floor(p.Y / GroundDamage.CellSize));

        /// <summary>The centre of cell (<paramref name="x"/>, <paramref name="y"/>) — where its entity stands.</summary>
        public static Vector3 Anchor(int x, int y, float z) => new((x + 0.5f) * GroundDamage.CellSize, (y + 0.5f) * GroundDamage.CellSize, z);

        /// <summary>
        /// Adds <paramref name="c"/> to <paramref name="d"/>: false when it is already there (the same crater twice) or when <paramref name="d"/> is full
        /// (<paramref name="full"/> = true: the caller opens another entity). Craters never merge — overlapping ones combine by the minimum (<see cref="CraterTable"/>).
        /// </summary>
        public static bool Add(ref GroundDamage d, in Crater c, out bool full)
        {
            full = false;
            for (int i = 0; i < d.Count; i++) if (d.Get(i) == c) return false;
            if (d.Count >= GroundDamage.Capacity) { full = true; return false; }
            d.Set(d.Count, c);
            d.Count++;
            return true;
        }
    }

    /// <summary>⭐ <c>CE-4591</c> (G-7) — <c>craters.json</c> (the file's own comment is the reference): what a burst does to the ground.</summary>
    public sealed class CraterRuleTable
    {
        public float MinKg { get; init; } = 0.5f;
        public float RadiusPerCbrtKg { get; init; } = 0.8f;
        public float DepthPerCbrtKg { get; init; } = 0.35f;
        public float RimPerCbrtKg { get; init; } = 0.08f;
        public float MaxBurstHeightPerRadius { get; init; } = 0.5f;
        public float LargeRadiusM { get; init; } = 3f;
        public float CoverAfterSmallSeconds { get; init; } = 30f;
        public float CoverAfterLargeSeconds { get; init; } = 60f;

        private static readonly Lazy<CraterRuleTable> s_shared = new(() =>
        {
            using var s = typeof(CraterRuleTable).Assembly.GetManifestResourceStream("Fdp.Toolkit.Terrain.craters.json")
                ?? throw new InvalidOperationException("craters.json is not embedded");
            using var r = new System.IO.StreamReader(s);
            return Parse(r.ReadToEnd());
        });

        /// <summary>The shipped table.</summary>
        public static CraterRuleTable Shared => s_shared.Value;

        public static CraterRuleTable Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cover = root.GetProperty("coverAfterSeconds");
            return new CraterRuleTable
            {
                MinKg = root.GetProperty("minKg").GetSingle(),
                RadiusPerCbrtKg = root.GetProperty("radiusPerCbrtKg").GetSingle(),
                DepthPerCbrtKg = root.GetProperty("depthPerCbrtKg").GetSingle(),
                RimPerCbrtKg = root.GetProperty("rimPerCbrtKg").GetSingle(),
                MaxBurstHeightPerRadius = root.GetProperty("maxBurstHeightPerRadius").GetSingle(),
                LargeRadiusM = root.GetProperty("largeRadiusM").GetSingle(),
                CoverAfterSmallSeconds = cover.GetProperty("small").GetSingle(),
                CoverAfterLargeSeconds = cover.GetProperty("large").GetSingle(),
            };
        }

        /// <summary>G-35 — how long after it is made a crater of <paramref name="kind"/> is cover (its gases cleared).</summary>
        public float CoverAfterSeconds(CraterKind kind) => kind == CraterKind.Large ? CoverAfterLargeSeconds : CoverAfterSmallSeconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4, G-7) — does a burst of <c>kg</c> at a point make a crater, and how big. ⛔ None below <see cref="CraterRuleTable.MinKg"/>,
    /// none for an air burst (higher above the ground than <see cref="CraterRuleTable.MaxBurstHeightPerRadius"/> × its radius), none on water, none on a
    /// building's floor (the burst is inside a footprint). Called on the DECIDING node only; its answer is written into the record (V-10).
    /// </summary>
    public static class CraterRule
    {
        public static bool TryDecide(CraterRuleTable table, TerrainWorld world, Vector3 burst, float kg, SimInstant now, out Crater crater)
        {
            crater = default;
            if (kg < table.MinKg || kg <= 0f) return false;
            float cbrt = MathF.Cbrt(kg);
            float radius = table.RadiusPerCbrtKg * cbrt;
            if (radius <= 0f) return false;
            float ground = world.GroundHeightAt(burst.X, burst.Y);
            if (burst.Z - ground > table.MaxBurstHeightPerRadius * radius) return false;   // an air burst: the ground is untouched
            if (world.InsideFootprint(burst.X, burst.Y) || OnFloor(world, burst.X, burst.Y)) return false;   // a solid piece, or a building's floor
            if (OnWater(world, burst.X, burst.Y)) return false;
            var kind = radius >= table.LargeRadiusM ? CraterKind.Large : CraterKind.Small;
            crater = new Crater(new Vector2(burst.X, burst.Y), radius, table.DepthPerCbrtKg * cbrt, table.RimPerCbrtKg * cbrt, now, kind);
            return true;
        }

        private static bool OnFloor(TerrainWorld world, float x, float y)
        {
            if (world.Buildings.Count == 0) return false;
            var near = t_near ??= new List<int>(8);
            near.Clear();
            var p = new Vector2(x, y);
            world.BuildingFootprints.Near(p, 0.5f, near);
            for (int i = 0; i < near.Count; i++)
                if ((uint)near[i] < (uint)world.Buildings.Count && PolygonMath.Contains(world.Buildings[near[i]].Footprint, p)) return true;
            return false;
        }
        [ThreadStatic] private static List<int>? t_near;

        private static bool OnWater(TerrainWorld world, float x, float y)
        {
            var p = new Vector2(x, y);
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                var s = world.Surfaces[i];
                if (s.Type != TerrainSurfaceType.Water) continue;
                if (x < s.Min.X || x > s.Max.X || y < s.Min.Y || y > s.Max.Y) continue;
                if (PolygonMath.Contains(s.Polygon, p)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md D-1, R-4232) — the craters AS ONE VIEW SEES THEM: the union of every
    /// <see cref="GroundDamage"/> entity of that view, content-sorted, immutable; the SIMULATION'S TRUTH about the cratered ground — its height, and whether
    /// a sight / fire line dips under it — answered analytically from the records in the SAME tick on every node (a standalone brain and a display node
    /// included); the bake only caches it for the navmesh and the 3-D mesh. Overlapping craters combine by the MINIMUM of their offsets (commutative).
    /// <para>⭐ Rides on <see cref="DoorStates.Craters"/> — the view-table seam every sight / fire / path caller already passes (R-219: per entity components are
    /// what a background snapshot copies; ⛔ a world singleton would be SHARED by every snapshot — the design's "singleton" is this table, measured and
    /// recorded as lane ruling R-5020). Built when the records change, handed back unchanged otherwise (R-220: 0 B a steady frame).</para>
    /// </summary>
    public sealed class CraterTable
    {
        /// <summary>The side (m) of the lookup cells of <see cref="Offset"/>.</summary>
        public const float LookupCell = 16f;

        private readonly Crater[] _craters;                    // content-sorted
        private readonly Dictionary<long, int[]> _cells;       // lookup cell → the craters whose changed disc reaches it
        private readonly Vector2 _min, _max;                   // the box of every changed disc

        private CraterTable(Crater[] sorted)
        {
            _craters = sorted;
            var lists = new Dictionary<long, List<int>>();
            _min = new Vector2(float.MaxValue); _max = new Vector2(float.MinValue);
            for (int i = 0; i < sorted.Length; i++)
            {
                var c = sorted[i];
                float o = c.OuterRadius;
                var lo = c.Centre - new Vector2(o); var hi = c.Centre + new Vector2(o);
                _min = Vector2.Min(_min, lo); _max = Vector2.Max(_max, hi);
                int x0 = (int)MathF.Floor(lo.X / LookupCell), x1 = (int)MathF.Floor(hi.X / LookupCell);
                int y0 = (int)MathF.Floor(lo.Y / LookupCell), y1 = (int)MathF.Floor(hi.Y / LookupCell);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long k = Key(x, y);
                        if (!lists.TryGetValue(k, out var l)) lists[k] = l = new List<int>(2);
                        l.Add(i);
                    }
            }
            _cells = new Dictionary<long, int[]>(lists.Count);
            foreach (var (k, l) in lists) _cells[k] = l.ToArray();
            ulong h = 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            foreach (var c in sorted)
            {
                Mix(BitConverter.SingleToInt32Bits(c.Centre.X)); Mix(BitConverter.SingleToInt32Bits(c.Centre.Y)); Mix(BitConverter.SingleToInt32Bits(c.Radius));
                Mix(BitConverter.SingleToInt32Bits(c.Depth)); Mix(BitConverter.SingleToInt32Bits(c.Rim));
            }
            Hash = h == 0 ? 1 : h;
        }

        /// <summary>⭐ <c>CE-4591</c> (part 2) — a hash of the craters' SHAPES (never 0): the bake's input signature (a crater's birth time moves nothing it bakes).</summary>
        public ulong Hash { get; }

        /// <summary>
        /// ⭐⭐ <c>CE-4591</c> (part 2) — <paramref name="bare"/> with these craters laid into its posts: a NEW grid that copies only the tiles a crater's disc reaches
        /// (<see cref="TerrainHeightGrid.Patched"/>, P-0). Every post is the BARE post + <see cref="Offset"/> — so overlapping discs patched one after another give the same
        /// posts in any order. The bake's cache for the navmesh and the 3-D mesh (D-1).
        /// </summary>
        public TerrainHeightGrid Bake(TerrainHeightGrid bare)
        {
            var g = bare;
            var origin = bare.Origin; float cell = bare.CellSize;
            for (int i = 0; i < _craters.Length; i++)
            {
                var c = _craters[i]; float o = c.OuterRadius;
                int c0 = (int)MathF.Floor((c.Centre.X - o - origin.X) / cell), c1 = (int)MathF.Ceiling((c.Centre.X + o - origin.X) / cell);
                int r0 = (int)MathF.Floor((c.Centre.Y - o - origin.Y) / cell), r1 = (int)MathF.Ceiling((c.Centre.Y + o - origin.Y) / cell);
                if (c1 < 0 || r1 < 0 || c0 > bare.Cols - 1 || r0 > bare.Rows - 1) continue;
                g = g.Patched(c0, r0, c1, r1, (col, row, _) => bare[col, row] + Offset(origin.X + col * cell, origin.Y + row * cell));
            }
            return g;
        }

        /// <summary>The ground-shadow box (min, max) of crater <paramref name="i"/>'s changed disc — a dirty rectangle.</summary>
        public (Vector2 Min, Vector2 Max) Bounds(int i)
        {
            var c = _craters[i]; float o = c.OuterRadius;
            return (c.Centre - new Vector2(o), c.Centre + new Vector2(o));
        }

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        public int Count => _craters.Length;
        public Crater this[int i] => _craters[i];

        /// <summary>
        /// The change to the ground at (<paramref name="x"/>, <paramref name="y"/>): the minimum of the offsets of every crater whose disc holds the point,
        /// 0 where none does. One dictionary lookup and a few discs; ⛔ no allocation.
        /// </summary>
        public float Offset(float x, float y)
        {
            if (x < _min.X || x > _max.X || y < _min.Y || y > _max.Y) return 0f;
            if (!_cells.TryGetValue(Key((int)MathF.Floor(x / LookupCell), (int)MathF.Floor(y / LookupCell)), out var list)) return 0f;
            bool any = false; float best = 0f;
            for (int k = 0; k < list.Length; k++)
                if (_craters[list[k]].TryOffset(x, y, out float o) && (!any || o < best)) { best = o; any = true; }
            return best;
        }

        /// <summary>The ground of <paramref name="world"/> at (<paramref name="x"/>, <paramref name="y"/>) with these craters (over its BARE ground: a baked crater is not counted twice).</summary>
        public float GroundHeightAt(TerrainWorld world, float x, float y) => world.BareGroundHeightAt(x, y) + Offset(x, y);

        /// <summary>True when the segment's ground shadow meets no crater's changed disc (the common case — the plain ground answers).</summary>
        public bool Misses(Vector3 a, Vector3 b)
        {
            float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X), minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);
            if (maxX < _min.X || minX > _max.X || maxY < _min.Y || minY > _max.Y) return true;
            for (int i = 0; i < _craters.Length; i++) if (Interval(in _craters[i], a, b, out _, out _)) return false;
            return true;
        }

        /// <summary>The step (m) along a segment inside a crater's disc at which the cratered ground is sampled.</summary>
        public const float SampleStep = 0.25f;

        /// <summary>
        /// ⭐ D-1 — <see cref="TerrainHeightGrid.Crosses(Vector3, Vector3, out float, out float)"/> over the CRATERED ground: does the segment dip under it,
        /// where first (<paramref name="t"/>), and the highest ground over its crossing (<paramref name="crest"/>; only when <paramref name="wholeCrest"/>).
        /// Outside every crater's disc the grid answers exactly as it always did; inside one the cratered ground is sampled every <see cref="SampleStep"/> m.
        /// An end under the CRATERED ground is malformed (ignored), as the grid's own rule — so a man lying in a crater is not under the ground.
        /// ⛔ No allocation (per-thread scratch).
        /// </summary>
        public bool GroundCrosses(TerrainWorld world, Vector3 a, Vector3 b, bool wholeCrest, out float t, out float crest)
        {
            t = 1f; crest = float.NegativeInfinity;
            var grid = world.SimHeight;   // the bare grid: the baked craters are the cache, these are the truth
            if (a.Z < GroundHeightAt(world, a.X, a.Y) - TerrainHeightGrid.EndTolerance || b.Z < GroundHeightAt(world, b.X, b.Y) - TerrainHeightGrid.EndTolerance) return false;
            var d = b - a;
            float length = d.Length();
            if (length < 1e-4f) return false;

            // the intervals of the segment inside a crater's disc, sorted and merged
            var lo = t_lo ??= new float[16]; var hi = t_hi ??= new float[16];
            int n = 0;
            for (int i = 0; i < _craters.Length; i++)
            {
                if (!Interval(in _craters[i], a, b, out float s, out float e)) continue;
                if (n == lo.Length) { Array.Resize(ref t_lo, n * 2); Array.Resize(ref t_hi, n * 2); lo = t_lo!; hi = t_hi!; }
                int at = n++;
                while (at > 0 && lo[at - 1] > s) { lo[at] = lo[at - 1]; hi[at] = hi[at - 1]; at--; }
                lo[at] = s; hi[at] = e;
            }
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                if (m > 0 && lo[i] <= hi[m - 1]) { hi[m - 1] = MathF.Max(hi[m - 1], hi[i]); continue; }
                lo[m] = lo[i]; hi[m] = hi[i]; m++;
            }

            bool hit = false;
            float cur = 0f;
            for (int i = 0; i <= m; i++)
            {
                float s = i < m ? lo[i] : 1f, e = i < m ? hi[i] : 1f;
                // ① the plain ground from cur to s — the grid's own walk; its ends are where the craters' offset is 0
                if (s > cur)
                {
                    var p = a + d * cur; var q = a + d * s;
                    if (s < 1f && q.Z < world.BareGroundHeightAt(q.X, q.Y) - 0.01f && !hit)   // the line is under the plain ground where it enters a crater's disc
                    {
                        hit = true; t = s; crest = MathF.Max(crest, world.BareGroundHeightAt(q.X, q.Y));
                        if (!wholeCrest) return true;
                    }
                    if (grid != null)
                    {
                        float tt = 1f, cc = float.NegativeInfinity;
                        bool crossed = wholeCrest ? grid.Crosses(p, q, out tt, out cc) : grid.Crosses(p, q);
                        if (crossed)
                        {
                            if (!hit) { hit = true; t = cur + (s - cur) * tt; if (!wholeCrest) return true; }
                            crest = MathF.Max(crest, cc);
                        }
                    }
                }
                if (i == m) break;
                // ② inside the craters' discs: sample the cratered ground
                int steps = Math.Max(1, (int)MathF.Ceiling((e - s) * length / SampleStep));
                for (int k = 0; k <= steps; k++)
                {
                    float tk = s + (e - s) * k / steps;
                    if (tk * length < TerrainHeightGrid.EndTolerance || (1f - tk) * length < TerrainHeightGrid.EndTolerance) continue;   // an end is checked above, with the grid's tolerance
                    float x = a.X + d.X * tk, y = a.Y + d.Y * tk, z = a.Z + d.Z * tk;
                    float g = GroundHeightAt(world, x, y);
                    if (z < g - 0.01f)
                    {
                        if (!hit) { hit = true; t = tk; if (!wholeCrest) return true; }
                        crest = MathF.Max(crest, g);
                    }
                }
                cur = e;
            }
            return hit;
        }

        /// <summary>Where the segment's ground shadow is inside <paramref name="c"/>'s changed disc: [<paramref name="s"/>, <paramref name="e"/>] ⊂ [0, 1].</summary>
        private static bool Interval(in Crater c, Vector3 a, Vector3 b, out float s, out float e)
        {
            s = e = 0f;
            float ax = a.X - c.Centre.X, ay = a.Y - c.Centre.Y, dx = b.X - a.X, dy = b.Y - a.Y;
            float o = c.OuterRadius, r2 = o * o;
            float dd = dx * dx + dy * dy;
            if (dd < 1e-9f) { if (ax * ax + ay * ay >= r2) return false; s = 0f; e = 1f; return true; }
            float bb = ax * dx + ay * dy, cc = ax * ax + ay * ay - r2;
            float disc = bb * bb - dd * cc;
            if (disc <= 0f) return false;
            float sq = MathF.Sqrt(disc);
            s = MathF.Max((-bb - sq) / dd, 0f);
            e = MathF.Min((-bb + sq) / dd, 1f);
            return s < e;
        }

        [ThreadStatic] private static float[]? t_lo;
        [ThreadStatic] private static float[]? t_hi;

        // ── the view's table ───────────────────────────────────────────────────────────────────────────

        private static readonly Comparison<Crater> Order = static (p, q) =>
        {
            int c = p.Centre.X.CompareTo(q.Centre.X); if (c != 0) return c;
            c = p.Centre.Y.CompareTo(q.Centre.Y); if (c != 0) return c;
            c = p.Radius.CompareTo(q.Radius); if (c != 0) return c;
            c = p.Depth.CompareTo(q.Depth); if (c != 0) return c;
            c = p.Rim.CompareTo(q.Rim); if (c != 0) return c;
            c = p.BornAt.CompareTo(q.BornAt); if (c != 0) return c;
            return p.Kind.CompareTo(q.Kind);
        };

        /// <summary>
        /// The craters <paramref name="view"/> holds, or null when it holds none. ⭐ R-220 — this thread's previous table is handed back while no
        /// <see cref="GroundDamage"/> was written or added since and every entity it read is alive; a rebuild with the same content keeps the instance.
        /// </summary>
        public static CraterTable? Of(ISimulationView view)
        {
            if (view is not EntityRepository repo)   // a non-repository view (a test double): built each time, never cached
            {
                var q = view.Query().With<GroundDamage>().WithLifecycle(EntityLifecycle.All).Build();
                var scratch = new List<Crater>();
                foreach (var e in q) Collect(view.GetComponentRO<GroundDamage>(e), scratch);
                if (scratch.Count == 0) return null;
                scratch.Sort(Order);
                return new CraterTable(Dedupe(scratch).ToArray());
            }
            if (!repo.IsComponentTypeRegistered<GroundDamage>()) return null;

            if (ReferenceEquals(repo, t_repo) && t_stamp.HoldsSinceTaken(repo, typeof(GroundDamage))
                && repo.ComponentsAddedOf(ComponentType<GroundDamage>.ID) == t_added)
            {
                if (repo.StructuralChanges == t_structural) return t_last;
                if (StillAlive(repo)) { t_structural = repo.StructuralChanges; return t_last; }
            }

            var list = t_scratch ??= new List<Crater>(32);
            list.Clear();
            t_entityCount = 0;
            var query = ReferenceEquals(repo, t_queryRepo) ? t_query!
                : (t_query = repo.Query().With<GroundDamage>().WithLifecycle(EntityLifecycle.All).Build());
            t_queryRepo = repo;
            foreach (var e in query)
            {
                Collect(repo.GetComponentRO<GroundDamage>(e), list);
                var ents = t_entities ??= new Entity[8];
                if (t_entityCount == ents.Length) Array.Resize(ref t_entities, ents.Length * 2);
                t_entities![t_entityCount++] = e;
            }
            t_stamp.Take(repo); t_repo = repo; t_structural = repo.StructuralChanges;
            t_added = repo.ComponentsAddedOf(ComponentType<GroundDamage>.ID);

            list.Sort(Order);
            Dedupe(list);   // two nodes may both hold one crater (a re-send): one is enough
            if (t_last is { } last && last.SameContent(list)) return last;
            if (list.Count == 0) return t_last = null;
            t_builds++;
            return t_last = new CraterTable(list.ToArray());
        }

        private bool SameContent(List<Crater> sorted)
        {
            if (sorted.Count != _craters.Length) return false;
            for (int i = 0; i < _craters.Length; i++) if (_craters[i] != sorted[i]) return false;
            return true;
        }

        private static List<Crater> Dedupe(List<Crater> sorted)
        {
            for (int i = sorted.Count - 1; i > 0; i--) if (sorted[i] == sorted[i - 1]) sorted.RemoveAt(i);
            return sorted;
        }

        private static void Collect(in GroundDamage d, List<Crater> into)
        {
            int n = Math.Min((int)d.Count, GroundDamage.Capacity);
            for (int i = 0; i < n; i++) { var c = d.Get(i); if (c.Radius > 0f) into.Add(c); }
        }

        private static bool StillAlive(EntityRepository repo)
        {
            for (int k = 0; k < t_entityCount; k++)
                if (!repo.IsAlive(t_entities![k]) || !repo.HasComponent<GroundDamage>(t_entities[k])) return false;
            return true;
        }

        [ThreadStatic] private static Fdp.Toolkit.Replication.Utilities.FrameStamp t_stamp;
        [ThreadStatic] private static EntityRepository? t_repo;
        [ThreadStatic] private static CraterTable? t_last;
        [ThreadStatic] private static List<Crater>? t_scratch;
        [ThreadStatic] private static Entity[]? t_entities;
        [ThreadStatic] private static int t_entityCount;
        [ThreadStatic] private static long t_structural;
        [ThreadStatic] private static long t_added;
        [ThreadStatic] private static int t_builds;
        [ThreadStatic] private static EntityRepository? t_queryRepo;
        [ThreadStatic] private static EntityQuery? t_query;

        /// <summary>How many times THIS thread rebuilt the table and found it changed (rails).</summary>
        internal static int TableBuildsOnThisThread => t_builds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4 part 3; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4c-1 G-35) — a crater's RIM as cover: points round the inside of its bowl (at
    /// <see cref="RingFactor"/> × its radius, every <see cref="SpacingMetres"/> m) facing OUT, at the cratered ground, their stance by the rim's height above them (the wall
    /// rule: ≥ 1.5 m stand · ≥ 0.9 crouch · ≥ 0.45 prone; less is no cover). ⭐ A crater is cover only from <c>BornAt + coverAfterSeconds</c> (its gases cleared) — a man
    /// already in it is still hidden by the ground (TH-D). Read live from the view's craters by the EQS cover generator (D-1).
    /// </summary>
    public static class CraterCover
    {
        public const float RingFactor = 0.85f;
        public const float SpacingMetres = 2.5f;
        public const float QualityOf = 0.8f;

        /// <summary>Appends to <paramref name="into"/> the rim points of every crater of <paramref name="craters"/> within <paramref name="radius"/> of <paramref name="center"/> that is cover by <paramref name="now"/>.</summary>
        public static void Points(CraterTable craters, TerrainWorld world, CraterRuleTable rule, SimInstant now, Vector2 center, float radius, List<CoverPoint> into)
        {
            for (int i = 0; i < craters.Count; i++)
            {
                var c = craters[i];
                if (Vector2.Distance(c.Centre, center) > radius + c.Radius) continue;
                if (now.Seconds < c.BornAt.Seconds + rule.CoverAfterSeconds(c.Kind)) continue;   // the gases have not cleared
                float r = c.Radius * RingFactor;
                c.TryOffset(c.Centre.X + r, c.Centre.Y, out float at);
                float wall = c.Rim - at;                                                           // the rim's top above the point
                byte stance = wall >= 1.5f ? (byte)2 : wall >= 0.9f ? (byte)1 : wall >= 0.45f ? (byte)0 : byte.MaxValue;
                if (stance == byte.MaxValue) continue;
                int n = Math.Max(4, (int)MathF.Ceiling(2f * MathF.PI * r / SpacingMetres));
                for (int k = 0; k < n; k++)
                {
                    float a = 2f * MathF.PI * k / n;
                    var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    var p = c.Centre + dir * r;
                    if (Vector2.Distance(p, center) > radius) continue;
                    into.Add(new CoverPoint
                    {
                        PositionX = p.X, PositionY = p.Y, PositionZ = craters.GroundHeightAt(world, p.X, p.Y),
                        DirectionX = dir.X, DirectionY = dir.Y, Quality = QualityOf, StanceHeight = stance, Kind = CoverKind.Cover,
                    });
                }
            }
        }
    }
}
