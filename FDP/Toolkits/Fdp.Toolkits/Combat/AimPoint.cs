using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.World;
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

        /// <summary>
        /// ⭐ WL-2 — the target's MIDDLE: its position raised to the aim height of its logical stance (a person by posture, a vehicle
        /// by its hull, CE-3116). The ONE place both the round (<c>FireProcessingSystem</c>) and the turret (<c>BallisticAim</c>) take
        /// it from, so the gun points where the round will go.
        /// </summary>
        public static Vector3 Middle(EntityRepository repo, Entity target, out StanceId stance, out float hullHeight)
        {
            var at = repo.GetComponent<SimTransform>(target).Position;
            stance = HitModel.LogicalStance(repo, target);
            hullHeight = Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(repo, target);
            at.Z += TerrainWorldLosStrategy.AimHeightFor(repo, target, stance, hullHeight);
            return at;
        }

        /// <summary>⭐ CE-1125 — how far from the target's <see cref="Middle"/> (plus its hull height) a point may lie and still be ON the
        /// unit: a standing man's head is ~0.7 m above his middle, his feet ~1 m below it.</summary>
        public const float OnUnitMetres = 1.5f;

        /// <summary>⭐ CE-1125 — <paramref name="point"/> in <paramref name="unit"/>'s own frame (its position and rotation), so a point the
        /// operator's crosshair put on him can be carried as he moves (<see cref="FromUnitFrame"/>).</summary>
        public static Vector3 ToUnitFrame(EntityRepository repo, Entity unit, Vector3 point)
        {
            if (!repo.HasComponent<SimTransform>(unit)) return point;
            var t = repo.GetComponentRO<SimTransform>(unit);
            return Vector3.Transform(point - t.Position, Quaternion.Inverse(t.Rotation));
        }

        /// <summary>⭐ CE-1125 — a point held in <paramref name="unit"/>'s frame (<see cref="ToUnitFrame"/>), back in the world.</summary>
        public static Vector3 FromUnitFrame(EntityRepository repo, Entity unit, Vector3 local)
        {
            if (!repo.HasComponent<SimTransform>(unit)) return local;
            var t = repo.GetComponentRO<SimTransform>(unit);
            return t.Position + Vector3.Transform(local, t.Rotation);
        }

        /// <summary>
        /// ⭐ CE-1125 — where an operator's shot locked on <paramref name="unit"/> goes: <paramref name="crosshair"/> (the point the ray met
        /// on him) while it is ON him, else his <see cref="Middle"/>. 🔒 User, 2026-10-16: <i>"when i aim at character and fire i always hit
        /// the belly of a standing figure"</i> — the lock aimed at the middle and dropped the point.
        /// </summary>
        public static Vector3 Operator(EntityRepository repo, Entity unit, Vector3 crosshair)
        {
            var middle = Middle(repo, unit, out _, out float hullHeight);
            return Vector3.Distance(crosshair, middle) <= OnUnitMetres + hullHeight ? crosshair : middle;
        }

        public static Vector3 For(ISimulationView view, IWorldQuery? world, Vector3 eye, Vector3 middle,
            Entity target, StanceId stance, float hullHeight, float penetration, List<Vector3> scratch)
        {
            if (world == null || !world.SightBlocked(eye, middle)) return middle;

            float damage = 1f, pen = penetration;
            if (!TerrainPenetration.Carry(world, eye, middle, ref damage, ref pen, out _) && damage >= ShootThroughMinFraction)
                return middle;

            BodyProfile.Points(view, target, stance, hullHeight, scratch);   // ascending heights
            int lo = -1, hi = -1;
            for (int i = 0; i < scratch.Count; i++)
                if (!world.SightBlocked(eye, scratch[i])) { if (lo < 0) lo = i; hi = i; }
            if (lo < 0) return middle;

            float bottom = lo > 0 ? Edge(world, eye, scratch[lo - 1], scratch[lo]) : scratch[lo].Z;
            float top = hi < scratch.Count - 1 ? Edge(world, eye, scratch[hi + 1], scratch[hi]) : scratch[hi].Z;
            return scratch[lo] with { Z = 0.5f * (bottom + top) };
        }

        /// <summary>The visible edge between a <paramref name="hidden"/> and a <paramref name="seen"/> point on one vertical.</summary>
        private static float Edge(IWorldQuery world, Vector3 eye, Vector3 hidden, Vector3 seen)
        {
            float h = hidden.Z, s = seen.Z;
            for (int k = 0; k < EdgeSteps; k++)
            {
                float m = 0.5f * (h + s);
                if (world.SightBlocked(eye, seen with { Z = m })) h = m; else s = m;
            }
            return s;
        }
    }
}
