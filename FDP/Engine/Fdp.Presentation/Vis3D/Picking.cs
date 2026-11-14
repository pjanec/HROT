using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>What a 3-D pick hit.</summary>
    public enum PickKind : byte
    {
        /// <summary>Nothing — the 2-D answer (or the ray missed everything and ended at the horizon).</summary>
        None = 0,
        /// <summary>The fallback plane at the camera's look-at height.</summary>
        Ground = 1,
        /// <summary>The drawn terrain mesh (ground, wall or roof).</summary>
        Terrain = 2,
        /// <summary>An entity's box — the point is the entity's OWN position, so its 2-D pick box contains it.</summary>
        Entity = 3,
    }

    /// <summary>
    /// ⭐ CE-1033 S2 — the result of a pick (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.8, M17): the world point with its HEIGHT, what was
    /// hit, and for an entity its network id. The layers keep receiving X/Y; height reaches the gizmos through
    /// <see cref="Point"/>.<c>Z</c>.
    /// </summary>
    public readonly record struct PickResult(Vector3 Point, PickKind Kind, float Distance, int NetworkId = 0)
    {
        public static readonly PickResult None = new(Vector3.Zero, PickKind.None, float.PositiveInfinity);
    }

    /// <summary>What a pick may hit — P2: a drag sees only the terrain and the ground, never entities.</summary>
    [Flags]
    public enum PickFilter : byte
    {
        Terrain = 1,
        Entities = 2,
        All = Terrain | Entities,
        /// <summary>⭐ CE-1126 (DESIGN_Entity_Control §8g F-4) — an entity by the PARTS a round meets (its body-part boxes), not its selection
        /// box; the hit point is where the ray meets the part.</summary>
        EntityParts = 4,
        /// <summary>⭐ CE-1126 — what the FPS crosshair sees: the terrain and the entities' parts.</summary>
        Aim = Terrain | EntityParts,
    }

    /// <summary>
    /// ⭐ CE-1033 S2 — one source of pickable geometry for <see cref="MapCamera3D"/>: the terrain mesh, the entity boxes. Nearest hit
    /// wins. A ray is in HROT coordinates (Z up).
    /// </summary>
    public interface IPicker3D
    {
        /// <summary>Which filter bit this picker answers.</summary>
        PickFilter Kind { get; }

        /// <summary>The nearest hit along the ray within <paramref name="maxDistance"/>, or false.</summary>
        bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit);
    }

    /// <summary>
    /// ⭐ CE-1466 — a picker that answers for entity BODIES: whether the entity with <paramref name="networkId"/> is drawn as a body this picker
    /// hit-tests. Such an entity is clicked by the ray only — never by its 2-D footprint (<see cref="MapCamera3D.IsRayOnlyAnchor"/>).
    /// </summary>
    public interface IBodyPicker3D : IPicker3D
    {
        bool HasBody(long networkId);
    }

    /// <summary>
    /// ⭐ CE-1033 S2 (M17) — the ray against the DRAWN terrain (<see cref="ITerrainRenderGeometry"/>, the same triangles
    /// <see cref="TerrainLayer3D"/> draws, so what you click is what you see). Pure C#: a uniform grid over the triangles' XY
    /// bounds, walked cell by cell along the ray (Amanatides–Woo), Möller–Trumbore per triangle, no allocation per pick.
    /// Rebuilt when the geometry's <see cref="ITerrainRenderGeometry.Identity"/> changes.
    /// </summary>
    public sealed class TerrainPicker : IPicker3D
    {
        private readonly Func<ITerrainRenderGeometry?> _source;
        /// <summary>⭐ R-1046 — the grid is built on a worker (<see cref="BackgroundMeshBuild{TStreams}"/>, keyed by the geometry's identity) and swapped in on the frame thread: a pick NEVER builds it.</summary>
        private readonly BackgroundMeshBuild<PickGrid> _build = new();
        private PickGrid? _grid;

        public TerrainPicker(Func<ITerrainRenderGeometry?> source) => _source = source ?? throw new ArgumentNullException(nameof(source));

        public PickFilter Kind => PickFilter.Terrain;

        /// <summary>Triangles indexed (0 until the grid for the current geometry is built).</summary>
        public int TriangleCount => _grid?.A.Length ?? 0;

        /// <summary>True when the grid for the CURRENT geometry is built (a pick can hit).</summary>
        /// <remarks>⭐ CE-3423 — for a CHUNKED terrain: bound to THIS terrain and at least one chunk grid built (a pick there can hit).</remarks>
        public bool IsBuilt => _source() is { } geometry && (geometry is ITerrainChunkedGeometry c ? _chunks is { } g && g.IsFor(c) && (g.AllBuilt || g.Resident > 0) : _build.IsBuiltFor(geometry.Identity));

        /// <summary>Starts the build for the current geometry if it has not been asked, swaps in a finished one; returns <see cref="IsBuilt"/>. A pick does this itself.</summary>
        /// <remarks>⭐ CE-3423 — for a CHUNKED terrain it asks for every chunk's grid (through the in-flight cap) and is true once all are built: the rails' way to wait on a small
        /// terrain. ⛔ The frame never calls it — a pick asks only for the chunks its ray crosses.</remarks>
        public bool Update()
        {
            if (_source() is ITerrainChunkedGeometry chunked) { (_chunks ??= new ChunkPickGrids()).Bind(chunked); return _chunks.WarmAll(); }
            return Current() != null;
        }

        private PickGrid? Current()
        {
            var geometry = _source();
            if (geometry == null) return null;
            _build.Request(geometry.Identity, () => PickGrid.Build(geometry));
            _build.Pump(g => _grid = g);
            return _build.IsBuiltFor(geometry.Identity) ? _grid : null;
        }

        public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
        {
            hit = PickResult.None;
            float best;
            if (_source() is ITerrainChunkedGeometry chunked)   // ⭐ CE-3423 L-10 — per chunk, never the whole soup
            {
                (_chunks ??= new ChunkPickGrids()).Bind(chunked);
                if (!_chunks.Raycast(origin, direction, maxDistance, out best)) return false;
            }
            else
            {
                var g = Current();                                  // ⭐ R-1046: null while the grid builds on its worker — a pick never blocks, it just misses
                if (g == null || !g.Raycast(origin, direction, maxDistance, out best)) return false;
            }
            hit = new PickResult(origin + direction * best, PickKind.Terrain, best);
            return true;
        }

        private ChunkPickGrids? _chunks;

        /// <summary>⭐ CE-3423 — the per-chunk grids (null for a plain source) — a rail reads how many are built.</summary>
        internal ChunkPickGrids? Chunks => _chunks;

        /// <summary>Möller–Trumbore, two-sided.</summary>
        public static bool Intersect(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t >= 0f;
        }
    }

    /// <summary>⭐ R-1046 — the picker's index of the terrain: its triangles (HROT space) and the uniform grid over their XY bounds. Immutable once built, so it is built on a worker and swapped in whole.</summary>
    internal sealed class PickGrid
    {
        public Vector3[] A = Array.Empty<Vector3>(), B = Array.Empty<Vector3>(), C = Array.Empty<Vector3>();
        public float MinZ, MaxZ;
        public Vector2 Min;
        public float Cell = 1f;
        public int Nx, Ny;
        public int[] CellStart = Array.Empty<int>();
        public int[] CellTris = Array.Empty<int>();

        public int CellX(float x) => Math.Clamp((int)MathF.Floor((x - Min.X) / Cell), -1, Nx);
        public int CellY(float y) => Math.Clamp((int)MathF.Floor((y - Min.Y) / Cell), -1, Ny);

        /// <summary>The nearest hit along the ray (≤ <paramref name="maxDistance"/>): the grid walked cell by cell (Amanatides–Woo), Möller–Trumbore per triangle. No allocation.</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out float best)
        {
            best = float.PositiveInfinity;
            if (A.Length == 0) return false;
            // Clip the ray to the slab of the grid's Z range and walk the grid over that XY segment.
            if (!ClipToZ(origin, direction, maxDistance, out float t0, out float t1)) return false;
            var p0 = origin + direction * t0;
            var p1 = origin + direction * t1;
            int cx = CellX(p0.X), cy = CellY(p0.Y);
            int ex = CellX(p1.X), ey = CellY(p1.Y);
            var d2 = new Vector2(p1.X - p0.X, p1.Y - p0.Y);
            int stepX = d2.X > 0 ? 1 : -1, stepY = d2.Y > 0 ? 1 : -1;
            float tMaxX = d2.X != 0 ? ((Min.X + (cx + (stepX > 0 ? 1 : 0)) * Cell) - p0.X) / d2.X : float.PositiveInfinity;
            float tMaxY = d2.Y != 0 ? ((Min.Y + (cy + (stepY > 0 ? 1 : 0)) * Cell) - p0.Y) / d2.Y : float.PositiveInfinity;
            float tDeltaX = d2.X != 0 ? Cell / MathF.Abs(d2.X) : float.PositiveInfinity;
            float tDeltaY = d2.Y != 0 ? Cell / MathF.Abs(d2.Y) : float.PositiveInfinity;
            int guard = Nx + Ny + 4;
            while (guard-- > 0)
            {
                if (cx >= 0 && cy >= 0 && cx < Nx && cy < Ny)
                {
                    int cellIndex = cy * Nx + cx;
                    for (int k = CellStart[cellIndex]; k < CellStart[cellIndex + 1]; k++)
                    {
                        int tri = CellTris[k];
                        if (TerrainPicker.Intersect(origin, direction, A[tri], B[tri], C[tri], out float t) && t >= 0f && t < best && t <= maxDistance)
                            best = t;
                    }
                }
                // A hit inside the cells already walked cannot be beaten by a later cell once the walk passes it.
                float cellExit = MathF.Min(tMaxX, tMaxY);
                if (best < float.PositiveInfinity && t0 + cellExit * (t1 - t0) >= best) break;
                if (cx == ex && cy == ey) break;
                if (tMaxX < tMaxY) { tMaxX += tDeltaX; cx += stepX; }
                else { tMaxY += tDeltaY; cy += stepY; }
            }
            return !float.IsPositiveInfinity(best);
        }

        private bool ClipToZ(Vector3 o, Vector3 d, float max, out float t0, out float t1)
        {
            t0 = 0f; t1 = max;
            float lo = MinZ - 0.01f, hi = MaxZ + 0.01f;
            if (MathF.Abs(d.Z) < 1e-8f) return o.Z >= lo && o.Z <= hi;
            float ta = (lo - o.Z) / d.Z, tb = (hi - o.Z) / d.Z;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta); t1 = MathF.Min(t1, tb);
            return t1 >= t0;
        }

        /// <summary>The CPU build (off the frame thread): reads only the geometry's immutable terrain.</summary>
        public static PickGrid Build(ITerrainRenderGeometry geometry)
        {
            // ⭐ CE-4341 — the triangles TerrainLayer3D DRAWS (BuildTagged: the soup + the apron round the terrain + water), never Build's soup alone.
            //   ⛔ It read Build, so the hilly apron (CE-4122) was drawn but not pickable: a click or drag there fell through to the camera's look-at
            //   plane (z ≈ 0) — the ghost and the drop point sat under the drawn ground, at the wrong place behind the hill.
            geometry.BuildTagged(out var verts, out var indices, out _, out _);
            return Build(verts, indices);
        }

        /// <summary>⭐ CE-3423 — the grid over any triangles (HROT space): a chunk's, the outside's, or the whole soup's.</summary>
        public static PickGrid Build(Vector3[] verts, int[] indices)
        {
            var g = new PickGrid();
            int n = indices.Length / 3;
            g.A = new Vector3[n]; g.B = new Vector3[n]; g.C = new Vector3[n];
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            g.MinZ = float.MaxValue; g.MaxZ = float.MinValue;
            for (int t = 0; t < n; t++)
            {
                g.A[t] = verts[indices[3 * t]]; g.B[t] = verts[indices[3 * t + 1]]; g.C[t] = verts[indices[3 * t + 2]];
                for (int k = 0; k < 3; k++)
                {
                    var v = k == 0 ? g.A[t] : k == 1 ? g.B[t] : g.C[t];
                    min = Vector2.Min(min, new Vector2(v.X, v.Y)); max = Vector2.Max(max, new Vector2(v.X, v.Y));
                    g.MinZ = MathF.Min(g.MinZ, v.Z); g.MaxZ = MathF.Max(g.MaxZ, v.Z);
                }
            }
            if (n == 0) { g.Nx = g.Ny = 0; return g; }
            g.Min = min;
            var extent = max - min;
            // ~ 8 triangles per occupied cell on average; at least 2 m, at most 512 cells a side.
            float area = MathF.Max(1f, extent.X * extent.Y);
            g.Cell = Math.Clamp(MathF.Sqrt(area * 8f / n), 2f, MathF.Max(2f, MathF.Max(extent.X, extent.Y) / 8f));
            g.Nx = Math.Clamp((int)MathF.Ceiling(extent.X / g.Cell) + 1, 1, 512);
            g.Ny = Math.Clamp((int)MathF.Ceiling(extent.Y / g.Cell) + 1, 1, 512);
            g.Cell = MathF.Max(g.Cell, MathF.Max(extent.X / (g.Nx - 0.001f), extent.Y / (g.Ny - 0.001f)));

            var counts = new int[g.Nx * g.Ny + 1];
            var lists = new List<(int Cell, int Tri)>(n * 2);
            for (int t = 0; t < n; t++)
            {
                float x0 = MathF.Min(g.A[t].X, MathF.Min(g.B[t].X, g.C[t].X)), x1 = MathF.Max(g.A[t].X, MathF.Max(g.B[t].X, g.C[t].X));
                float y0 = MathF.Min(g.A[t].Y, MathF.Min(g.B[t].Y, g.C[t].Y)), y1 = MathF.Max(g.A[t].Y, MathF.Max(g.B[t].Y, g.C[t].Y));
                int ix0 = Math.Clamp(g.CellX(x0), 0, g.Nx - 1), ix1 = Math.Clamp(g.CellX(x1), 0, g.Nx - 1);
                int iy0 = Math.Clamp(g.CellY(y0), 0, g.Ny - 1), iy1 = Math.Clamp(g.CellY(y1), 0, g.Ny - 1);
                for (int iy = iy0; iy <= iy1; iy++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    { lists.Add((iy * g.Nx + ix, t)); counts[iy * g.Nx + ix]++; }
            }
            g.CellStart = new int[g.Nx * g.Ny + 1];
            for (int i = 0; i < g.Nx * g.Ny; i++) g.CellStart[i + 1] = g.CellStart[i] + counts[i];
            g.CellTris = new int[lists.Count];
            var fill = (int[])g.CellStart.Clone();
            foreach (var (cell, tri) in lists) g.CellTris[fill[cell]++] = tri;
            return g;
        }
    }

    /// <summary>
    /// ⭐ CE-3423 L-10 (docs/DESIGN_Map_3D_Mode.md §6ah) — the picker's index of a CHUNKED terrain: a <see cref="PickGrid"/> per chunk (its level-0 ground + what stands on it),
    /// built on a worker when a ray first crosses the chunk, and one for the outside (the apron). A pick walks the chunk grid along the ray and tests the built ones — it never
    /// builds, so the first ray over a chunk misses it and asks; the hover pick of the next frames finds it ready. ⭐ At most <see cref="MaxResident"/> chunk grids are kept
    /// (the least recently used go). No allocation on a steady pick.
    /// </summary>
    internal sealed class ChunkPickGrids
    {
        public const int MaxInFlight = 2;
        public const int MaxResident = 64;

        private ITerrainChunkedGeometry? _geometry;
        private object? _identity;
        private TerrainChunkLayout _layout = new(default, 1f, 1f, 0, 0, 1, 0f, 0f);
        private BackgroundMeshBuild<PickGrid>[] _builds = Array.Empty<BackgroundMeshBuild<PickGrid>>();
        private PickGrid?[] _grids = Array.Empty<PickGrid?>();
        private long[] _used = Array.Empty<long>();
        private readonly BackgroundMeshBuild<PickGrid> _outsideBuild = new();
        private PickGrid? _outside;
        private readonly int[] _flight = new int[MaxInFlight];
        private int _inFlight, _resident;
        private long _tick;

        /// <summary>How many chunk grids are built (a rail).</summary>
        public int Resident => _resident;

        /// <summary>Bound to <paramref name="geometry"/> (its identity) — a swapped terrain is not built until bound and built anew.</summary>
        public bool IsFor(ITerrainChunkedGeometry geometry) => _identity != null && Equals(_identity, geometry.Identity);

        public void Bind(ITerrainChunkedGeometry geometry)
        {
            if (Equals(_identity, geometry.Identity)) { _geometry = geometry; return; }   // the same terrain (a new query object per call is the same identity)
            _geometry = geometry; _identity = geometry.Identity;
            _layout = geometry.Chunks;
            int n = _layout.Nx * _layout.Ny;
            _builds = new BackgroundMeshBuild<PickGrid>[n];
            for (int i = 0; i < n; i++) _builds[i] = new BackgroundMeshBuild<PickGrid>();
            _grids = new PickGrid?[n]; _used = new long[n];
            _inFlight = 0; _resident = 0; _outside = null; AllBuilt = false;
            _outsideBuild.Reset();
            var g = geometry;
            _outsideBuild.Request(geometry.Identity, () => { g.BuildOutside(out var v, out var i, out _, out _); return PickGrid.Build(v, i); });
        }

        private void Pump()
        {
            _outsideBuild.Pump(grid => _outside = grid);
            for (int f = 0; f < _inFlight; f++)
            {
                int k = _flight[f];
                bool done = _builds[k].Pump(grid => { _grids[k] = grid; _resident++; });
                if (done || _builds[k].State == MeshBuildState.Failed) { _flight[f] = _flight[--_inFlight]; f--; }
            }
            while (_resident > MaxResident) Evict();
        }

        private void Evict()
        {
            int oldest = -1; long when = long.MaxValue;
            for (int k = 0; k < _grids.Length; k++) if (_grids[k] != null && _used[k] < when) { when = _used[k]; oldest = k; }
            if (oldest < 0) { _resident = 0; return; }
            _grids[oldest] = null; _builds[oldest].Reset(); _resident--;
        }

        private void Ask(int k)
        {
            if (_inFlight >= MaxInFlight || _builds[k].State != MeshBuildState.Idle || _geometry is not { } g) return;
            int cx = k % _layout.Nx, cy = k / _layout.Nx;
            _flight[_inFlight++] = k;
            _builds[k].Request(_identity!, () =>
            {
                g.BuildChunk(cx, cy, 0, TerrainChunkPart.ChunkGround, out var gv, out var gi, out _, out _);
                g.BuildChunk(cx, cy, 0, TerrainChunkPart.ChunkObjects, out var ov, out var oi, out _, out _);
                var v = new Vector3[gv.Length + ov.Length]; gv.CopyTo(v, 0); ov.CopyTo(v, gv.Length);
                var i = new int[gi.Length + oi.Length]; gi.CopyTo(i, 0);
                for (int j = 0; j < oi.Length; j++) i[gi.Length + j] = oi[j] + gv.Length;
                return PickGrid.Build(v, i);
            });
        }

        /// <summary>All chunk grids and the outside are built (<see cref="WarmAll"/>).</summary>
        public bool AllBuilt { get; private set; }

        /// <summary>Asks for every chunk not built yet (through the in-flight cap); true once every one and the outside are built.</summary>
        public bool WarmAll()
        {
            Pump();
            bool all = _outside != null;
            for (int k = 0; k < _grids.Length; k++) if (_grids[k] == null) { all = false; Ask(k); }
            return AllBuilt = all;
        }

        /// <summary>The nearest drawn-terrain hit along the ray; the chunks it crosses that are not built yet are asked for (and missed).</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out float best)
        {
            Pump();
            _tick++;
            best = float.PositiveInfinity;
            if (_outside != null && _outside.Raycast(origin, direction, maxDistance, out float to)) best = to;
            if (_layout.Nx == 0) return !float.IsPositiveInfinity(best);

            // clip to the terrain's height slab, then walk the CHUNK grid over that XY segment
            float t0 = 0f, t1 = maxDistance, lo = _layout.MinZ - 1f, hi = _layout.MaxZ + 1f;
            if (MathF.Abs(direction.Z) < 1e-8f) { if (origin.Z < lo || origin.Z > hi) return !float.IsPositiveInfinity(best); }
            else
            {
                float ta = (lo - origin.Z) / direction.Z, tb = (hi - origin.Z) / direction.Z;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = MathF.Max(t0, ta); t1 = MathF.Min(t1, tb);
                if (t1 < t0) return !float.IsPositiveInfinity(best);
            }
            var p0 = origin + direction * t0; var p1 = origin + direction * t1;
            float size = _layout.ChunkSize;
            int cx = (int)MathF.Floor((p0.X - _layout.Origin.X) / size), cy = (int)MathF.Floor((p0.Y - _layout.Origin.Y) / size);
            int ex = (int)MathF.Floor((p1.X - _layout.Origin.X) / size), ey = (int)MathF.Floor((p1.Y - _layout.Origin.Y) / size);
            var d2 = new Vector2(p1.X - p0.X, p1.Y - p0.Y);
            int sx = d2.X > 0 ? 1 : -1, sy = d2.Y > 0 ? 1 : -1;
            float tMaxX = d2.X != 0 ? ((_layout.Origin.X + (cx + (sx > 0 ? 1 : 0)) * size) - p0.X) / d2.X : float.PositiveInfinity;
            float tMaxY = d2.Y != 0 ? ((_layout.Origin.Y + (cy + (sy > 0 ? 1 : 0)) * size) - p0.Y) / d2.Y : float.PositiveInfinity;
            float tdx = d2.X != 0 ? size / MathF.Abs(d2.X) : float.PositiveInfinity, tdy = d2.Y != 0 ? size / MathF.Abs(d2.Y) : float.PositiveInfinity;
            int guard = _layout.Nx + _layout.Ny + 4;
            while (guard-- > 0)
            {
                if (cx >= 0 && cy >= 0 && cx < _layout.Nx && cy < _layout.Ny)
                {
                    int k = cy * _layout.Nx + cx;
                    if (_grids[k] is { } g)
                    {
                        _used[k] = _tick;
                        if (g.Raycast(origin, direction, MathF.Min(maxDistance, best), out float t) && t < best) best = t;
                    }
                    else Ask(k);
                }
                float exit = MathF.Min(tMaxX, tMaxY);
                if (best < float.PositiveInfinity && t0 + exit * (t1 - t0) >= best) break;
                if (cx == ex && cy == ey) break;
                if (tMaxX < tMaxY) { tMaxX += tdx; cx += sx; } else { tMaxY += tdy; cy += sy; }
            }
            return !float.IsPositiveInfinity(best);
        }
    }

    /// <summary>Ray vs an oriented box (Kay–Kajiya slabs in the box's frame) — the entity pick volume (P1).</summary>
    public static class RayBox
    {
        /// <summary>Entry distance of the ray into the box centred at <paramref name="centre"/>, rotated by <paramref name="rotation"/>,
        /// with half-extents <paramref name="half"/>; false when it misses.</summary>
        public static bool Intersect(Vector3 origin, Vector3 direction, Vector3 centre, Quaternion rotation, Vector3 half, out float t)
        {
            var inv = Quaternion.Inverse(rotation);
            var o = Vector3.Transform(origin - centre, inv);
            var d = Vector3.Transform(direction, inv);
            float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float oo = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
                float dd = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
                float hh = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
                if (MathF.Abs(dd) < 1e-9f)
                {
                    if (oo < -hh || oo > hh) { t = 0f; return false; }
                    continue;
                }
                float ta = (-hh - oo) / dd, tb = (hh - oo) / dd;
                if (ta > tb) (ta, tb) = (tb, ta);
                tMin = MathF.Max(tMin, ta); tMax = MathF.Min(tMax, tb);
                if (tMin > tMax) { t = 0f; return false; }
            }
            t = tMin >= 0f ? tMin : tMax;
            return tMax >= 0f;
        }
    }
}
