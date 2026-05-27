using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// A single waypoint in a planned navigation path.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> Vector3 (12) + byte (1) + byte (1) + 2 pad (2) + float (4) + float (4) = 24 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NavWaypoint
    {
        /// <summary>World-space position of the waypoint (metres, FDP Cartesian).</summary>
        public Vector3 Position { get; init; }          // 12 bytes

        /// <summary>How the agent traverses the edge leading to this waypoint.</summary>
        public TraversalKind Traversal { get; init; }   //  1 byte

        /// <summary>Surface type at this waypoint.</summary>
        public SurfaceType Surface { get; init; }       //  1 byte

        // 2 bytes of explicit padding.
        private readonly byte _pad0;                    //  1 byte
        private readonly byte _pad1;                    //  1 byte

        /// <summary>Time offset from path start to this waypoint (seconds); 0 = unknown.</summary>
        public float TimeOffset { get; init; }          //  4 bytes

        // Reserved for future use (e.g., traversal cost).
        private readonly float _reserved;               //  4 bytes
    }                                                   // = 24 bytes total
}
