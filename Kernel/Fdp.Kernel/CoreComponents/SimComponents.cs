using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>World position (meters) and orientation. Present on every entity with a spatial location.</summary>
    [StructLayout(LayoutKind.Sequential)]
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
    public struct SimVelocity
    {
		// linear velocity in world coordinates (m/s) [x, y, z]
		public Vector3 Linear;

		// angular velocity in world coordinates (rad/s) [roll, pitch, yaw]
		public Vector3 Angular;
    }

    /// <summary>
    /// Read-only health mirror written by <c>FDP.Toolkit.Combat.Systems.DamageSystem</c>
    /// each frame after damage is applied.  Lives in <c>Fdp.Kernel</c> so that
    /// <c>FDP.Toolkit.Behavior</c> systems (e.g. <c>MissionDirectorSystem</c>) can
    /// react to health changes without creating a circular assembly dependency
    /// (Combat already references Behavior).
    /// </summary>
    /// <remarks>
    /// <b>DEBT-033:</b> Presence on an entity is optional.  Add this component at
    /// spawn time alongside <c>Health</c> if you want behaviour-level reactions
    /// to health state.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct HealthData
    {
        /// <summary>Current hit-points (0 = destroyed).</summary>
        public float Current;

        /// <summary>Maximum hit-points at full health.</summary>
        public float Max;

        /// <summary>Current / Max, clamped to [0, 1].  Returns 0 when <see cref="Max"/> is zero.</summary>
        public float Fraction => Max > 0f ? Current / Max : 0f;
    }}