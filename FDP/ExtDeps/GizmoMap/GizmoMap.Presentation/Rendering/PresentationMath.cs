using System.Numerics;

namespace GizmoMap.Presentation
{
    public static class PresentationMath
    {
        /// <summary>
        /// Constructs a rotation quaternion using the FDP coordinate convention.
        /// WARNING: This intentionally duplicates Fdp.Core.SimMath.FromYawPitchRoll.
        /// GizmoMap.Presentation must not reference Fdp.Core.
        /// If the core rotation order (Z-Y-X) or handedness changes, keep this in sync.
        /// </summary>
        public static Quaternion FromYawPitchRoll(float yawRad, float pitchRad, float rollRad)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitY, pitchRad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rollRad);
        }
    }
}
