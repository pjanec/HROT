using System;
using System.Numerics;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-2173</c> slice 6c (B13, B16 / AT2, R-2020, R-2021; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7c, §9e) — where to
    /// AIM a straight round so it meets a moving target: the ONE predictor, shared by the air attack (behaviors) and the aim path's lead at
    /// an aircraft (CE-2183, backend). A round leaves at <c>roundSpeed</c> along the aim PLUS the shooter's own velocity (A4: an
    /// air-launched round keeps its carrier's velocity — a ground shooter passes zero), so the intercept is solved in the shooter's frame:
    /// the target closes at (its velocity − the shooter's). Returns the point to aim AT — the target's place after the flight time, shifted
    /// by the shooter's own drift over it. Gravity drop is not here (a direct-fire round; a bomb uses <c>BombBallistics</c>).
    /// </summary>
    public static class TargetLead
    {
        /// <param name="shooter">where the round starts.</param>
        /// <param name="shooterVelocity">the shooter's velocity (the round inherits it; zero on the ground).</param>
        /// <param name="roundSpeed">the round's own speed along the aim (m/s); ≤ 0 ⇒ the target itself.</param>
        /// <param name="target">the target now.</param>
        /// <param name="targetVelocity">the target's velocity.</param>
        public static Vector3 Point(Vector3 shooter, Vector3 shooterVelocity, float roundSpeed, Vector3 target, Vector3 targetVelocity)
        {
            float t = FlightTime(shooter, shooterVelocity, roundSpeed, target, targetVelocity);
            return t > 0f ? target + (targetVelocity - shooterVelocity) * t : target;
        }

        /// <summary>The round's flight time to the intercept (s): the smallest positive root of |D + W·t| = s·t with D the offset to the
        /// target and W its velocity relative to the shooter; 0 when the round can never catch it (or has no speed).</summary>
        public static float FlightTime(Vector3 shooter, Vector3 shooterVelocity, float roundSpeed, Vector3 target, Vector3 targetVelocity)
        {
            if (roundSpeed <= 0f) return 0f;
            var d = target - shooter;
            var w = targetVelocity - shooterVelocity;
            float a = Vector3.Dot(w, w) - roundSpeed * roundSpeed;
            float b = 2f * Vector3.Dot(d, w);
            float c = Vector3.Dot(d, d);
            if (MathF.Abs(a) < 1e-6f) return b < 0f ? -c / b : 0f;   // the target closes at exactly the round's speed
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return 0f;
            float sq = MathF.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
            float t = t1 > 0f && t2 > 0f ? MathF.Min(t1, t2) : MathF.Max(t1, t2);
            return t > 0f ? t : 0f;
        }
    }
}
