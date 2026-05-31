using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Axis-aligned bounding box in 2-D world space (X/Y ground plane).
    /// Used by <see cref="IFakeNavmeshProviderTestApi.BumpVersion"/> to scope
    /// version bumps to a spatial region.
    /// </summary>
    public struct BoundingBox2D
    {
        public Vector2 Min;
        public Vector2 Max;

        public BoundingBox2D(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Returns true if <paramref name="p"/> is inside or on the boundary.</summary>
        public bool Contains(Vector2 p)
            => p.X >= Min.X && p.X <= Max.X
            && p.Y >= Min.Y && p.Y <= Max.Y;
    }
}
