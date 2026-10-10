using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-2 (peek-and-fire D1, revised `2026-10-09` by the user) — WHERE an aimed round aims at an entity.
    /// 🔒 User: <i>"aiming at highest point meant we wont hit, we need to aim in between the lowest seen body part and highest
    /// one"</i> · <i>"if the body is hidden behind a weak penetrable obstacle, we could aim to body center if we know where the
    /// target is"</i>. In order:
    /// <list type="number">
    ///   <item>the <b>middle</b> (the mid-silhouette, today's aim) when the shooter's eye sees it — a target in the open is unchanged;</item>
    ///   <item>the <b>middle</b> when what hides it is WEAK: a round of this <paramref name="penetration"/> carried there through the
    ///     terrain (<see cref="TerrainPenetration.Carry"/>, the one penetration rule) arrives with at least
    ///     <see cref="ShootThroughMinFraction"/> of its damage;</item>
    ///   <item>else the <b>middle of the SEEN part</b> of the body: halfway between its lowest and highest visible heights, each edge
    ///     refined between the body samples by bisection (a crouched man behind a 0.9 m sill ⇒ ≈ 0.95 m, the band over the sill);</item>
    ///   <item>nothing seen ⇒ the middle (the sight gate, D4, normally prevents this).</item>
    /// </list>
    /// </summary>
    public static class AimPoint
    {
        /// <summary>A hidden middle is shot THROUGH when at least this share of the round's damage arrives there.</summary>
        public const float ShootThroughMinFraction = 0.5f;

        /// <summary>Bisection steps per visible edge (body samples are ≥ 0.15 m apart ⇒ ≤ 1 cm).</summary>
        private const int EdgeSteps = 5;

        public static Vector3 For(ISimulationView view, TerrainWorld? world, DoorStates? doors, Vector3 eye, Vector3 middle,
            Entity target, StanceId stance, float hullHeight, float penetration, List<Vector3> scratch)
        {
            if (world == null || !world.SegmentBlocked(eye, middle, doors)) return middle;

            float damage = 1f, pen = penetration;
            if (!TerrainPenetration.Carry(world, eye, middle, ref damage, ref pen, out _, doors: doors) && damage >= ShootThroughMinFraction)
                return middle;

            BodyProfile.Points(view, target, stance, hullHeight, scratch);   // ascending heights
            int lo = -1, hi = -1;
            for (int i = 0; i < scratch.Count; i++)
                if (!world.SegmentBlocked(eye, scratch[i], doors)) { if (lo < 0) lo = i; hi = i; }
            if (lo < 0) return middle;

            float bottom = lo > 0 ? Edge(world, doors, eye, scratch[lo - 1], scratch[lo]) : scratch[lo].Z;
            float top = hi < scratch.Count - 1 ? Edge(world, doors, eye, scratch[hi + 1], scratch[hi]) : scratch[hi].Z;
            return scratch[lo] with { Z = 0.5f * (bottom + top) };
        }

        /// <summary>The visible edge between a <paramref name="hidden"/> and a <paramref name="seen"/> point on one vertical.</summary>
        private static float Edge(TerrainWorld world, DoorStates? doors, Vector3 eye, Vector3 hidden, Vector3 seen)
        {
            float h = hidden.Z, s = seen.Z;
            for (int k = 0; k < EdgeSteps; k++)
            {
                float m = 0.5f * (h + s);
                if (world.SegmentBlocked(eye, seen with { Z = m }, doors)) h = m; else s = m;
            }
            return s;
        }
    }
}
