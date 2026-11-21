using System;
using System.Numerics;
using CarKinem.Core;

namespace CarKinem.Controllers
{
    /// <summary>
    /// Kinematic bicycle model for vehicle motion.
    /// </summary>
    public static class BicycleModel
    {
        /// <summary>
        /// Integrate bicycle model for one timestep.
        /// Updates position, heading, and speed.
        /// </summary>
        /// <param name="pos">Current vehicle position (modified in-place)</param>
        /// <param name="fwd">Current vehicle forward vector (modified in-place)</param>
        /// <param name="state">Current vehicle state (other fields modified in-place)</param>
        /// <param name="steerAngle">Steering angle command (radians)</param>
        /// <param name="accel">Acceleration command (m/s²)</param>
        /// <param name="dt">Timestep (seconds)</param>
        /// <param name="wheelBase">Distance between axles (meters)</param>
        /// <param name="maxSpeedRev">⭐ <c>CE-2167</c> (V5) — how fast it may back up (m/s, ≥ 0); 0 = it never reverses (the default).</param>
        /// <param name="maxSpeedFwd">Its top forward speed (m/s); unbounded by default.</param>
        /// <param name="maxLatAccel">⭐ <c>CE-4345</c> — the type's lateral-acceleration limit (m/s², <see cref="VehicleParams.MaxLatAccel"/>): the wheel is held to
        /// <see cref="SteerLimit"/> at the step's speed, so the turning radius never falls below v²/a. 0 = no limit (the old model).</param>
        /// <returns>The yaw rate (rad/s) the step turned at.</returns>
        public static float Integrate(
            ref Vector2 pos,
            ref Vector2 fwd,
            ref VehicleState state,
            float steerAngle,
            float accel,
            float dt,
            float wheelBase,
            float maxSpeedRev = 0f,
            float maxSpeedFwd = float.MaxValue,
            float maxLatAccel = 0f)
        {
            // 1. Update speed — ⭐ CE-2167 (V5): clamped to [−maxSpeedRev, maxSpeedFwd]. ⛔ It was ≥ 0, so the kinematics' reverse
            //    (speedSign −1) could never back an AI vehicle up: only the manual drive reversed.
            state.Speed += accel * dt;
            state.Speed = Math.Clamp(state.Speed, -MathF.Max(0f, maxSpeedRev), maxSpeedFwd);

            // ⭐ CE-4345 — the turn a vehicle can make falls with speed: the wheel at most SteerLimit at THIS speed (the possessed drive
            //   and the AI both integrate here, so both obey it)
            float limit = SteerLimit(state.Speed, wheelBase, maxLatAccel, MathF.PI * 0.5f - 1e-3f);
            steerAngle = Math.Clamp(steerAngle, -limit, limit);

            // 2. Calculate angular velocity (yaw rate)
            // omega = (v / L) * tan(delta)
            float angularVel = wheelBase > 0f ? (state.Speed / wheelBase) * MathF.Tan(steerAngle) : 0f;
            
            // 3. Rotate forward vector (2D rotation matrix or just angle math)
            // Using rotation matrix on Forward vector is efficient because we already have the vector
            float rotAngle = angularVel * dt;
            
            // Optimization: Small angle approximation if rotAngle close to 0? 
            // For now explicit sin/cos is safer.
            float c = MathF.Cos(rotAngle);
            float s = MathF.Sin(rotAngle);
            
            Vector2 newForward = new Vector2(
                fwd.X * c - fwd.Y * s,
                fwd.X * s + fwd.Y * c
            );
            
            // Re-normalize to prevent drift
            fwd = VectorMath.SafeNormalize(newForward, fwd);
            
            // 4. Update position
            // Assuming constant velocity over dt for position integration step (Euler)
            // Better: RK4, but Euler is standard for games/sims usually.
            // Using updated speed and forward
            pos += fwd * state.Speed * dt;
            
            // 5. Update state metadata
            state.SteerAngle = (Fdp.Core.Angle)(steerAngle);
            state.Accel = accel;
            return angularVel;
        }

        /// <summary>
        /// ⭐ <c>CE-4345</c> — the largest wheel angle at <paramref name="speed"/>: <paramref name="maxSteer"/>, and no more than the angle whose
        /// lateral acceleration v²·tan δ / L reaches <paramref name="maxLatAccel"/> ⇒ the turning radius L / tan δ ≥ v² / a. A slow vehicle turns
        /// on full lock; a fast one turns wide. <paramref name="maxLatAccel"/> ≤ 0 = no limit.
        /// </summary>
        public static float SteerLimit(float speed, float wheelBase, float maxLatAccel, float maxSteer)
        {
            float v2 = speed * speed;
            if (maxLatAccel <= 0f || wheelBase <= 0f || v2 < 1e-6f) return maxSteer;
            return MathF.Min(maxSteer, MathF.Atan(maxLatAccel * wheelBase / v2));
        }

        /// <summary>⭐ <c>CE-4345</c> — the fastest a vehicle may go with its wheel at <paramref name="steer"/> (the inverse of <see cref="SteerLimit"/>):
        /// √(a·L / |tan δ|); unlimited for a straight wheel or no limit.</summary>
        public static float MaxCorneringSpeed(float steer, float wheelBase, float maxLatAccel)
        {
            float t = MathF.Abs(MathF.Tan(steer));
            if (t < 1e-4f || maxLatAccel <= 0f || wheelBase <= 0f) return float.MaxValue;
            return MathF.Sqrt(maxLatAccel * wheelBase / t);
        }
    }
}
