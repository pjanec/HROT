using System;
using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Phase 4 stub navmesh provider.
    /// IsReachable: always true. TryGetPathDistance: returns Euclidean distance.
    /// GetRandomPointsInRadius: returns a small fixed grid of sample points.
    /// </summary>
    public sealed class StubNavmeshProvider : INavmeshProvider
    {
        /// <inheritdoc/>
        public bool IsReachable(Vector2 from, Vector2 to) => true;

        /// <inheritdoc/>
        public bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist)
        {
            pathDist = Vector2.Distance(from, to);
            return true;
        }

        /// <inheritdoc/>
        public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results)
        {
            // Stub: return a 3x3 grid of sample points within the radius.
            int count = 0;
            float step = radius / 2f;
            for (float dx = -step; dx <= step && count < results.Length; dx += step)
            {
                for (float dy = -step; dy <= step && count < results.Length; dy += step)
                {
                    var p = new Vector2(center.X + dx, center.Y + dy);
                    if (Vector2.Distance(center, p) <= radius)
                        results[count++] = p;
                }
            }
            return count;
        }
    }
}
