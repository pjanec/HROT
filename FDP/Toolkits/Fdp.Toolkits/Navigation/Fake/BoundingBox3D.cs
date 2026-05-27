using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Axis-aligned bounding box in 3-D world space.
    /// Used by <see cref="IVolumetricPathProvider.QueryVersion(BoundingBox3D)"/> to scope
    /// version queries to a spatial region.
    /// </summary>
    public struct BoundingBox3D
    {
        public Vector3 Min;
        public Vector3 Max;

        public BoundingBox3D(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Returns true if <paramref name="p"/> is strictly inside or on the boundary.</summary>
        public bool Contains(Vector3 p)
            => p.X >= Min.X && p.X <= Max.X
            && p.Y >= Min.Y && p.Y <= Max.Y
            && p.Z >= Min.Z && p.Z <= Max.Z;

        /// <summary>
        /// Returns true if the line segment from <paramref name="a"/> to <paramref name="b"/>
        /// intersects or lies inside this box (slab test).
        /// </summary>
        public bool IntersectsLine(Vector3 a, Vector3 b)
        {
            if (Contains(a) || Contains(b)) return true;

            float tMin = 0f, tMax = 1f;
            float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;

            if (!SlabTest(a.X, dx, Min.X, Max.X, ref tMin, ref tMax)) return false;
            if (!SlabTest(a.Y, dy, Min.Y, Max.Y, ref tMin, ref tMax)) return false;
            if (!SlabTest(a.Z, dz, Min.Z, Max.Z, ref tMin, ref tMax)) return false;
            return tMin <= tMax;
        }

        private static bool SlabTest(float origin, float dir, float lo, float hi,
                                     ref float tMin, ref float tMax)
        {
            if (System.MathF.Abs(dir) < 1e-8f)
            {
                // Ray is parallel to slab; check if origin is within slab.
                return origin >= lo && origin <= hi;
            }
            float t1 = (lo - origin) / dir;
            float t2 = (hi - origin) / dir;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tMin = System.MathF.Max(tMin, t1);
            tMax = System.MathF.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
