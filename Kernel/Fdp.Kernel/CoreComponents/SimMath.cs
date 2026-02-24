using System;
using System.Numerics;

namespace Fdp.Kernel
{
    /// <summary>
    /// Math helpers using the FDP world coordinate convention:
    /// right-handed, X=east, Y=north, Z=up.
    /// Yaw   = rotation around Z (0 = east, +90° = north).
    /// Pitch = rotation around Y (0 = horizontal, +90° = straight down).
    /// Roll  = rotation around X (0 = level, +90° = right wing down).
    /// </summary>
    public static class SimMath
    {
        /// <summary>Construct a rotation quaternion from our yaw/pitch/roll convention (radians).</summary>
        public static Quaternion FromYawPitchRoll(float yawRad, float pitchRad, float rollRad)
        {
            // Apply Z-Y-X (yaw, then pitch, then roll) in our coordinate system.
            return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitY, pitchRad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rollRad);
        }

        /// <summary>Convenience: yaw-only rotation (most common case for ground vehicles).</summary>
        public static Quaternion FromYaw(float yawRad) => FromYawPitchRoll(yawRad, 0f, 0f);

        /// <summary>
        /// Extracts the yaw angle (rotation around Z) from a rotation quaternion, in radians.
        /// Returns the angle in [-π, +π].
        /// </summary>
        public static float ExtractYaw(Quaternion q)
        {
            // Transform the east-facing unit vector (UnitX) and measure its XY-plane angle.
            var forward = Vector3.Transform(Vector3.UnitX, q);
            return MathF.Atan2(forward.Y, forward.X);
        }

        // Named compass directions — eliminates magic numbers at call sites:
        public static readonly Quaternion FacingEast  = FromYaw(0f);
        public static readonly Quaternion FacingNorth = FromYaw(MathF.PI / 2f);
        public static readonly Quaternion FacingWest  = FromYaw(MathF.PI);
        public static readonly Quaternion FacingSouth = FromYaw(-MathF.PI / 2f);
    }
}
