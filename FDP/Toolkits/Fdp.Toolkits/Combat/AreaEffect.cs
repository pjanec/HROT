using System;
using System.Collections.Generic;
using Fdp.Toolkit.Terrain;

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
        public static bool IsClosedBarrier(in TerrainWorld.FireCrossing c)
            => c.Kind is "slab" or "ramp" or "door" || (c.Kind == "panel" && c.Building != null);

        /// <summary>Fragments through the terrain pieces on one line: the product of each piece's penetration chance.</summary>
        public static float FragmentTransmission(List<TerrainWorld.FireCrossing> crossings, float fragmentPenetrationMm)
        {
            float t = 1f;
            for (int i = 0; i < crossings.Count && t > 0f; i++)
                t *= ArmorModel.PenetrationChance(fragmentPenetrationMm, crossings[i].ResistanceMmRha);
            return t;
        }

        /// <summary>The blast through the closed barriers on one line (open obstacles do not attenuate — they shadow).</summary>
        public static float ClosedBarrierTransmission(List<TerrainWorld.FireCrossing> crossings)
        {
            float t = 1f;
            for (int i = 0; i < crossings.Count; i++)
                if (IsClosedBarrier(crossings[i])) t *= BarrierTransmission(crossings[i].ResistanceMmRha);
            return t;
        }

        /// <summary>
        /// The deepest terrain shadow on the line to the target's HIGHEST body point (<paramref name="horizontalLength"/> m long): an
        /// open obstacle crossed on that line stands above the whole body. 1 = none.
        /// </summary>
        public static float TerrainShadow(List<TerrainWorld.FireCrossing> crossingsToTop, float targetBaseZ, float horizontalLength)
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
