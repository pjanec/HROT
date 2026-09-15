using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Core
{
    /// <summary>World position (meters) and orientation. Present on every entity with a spatial location.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SimTransform)]
    // ⭐⭐⭐ (0,0,0) is never a correct starting value — an origin flash, a wrong spatial-hash cell
    //   and a bogus first path query — so the CREATOR owns it at birth whatever its role, and NO
    //   role may claim it while promoting someone else's ghost. 📄 BirthCriticalAttribute.
    [BirthCritical]
    // ⭐ …and the real value is authored (SpawnEntityCommand.InitialTransform) or replicated, never
    //   derivable from the template. 📄 PerInstanceValueAttribute.
    [PerInstanceValue]
    // ⭐ A spatially-located entity needs a sim (Muscle) peer to place it on terrain / navmesh before it is
    //   fully live — so the reliable-init barrier waits on the roles that provide that init
    //   (HrotRoleComponentSets.Initialises). Role-agnostic marker; CE-283 piece C §3b. Read ONLY by the
    //   barrier's role-filter, so it changes nothing for non-reliable entities.
    [RequiresPeerInit]
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
    // ⭐ Per-instance (SpawnEntityCommand.InitialVelocity), so the attribute is honest — ⚠ but the
    //   wire writes NetworkVelocity, not SimVelocity, so the derivation's INGRESS intersection
    //   drops it and it produces NO mandatory requirement today. That is the intended outcome:
    //   excluded because nothing ingresses it, NOT by mislabelling the component.
    // ⛔ NOT [BirthCritical] — zero velocity IS a correct starting value.
    [PerInstanceValue]
    public struct SimVelocity
    {
		// linear velocity in world coordinates (m/s) [x, y, z]
		public Vector3 Linear;

		// angular velocity in world coordinates (rad/s) [roll, pitch, yaw]
		public Vector3 Angular;
    }
}