using System.Numerics;
using System.Runtime.InteropServices;

namespace CarKinem.Road
{
    /// <summary>
    /// Road graph node (junction/intersection).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoadNode
    {
        public Vector2 Position;          // World position
        /// <summary>⭐ CE-1034 H5c (TH-L) — the road's height here ABOVE THE GROUND (m; 0 = on the ground, +6 = a ramp top / a deck).
        /// The road's real height is resolved from it and snapped to the surface nearest it (<c>RoadHeights</c>).</summary>
        public float AboveGround;
        public int FirstSegmentIndex;     // Index of first outgoing segment
        public int SegmentCount;          // Number of outgoing segments
    }
}
