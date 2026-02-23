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
        public static void Integrate(
            ref Vector2 pos,
            ref Vector2 fwd,
            ref VehicleState state,
            float steerAngle,
            float accel,
            float dt,
            float wheelBase)
        {
            // 1. Update speed
            state.Speed += accel * dt;
            
            // QA FIX #3: No reverse driving (deadlock prevention)
            if (state.Speed < 0f)
                state.Speed = 0f;
            
            // 2. Calculate angular velocity (yaw rate)
            // omega = (v / L) * tan(delta)
            float angularVel = (state.Speed / wheelBase) * MathF.Tan(steerAngle);
            
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
            state.SteerAngle = steerAngle;
            state.Accel = accel;
        }
    }
}
