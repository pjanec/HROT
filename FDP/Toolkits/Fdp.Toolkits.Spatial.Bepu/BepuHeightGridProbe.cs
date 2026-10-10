using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities.Memory;

namespace Fdp.Toolkit.Spatial.Bepu
{
    /// <summary>
    /// ⭐ CE-1035 Q0 — a SPIKE probe, not production: a height grid (<c>DESIGN_Terrain_Height.md</c> TH-B) as a Bepu static
    /// <see cref="Mesh"/>, to measure what a ground trace (TH-D) costs in memory and time against a plain triangle scan.
    /// Each cell is split along the same diagonal (lower-left → upper-right) everywhere, so the mesh and
    /// <see cref="HeightAt"/> describe one surface.
    /// </summary>
    public sealed class BepuHeightGridProbe : IDisposable
    {
        private readonly BufferPool _pool = new();
        private Mesh _mesh;
        private readonly Vector2 _origin;
        private readonly float _cell;
        private readonly int _cols, _rows;   // vertices per row / per column
        private readonly float[] _z;          // row-major, _cols × _rows
        private readonly float _top;

        public int TriangleCount { get; }

        public BepuHeightGridProbe(Vector2 origin, float cellSize, int cols, int rows, float[] z)
        {
            _origin = origin; _cell = cellSize; _cols = cols; _rows = rows; _z = z;
            _top = z.Max() + 1f;
            TriangleCount = (cols - 1) * (rows - 1) * 2;
            _pool.Take<Triangle>(TriangleCount, out var tris);
            int t = 0;
            for (int r = 0; r + 1 < rows; r++)
                for (int c = 0; c + 1 < cols; c++)
                {
                    var p00 = V(c, r); var p10 = V(c + 1, r); var p01 = V(c, r + 1); var p11 = V(c + 1, r + 1);
                    tris[t++] = new Triangle(p00, p10, p11);
                    tris[t++] = new Triangle(p00, p11, p01);
                }
            _mesh = Mesh.CreateWithSweepBuild(tris, Vector3.One, _pool);
        }

        private Vector3 V(int c, int r) => new(_origin.X + c * _cell, _origin.Y + r * _cell, _z[r * _cols + c]);

        /// <summary>The surface height at (x, y) by plain arithmetic — O(1), the reference.</summary>
        public float HeightAt(float x, float y)
        {
            float fx = (x - _origin.X) / _cell, fy = (y - _origin.Y) / _cell;
            int c = Math.Clamp((int)MathF.Floor(fx), 0, _cols - 2), r = Math.Clamp((int)MathF.Floor(fy), 0, _rows - 2);
            float u = fx - c, v = fy - r;
            float z00 = _z[r * _cols + c], z10 = _z[r * _cols + c + 1], z01 = _z[(r + 1) * _cols + c], z11 = _z[(r + 1) * _cols + c + 1];
            return u >= v ? z00 + u * (z10 - z00) + v * (z11 - z10)    // triangle (p00, p10, p11)
                          : z00 + v * (z01 - z00) + u * (z11 - z01);   // triangle (p00, p11, p01)
        }

        /// <summary>The surface height at (x, y) by casting a ray straight down at the Bepu mesh.</summary>
        public float HeightByRay(float x, float y)
        {
            var hit = new FirstHit();
            var ray = new RayData { Origin = new Vector3(x, y, _top), Direction = new Vector3(0, 0, -1) };
            float maxT = _top + 10_000f;
            var pose = RigidPose.Identity;
            _mesh.RayTest(in pose, in ray, ref maxT, _pool, ref hit);
            return hit.Hit ? _top - hit.T : float.NaN;
        }

        /// <summary>True when the segment passes below the surface somewhere (the ground trace, TH-D) — Bepu mesh ray.</summary>
        public bool SegmentHitsGround(Vector3 a, Vector3 b)
        {
            var hit = new FirstHit();
            var ray = new RayData { Origin = a, Direction = b - a };
            float maxT = 1f;
            var pose = RigidPose.Identity;
            _mesh.RayTest(in pose, in ray, ref maxT, _pool, ref hit);
            return hit.Hit;
        }

        /// <summary>The same question by testing every triangle under the segment's box — the scan a hand-written trace starts from.</summary>
        public bool SegmentHitsGroundByScan(Vector3 a, Vector3 b)
        {
            var d = b - a;
            float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X), minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);
            int c0 = Math.Clamp((int)((minX - _origin.X) / _cell), 0, _cols - 2), c1 = Math.Clamp((int)((maxX - _origin.X) / _cell), 0, _cols - 2);
            int r0 = Math.Clamp((int)((minY - _origin.Y) / _cell), 0, _rows - 2), r1 = Math.Clamp((int)((maxY - _origin.Y) / _cell), 0, _rows - 2);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    var p00 = V(c, r); var p10 = V(c + 1, r); var p01 = V(c, r + 1); var p11 = V(c + 1, r + 1);
                    if (Triangle.RayTest(p00, p10, p11, a, d, out float t1, out _) && t1 <= 1f) return true;
                    if (Triangle.RayTest(p00, p11, p01, a, d, out float t2, out _) && t2 <= 1f) return true;
                }
            return false;
        }

        private struct FirstHit : IShapeRayHitHandler
        {
            public bool Hit;
            public float T;
            public bool AllowTest(int childIndex) => true;
            public void OnRayHit(in RayData ray, ref float maximumT, float t, Vector3 normal, int childIndex)
            {
                if (!Hit || t < T) { Hit = true; T = t; }
                maximumT = t;   // keep only nearer hits from here on
            }
        }

        public void Dispose()
        {
            _mesh.Dispose(_pool);
            _pool.Clear();
        }
    }
}
