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
    public readonly record struct PickResult(Vector3 Point, PickKind Kind, float Distance, long NetworkId = 0)
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
        public bool IsBuilt => _source() is { } geometry && _build.IsBuiltFor(geometry.Identity);

        /// <summary>Starts the build for the current geometry if it has not been asked, swaps in a finished one; returns <see cref="IsBuilt"/>. A pick does this itself.</summary>
        public bool Update() => Current() != null;

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
            var g = Current();                                  // ⭐ R-1046: null while the grid builds on its worker — a pick never blocks, it just misses
            if (g == null || g.A.Length == 0) return false;

            // Clip the ray to the slab of the terrain's Z range and walk the grid over that XY segment.
            if (!ClipToZ(g, origin, direction, maxDistance, out float t0, out float t1)) return false;
            var p0 = origin + direction * t0;
            var p1 = origin + direction * t1;
            float best = float.PositiveInfinity;

            int cx = g.CellX(p0.X), cy = g.CellY(p0.Y);
            int ex = g.CellX(p1.X), ey = g.CellY(p1.Y);
            var d2 = new Vector2(p1.X - p0.X, p1.Y - p0.Y);
            int stepX = d2.X > 0 ? 1 : -1, stepY = d2.Y > 0 ? 1 : -1;
            float tMaxX = d2.X != 0 ? ((g.Min.X + (cx + (stepX > 0 ? 1 : 0)) * g.Cell) - p0.X) / d2.X : float.PositiveInfinity;
            float tMaxY = d2.Y != 0 ? ((g.Min.Y + (cy + (stepY > 0 ? 1 : 0)) * g.Cell) - p0.Y) / d2.Y : float.PositiveInfinity;
            float tDeltaX = d2.X != 0 ? g.Cell / MathF.Abs(d2.X) : float.PositiveInfinity;
            float tDeltaY = d2.Y != 0 ? g.Cell / MathF.Abs(d2.Y) : float.PositiveInfinity;
            int guard = g.Nx + g.Ny + 4;
            while (guard-- > 0)
            {
                if (cx >= 0 && cy >= 0 && cx < g.Nx && cy < g.Ny)
                {
                    int cellIndex = cy * g.Nx + cx;
                    for (int k = g.CellStart[cellIndex]; k < g.CellStart[cellIndex + 1]; k++)
                    {
                        int tri = g.CellTris[k];
                        if (Intersect(origin, direction, g.A[tri], g.B[tri], g.C[tri], out float t) && t >= 0f && t < best && t <= maxDistance)
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
            if (float.IsPositiveInfinity(best)) return false;
            hit = new PickResult(origin + direction * best, PickKind.Terrain, best);
            return true;
        }

        private bool ClipToZ(PickGrid g, Vector3 o, Vector3 d, float max, out float t0, out float t1)
        {
            t0 = 0f; t1 = max;
            float lo = g.MinZ - 0.01f, hi = g.MaxZ + 0.01f;
            if (MathF.Abs(d.Z) < 1e-8f) return o.Z >= lo && o.Z <= hi;
            float ta = (lo - o.Z) / d.Z, tb = (hi - o.Z) / d.Z;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta); t1 = MathF.Min(t1, tb);
            return t1 >= t0;
        }

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

        /// <summary>The CPU build (off the frame thread): reads only the geometry's immutable terrain.</summary>
        public static PickGrid Build(ITerrainRenderGeometry geometry)
        {
            var g = new PickGrid();
            geometry.Build(out var verts, out var indices, out _);
            int n = indices.Length / 3;
            g.A = new Vector3[n]; g.B = new Vector3[n]; g.C = new Vector3[n];
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            g.MinZ = float.MaxValue; g.MaxZ = float.MinValue;
            for (int t = 0; t < n; t++)
            {
                g.A[t] = verts[indices[3 * t]]; g.B[t] = verts[indices[3 * t + 1]]; g.C[t] = verts[indices[3 * t + 2]];
                foreach (var v in new[] { g.A[t], g.B[t], g.C[t] })
                {
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
