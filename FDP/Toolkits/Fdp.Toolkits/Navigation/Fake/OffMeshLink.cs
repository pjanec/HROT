using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// A directional link connecting two polygons that requires a special traversal action
    /// (e.g., jumping, climbing, entering a door).
    /// </summary>
    public sealed class OffMeshLink
    {
        public int           FromPolygonId;
        public int           ToPolygonId;
        /// <summary>Start position of the link (near FromPolygon).</summary>
        public Vector3       StartPos;
        /// <summary>End position of the link (near ToPolygon).</summary>
        public Vector3       EndPos;
        public TraversalKind Kind = TraversalKind.Jump;
        /// <summary>Extra movement cost for using this link.</summary>
        public float         Cost = 1f;
    }
}
