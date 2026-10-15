using System;
using System.Collections.Generic;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ Buildings programme Stage 6 (<c>CE-1032</c>, R-225 W-6′/W-7′) — THE area-effect rules of a warhead, as pure functions, so the
    /// system, the rails and <c>GET /combat/detonations</c> read one model. 📄 docs/DESIGN_Building_Interiors.md §3k.
    /// <para><b>Fragments</b> fly straight: each terrain piece on the line passes them with <see cref="ArmorModel.PenetrationChance"/>
    /// (openings pass), any collider on it stops them (<c>ColliderOcclusion</c>); damage falls off with the square of the distance
    /// fraction to the warhead's reach. <b>Blast</b> does full damage inside the lethal radius, falling linearly to 0 at the injury
    /// radius; a CLOSED barrier (a slab, a shut door, a building's wall) passes it by its material
    /// (<see cref="BarrierTransmission"/>), an open obstacle taller than the target puts it in a diffraction SHADOW
    /// (<see cref="ShadowFactor"/>) that recovers a few obstacle heights back. ⚠ Confinement (a room amplifying the blast) is v2
    /// (W-7v2). Expected values, no dice (R-212).</para>
    /// </summary>
    public static class AreaEffect
    {
        /// <summary>⭐ CE-3203 (DESIGN_Building_Interiors §3k.1, W-6″) — what a burst's fragments meet: a man, a hull, or a weapon (equipment).</summary>
        public enum FragmentTarget : byte { Person = 0, Hull = 1, Equipment = 2 }

        /// <summary>⭐ CE-3203 — the area a MAN presents to a ground burst, by stance (m²): standing 0.7, crouched 0.45, prone 0.15.</summary>
        public static float PersonArea(Fdp.Toolkit.Tkb.Domain.StanceId stance) => stance switch
        {
            Fdp.Toolkit.Tkb.Domain.StanceId.Prone => 0.15f,
            Fdp.Toolkit.Tkb.Domain.StanceId.Crouched => 0.45f,
            _ => 0.7f,
        };

        /// <summary>⭐ CE-3203 — the share of a man body armour covers (the plates); the rest of him is open to a fragment.</summary>
        public const float BodyArmourCoverage = 0.35f;

        /// <summary>⭐ CE-3203 — one target's fragment result: the fragments that strike it, the chance one defeats its armour, the damage.</summary>
        public readonly record struct FragmentResult(float Hits, float ArmourChance, float Damage);

        /// <summary>
        /// ⭐⭐ CE-3203 (DESIGN_Building_Interiors §3k.1, W-6″) — THE fragment law, for every target. Of the burst's
        /// <see cref="Fdp.Toolkit.Tkb.Domain.WarheadDto.FragmentCount"/> fragments, <c>N · area / (2πr² + area)</c> strike a target presenting
        /// <paramref name="area"/> m² at <paramref name="distance"/> m (a ground burst's hemisphere); each still flies at <c>e^(−r/λ)</c> of its
        /// speed, so its damage and its penetration fall with it. One hit does a MAN <c>FragmentWoundDamage</c>, a hull or a weapon
        /// <c>FragmentHitDamage</c> (a bullet's law), if it defeats <paramref name="armour"/> mm (a man's plates cover only
        /// <see cref="BodyArmourCoverage"/> of him). Times the share of the target the rays reach (<paramref name="exposure"/>).
        /// Null when the warhead states no fragment count (a hand-written TKB warhead): the caller keeps its old rule.
        /// </summary>
        public static FragmentResult? Fragments(Fdp.Toolkit.Tkb.Domain.WarheadDto w, FragmentTarget target, float area, float distance,
                                                float armour, float exposure)
        {
            if (w.FragmentCount <= 0f || area <= 0f) return null;
            float r = MathF.Max(distance, 0.5f);
            if (w.FragmentRadiusM > 0f && r >= w.FragmentRadiusM) return new FragmentResult(0f, 0f, 0f);
            float hits = w.FragmentCount * area / (2f * MathF.PI * r * r + area);
            float speed = w.FragmentDragM > 0f ? MathF.Exp(-r / w.FragmentDragM) : 1f;
            float chance = ArmorModel.PenetrationChance(w.FragmentPenetrationMm * speed, armour);
            float perHit;
            if (target == FragmentTarget.Person)
            {
                chance = 1f - BodyArmourCoverage * (1f - chance);   // the plates cover the torso; a limb or the head is open
                perHit = (w.FragmentWoundDamage > 0f ? w.FragmentWoundDamage : w.FragmentHitDamage) * speed;
            }
            else perHit = w.FragmentHitDamage * speed;
            return new FragmentResult(hits, chance, hits * perHit * chance * MathF.Max(exposure, 0f));
        }

        /// <summary>
        /// ⭐ <c>CE-3212</c> (hit location §10 S3-2) — what ONE fragment of <paramref name="w"/> still does at <paramref name="distance"/> m to
        /// <paramref name="target"/>: its damage (a man's wound or a bullet's law) and its penetration (mm), both slowed by drag — the same
        /// numbers <see cref="Fragments"/> uses, so a caller splitting the hits over a body's parts keeps the law.
        /// </summary>
        public static (float Damage, float PenetrationMm) FragmentHit(Fdp.Toolkit.Tkb.Domain.WarheadDto w, FragmentTarget target, float distance)
        {
            float r = MathF.Max(distance, 0.5f);
            float speed = w.FragmentDragM > 0f ? MathF.Exp(-r / w.FragmentDragM) : 1f;
            float perHit = target == FragmentTarget.Person && w.FragmentWoundDamage > 0f ? w.FragmentWoundDamage : w.FragmentHitDamage;
            return (perHit * speed, w.FragmentPenetrationMm * speed);
        }

        /// <summary>⭐ CE-3203 — the distance at which a STANDING man in the open expects less than one point of fragment damage from
        /// <paramref name="w"/>: the law's own reach (the generated <c>FragmentRadiusM</c>).</summary>
        public static float FragmentHazardRadius(Fdp.Toolkit.Tkb.Domain.WarheadDto w)
        {
            var open = w with { FragmentRadiusM = 0f };
            float lo = 0.5f, hi = 0.5f;
            while (hi < 5000f && (Fragments(open, FragmentTarget.Person, 0.7f, hi, 0f, 1f)?.Damage ?? 0f) >= 1f) { lo = hi; hi *= 2f; }
            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (lo + hi);
                if ((Fragments(open, FragmentTarget.Person, 0.7f, mid, 0f, 1f)?.Damage ?? 0f) >= 1f) lo = mid; else hi = mid;
            }
            return MathF.Round(hi, 1);
        }

        /// <summary>Blast left right behind an obstacle taller than the target (W-7′: "≈ 0.3 right behind a tall wall").</summary>
        public const float ShadowFloor = 0.3f;

        /// <summary>How many obstacle heights behind it the blast is back to full (W-7′: "≈ 1 a few wall-heights back").</summary>
        public const float ShadowRecoveryHeights = 3f;

        /// <summary>A closed barrier passes the blast as <c>exp(−resistance / this)</c>: 0.2 m of concrete (300 mm RHA) ≈ 5 %, a wooden
        /// door ≈ 95 % (it is blown in).</summary>
        public const float BarrierResistanceMm = 100f;

        /// <summary>A round that stops on a surface bursts this far back along its flight, so the surface it struck is between the burst
        /// and whatever is behind that surface (a roof shields the room below).</summary>
        public const float BurstStandOffMetres = 0.1f;

        /// <summary>Fragment damage fraction at <paramref name="distance"/>: <c>(1 − r/reach)²</c>, 0 beyond the reach.</summary>
        public static float FragmentFalloff(float distance, float reach)
        {
            if (reach <= 0f || distance >= reach) return 0f;
            float f = 1f - MathF.Max(distance, 0f) / reach;
            return f * f;
        }

        /// <summary>Blast damage fraction: 1 inside <paramref name="lethal"/>, linear to 0 at <paramref name="injury"/>.</summary>
        public static float BlastFalloff(float distance, float lethal, float injury)
        {
            if (injury <= 0f || distance >= injury) return 0f;
            if (distance <= lethal) return 1f;
            return (injury - distance) / (injury - lethal);
        }

        /// <summary>
        /// The diffraction factor behind an obstacle <paramref name="obstacleHeight"/> tall (above the target's feet), the target
        /// <paramref name="behind"/> metres behind it: <see cref="ShadowFloor"/> against it, back to 1 at
        /// <see cref="ShadowRecoveryHeights"/> heights.
        /// </summary>
        public static float ShadowFactor(float obstacleHeight, float behind)
        {
            if (obstacleHeight <= 0f) return 1f;
            float k = Math.Clamp(behind / (ShadowRecoveryHeights * obstacleHeight), 0f, 1f);
            return ShadowFloor + (1f - ShadowFloor) * k;
        }

        /// <summary>The blast passed by a closed barrier of <paramref name="resistanceMmRha"/>.</summary>
        public static float BarrierTransmission(float resistanceMmRha) => MathF.Exp(-MathF.Max(resistanceMmRha, 0f) / BarrierResistanceMm);

        /// <summary>
        /// A CLOSED barrier: something the burst and the target are on opposite sides of, not something to bend round — a floor
        /// slab or stair, a shut door, a wall panel of a building. A free-standing wall, a fence and a solid block are open
        /// obstacles (the wave diffracts over them).
        /// </summary>
        public static bool IsClosedBarrier(in TraceCrossing c) => c.ClosedBarrier;   // ⭐ CE-1035 Q1 — the world query says it (R-252)

        /// <summary>Fragments through the terrain pieces on one line: the product of each piece's penetration chance.</summary>
        public static float FragmentTransmission(List<TraceCrossing> crossings, float fragmentPenetrationMm)
        {
            float t = 1f;
            for (int i = 0; i < crossings.Count && t > 0f; i++)
                t *= ArmorModel.PenetrationChance(fragmentPenetrationMm, crossings[i].Loss);
            return t;
        }

        /// <summary>The blast through the closed barriers on one line (open obstacles do not attenuate — they shadow).</summary>
        public static float ClosedBarrierTransmission(List<TraceCrossing> crossings)
        {
            float t = 1f;
            for (int i = 0; i < crossings.Count; i++)
                if (IsClosedBarrier(crossings[i])) t *= BarrierTransmission(crossings[i].Loss);
            return t;
        }

        /// <summary>
        /// The deepest terrain shadow on the line to the target's HIGHEST body point (<paramref name="horizontalLength"/> m long): an
        /// open obstacle crossed on that line stands above the whole body. 1 = none.
        /// </summary>
        public static float TerrainShadow(List<TraceCrossing> crossingsToTop, float targetBaseZ, float horizontalLength)
        {
            float best = 1f;
            for (int i = 0; i < crossingsToTop.Count; i++)
            {
                var c = crossingsToTop[i];
                if (IsClosedBarrier(c)) continue;
                best = MathF.Min(best, ShadowFactor(c.TopZ - targetBaseZ, (1f - c.T) * horizontalLength));
            }
            return best;
        }
    }
}
