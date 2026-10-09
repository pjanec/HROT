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

        /// <summary>
        /// ⭐ Stage 7a — the points of one <paramref name="kind"/> within <paramref name="radius"/> (the nearest when more match than
        /// fit). A provider that knows only cover answers <see cref="CoverKind.Cover"/> with every point and nothing else.
        /// 📄 docs/DESIGN_Building_Interiors.md §3l C5.
        /// </summary>
        int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results, CoverKind kind)
            => kind == CoverKind.Cover ? GetCoverPointsInRadius(center, radius, results) : 0;
    }
}
