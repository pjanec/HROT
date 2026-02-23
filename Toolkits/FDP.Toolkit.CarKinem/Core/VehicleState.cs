using System.Numerics;
using System.Runtime.InteropServices;

namespace CarKinem.Core
{
    /// <summary>
    /// Per-vehicle physics state (double-buffered by ECS).
    /// Uses bicycle kinematic model.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VehicleState
    {
        public float Speed;         // Scalar forward speed (m/s, >= 0)
        public float SteerAngle;    // Current wheel angle (radians)
        public float Accel;         // Longitudinal acceleration (m/s²)
        
        // Metadata
        public int CurrentLaneIndex; // For lane-aware logic
    }
}
