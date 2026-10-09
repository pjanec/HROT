using System;
using System.Numerics;
using CarKinem.Core;

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

        /// <summary>The signed angle (rad, −π…π) from <paramref name="from"/> to <paramref name="to"/>, counter-clockwise positive.</summary>
        public static float SignedAngle(Vector2 from, Vector2 to)
            => MathF.Atan2((from.X * to.Y) - (from.Y * to.X), Vector2.Dot(from, to));
    }
}
