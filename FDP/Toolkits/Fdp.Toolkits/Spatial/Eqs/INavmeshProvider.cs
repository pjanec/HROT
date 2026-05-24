using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Navmesh query interface consumed by the Muscle tier.
    /// Phase 4 uses StubNavmeshProvider (Euclidean distance).
    /// Phase 4+ will replace with DotRecast integration (separate workstream).
    /// </summary>
    [ComponentId(GlobalComponentIds.INavmeshProvider)]
    public interface INavmeshProvider
    {
        /// <summary>Returns true if a navmesh path exists between the two positions.</summary>
        bool IsReachable(Vector2 from, Vector2 to);

        /// <summary>
        /// Returns true and writes the path distance into <paramref name="pathDist"/>
        /// if a path exists. Returns false if the target is unreachable.
        /// </summary>
        bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist);

        /// <summary>
        /// Samples random reachable points within radius of center.
        /// Returns the number of points written to <paramref name="results"/>.
        /// </summary>
        int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results);
    }
}
