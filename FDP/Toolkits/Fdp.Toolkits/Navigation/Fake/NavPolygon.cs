using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// A convex (or simple) polygon in the fake navmesh. Vertices listed in order.
    /// Walkability checks use the (X, Z) plane.
    /// </summary>
    public sealed class NavPolygon
    {
        public int          Id;
        public Vector3[]    Vertices     = System.Array.Empty<Vector3>();
        public SurfaceType  SurfaceType  = SurfaceType.Generic;
        public bool         IsBlocked;

        /// <summary>
        /// Returns the average of all vertex positions (centroid).
        /// </summary>
        public Vector3 Centroid()
        {
            if (Vertices.Length == 0) return Vector3.Zero;
            var sum = Vector3.Zero;
            foreach (var v in Vertices) sum += v;
            return sum / Vertices.Length;
        }
    }
}
