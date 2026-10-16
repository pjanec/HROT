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
            float elGoal = ElevationGoal(elevation, t);
            float azDelta = AzimuthDelta(pose, azimuth, t, out float azNow);

            return new TurretPose
            {
                Azimuth = Wrap(azNow + Math.Clamp(azDelta, -maxAz, maxAz)),
                Elevation = pose.Elevation + Math.Clamp(elGoal - pose.Elevation, -maxEl, maxEl),
            };
        }

        /// <summary>
        /// ⭐ <c>CE-1109</c> (DESIGN_Indirect_Fire FM-4) — the seconds <see cref="Step"/> needs from <paramref name="pose"/> to be on
        /// (<paramref name="azimuth"/>, <paramref name="elevation"/>): <see cref="Step"/>'s own arithmetic in closed form — both axes slew
        /// together, so the slower axis decides; the same short way round / inside-the-arc rule; done once within
        /// <see cref="OnTargetTolerance"/>, as <see cref="OnTarget"/> reads it. An axis already there costs 0; one that must move at a rate
        /// of 0 never arrives (+∞).
        /// </summary>
        public static float SecondsTo(TurretPose pose, float azimuth, float elevation, TurretDto t)
        {
            float az = AxisSeconds(MathF.Abs(AzimuthDelta(pose, azimuth, t, out _)), t.TraverseRateDegPerSec);
            float el = AxisSeconds(MathF.Abs(ElevationGoal(elevation, t) - pose.Elevation), t.ElevationRateDegPerSec);
            return MathF.Max(az, el);
        }

        private static float AxisSeconds(float delta, float rateDegPerSec)
            => delta <= OnTargetTolerance ? 0f : rateDegPerSec <= 0f ? float.PositiveInfinity : (delta - OnTargetTolerance) / (rateDegPerSec * Deg);

        private static float ElevationGoal(float elevation, TurretDto t) => Math.Clamp(elevation, t.ElevationMinDeg * Deg, t.ElevationMaxDeg * Deg);

        /// <summary>The azimuth still to turn from <paramref name="pose"/>: the short way round all round, else inside the arc (never
        /// through its dead zone). <paramref name="azNow"/> is the pose's azimuth as the slew starts from it.</summary>
        private static float AzimuthDelta(TurretPose pose, float azimuth, TurretDto t, out float azNow)
        {
            if (IsFullCircle(t))
            {
                azNow = Wrap(pose.Azimuth);
                return Wrap(azimuth - azNow);            // the short way round
            }
            float lo = t.TraverseMinDeg * Deg, hi = t.TraverseMaxDeg * Deg;
            azNow = Math.Clamp(Wrap(pose.Azimuth), lo, hi);
            return Math.Clamp(Wrap(azimuth), lo, hi) - azNow;   // inside the arc: never through the dead zone
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
