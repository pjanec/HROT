using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Core
{
    /// <summary>
    /// Per-vehicle physics state (double-buffered by ECS).
    /// Uses bicycle kinematic model.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.VehicleState)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct VehicleState
    {
        public float Speed;         // Scalar forward speed (m/s, >= 0)
        public float SteerAngle;    // Current wheel angle (radians)
        public float Accel;         // Longitudinal acceleration (m/s²)
        
        // Metadata
        public int CurrentLaneIndex; // For lane-aware logic
    }
}
