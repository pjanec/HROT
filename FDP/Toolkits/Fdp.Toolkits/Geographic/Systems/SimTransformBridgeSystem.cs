using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Static math helpers for converting between ECS SimTransform/SimVelocity and
    /// geodetic/compass representations used by network translators.
    /// <para>
    /// The former Execute() / IEcsModuleSystem implementation (SimTransformBridgeSystem
    /// copying SimTransform to GeoTransform every tick) has been removed. Geodetic
    /// conversion now happens on-demand in GeoSpatialEgressTranslator.
    /// </para>
    /// </summary>
    public static class SimTransformBridgeSystem
    {
        /// <summary>
        /// Converts <see cref="SimTransform.Rotation"/> to compass heading degrees [0, 360).
        /// UnitX-forward convention: matches CarKinematicsSystem.
        /// X=East, Y=North. 0=North, 90=East, clockwise.
        /// </summary>
        public static float RotationToHeadingDeg(Quaternion rotation)
        {
            // Degenerate guard: when the nose points nearly straight up/down, the body-forward
            // axis projects to ~zero in the XY (ENU) plane and compass heading is undefined
            // (gimbal lock) → fall back to 0. Non-degenerate headings are unchanged.
            Vector3 fwd = Vector3.Transform(Vector3.UnitX, rotation);
            if (new Vector2(fwd.X, fwd.Y).LengthSquared() < 1e-6f)
                return 0f;

            var (yawDeg, _, _) = SimMath.ToYawPitchRollDeg(rotation);
            return (90f - yawDeg + 360f) % 360f;
        }

        /// <summary>
        /// Extracts pitch and roll from a <see cref="SimTransform.Rotation"/> quaternion.
        /// Uses the same UnitX-forward convention as <see cref="RotationToHeadingDeg"/>.
        /// ENU frame: X=East (body forward), Y=North (body left), Z=Up.
        ///
        /// <b>Pitch</b> (<paramref name="pitchDeg"/>): positive = nose up.
        /// <b>Roll</b> (<paramref name="rollDeg"/>): positive = right wing down.
        /// </summary>
        public static void RotationToPitchRollDeg(Quaternion rotation,
                                                   out float pitchDeg,
                                                   out float rollDeg)
        {
            // SimMath's pitch convention is nose-DOWN-positive (see SimMath docs); this bridge
            // (and the DIS/aerospace wire protocol its consumers feed) uses nose-UP-positive.
            // Negate to convert. Consumers (GeoSpatialEgressTranslator, BdcWorldPosTranslator)
            // write this value straight to the wire, so the sign must be correct here.
            var (_, p, r) = SimMath.ToYawPitchRollDeg(rotation);
            pitchDeg = -p;
            rollDeg = r;
        }

        /// <summary>
        /// Converts a compass heading in degrees [0, 360) back to a <see cref="Quaternion"/>
        /// using the FDP world-coordinate convention (X=East, Y=North, yaw 0=East, +90°=North).
        /// Inverse of <see cref="RotationToHeadingDeg"/>.
        /// </summary>
        /// <param name="headingDeg">Compass heading in degrees (0=North, 90=East, clockwise).</param>
        public static Quaternion HeadingDegToRotation(float headingDeg)
        {
            // heading = (90 - mathYaw_deg + 360) % 360  →  mathYaw_rad = (90 - heading) * π/180
            float mathYawRad = (90f - headingDeg) * (MathF.PI / 180f);
            return SimMath.FromYaw(mathYawRad);
        }

        /// <summary>
        /// Converts a world-space ENU velocity vector to compass azimuth degrees [0, 360).
        /// Falls back to <paramref name="fallback"/> when the speed is negligible.
        /// </summary>
        public static float VelocityToAzimuthDeg(Vector3 linearENU, float fallback)
        {
            Vector2 xy = new Vector2(linearENU.X, linearENU.Y);
            if (xy.LengthSquared() < 1e-4f) return fallback;
            float mathYaw = MathF.Atan2(xy.Y, xy.X);
            return (90f - mathYaw * (180f / MathF.PI) + 360f) % 360f;
        }
    }
}
