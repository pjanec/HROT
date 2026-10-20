using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Physics.BodyParts;
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
    ///   <item>the <b>middle</b> when the shooter's eye sees it — for a person his CHEST, where his pose puts it (<see cref="Middle"/>,
    ///     <c>CE-2325</c>); a vehicle the middle of its hull;</item>
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
        /// ⭐ WL-2 — the target's MIDDLE: a person's CHEST for his pose (<see cref="Chest"/>, CE-2325), a vehicle's position raised to
        /// half its hull (CE-3116). The ONE place both the round (<c>FireProcessingSystem</c>) and the turret (<c>BallisticAim</c>) take
        /// it from, so the gun points where the round will go.
        /// </summary>
        public static Vector3 Middle(EntityRepository repo, Entity target, out StanceId stance, out float hullHeight)
        {
            var tf = repo.GetComponent<SimTransform>(target);
            var at = tf.Position;
            stance = HitModel.LogicalStance(repo, target);
            hullHeight = Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(repo, target);
            if (hullHeight > 0f)
            {
                at.Z += TerrainWorldLosStrategy.AimHeightFor(repo, target, stance, hullHeight);
                return at;
            }
            // ⭐⭐ CE-2325 (R-2064) — a PERSON is aimed at his CHEST, where it really is: his pose's torso, part way to its head, from
            //   the same boxes his hits are taken on (PersonBody — the drawn figure). ⛔ SUPERSEDED: half the eye height — 0.85 m
            //   standing, 0.55 m crouched: the crotch and the hips, under a window's sill.
            var chest = Chest(PersonBody.PoseOf(repo, target)) * PersonBody.ScaleOf(Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.Height(repo, target));
            at += Vector3.Transform(chest, tf.Rotation);
            // ⭐ CE-3257 (BR-5, LN-6) — a LEANING man: the chest of the half he shows
            at += Leans.AimOffset(repo, target, stance, chest.Z, TerrainWorldLosStrategy.EyeHeightFor(repo, target, stance));
            return at;
        }

        /// <summary>How far from the torso's centre toward the head's the chest lies (0 = the torso's centre, 1 = the head's).</summary>
        public const float ChestTowardHead = 0.35f;

        /// <summary>
        /// ⭐ <c>CE-2325</c> (R-2064) — the CHEST of <paramref name="pose"/>, in the body frame of the reference figure: the torso box's centre,
        /// <see cref="ChestTowardHead"/> of the way to the head's (≈ 1.23 m standing aimed, 0.89 m crouched; on the lying torso when prone).
        /// </summary>
        public static Vector3 Chest(PersonPose pose)
        {
            Vector3 torso = default, head = default;
            foreach (var box in PersonBody.Boxes(pose))
            {
                if (box.Part == PersonPart.Torso) torso = box.Centre;
                else if (box.Part == PersonPart.Head) head = box.Centre;
            }
            return torso + (head - torso) * ChestTowardHead;
        }

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
        /// ⭐ CE-1126 (DESIGN_Entity_Control §8g F-5) — where an operator's shot locked on <paramref name="unit"/> goes: the point the crosshair
        /// put on one of his PARTS (the crosshair meets the round's own boxes now), carried with him; his <see cref="Middle"/> only when the
        /// shot names him and no point. ⛔ The 1.5 m "is it on him" test (CE-1125) is gone: it covered a crosshair box that disagreed with the
        /// parts. 🔒 User, 2026-10-16: <i>"the 1.5m hardcoded threshold also looks suspicious"</i>.
        /// </summary>
        public static Vector3 Operator(EntityRepository repo, Entity unit, Vector3 crosshair)
            => crosshair != Vector3.Zero ? crosshair : Middle(repo, unit, out _, out _);

        public static Vector3 For(ISimulationView view, IWorldQuery? world, Vector3 eye, Vector3 middle,
            Entity target, StanceId stance, float hullHeight, float penetration, List<Vector3> scratch)
        {
            if (world == null) return middle;
            bool middleSeen = !world.SightBlocked(eye, middle);
            if (!middleSeen && ShootsThrough(world, eye, middle, penetration)) return middle;

            BodyProfile.Points(view, target, stance, hullHeight, scratch);   // ascending heights
            int lo = -1, hi = -1;
            for (int i = 0; i < scratch.Count; i++)
                if (!world.SightBlocked(eye, scratch[i])) { if (lo < 0) lo = i; hi = i; }
            if (lo < 0) return middle;
            // ⭐ CE-2325 (R-2064) — the chest seen and nothing below it hidden, or hidden only by what a round goes through: the chest
            if (middleSeen && (lo == 0 || ShootsThrough(world, eye, scratch[lo - 1], penetration))) return middle;

            float bottom = lo > 0 ? Edge(world, eye, scratch[lo - 1], scratch[lo]) : scratch[lo].Z;
            float top = hi < scratch.Count - 1 ? Edge(world, eye, scratch[hi + 1], scratch[hi]) : scratch[hi].Z;
            float band = 0.5f * (bottom + top);
            // ⭐⭐ CE-2325 — the chest seen just over a cover that STOPS a round (a man at a window, his chest at the sill): aim no lower
            //   than the middle of what shows, or half the rounds go into the sill. 🔒 User, 2026-10-20: "firing a shot to the center
            //   results in hitting the bottom part of the window not the figure in the window".
            if (middleSeen) return middle.Z >= band ? middle : middle with { Z = band };
            return scratch[lo] with { Z = band };
        }

        /// <summary>A round of <paramref name="penetration"/> carried from <paramref name="eye"/> to <paramref name="point"/> through the terrain
        /// arrives with at least <see cref="ShootThroughMinFraction"/> of its damage (the cover in between is WEAK).</summary>
        private static bool ShootsThrough(IWorldQuery world, Vector3 eye, Vector3 point, float penetration)
        {
            float damage = 1f, pen = penetration;
            return !TerrainPenetration.Carry(world, eye, point, ref damage, ref pen, out _) && damage >= ShootThroughMinFraction;
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
