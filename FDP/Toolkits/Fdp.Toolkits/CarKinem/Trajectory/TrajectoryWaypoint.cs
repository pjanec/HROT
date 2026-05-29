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
    }
}
