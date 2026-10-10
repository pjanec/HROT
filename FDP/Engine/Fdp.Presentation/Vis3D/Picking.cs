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
        private object? _identity;
        private Vector3[] _a = Array.Empty<Vector3>(), _b = Array.Empty<Vector3>(), _c = Array.Empty<Vector3>();
        private float _minZ, _maxZ;
        private Vector2 _min;
        private float _cell = 1f;
        private int _nx, _ny;
        private int[] _cellStart = Array.Empty<int>();
        private int[] _cellTris = Array.Empty<int>();

        public TerrainPicker(Func<ITerrainRenderGeometry?> source) => _source = source ?? throw new ArgumentNullException(nameof(source));

        public PickFilter Kind => PickFilter.Terrain;

        /// <summary>Triangles indexed (0 until the first pick with a world).</summary>
        public int TriangleCount => _a.Length;

        public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
        {
            hit = PickResult.None;
            var geometry = _source();
            if (geometry == null) return false;
            if (!ReferenceEquals(geometry.Identity, _identity)) Build(geometry);
            if (_a.Length == 0) return false;

            // Clip the ray to the slab of the terrain's Z range and walk the grid over that XY segment.
            if (!ClipToZ(origin, direction, maxDistance, out float t0, out float t1)) return false;
            var p0 = origin + direction * t0;
            var p1 = origin + direction * t1;
            float best = float.PositiveInfinity;

            int cx = CellX(p0.X), cy = CellY(p0.Y);
            int ex = CellX(p1.X), ey = CellY(p1.Y);
            var d2 = new Vector2(p1.X - p0.X, p1.Y - p0.Y);
            int stepX = d2.X > 0 ? 1 : -1, stepY = d2.Y > 0 ? 1 : -1;
            float tMaxX = d2.X != 0 ? ((_min.X + (cx + (stepX > 0 ? 1 : 0)) * _cell) - p0.X) / d2.X : float.PositiveInfinity;
            float tMaxY = d2.Y != 0 ? ((_min.Y + (cy + (stepY > 0 ? 1 : 0)) * _cell) - p0.Y) / d2.Y : float.PositiveInfinity;
            float tDeltaX = d2.X != 0 ? _cell / MathF.Abs(d2.X) : float.PositiveInfinity;
            float tDeltaY = d2.Y != 0 ? _cell / MathF.Abs(d2.Y) : float.PositiveInfinity;
            int guard = _nx + _ny + 4;
            while (guard-- > 0)
            {
                if (cx >= 0 && cy >= 0 && cx < _nx && cy < _ny)
                {
                    int cellIndex = cy * _nx + cx;
                    for (int k = _cellStart[cellIndex]; k < _cellStart[cellIndex + 1]; k++)
                    {
                        int tri = _cellTris[k];
                        if (Intersect(origin, direction, _a[tri], _b[tri], _c[tri], out float t) && t >= 0f && t < best && t <= maxDistance)
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

        private int CellX(float x) => Math.Clamp((int)MathF.Floor((x - _min.X) / _cell), -1, _nx);
        private int CellY(float y) => Math.Clamp((int)MathF.Floor((y - _min.Y) / _cell), -1, _ny);

        private bool ClipToZ(Vector3 o, Vector3 d, float max, out float t0, out float t1)
        {
            t0 = 0f; t1 = max;
            float lo = _minZ - 0.01f, hi = _maxZ + 0.01f;
            if (MathF.Abs(d.Z) < 1e-8f) return o.Z >= lo && o.Z <= hi;
            float ta = (lo - o.Z) / d.Z, tb = (hi - o.Z) / d.Z;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta); t1 = MathF.Min(t1, tb);
            return t1 >= t0;
        }

        private void Build(ITerrainRenderGeometry geometry)
        {
            _identity = geometry.Identity;
            geometry.Build(out var verts, out var indices, out _);
            int n = indices.Length / 3;
            _a = new Vector3[n]; _b = new Vector3[n]; _c = new Vector3[n];
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            _minZ = float.MaxValue; _maxZ = float.MinValue;
            for (int t = 0; t < n; t++)
            {
                _a[t] = verts[indices[3 * t]]; _b[t] = verts[indices[3 * t + 1]]; _c[t] = verts[indices[3 * t + 2]];
                foreach (var v in new[] { _a[t], _b[t], _c[t] })
                {
                    min = Vector2.Min(min, new Vector2(v.X, v.Y)); max = Vector2.Max(max, new Vector2(v.X, v.Y));
                    _minZ = MathF.Min(_minZ, v.Z); _maxZ = MathF.Max(_maxZ, v.Z);
                }
            }
            if (n == 0) { _nx = _ny = 0; return; }
            _min = min;
            var extent = max - min;
            // ~ 8 triangles per occupied cell on average; at least 2 m, at most 512 cells a side.
            float area = MathF.Max(1f, extent.X * extent.Y);
            _cell = Math.Clamp(MathF.Sqrt(area * 8f / n), 2f, MathF.Max(2f, MathF.Max(extent.X, extent.Y) / 8f));
            _nx = Math.Clamp((int)MathF.Ceiling(extent.X / _cell) + 1, 1, 512);
            _ny = Math.Clamp((int)MathF.Ceiling(extent.Y / _cell) + 1, 1, 512);
            _cell = MathF.Max(_cell, MathF.Max(extent.X / (_nx - 0.001f), extent.Y / (_ny - 0.001f)));

            var counts = new int[_nx * _ny + 1];
            var lists = new List<(int Cell, int Tri)>(n * 2);
            for (int t = 0; t < n; t++)
            {
                float x0 = MathF.Min(_a[t].X, MathF.Min(_b[t].X, _c[t].X)), x1 = MathF.Max(_a[t].X, MathF.Max(_b[t].X, _c[t].X));
                float y0 = MathF.Min(_a[t].Y, MathF.Min(_b[t].Y, _c[t].Y)), y1 = MathF.Max(_a[t].Y, MathF.Max(_b[t].Y, _c[t].Y));
                int ix0 = Math.Clamp(CellX(x0), 0, _nx - 1), ix1 = Math.Clamp(CellX(x1), 0, _nx - 1);
                int iy0 = Math.Clamp(CellY(y0), 0, _ny - 1), iy1 = Math.Clamp(CellY(y1), 0, _ny - 1);
                for (int iy = iy0; iy <= iy1; iy++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    { lists.Add((iy * _nx + ix, t)); counts[iy * _nx + ix]++; }
            }
            _cellStart = new int[_nx * _ny + 1];
            for (int i = 0; i < _nx * _ny; i++) _cellStart[i + 1] = _cellStart[i] + counts[i];
            _cellTris = new int[lists.Count];
            var fill = (int[])_cellStart.Clone();
            foreach (var (cell, tri) in lists) _cellTris[fill[cell]++] = tri;
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
