using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Cover database interface consumed by the Muscle tier.
    /// Implementations may be designer-authored (ManualCoverProvider) or
    /// auto-computed from navmesh edges (future stage).
    /// </summary>
    [ComponentId(GlobalComponentIds.ICoverProvider)]
    public interface ICoverProvider
    {
        /// <summary>
        /// Populates <paramref name="results"/> with cover points within <paramref name="radius"/>
        /// of <paramref name="center"/>. Returns the actual number of points written.
        /// </summary>
        int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results);
    }
}
