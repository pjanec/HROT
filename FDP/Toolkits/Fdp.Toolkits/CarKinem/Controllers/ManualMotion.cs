using System;
using System.Numerics;
using CarKinem.Core;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3173</c> (M-2, M-3; <c>docs/DESIGN_Entity_Control.md</c> §6, §8b) — the MANUAL integrators: what an operator's
    /// <c>ManualDrive</c> does to a body in <see cref="KinematicsMode.Manual"/>. Their own functions, so the autonomous models
    /// (<see cref="HumanGait"/>, <see cref="BicycleModel"/>) are untouched — ⚠ both clamp speed ≥ 0, and a manual body reverses.
    /// </summary>
    public static class ManualMotion
    {
        /// <summary>M-2 — backwards and sideways a person moves at this share of his forward speed.</summary>
        public const float BackAndStrafeFactor = 0.6f;

        /// <summary>
        /// A PERSON: <paramref name="forward"/> / <paramref name="lateral"/> ∈ [−1, 1] in his body frame (lateral + = left), his body
        /// turned toward the absolute <paramref name="yaw"/> at <see cref="HumanGait.TurnRate"/> (on the spot when no axis is held).
        /// Back and strafe run at <see cref="BackAndStrafeFactor"/>. The velocity approaches the wanted one at
        /// <paramref name="accel"/>. Returns the yaw rate; <paramref name="velocity"/> is the world velocity (with its lateral part).
        /// </summary>
        public static float Person(ref Vector2 pos, ref Vector2 fwd, ref Vector2 velocity, ref VehicleState state,
            float forward, float lateral, float yaw, float topSpeed, float accel, float dt)
        {
            if (dt <= 0f) return 0f;
            // turn toward the asked yaw
            var wantedFwd = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw));
            float error = SignedAngle(fwd, wantedFwd);
            float most = HumanGait.TurnRate * dt;
            float turned = Math.Clamp(error, -most, most);
            fwd = Rotate(fwd, turned);

            // the wanted velocity in the body frame, then in the world
            forward = Math.Clamp(forward, -1f, 1f);
            lateral = Math.Clamp(lateral, -1f, 1f);
            var body = new Vector2(forward >= 0f ? forward : forward * BackAndStrafeFactor, lateral * BackAndStrafeFactor) * topSpeed;
            if (body.Length() > topSpeed) body = Vector2.Normalize(body) * topSpeed;
            var left = new Vector2(-fwd.Y, fwd.X);
            var wanted = fwd * body.X + left * body.Y;

            var change = wanted - velocity;
            float step = MathF.Max(accel, 0f) * dt;
            velocity = change.Length() <= step ? wanted : velocity + Vector2.Normalize(change) * step;

            pos += velocity * dt;
            state.Speed = Vector2.Dot(velocity, fwd);   // signed: backwards is negative
            state.SteerAngle = 0f;
            state.Accel = 0f;
            return turned / dt;
        }

        /// <summary>
        /// A GROUND VEHICLE: <paramref name="throttle"/> ∈ [−1, 1] (− brakes, and once stopped reverses), <paramref name="steer"/> ∈
        /// [−1, 1] of <see cref="VehicleParams.MaxSteerAngle"/>, the wheel turned toward it at <see cref="VehicleParams.MaxSteerRate"/>
        /// (instant when the TKB sets none). The bicycle model's geometry, with reverse. Returns the yaw rate.
        /// </summary>
        public static float Vehicle(ref Vector2 pos, ref Vector2 fwd, ref VehicleState state, in VehicleParams p,
            float throttle, float steer, float dt)
        {
            if (dt <= 0f) return 0f;
            throttle = Math.Clamp(throttle, -1f, 1f);
            steer = Math.Clamp(steer, -1f, 1f);

            float wantedSteer = steer * p.MaxSteerAngle;
            float slew = p.MaxSteerRate > 0f ? p.MaxSteerRate * dt : float.MaxValue;
            state.SteerAngle += Math.Clamp(wantedSteer - state.SteerAngle, -slew, slew);

            float accel;
            if (throttle > 0f)
                accel = state.Speed < 0f ? p.MaxDecel : Toward(state.Speed, throttle * p.MaxSpeedFwd, p.MaxAccel, dt);
            else if (throttle < 0f)
                accel = state.Speed > 0.05f ? -p.MaxDecel : Toward(state.Speed, throttle * p.MaxSpeedRev, p.MaxAccel, dt);
            else
                accel = Toward(state.Speed, 0f, p.MaxDecel * 0.25f, dt);   // coasting rolls to a stop

            // ⚠ held back, braking runs on through zero into reverse — that IS "brake, then reverse" (M-3); coasting cannot
            //   cross zero (Toward never overshoots)
            state.Speed = Math.Clamp(state.Speed + accel * dt, -p.MaxSpeedRev, p.MaxSpeedFwd);
            state.Accel = accel;

            float yawRate = p.WheelBase > 0f ? state.Speed / p.WheelBase * MathF.Tan(state.SteerAngle) : 0f;
            fwd = Rotate(fwd, yawRate * dt);
            pos += fwd * state.Speed * dt;
            return yawRate;
        }

        /// <summary>The acceleration that brings <paramref name="speed"/> toward <paramref name="target"/>, at most
        /// <paramref name="max"/> (and no overshoot in one step).</summary>
        private static float Toward(float speed, float target, float max, float dt)
        {
            float need = (target - speed) / dt;
            return Math.Clamp(need, -MathF.Abs(max), MathF.Abs(max));
        }

        private static Vector2 Rotate(Vector2 v, float angle)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return VectorMath.SafeNormalize(new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c), v);
        }

        private static float SignedAngle(Vector2 from, Vector2 to)
            => MathF.Atan2(from.X * to.Y - from.Y * to.X, Vector2.Dot(from, to));
    }
}
