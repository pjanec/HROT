using System;
using System.Numerics;
using CarKinem.Trajectory;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐ <c>CE-3452</c> (P1, the bug queue) — a VEHICLE's progress along its path as the POINT IT IS ON: the first point, in path order from its progress
    /// forward within <c>window</c> metres, that lies within <see cref="OnPathM"/> of it. 📌 Measured live (<c>ua-moto-parked-bomb</c>): a path that starts with
    /// a short join segment pointing away from the bike's heading (onto the lane) gained NOTHING while the bike drove 20 m east along the lane (progress is
    /// integrated along the tangent AT the progress — CE-2059), so pure pursuit kept chasing the path's start and the bike looped back to it; in other runs the
    /// 4 s no-progress watchdog failed the move (FailedBlocked) while it drove. ⭐ The FIRST on-path point, not the closest: a path that doubles back can never
    /// skip its turn. A person keeps <see cref="HumanGait.Progress"/> (its segment walk).
    /// </summary>
    public static class PathProgress
    {
        /// <summary>How near the path (m) a vehicle must be for a point to count as the one it is on.</summary>
        public const float OnPathM = 2f;

        /// <summary>Below this share of the speed along the path's tangent (cos ~75°) the tangent's progress counts as stuck.</summary>
        public const float StuckAlong = 0.25f;

        /// <summary>The speed (m/s) above which a vehicle counts as moving for <see cref="StuckAlong"/>.</summary>
        public const float StuckSpeed = 0.5f;

        /// <summary>How well the vehicle's travel must agree with a segment's direction (cos ~45°) for it to be ON that segment.</summary>
        public const float AlongAgree = 0.7f;

        /// <summary>The progress at the first point of <paramref name="traj"/> ahead of <paramref name="progress"/> (within <paramref name="window"/> m) that
        /// <paramref name="pos"/> is on, TRAVELLING along it (<paramref name="travel"/>, a unit vector, within <see cref="AlongAgree"/> of the segment's direction);
        /// <paramref name="progress"/> when there is none (a looped path, off the path, or moving across it). 0 B.
        /// ⭐ The travel test: 📌 a technical manoeuvring slowly at its scoot point (<c>ua-technical-scoot</c>) passed within 2 m of a LATER leg of its
        /// turning path while heading across it, and the progress jumped there — skipping the turn; it crept away and never edged out to fire.</summary>
        public static float OnPathAhead(in CustomTrajectory traj, float progress, Vector2 pos, float window, Vector2 travel)
        {
            int n = traj.Waypoints.Length;
            if (traj.IsLooped != 0 || n < 2 || window <= 0f) return progress;
            float limit = progress + window, onPath2 = OnPathM * OnPathM;
            for (int k = 0; k < n - 1; k++)
            {
                var a = traj.Waypoints[k];
                var b = traj.Waypoints[k + 1];
                if (b.CumulativeDistance <= progress) continue;   // behind the progress
                if (a.CumulativeDistance > limit) break;           // past the window
                var pa = new Vector2(a.Position.X, a.Position.Y);
                var ab = new Vector2(b.Position.X, b.Position.Y) - pa;
                float len2 = ab.LengthSquared();
                if (len2 < 1e-8f) continue;
                float t = Math.Clamp(Vector2.Dot(pos - pa, ab) / len2, 0f, 1f);
                float s = a.CumulativeDistance + t * (b.CumulativeDistance - a.CumulativeDistance);
                if (s < progress || s > limit) continue;
                if (Vector2.DistanceSquared(pos, pa + ab * t) <= onPath2 && Vector2.Dot(travel, ab) >= AlongAgree * MathF.Sqrt(len2)) return s;
            }
            return progress;
        }
    }
}
