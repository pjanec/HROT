using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Core
{
    /// <summary>World position (meters) and orientation. Present on every entity with a spatial location.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SimTransform)]
    public struct SimTransform
    {
        // Flat-Earth Cartesian (meters)
        // Right handed
        //   X = east
        //   Y = north
        //   Z = up
        public Vector3    Position;

		// World-space orientation
		// rotation order: yaw-pitch-roll (first around Z, then Y, then X in positive sense)
		//   yaw: 0=X axis direction (east), +90=Y axis direction (north)
		//   pitch: 0=horizontal, +90=straight down (-Z direction)
		//   roll: 0=level, +90=right wing down (clockwise when looking in direction of travel)
		public Quaternion Rotation;
	}

    /// <summary>Linear and angular velocity. Present on every moving entity.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SimVelocity)]
    public struct SimVelocity
    {
		// linear velocity in world coordinates (m/s) [x, y, z]
		public Vector3 Linear;

		// angular velocity in world coordinates (rad/s) [roll, pitch, yaw]
		public Vector3 Angular;
    }
}