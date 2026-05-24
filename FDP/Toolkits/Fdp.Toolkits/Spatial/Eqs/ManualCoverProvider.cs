using System;
using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Designer-placed cover provider backed by a flat array.
    /// Linear scan is acceptable at this stage (maps are small).
    /// Registered as a managed singleton via repo.SetSingletonManaged&lt;ICoverProvider&gt;.
    /// </summary>
    public sealed class ManualCoverProvider : ICoverProvider
    {
        private readonly CoverPoint[] _points;

        public ManualCoverProvider(CoverPoint[] points)
        {
            _points = points;
        }

        /// <inheritdoc/>
        public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
        {
            float radiusSq = radius * radius;
            int count = 0;
            foreach (var point in _points)
            {
                if (count >= results.Length) break;
                float dx = point.PositionX - center.X;
                float dy = point.PositionY - center.Y;
                if (dx * dx + dy * dy <= radiusSq)
                    results[count++] = point;
            }
            return count;
        }
    }
}
