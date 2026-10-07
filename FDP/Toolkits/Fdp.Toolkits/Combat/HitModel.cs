using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>AQ85</c> (R-216) — WHETHER A ROUND HITS: a deterministic aim deflection at spawn. The bullet's direction is rotated by
    /// <c>θ = σ · d(k)</c> (σ = <see cref="Sigma"/>, d(k) a fixed golden-ratio sequence over the shooter's shot count) and the
    /// EXISTING physics decides the hit, the near miss and the bystander. No dice: the same scenario gives the same hits every run.
    /// <para>⭐ ONE function family for the shot (<c>FireProcessingSystem</c>) and the AI's estimate (<see cref="HitChance"/> in
    /// <c>WeaponEffectivenessVsTarget</c>) — the R-212 A1 shape, beside <see cref="ArmorModel"/>.</para>
    /// 📄 docs/blueprints/Architect_Question_85_Hit_Chance.md §3 A–F.
    /// </summary>
    public static class HitModel
    {
        /// <summary>Above this speed (m/s) the shooter is MOVING: σ × <see cref="MovingFactor"/>.</summary>
        public const float MovingSpeed = 0.5f;
        public const float MovingFactor = 2f;
        /// <summary>For this long after a hit or near miss the shooter is SUPPRESSED: σ × <see cref="UnderFireFactor"/>.</summary>
        public const double UnderFireSeconds = 5.0;
        public const float UnderFireFactor = 2f;
        public const float ProneFactor = 0.5f;
        public const float CrouchedFactor = 0.75f;
        /// <summary>The target circle the AI assumes when the target has no collider (a soldier, m).</summary>
        public const float DefaultTargetRadius = 0.3f;

        private const double GoldenFraction = 0.6180339887498949;

        /// <summary>
        /// σ (radians, the half-width of a uniform spread) of <paramref name="shooter"/> firing <paramref name="mount"/>: the mount's
        /// <see cref="WeaponMountDto.DispersionMils"/> (0 = exact aim ⇒ 0) × 2 moving × 2 under fire × stance (prone 0.5, crouched 0.75).
        /// ⭐ The stance is the LOGICAL one — the brain's <see cref="StanceIntent.TargetStance"/>, applied the tick it is ordered
        /// (DESIGN_Building_Interiors.md §3f); no stance component ⇒ standing. Range is NOT a factor (it comes from geometry).
        /// </summary>
        public static float Sigma(EntityRepository repo, Entity shooter, WeaponMountDto? mount, double now)
        {
            float mils = mount?.DispersionMils ?? 0f;
            if (mils <= 0f) return 0f;
            float sigma = mils / 1000f;

            if (repo.HasComponent<SimVelocity>(shooter) && repo.GetComponentRO<SimVelocity>(shooter).Linear.Length() > MovingSpeed)
                sigma *= MovingFactor;
            if (repo.HasComponent<UnderFire>(shooter) && now - repo.GetComponentRO<UnderFire>(shooter).LastTime < UnderFireSeconds)
                sigma *= UnderFireFactor;
            sigma *= StanceFactor(LogicalStance(repo, shooter));
            return sigma;
        }

        /// <summary>The stance factor on σ.</summary>
        public static float StanceFactor(StanceId stance) => stance switch
        {
            StanceId.Prone    => ProneFactor,
            StanceId.Crouched => CrouchedFactor,
            _                 => 1f,
        };

        /// <summary>The unit's LOGICAL stance: what its brain ordered (<see cref="StanceIntent"/>); standing when none.</summary>
        public static StanceId LogicalStance(EntityRepository repo, Entity e) => Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(repo, e);

        /// <summary><c>d(k) = 2·frac(k·φ⁻¹) − 1</c> ∈ [−1, 1): a low-discrepancy sequence, so any run of shots covers the spread evenly.</summary>
        public static float Sequence(uint k)
        {
            double f = (k * GoldenFraction) % 1.0;
            return (float)(2.0 * f - 1.0);
        }

        /// <summary>The deflection angle (radians) of the shooter's <paramref name="ordinal"/>-th shot.</summary>
        public static float Deflection(float sigma, uint ordinal) => sigma <= 0f ? 0f : sigma * Sequence(ordinal);

        /// <summary><paramref name="direction"/> turned by <paramref name="angle"/> radians about the vertical (hits are 2-D circles).</summary>
        public static Vector3 Rotate(Vector3 direction, float angle)
        {
            if (angle == 0f) return direction;
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new Vector3(direction.X * c - direction.Y * s, direction.X * s + direction.Y * c, direction.Z);
        }

        /// <summary>
        /// The chance a round hits a circle of <paramref name="radius"/> at <paramref name="range"/> for a uniform spread of half-width
        /// <paramref name="sigma"/>: <c>clamp(atan(r / range) / σ, 0, 1)</c>; σ = 0 ⇒ 1 (exact aim, today's behaviour).
        /// </summary>
        public static float HitChance(float sigma, float range, float radius)
        {
            if (sigma <= 0f) return 1f;
            if (range <= radius) return 1f;
            return Math.Clamp(MathF.Atan(radius / range) / sigma, 0f, 1f);
        }

        /// <summary>⭐ E — stamps <see cref="UnderFire"/> on <paramref name="unit"/> at <paramref name="now"/> (no-op when unregistered).</summary>
        public static void StampUnderFire(EntityRepository repo, Entity unit, double now)
        {
            if (!repo.IsComponentTypeRegistered<UnderFire>() || !repo.IsAlive(unit)) return;
            if (repo.HasComponent<UnderFire>(unit)) repo.GetComponentRW<UnderFire>(unit).LastTime = now;
            else repo.AddComponent(unit, new UnderFire { LastTime = now });
        }

        /// <summary>
        /// The shooter's next shot ordinal k = 1, 2, 3 … (k = 0 would be d = −1, the very edge of the spread, for every unit's first
        /// round). 1 every time when the component is unregistered.
        /// </summary>
        public static uint NextOrdinal(EntityRepository repo, Entity shooter)
        {
            if (!repo.IsComponentTypeRegistered<ShotOrdinal>()) return 1u;
            if (!repo.HasComponent<ShotOrdinal>(shooter)) { repo.AddComponent(shooter, new ShotOrdinal { Count = 1 }); return 1u; }
            ref var o = ref repo.GetComponentRW<ShotOrdinal>(shooter);
            return ++o.Count;
        }

        /// <summary>The sim time (s) of <paramref name="repo"/>, 0 without a clock.</summary>
        public static double Now(EntityRepository repo)
            => repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;
    }
}
