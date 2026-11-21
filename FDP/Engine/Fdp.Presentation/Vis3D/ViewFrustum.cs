using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ CE-4388 (render under load, R-2) — THE view frustum of the 3-D pass being drawn, shared: the chunked terrain (<c>TerrainChunks3D</c>, where it was born) and the
    /// unit bodies (<c>EntityBodyLayer3D</c>) cull against the same planes. Read from rlgl's CURRENT matrices, so it is the camera of THIS pass (the window or a film).
    /// raylib space throughout (HROT (x, y, z) → raylib (x, z, −y)). ⛔ No allocation: the caller owns the six-plane array.
    /// </summary>
    public static class ViewFrustum
    {
        /// <summary>The eye (raylib space) from a modelview matrix.</summary>
        public static bool TryEye(Matrix4x4 view, out Vector3 eye)
        {
            eye = default;
            if (!Matrix4x4.Invert(view, out var inv)) return false;
            eye = new Vector3(inv.M14, inv.M24, inv.M34);
            return float.IsFinite(eye.X) && float.IsFinite(eye.Y) && float.IsFinite(eye.Z);
        }

        /// <summary>The six planes (Gribb–Hartmann on projection × view) into <paramref name="planes"/> (length ≥ 6).</summary>
        public static bool TryPlanes(Matrix4x4 view, Matrix4x4 proj, Vector4[] planes)
        {
            var m = Matrix4x4.Multiply(proj, view);
            var r1 = new Vector4(m.M11, m.M12, m.M13, m.M14);
            var r2 = new Vector4(m.M21, m.M22, m.M23, m.M24);
            var r3 = new Vector4(m.M31, m.M32, m.M33, m.M34);
            var r4 = new Vector4(m.M41, m.M42, m.M43, m.M44);
            planes[0] = r4 + r1; planes[1] = r4 - r1; planes[2] = r4 + r2; planes[3] = r4 - r2; planes[4] = r4 + r3; planes[5] = r4 - r3;
            for (int i = 0; i < 6; i++) if (!float.IsFinite(planes[i].X) || planes[i] == Vector4.Zero) return false;
            return true;
        }

        /// <summary>The planes of the pass being drawn now (rlgl's current modelview and projection).</summary>
        public static bool TryCurrent(Vector4[] planes) => TryPlanes(Rlgl.GetMatrixModelview(), Rlgl.GetMatrixProjection(), planes);

        /// <summary>The box (raylib space) is at least partly on the inner side of every plane — the p-vertex test.</summary>
        public static bool BoxInside(Vector4[] planes, Vector3 min, Vector3 max)
        {
            for (int i = 0; i < 6; i++)
            {
                var p = planes[i];
                float x = p.X >= 0 ? max.X : min.X, y = p.Y >= 0 ? max.Y : min.Y, z = p.Z >= 0 ? max.Z : min.Z;
                if ((p.X * x) + (p.Y * y) + (p.Z * z) + p.W < 0f) return false;
            }
            return true;
        }

        /// <summary>⭐ CE-4557 (docs/DESIGN_Map_3D_Mode.md §6ak D-1) — the pass's screen scale: how many PIXELS a length of metres covers at a point (perspective
        /// or orthographic) — from the projection's vertical scale, the framebuffer's height and the point's clip w. Built once per frame; 0 B.</summary>
        public readonly struct ScreenScale
        {
            private readonly Vector4 _w;          // the row of projection × view that gives a (raylib) point's clip w
            private readonly float _pixelsPerUnit;  // |P.M22| · height / 2: pixels of one unit of ndc-scaled length at w = 1

            public ScreenScale(Vector4 wRow, float pixelsPerUnit) { _w = wRow; _pixelsPerUnit = pixelsPerUnit; }

            /// <summary>False before a pass set it (every size then reads as huge).</summary>
            public bool IsSet => _pixelsPerUnit > 0f;

            /// <summary>The pixels <paramref name="metres"/> cover at HROT point <paramref name="hrot"/> (+∞ at or behind the eye; +∞ when unset).</summary>
            public float PixelsOf(Vector3 hrot, float metres)
            {
                float w = (_w.X * hrot.X) + (_w.Y * hrot.Z) - (_w.Z * hrot.Y) + _w.W;   // raylib (x, z, −y)
                return _pixelsPerUnit <= 0f || w <= 1e-4f ? float.PositiveInfinity : metres * _pixelsPerUnit / w;
            }
        }

        /// <summary>⭐ CE-4557 — the screen scale of <paramref name="view"/> × <paramref name="proj"/> on a framebuffer <paramref name="heightPx"/> tall.</summary>
        public static ScreenScale ScaleOf(Matrix4x4 view, Matrix4x4 proj, int heightPx)
        {
            var m = Matrix4x4.Multiply(proj, view);
            return new ScreenScale(new Vector4(m.M41, m.M42, m.M43, m.M44), MathF.Abs(proj.M22) * heightPx * 0.5f);
        }

        /// <summary>⭐ CE-4557 — the screen scale of the pass being drawn now (rlgl's current matrices and framebuffer — the window's or a film's).</summary>
        public static ScreenScale CurrentScale() => ScaleOf(Rlgl.GetMatrixModelview(), Rlgl.GetMatrixProjection(), Rlgl.GetFramebufferHeight());

        /// <summary>A cube of half-size <paramref name="radius"/> round a HROT-space point is in view.</summary>
        public static bool AroundInside(Vector4[] planes, Vector3 hrot, float radius)
        {
            var c = new Vector3(hrot.X, hrot.Z, -hrot.Y);
            var r = new Vector3(radius);
            return BoxInside(planes, c - r, c + r);
        }
    }
}
