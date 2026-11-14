using System.Numerics;
using System.Runtime.InteropServices;

namespace CarKinem.Trajectory
{
    /// <summary>
    /// Custom trajectory waypoint.
    /// Linear interpolation between waypoints.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TrajectoryWaypoint
    {
        public Vector3 Position;      // World position (Sim Z-up). Z carried for fidelity/replication;
                                      // spline curvature + heading are computed on the XY projection (§0.2, P3D-303).
        public Vector2 Tangent;       // Optional 2D tangent for smooth curves (zero for linear)
        public float DesiredSpeed;    // Desired speed at this waypoint (m/s)
        public float CumulativeDistance; // Precomputed distance from start (meters), XY arc length
        // ⭐ Buildings 5d-3 (📄 docs/DESIGN_Building_Interiors.md §3j N4) — how the agent passes THIS waypoint, a
        //   Fdp.Toolkit.Navigation.TraversalKind (0 Walk · 3 Door …) as a byte: the planner marks doorway corners, and the
        //   mover's door system reads the mark to stop and open a closed door. 0 for every path that carries no marks.
        public byte Traversal;
    }
}
