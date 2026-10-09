using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Trajectory;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3145</c> (R-247) — how a PERSON moves: it turns on the spot, then walks where it faces. The car model a pedestrian
    /// used to share (<see cref="BicycleModel"/>: steering geometry, a 0.3 m turn circle, forward speed while turning) made a soldier
    /// facing away from his goal walk a half-circle forward first — 📐 measured on <c>bt-window-duel</c>: A, 0.4 m from House A's
    /// open stairwell, stepped into it on his first move and fell to the ground floor.
    /// <list type="bullet">
    ///   <item>The heading turns straight towards the wanted direction at <see cref="TurnRate"/> — no steering angle, no radius.</item>
    ///   <item>More than <see cref="WalkWithinAngle"/> off ⇒ the speed target is 0: he stops (<see cref="StopDecel"/> at least) and
    ///     turns where he stands; within it he walks along the new heading.</item>
    ///   <item>He tracks his path closely (<see cref="MinPathLookahead"/>) and never steps off a ledge (<see cref="MaxStepDown"/>,
    ///     in <c>CarKinematicsSystem</c> where the surface is known).</item>
    /// </list>
    /// 📄 docs/DESIGN_Peek_And_Fire.md D17.
    /// </summary>
    public static class HumanGait
    {
        /// <summary>How fast a person turns (rad/s): an about-face in half a second.</summary>
        public const float TurnRate = 2f * MathF.PI;

        /// <summary>Walk only when the heading is within this of the wanted direction (rad, 30°); farther off he turns in place.</summary>
        public const float WalkWithinAngle = MathF.PI / 6f;

        /// <summary>A person stops within a step (m/s²) — at least this, whatever the mount's braking says.</summary>
        public const float StopDecel = 4f;

        /// <summary>How far ahead on his path a person aims (m, at least) and per m/s of speed (s): he follows it closely — a car's
        /// 1 m minimum cut a corner over House A's stairwell (CE-3145).</summary>
        public const float MinPathLookahead = 0.35f, PathLookaheadSeconds = 0.25f;   // only for a looped path (Aim handles the rest)

        /// <summary>The largest drop a person steps down in one step (m); a bigger one is a ledge he does not walk off.</summary>
        public const float MaxStepDown = 1.0f;   // a soldier steps or hops down a metre (a stair's side); a storey is a fall

        /// <summary>Within this of the floor under him (m), a person is standing on it (the ledge rule applies).</summary>
        public const float OnFloorTolerance = 0.15f;

        /// <summary>
        /// The speed a person should aim for now: <paramref name="wantedSpeed"/> when <paramref name="fwd"/> is within
        /// <see cref="WalkWithinAngle"/> of <paramref name="wanted"/>, else 0 (turn first).
        /// </summary>
        public static float SpeedTarget(Vector2 fwd, Vector2 wanted, float wantedSpeed)
            => wantedSpeed > 0f && MathF.Abs(SignedAngle(fwd, wanted)) > WalkWithinAngle ? 0f : wantedSpeed;

        /// <summary>
        /// One step: turn <paramref name="fwd"/> towards <paramref name="wanted"/> (when there is somewhere to go), update the speed by
        /// <paramref name="accel"/>, move along the new heading. Returns the yaw rate (rad/s).
        /// </summary>
        public static float Integrate(ref Vector2 pos, ref Vector2 fwd, ref VehicleState state, Vector2 wanted, bool moving, float accel, float dt)
        {
            float turned = 0f;
            if (moving && wanted.LengthSquared() > 1e-6f)
            {
                float error = SignedAngle(fwd, wanted);
                float most = TurnRate * MathF.Max(dt, 0f);   // ⚠ the host can hand a NEGATIVE step (measured in-process): no turn then
                turned = Math.Clamp(error, -most, most);
                float c = MathF.Cos(turned), s = MathF.Sin(turned);
                fwd = VectorMath.SafeNormalize(new Vector2(fwd.X * c - fwd.Y * s, fwd.X * s + fwd.Y * c), fwd);
            }

            state.Speed = MathF.Max(0f, state.Speed + (accel * dt));
            pos += fwd * state.Speed * dt;
            state.SteerAngle = 0f;
            state.Accel = accel;
            return dt > 0f ? turned / dt : 0f;
        }

        /// <summary>A path corner counts as reached within this (m) — then he heads for the next one.</summary>
        public const float CornerReach = 0.25f;

        /// <summary>How far ahead of where he stands on the path a person aims (m) — never past the corner he is walking to.</summary>
        public const float PathAim = 1.0f;

        /// <summary>
        /// ⭐ <c>CE-3145</c> — where a person on a path heads: <see cref="PathAim"/> ahead of his projection on the segment he walks,
        /// CLAMPED TO THAT SEGMENT'S END CORNER, so a drift closes onto the path (CE-3115) but a corner is never cut (📐 a car's
        /// lookahead cut one over House A's stairwell). Within <see cref="CornerReach"/> of the corner his progress snaps to it and
        /// the next segment is walked; the gait turns him there. False on a looped path or past the last corner.
        /// </summary>
        public static bool Aim(in CustomTrajectory traj, ref float progress, Vector2 pos, out Vector2 aim)
        {
            aim = default;
            int n = traj.Waypoints.Length;
            if (traj.IsLooped != 0 || n < 2) return false;
            int k = Segment(in traj, progress);
            while (k < n - 1)
            {
                var a = traj.Waypoints[k];
                var b = traj.Waypoints[k + 1];
                var pa = new Vector2(a.Position.X, a.Position.Y);
                var corner = new Vector2(b.Position.X, b.Position.Y);
                if (k + 1 < n - 1 && Vector2.Distance(pos, corner) <= CornerReach)
                {
                    progress = MathF.Max(progress, b.CumulativeDistance);   // reached: on to the next corner
                    k++;
                    continue;
                }
                var ab = corner - pa;
                float len = ab.Length();
                if (len < 1e-4f) { aim = corner; return true; }
                var dir = ab / len;
                float along = Math.Clamp(Vector2.Dot(pos - pa, dir), 0f, len);
                aim = pa + (dir * MathF.Min(along + PathAim, len));
                return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-3145</c> — a person's progress along his path: his position projected on the segment he is walking (never behind
        /// where he was). ⛔ Not dead-reckoned from speed × heading — that stalled while he turned on the spot and ran ahead when he cut.
        /// </summary>
        public static float Progress(in CustomTrajectory traj, float progress, Vector2 pos)
        {
            int n = traj.Waypoints.Length;
            if (traj.IsLooped != 0 || n < 2) return progress;
            int k = Segment(in traj, progress);
            if (k >= n - 1) return progress;
            var a = traj.Waypoints[k];
            var b = traj.Waypoints[k + 1];
            var pa = new Vector2(a.Position.X, a.Position.Y);
            var ab = new Vector2(b.Position.X, b.Position.Y) - pa;
            float len = ab.Length();
            if (len < 1e-4f) return MathF.Max(progress, b.CumulativeDistance);
            float along = Math.Clamp(Vector2.Dot(pos - pa, ab / len), 0f, len);
            return MathF.Max(progress, a.CumulativeDistance + along);
        }

        private static int Segment(in CustomTrajectory traj, float progress)
        {
            int k = 0;
            while (k < traj.Waypoints.Length - 2 && traj.Waypoints[k + 1].CumulativeDistance <= progress) k++;
            return k;
        }

        /// <summary>The signed angle (rad, −π…π) from <paramref name="from"/> to <paramref name="to"/>, counter-clockwise positive.</summary>
        public static float SignedAngle(Vector2 from, Vector2 to)
            => MathF.Atan2((from.X * to.Y) - (from.Y * to.X), Vector2.Dot(from, to));
    }
}
