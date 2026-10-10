using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-1034 H1 (<c>docs/DESIGN_Terrain_Height.md</c> TH-A/TH-B, R-248) — the ground's height as a regular grid of samples, in
    /// local metres. Between samples the ground is two triangles per cell, split along the south-west → north-east diagonal — the
    /// same split <see cref="TerrainWorldMesh"/> uses — so <see cref="Sample"/>, the navmesh and the drawn terrain describe ONE
    /// surface. Outside the grid the edge sample holds.
    /// </summary>
    public sealed class TerrainHeightGrid
    {
        private readonly float[] _z;   // row-major, row 0 = south

        /// <summary>The south-west sample's position.</summary>
        public Vector2 Origin { get; }
        public float CellSize { get; }
        /// <summary>Samples per row (west → east).</summary>
        public int Cols { get; }
        /// <summary>Rows (south → north).</summary>
        public int Rows { get; }
        public float MinZ { get; }
        public float MaxZ { get; }

        /// <summary>The north-east sample's position.</summary>
        public Vector2 Max => Origin + new Vector2((Cols - 1) * CellSize, (Rows - 1) * CellSize);

        public TerrainHeightGrid(Vector2 origin, float cellSize, int cols, int rows, float[] z)
        {
            if (cols < 2 || rows < 2) throw new ArgumentException($"A height grid needs at least 2×2 samples (got {cols}×{rows}).");
            if (!(cellSize > 0f)) throw new ArgumentException($"A height grid's cell size must be positive (got {cellSize}).");
            if (z.Length != cols * rows) throw new ArgumentException($"A {cols}×{rows} height grid needs {cols * rows} samples (got {z.Length}).");
            Origin = origin; CellSize = cellSize; Cols = cols; Rows = rows; _z = z;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach (float v in z) { lo = MathF.Min(lo, v); hi = MathF.Max(hi, v); }
            MinZ = lo; MaxZ = hi;
        }

        /// <summary>The sample at column <paramref name="c"/>, row <paramref name="r"/> (row 0 = south).</summary>
        public float this[int c, int r] => _z[r * Cols + c];

        /// <summary>The ground height at (x, y): planar inside each of a cell's two triangles; the edge holds outside the grid.</summary>
        public float Sample(float x, float y)
        {
            float fx = Math.Clamp((x - Origin.X) / CellSize, 0f, Cols - 1);
            float fy = Math.Clamp((y - Origin.Y) / CellSize, 0f, Rows - 1);
            int c = Math.Min((int)fx, Cols - 2), r = Math.Min((int)fy, Rows - 2);
            float u = fx - c, v = fy - r;
            float z00 = this[c, r], z10 = this[c + 1, r], z01 = this[c, r + 1], z11 = this[c + 1, r + 1];
            return u >= v ? z00 + u * (z10 - z00) + v * (z11 - z10)    // the south-east triangle (00, 10, 11)
                          : z00 + v * (z01 - z00) + u * (z11 - z01);   // the north-west triangle (00, 11, 01)
        }

        /// <summary>
        /// ⭐ TH-C — the lowest ground under a closed <paramref name="polygon"/>: its corners and every sample inside it. A building
        /// that stands on it has no gap under its downhill side.
        /// </summary>
        public float LowestUnder(IReadOnlyList<Vector2> polygon)
        {
            float lo = float.PositiveInfinity;
            var min = new Vector2(float.PositiveInfinity); var max = new Vector2(float.NegativeInfinity);
            foreach (var p in polygon) { lo = MathF.Min(lo, Sample(p.X, p.Y)); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            if (polygon.Count >= 3)
            {
                var ring = polygon as Vector2[] ?? new List<Vector2>(polygon).ToArray();
                int c0 = Math.Max(0, (int)MathF.Ceiling((min.X - Origin.X) / CellSize)), c1 = Math.Min(Cols - 1, (int)MathF.Floor((max.X - Origin.X) / CellSize));
                int r0 = Math.Max(0, (int)MathF.Ceiling((min.Y - Origin.Y) / CellSize)), r1 = Math.Min(Rows - 1, (int)MathF.Floor((max.Y - Origin.Y) / CellSize));
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                        if (PolygonMath.Contains(ring, Origin + new Vector2(c * CellSize, r * CellSize))) lo = MathF.Min(lo, this[c, r]);
            }
            return lo;
        }

        /// <summary>⭐ TH-C — the lowest ground along an open <paramref name="line"/> (a wall, a fence), sampled every cell.</summary>
        public float LowestAlong(IReadOnlyList<Vector2> line)
        {
            float lo = float.PositiveInfinity;
            for (int i = 0; i < line.Count; i++)
            {
                lo = MathF.Min(lo, Sample(line[i].X, line[i].Y));
                if (i + 1 >= line.Count) break;
                var a = line[i]; var b = line[i + 1];
                int steps = (int)MathF.Ceiling(Vector2.Distance(a, b) / CellSize);
                for (int s = 1; s < steps; s++) { var p = Vector2.Lerp(a, b, s / (float)steps); lo = MathF.Min(lo, Sample(p.X, p.Y)); }
            }
            return lo;
        }

        /// <summary>
        /// Reads an ESRI ASCII grid (TH-A): the header (<c>ncols</c>, <c>nrows</c>, <c>xllcorner</c>|<c>xllcenter</c>,
        /// <c>yllcorner</c>|<c>yllcenter</c>, <c>cellsize</c>, optional <c>NODATA_value</c>), then <c>nrows</c> rows of heights, the
        /// NORTH row first. Coordinates are the terrain's local metres. A NODATA sample takes <paramref name="noData"/>.
        /// </summary>
        public static TerrainHeightGrid ParseAsciiGrid(string text, string where, float noData = 0f)
        {
            var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var header = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            bool xCenter = false, yCenter = false;
            int i = 0;
            while (i + 1 < tokens.Length && !IsNumber(tokens[i]))
            {
                string key = tokens[i];
                if (!double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new ArgumentException($"Height grid {where}: header '{key}' has no number.");
                if (key.Equals("xllcenter", StringComparison.OrdinalIgnoreCase)) xCenter = true;
                if (key.Equals("yllcenter", StringComparison.OrdinalIgnoreCase)) yCenter = true;
                header[key] = value;
                i += 2;
            }
            double Need(string a, string? b = null)
                => header.TryGetValue(a, out double v) || (b != null && header.TryGetValue(b, out v))
                    ? v : throw new ArgumentException($"Height grid {where}: the header has no '{a}'{(b != null ? $" / '{b}'" : "")}.");
            int cols = (int)Need("ncols"), rows = (int)Need("nrows");
            float cell = (float)Need("cellsize");
            float x0 = (float)Need("xllcorner", "xllcenter") + (xCenter ? 0f : cell * 0.5f);   // corner ⇒ the first cell's centre
            float y0 = (float)Need("yllcorner", "yllcenter") + (yCenter ? 0f : cell * 0.5f);
            bool hasNoData = header.TryGetValue("NODATA_value", out double nd);

            if (tokens.Length - i != cols * rows)
                throw new ArgumentException($"Height grid {where}: {cols}×{rows} needs {cols * rows} heights (found {tokens.Length - i}).");
            var z = new float[cols * rows];
            for (int row = 0; row < rows; row++)          // the file's first row is the NORTH edge
                for (int c = 0; c < cols; c++)
                {
                    double v = double.Parse(tokens[i++], NumberStyles.Float, CultureInfo.InvariantCulture);
                    z[(rows - 1 - row) * cols + c] = hasNoData && v == nd ? noData : (float)v;
                }
            return new TerrainHeightGrid(new Vector2(x0, y0), cell, cols, rows, z);
        }

        private static bool IsNumber(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
