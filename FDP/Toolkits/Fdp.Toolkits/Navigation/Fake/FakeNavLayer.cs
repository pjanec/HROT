using System;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// A single agent-traversal layer in the fake navmesh.
    /// </summary>
    public sealed class FakeNavLayer
    {
        /// <summary>Layer bit used in <c>layerMask</c> queries.</summary>
        public uint        Layer;

        public NavPolygon[]  Polygons     = Array.Empty<NavPolygon>();

        /// <summary>
        /// Adjacency list indexed by position of the polygon in <see cref="Polygons"/>.
        /// <c>Adjacency[i]</c> contains the indices (into <see cref="Polygons"/>) of polygons
        /// reachable from <c>Polygons[i]</c> by normal walking.
        /// </summary>
        public int[][]       Adjacency    = Array.Empty<int[]>();

        public OffMeshLink[] OffMeshLinks = Array.Empty<OffMeshLink>();

        /// <summary>
        /// Monotone version counter. Incremented whenever polygon walkability or link
        /// connectivity changes (used by <see cref="FakeNavmeshProvider.QueryVersion"/>).
        /// </summary>
        public uint          Version      = 1u;
    }
}
