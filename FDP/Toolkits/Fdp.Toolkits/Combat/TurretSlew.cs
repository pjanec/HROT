using System;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ WL-2 (docs/DESIGN_Weapon_Layers.md §5c, W-8, W-9) — a turret's limits and slew, pure arithmetic on <see cref="TurretPose"/>
    /// (radians) against its <see cref="TurretDto"/> (degrees). A full-circle traverse (−180…180) turns the short way round; a limited
    /// arc slews inside it and never through its dead zone.
    /// </summary>
    public static class TurretSlew
    {
        private const float Deg = MathF.PI / 180f;

        /// <summary>Both angles within this of the aim count as on target (0.5°).</summary>
        public const float OnTargetTolerance = 0.5f * Deg;

        /// <summary>True when the turret traverses all round.</summary>
        public static bool IsFullCircle(TurretDto t) => t.TraverseMaxDeg - t.TraverseMinDeg >= 359.9f;

        /// <summary>True when the turret can point at (<paramref name="azimuth"/>, <paramref name="elevation"/>).</summary>
        public static bool InArc(TurretDto t, float azimuth, float elevation)
        {
            if (elevation < t.ElevationMinDeg * Deg - 1e-5f || elevation > t.ElevationMaxDeg * Deg + 1e-5f) return false;
            if (IsFullCircle(t)) return true;
            float az = Wrap(azimuth);
            return az >= t.TraverseMinDeg * Deg - 1e-5f && az <= t.TraverseMaxDeg * Deg + 1e-5f;
        }

        /// <summary>
        /// One step of <paramref name="dt"/> seconds from <paramref name="pose"/> toward (<paramref name="azimuth"/>,
        /// <paramref name="elevation"/>) at the turret's rates, the result kept inside its limits.
        /// </summary>
        public static TurretPose Step(TurretPose pose, float azimuth, float elevation, TurretDto t, float dt)
        {
            float maxAz = MathF.Max(0f, t.TraverseRateDegPerSec) * Deg * dt;
            float maxEl = MathF.Max(0f, t.ElevationRateDegPerSec) * Deg * dt;
            float elGoal = Math.Clamp(elevation, t.ElevationMinDeg * Deg, t.ElevationMaxDeg * Deg);

            float azNow, azDelta;
            if (IsFullCircle(t))
            {
                azNow = Wrap(pose.Azimuth);
                azDelta = Wrap(azimuth - azNow);            // the short way round
            }
            else
            {
                float lo = t.TraverseMinDeg * Deg, hi = t.TraverseMaxDeg * Deg;
                azNow = Math.Clamp(Wrap(pose.Azimuth), lo, hi);
                azDelta = Math.Clamp(Wrap(azimuth), lo, hi) - azNow;   // inside the arc: never through the dead zone
            }

            return new TurretPose
            {
                Azimuth = Wrap(azNow + Math.Clamp(azDelta, -maxAz, maxAz)),
                Elevation = pose.Elevation + Math.Clamp(elGoal - pose.Elevation, -maxEl, maxEl),
            };
        }

        /// <summary>True when <paramref name="pose"/> is within <see cref="OnTargetTolerance"/> of the aim on both angles.</summary>
        public static bool OnTarget(TurretPose pose, float azimuth, float elevation)
            => MathF.Abs(Wrap(azimuth - pose.Azimuth)) <= OnTargetTolerance && MathF.Abs(elevation - pose.Elevation) <= OnTargetTolerance;

        /// <summary>An angle into (−π, π].</summary>
        public static float Wrap(float a)
        {
            a %= 2f * MathF.PI;
            if (a <= -MathF.PI) a += 2f * MathF.PI;
            else if (a > MathF.PI) a -= 2f * MathF.PI;
            return a;
        }
    }
}
