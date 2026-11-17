using System;
using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ CE-10003 (air combat AC-8, RV-10; docs/designs/air-attack/DESIGN_Air_Combat.md §3) — THE WEAPON EMPLOYMENT ZONE of a motor missile
    /// (AC-6's model: boost from the launcher's speed to <see cref="MissileDto.SpeedMps"/> over the burn, then coast losing
    /// <see cref="MissileDto.DragPerS"/> per s, defeated below <see cref="MissileDto.MinSpeedMps"/>), stepped in one dimension along the line of
    /// sight: the missile's distance flown D(t) against the target's opening along that line. <b>Max range</b> = max over t of D(t) − v_open·t
    /// with the target's PRESENT opening speed (its present aspect); <b>no-escape range</b> = the same with the target running straight away at
    /// its full speed. ⭐ Public and static so the fighter's brain (<c>AirCombatNodes</c>) and the HUD (ui, <c>FighterHud</c>) read ONE zone.
    /// Allocation-free (a fixed step loop; called at the 2 Hz decision).
    /// </summary>
    public static class EmploymentZone
    {
        /// <summary>The step (s) and the longest flight considered (s).</summary>
        public const float StepS = 0.05f, MaxFlightS = 60f;
        /// <summary>No missile inside this (m): its fuze arms and its seeker can follow.</summary>
        public const float MinRangeM = 500f;

        /// <summary>The farthest launch range (m) at which <paramref name="m"/>, launched at <paramref name="launcherSpeed"/>, still reaches a
        /// target OPENING at <paramref name="openingMps"/> (negative = closing) before it is defeated. 0 for a missile with no motor model.</summary>
        public static float MaxRange(MissileDto m, float launcherSpeed, float openingMps)
        {
            if (m == null || m.SpeedMps <= 0f) return 0f;
            float v = MathF.Max(launcherSpeed, 30f), d = 0f, best = 0f;
            float burn = m.BurnSeconds, boost = burn > 0f ? MathF.Max(0f, m.SpeedMps - v) / burn : 0f;
            bool motor = m.MaxG > 0f;
            if (!motor) v = m.SpeedMps;
            for (float t = StepS; t <= MaxFlightS; t += StepS)
            {
                if (motor)
                {
                    if (t <= burn) v = MathF.Min(m.SpeedMps, v + boost * StepS);
                    else v -= m.DragPerS * StepS;
                    if (v < m.MinSpeedMps) break;   // defeated: the missile can reach no further
                }
                d += v * StepS;
                float reach = d - openingMps * t;
                if (reach > best) best = reach;
            }
            return best;
        }

        /// <summary>The zone against a target at <paramref name="target"/> moving <paramref name="targetVel"/>, seen from the launcher:
        /// (max range at its present aspect, the no-escape range — it turns tail and runs at its full speed). Metres.</summary>
        public static (float Max, float NoEscape) Of(MissileDto m, Vector3 shooter, Vector3 shooterVel, Vector3 target, Vector3 targetVel)
        {
            var los = target - shooter;
            float r = los.Length();
            var u = r > 1e-3f ? los / r : Vector3.UnitX;
            float launch = shooterVel.Length();
            return (MaxRange(m, launch, Vector3.Dot(targetVel, u)), MaxRange(m, launch, targetVel.Length()));
        }

        /// <summary>True when a shot at range <paramref name="range"/> lies inside the zone's <paramref name="fraction"/> of the max range (a
        /// Veteran's 0.8), or — <paramref name="noEscape"/> — inside the no-escape range (an Ace); never inside <see cref="MinRangeM"/>.</summary>
        public static bool Inside(MissileDto m, Vector3 shooter, Vector3 shooterVel, Vector3 target, Vector3 targetVel, float fraction, bool noEscape)
        {
            float range = Vector3.Distance(shooter, target);
            if (range < MinRangeM) return false;
            var (max, ne) = Of(m, shooter, shooterVel, target, targetVel);
            return range <= (noEscape ? MathF.Max(ne, MinRangeM) : max * fraction);
        }
    }
}
