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

        /// <summary>Hits within this distance of either end of a segment are the end touching the ground, not the ground in the way.</summary>
        public const float EndTolerance = 0.05f;

        /// <summary>
        /// ⭐ CE-1034 H2 (TH-D) — the ground trace: whether the segment <paramref name="a"/>→<paramref name="b"/> passes BELOW the ground
        /// somewhere strictly between its ends (a hill in the way). Walks only the cells the segment's shadow crosses (Amanatides–Woo)
        /// and tests their two triangles — allocation-free, no memory beyond the grid. <paramref name="t"/> is where it first enters the
        /// ground (0..1 along the segment); <paramref name="crest"/> is the highest ground sample the walk passed (the hill's top, for
        /// a blast's diffraction shadow). An end UNDER the ground is a malformed query, not an occluder — it reports no crossing.
        /// </summary>
        public bool Crosses(Vector3 a, Vector3 b, out float t, out float crest) => Crosses(a, b, wholeCrest: true, out t, out crest);

        /// <summary>The yes/no form for sight: stops at the first cell the line dips under (no crest).</summary>
        public bool Crosses(Vector3 a, Vector3 b) => Crosses(a, b, wholeCrest: false, out _, out _);

        private bool Crosses(Vector3 a, Vector3 b, bool wholeCrest, out float t, out float crest)
        {
            t = 1f; crest = float.NegativeInfinity;
            if (a.Z < Sample(a.X, a.Y) - EndTolerance || b.Z < Sample(b.X, b.Y) - EndTolerance) return false;
            var d = b - a;
            float length = d.Length();
            if (length < 1e-4f) return false;
            float eps = EndTolerance / length;

            // clip the segment's shadow to the grid (Liang–Barsky); outside it the edge holds, which no line above both ends dips under
            float t0 = 0f, t1 = 1f;
            var max = Max;
            if (!Clip(-d.X, a.X - Origin.X, ref t0, ref t1) || !Clip(d.X, max.X - a.X, ref t0, ref t1)
                || !Clip(-d.Y, a.Y - Origin.Y, ref t0, ref t1) || !Clip(d.Y, max.Y - a.Y, ref t0, ref t1)) return false;

            float gx = (a.X + d.X * t0 - Origin.X) / CellSize, gy = (a.Y + d.Y * t0 - Origin.Y) / CellSize;
            float ex = (a.X + d.X * t1 - Origin.X) / CellSize, ey = (a.Y + d.Y * t1 - Origin.Y) / CellSize;
            int c = Math.Clamp((int)gx, 0, Cols - 2), r = Math.Clamp((int)gy, 0, Rows - 2);
            int cEnd = Math.Clamp((int)ex, 0, Cols - 2), rEnd = Math.Clamp((int)ey, 0, Rows - 2);
            float dx = ex - gx, dy = ey - gy;
            int stepC = dx > 0 ? 1 : -1, stepR = dy > 0 ? 1 : -1;
            float tDeltaX = dx != 0 ? MathF.Abs(1f / dx) : float.PositiveInfinity;
            float tDeltaY = dy != 0 ? MathF.Abs(1f / dy) : float.PositiveInfinity;
            float tMaxX = dx != 0 ? (stepC > 0 ? c + 1 - gx : gx - c) * tDeltaX : float.PositiveInfinity;
            float tMaxY = dy != 0 ? (stepR > 0 ? r + 1 - gy : gy - r) * tDeltaY : float.PositiveInfinity;
            bool hit = false;
            for (int guard = Cols + Rows + 2; guard > 0; guard--)
            {
                var p00 = Corner(c, r); var p10 = Corner(c + 1, r); var p01 = Corner(c, r + 1); var p11 = Corner(c + 1, r + 1);
                crest = MathF.Max(crest, MathF.Max(MathF.Max(p00.Z, p10.Z), MathF.Max(p01.Z, p11.Z)));
                if (!hit)
                {
                    if (SegmentTriangle(a, d, p00, p10, p11, out float h) && h > eps && h < 1f - eps) { t = MathF.Min(t, h); hit = true; }
                    if (SegmentTriangle(a, d, p00, p11, p01, out h) && h > eps && h < 1f - eps) { t = MathF.Min(t, h); hit = true; }
                    // the first cell along the line that the segment dips under — the cells are walked in order; the crest needs the rest
                    if (hit && !wholeCrest) return true;
                }
                if (c == cEnd && r == rEnd) break;
                if (tMaxX < tMaxY) { tMaxX += tDeltaX; c += stepC; if (c < 0 || c > Cols - 2) break; }
                else { tMaxY += tDeltaY; r += stepR; if (r < 0 || r > Rows - 2) break; }
            }
            return hit;
        }

        private Vector3 Corner(int c, int r) => new(Origin.X + c * CellSize, Origin.Y + r * CellSize, this[c, r]);

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (MathF.Abs(p) < 1e-12f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        /// <summary>Möller–Trumbore, both faces: where along <c>o + t·d</c> (t ∈ [0, 1]) the segment meets the triangle.</summary>
        private static bool SegmentTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            var e1 = v1 - v0; var e2 = v2 - v0;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            var s = o - v0;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t >= 0f && t <= 1f;
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
