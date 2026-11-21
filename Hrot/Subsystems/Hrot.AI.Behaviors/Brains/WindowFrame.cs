using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Survey;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2275</c> D-2l (DA-77…DA-84, R-2059) — pure: a window post's FRAME from a <c>BuildingSurvey</c> answer, which of its two frame
    /// points faces a target, and which way a man standing at one leans toward the opening. A trained man watches and fires from beside the
    /// frame, behind the wall, leaning out (backend's <see cref="Leans"/>). 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c D-2l.
    /// </summary>
    public static class WindowFrame
    {
        /// <summary>DA-82 — how far past the frame edge along the wall his body stands (his shoulder at the edge) · how far inside the wall.</summary>
        public const float AlongMetres = 0.25f, InsideMetres = 0.5f;
        /// <summary>DA-84 — a window portal farther than this (flat) from the post is not its window.</summary>
        public const float MatchMetres = 2.5f;
        private const byte WindowPortal = 1;   // BuildingPortalKind.Window

        /// <summary>
        /// The frame of the window nearest <paramref name="post"/> (a window point on its centre line, inside the room): the two frame
        /// points (<paramref name="a"/> on the left of a man looking OUT, <paramref name="b"/> on his right, at the post's height) and the
        /// opening's centre (at the post's height). False when no window portal lies within <see cref="MatchMetres"/>.
        /// </summary>
        public static bool FromSurvey(WorldSurveyPayload? answer, Vector3 post, out Vector3 a, out Vector3 b, out Vector3 opening)
        {
            a = b = opening = default;
            if (answer == null) return false;
            SurveyPortal? best = null;
            float bestD = MatchMetres;
            foreach (var p in answer.Portals)
            {
                if (p.Kind != WindowPortal) continue;
                float d = Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(post.X, post.Y));
                if (d <= bestD) { bestD = d; best = p; }
            }
            if (best == null) return false;
            var centre = new Vector2(best.X, best.Y);
            // outward: the portal's face when the answer has it, else from the post (inside) toward the portal
            Vector2 outward = Face(answer, best, out var faceOut) ? faceOut : centre - new Vector2(post.X, post.Y);
            if (outward.LengthSquared() < 1e-6f) return false;
            outward = Vector2.Normalize(outward);
            var left = new Vector2(-outward.Y, outward.X);   // a man looking out: his left
            float half = MathF.Max(0.2f, best.Width * 0.5f) + AlongMetres;
            var inside = centre - outward * InsideMetres;
            var la = inside + left * half;
            var lb = inside - left * half;
            a = new Vector3(la.X, la.Y, post.Z);
            b = new Vector3(lb.X, lb.Y, post.Z);
            opening = new Vector3(centre.X, centre.Y, post.Z);
            return true;
        }

        /// <summary>The frame point a man should hold to see <paramref name="target"/>: the one on the side AWAY from it (from the left of
        /// the frame he sees ahead and to the right, DA-79).</summary>
        public static Vector3 Facing(Vector3 a, Vector3 b, Vector3 opening, Vector3 target)
        {
            var toTarget = new Vector2(target.X - opening.X, target.Y - opening.Y);
            float da = Vector2.Dot(new Vector2(a.X - opening.X, a.Y - opening.Y), toTarget);
            float db = Vector2.Dot(new Vector2(b.X - opening.X, b.Y - opening.Y), toTarget);
            return da <= db ? a : b;
        }

        /// <summary>The other frame point of the window (the one <paramref name="at"/> is not).</summary>
        public static Vector3 Other(Vector3 a, Vector3 b, Vector3 at)
            => Vector3.DistanceSquared(at, a) <= Vector3.DistanceSquared(at, b) ? b : a;

        /// <summary>DA-79 (refined) — the side a man facing <paramref name="facingBearing"/> (compass, 0 = north, clockwise) at
        /// <paramref name="at"/> leans to reach <paramref name="opening"/>: the opening on his left ⇒ <see cref="LeanSide.LeanLeft"/>.</summary>
        public static LeanSide SideToward(Vector3 at, float facingBearing, Vector3 opening)
        {
            float toOpening = LookAt.BearingTo(at, opening);
            float delta = ((toOpening - facingBearing) % 360f + 540f) % 360f - 180f;   // signed, −180…180; negative = to his left
            return delta < 0f ? LeanSide.LeanLeft : LeanSide.LeanRight;
        }

        /// <summary>The side a man at frame point <paramref name="at"/> LOOKING OUT (from between the frame points toward the opening) leans to
        /// reach <paramref name="opening"/> — the frame's own geometry, for a man whose facing is not known.</summary>
        public static LeanSide SideFromFrame(Vector3 a, Vector3 b, Vector3 opening, Vector3 at)
            => SideToward(at, LookAt.BearingTo((a + b) * 0.5f, opening), opening);

        /// <summary>The side <paramref name="self"/> leans toward <paramref name="opening"/> from where he stands, by his body's facing.</summary>
        public static LeanSide SideToward(EntityRepository world, Entity self, Vector3 opening)
        {
            if (!world.HasComponent<SimTransform>(self)) return LeanSide.LeanNone;
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(self);
            return SideToward(tf.Position, LookAt.FacingBearing(tf.Rotation), opening);
        }

        private static bool Face(WorldSurveyPayload answer, SurveyPortal portal, out Vector2 outward)
        {
            outward = default;
            if (portal.Face < 0) return false;
            int k = 0;
            foreach (var f in answer.Faces)
            {
                if (f.Building != portal.Building) continue;
                if (k++ != portal.Face) continue;
                outward = new Vector2(f.OutX, f.OutY);
                return outward.LengthSquared() > 1e-6f;
            }
            return false;
        }
    }
}
