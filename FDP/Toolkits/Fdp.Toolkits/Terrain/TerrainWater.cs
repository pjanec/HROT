using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4695</c> (backend, cross-lane — user "fix yourself") — THE ONE "is this point under authored water?" answer: the terrain's
    /// <see cref="TerrainSurfaceType.Water"/> polygons filed in 32 m bins (a point's bin holds every polygon that can contain it). Read by the ground mesh (a cell
    /// under water is cut — it held its own copy of these bins until now), the bridge plan (<see cref="BridgeLaying"/>), and a walker's step (a man never wades
    /// into a river from dry ground — <c>CarKinematicsSystem.FinishMove</c>). Built once per surface LIST (a derived world shares its parent's surfaces, so it
    /// shares the index too); a query allocates nothing.
    /// </summary>
    public sealed class TerrainWater
    {
        private const float Bin = 32f;
        private static readonly ConditionalWeakTable<IReadOnlyList<TerrainSurface>, TerrainWater> Cache = new();

        private readonly Vector2 _min;
        private readonly int _nx, _ny;
        private readonly int[]?[] _bins;
        private readonly TerrainSurface[] _water;

        /// <summary>The index of <paramref name="world"/>'s water (built on first ask, then shared by every world with the same surface list).</summary>
        public static TerrainWater Of(TerrainWorld world) => Cache.GetValue(world.Surfaces, static s => new TerrainWater(s));

        private TerrainWater(IReadOnlyList<TerrainSurface> surfaces)
        {
            var water = new List<TerrainSurface>();
            for (int i = 0; i < surfaces.Count; i++) if (surfaces[i].Type == TerrainSurfaceType.Water) water.Add(surfaces[i]);
            _water = water.ToArray();
            if (_water.Length == 0) { _bins = Array.Empty<int[]?>(); return; }
            var lo = _water[0].Min; var hi = _water[0].Max;
            foreach (var s in _water) { lo = Vector2.Min(lo, s.Min); hi = Vector2.Max(hi, s.Max); }
            _min = lo;
            _nx = Math.Max(1, (int)MathF.Ceiling((hi.X - lo.X) / Bin) + 1);
            _ny = Math.Max(1, (int)MathF.Ceiling((hi.Y - lo.Y) / Bin) + 1);
            var bins = new List<int>?[_nx * _ny];
            for (int i = 0; i < _water.Length; i++)
            {
                int x0 = Col(_water[i].Min.X), x1 = Col(_water[i].Max.X), y0 = Row(_water[i].Min.Y), y1 = Row(_water[i].Max.Y);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        (bins[y * _nx + x] ??= new List<int>()).Add(i);
            }
            _bins = new int[]?[bins.Length];
            for (int i = 0; i < bins.Length; i++) _bins[i] = bins[i]?.ToArray();
        }

        /// <summary>Whether the terrain has any water at all (a dry terrain answers every query at once).</summary>
        public bool Any => _water.Length > 0;

        /// <summary>Whether <paramref name="p"/> lies under authored water.</summary>
        public bool Contains(Vector2 p)
        {
            if (_water.Length == 0) return false;
            if (p.X < _min.X || p.Y < _min.Y) return false;
            int cx = (int)MathF.Floor((p.X - _min.X) / Bin), cy = (int)MathF.Floor((p.Y - _min.Y) / Bin);
            if (cx >= _nx || cy >= _ny) return false;
            var bin = _bins[cy * _nx + cx];
            if (bin == null) return false;
            for (int k = 0; k < bin.Length; k++)
            {
                var s = _water[bin[k]];
                if (p.X < s.Min.X || p.Y < s.Min.Y || p.X > s.Max.X || p.Y > s.Max.Y) continue;
                if (PolygonMath.Contains(s.Polygon, p)) return true;
            }
            return false;
        }

        private int Col(float x) => Math.Clamp((int)MathF.Floor((x - _min.X) / Bin), 0, _nx - 1);
        private int Row(float y) => Math.Clamp((int)MathF.Floor((y - _min.Y) / Bin), 0, _ny - 1);
    }
}
