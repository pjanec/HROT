using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ M7 / CE-2168 (DESIGN_FPV_Drone §2, Motion Domains MD-H) — the first air motion model: a POINT MASS that hovers, climbs and
    /// yaws. <b>Autonomous</b>: toward the intent's 3-D point, turning ≤ <c>TurnRateDeg</c>, climbing ≤ <c>ClimbMps</c>, at
    /// min(<c>TargetSpeed</c>, <c>MaxSpeedMps</c>); within the arrival radius (2 m unless the intent states one) it stops and reports
    /// <c>Arrived</c> on <see cref="NavigationStatus"/>, so <c>LocomotionMoveTo.Status</c> works unchanged. No intent ⇒ it hovers.
    /// <b>Manual</b>: <see cref="ManualDrive"/> Forward / Lateral in the body frame × top speed, Yaw = the absolute heading it turns to,
    /// Vertical × climb rate. A DEAD rotorcraft (<c>Health</c> ≤ 0) falls under gravity whatever it is told. It owns its Z (MD-G): it is
    /// never clamped to the ground. No aerodynamics.
    /// </summary>
    public sealed class RotorcraftModel : IMotionModel
    {
        public const float DefaultArrivalRadiusM = 2f;
        public const float Gravity = 9.81f;

        public LocomotionClass Class => LocomotionClass.Rotorcraft;

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            var p = Params(f);
            if (Landed(ref f)) return;
            if (Falling(ref f, dt)) return;
            if (intent.Mode == NavigationMode.None || intent.Mode == NavigationMode.Manual) { Hover(ref f, p, dt); return; }

            var to = intent.FinalDestination - f.Transform.Position;
            float arrive = intent.ArrivalRadius > 0f ? intent.ArrivalRadius : DefaultArrivalRadiusM;
            if (to.Length() <= arrive)
            {
                f.Velocity.Linear = Vector3.Zero;
                SetVertical(ref f, 0f);
                Report(ref f, intent, NavigationResult.Arrived, NavigationPhase.Completed);
                return;
            }

            float yaw = YawOf(f.Transform.Rotation);
            var flat = new Vector2(to.X, to.Y);
            if (flat.LengthSquared() > 1e-6f) yaw = TurnToward(yaw, MathF.Atan2(flat.Y, flat.X), p.TurnRateDeg, dt);
            float speed = MathF.Min(intent.TargetSpeed > 0f ? intent.TargetSpeed : p.MaxSpeedMps, p.MaxSpeedMps);
            // a point mass flies along its nose; it does not overshoot a point closer than one frame's travel
            float along = MathF.Min(speed, flat.Length() / MathF.Max(dt, 1e-4f));
            float vz = Math.Clamp(to.Z / MathF.Max(dt, 1e-4f), -p.ClimbMps, p.ClimbMps);
            Fly(ref f, yaw, new Vector3(MathF.Cos(yaw) * along, MathF.Sin(yaw) * along, vz), dt);
            Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.Following);
        }

        public void Manual(ref MotionFrame f, in ManualDrive d, float dt)
        {
            var p = Params(f);
            if (Landed(ref f)) return;
            if (Falling(ref f, dt)) return;
            float yaw = TurnToward(YawOf(f.Transform.Rotation), d.Yaw, p.TurnRateDeg, dt);
            float fwd = Math.Clamp(d.Forward, -1f, 1f) * p.MaxSpeedMps, lat = Math.Clamp(d.Lateral, -1f, 1f) * p.MaxSpeedMps;
            var v = new Vector3(MathF.Cos(yaw) * fwd - MathF.Sin(yaw) * lat, MathF.Sin(yaw) * fwd + MathF.Cos(yaw) * lat,
                                Math.Clamp(d.Vertical, -1f, 1f) * p.ClimbMps);
            Fly(ref f, yaw, v, dt);
        }

        private static void Hover(ref MotionFrame f, in FlightParams p, float dt)
        {
            f.Velocity.Linear = Vector3.Zero;
            SetVertical(ref f, 0f);
        }

        private static void Fly(ref MotionFrame f, float yaw, Vector3 v, float dt)
        {
            f.Velocity.Linear = v;
            f.Transform.Position += v * dt;
            f.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);
            SetVertical(ref f, v.Z);
        }

        /// <summary>A dead rotorcraft falls (its horizontal speed decays); true when it took over the frame.</summary>
        private static bool Falling(ref MotionFrame f, float dt)
        {
            var w = f.World;
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || !w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(f.Self)
                || w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(f.Self).Current > 0f) return false;
            var v = f.Velocity.Linear;
            float decay = MathF.Max(0f, 1f - 0.5f * dt);
            v = new Vector3(v.X * decay, v.Y * decay, v.Z - Gravity * dt);
            f.Velocity.Linear = v;
            f.Transform.Position += v * dt;
            SetVertical(ref f, v.Z);
            return true;
        }

        /// <summary>A flyer that has met something (<c>DroneImpactSystem</c>: <c>Airborne = 0</c>) stays where it is.</summary>
        private static bool Landed(ref MotionFrame f)
        {
            if (!f.World.IsComponentTypeRegistered<FlightState>() || !f.World.HasComponent<FlightState>(f.Self)
                || f.World.GetComponentRO<FlightState>(f.Self).Airborne != 0) return false;
            f.Velocity.Linear = Vector3.Zero;
            return true;
        }

        private static FlightParams Params(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<FlightParams>() && f.World.HasComponent<FlightParams>(f.Self)
                ? f.World.GetComponentRO<FlightParams>(f.Self)
                : FlightParams.From(new FlightParamsDto());

        private static void SetVertical(ref MotionFrame f, float vz)
        {
            if (!f.World.IsComponentTypeRegistered<FlightState>() || !f.World.HasComponent<FlightState>(f.Self)) return;
            ref var s = ref f.World.GetComponentRW<FlightState>(f.Self);
            s.VerticalSpeed = vz;
        }

        private static void Report(ref MotionFrame f, in NavigationIntent intent, NavigationResult result, NavigationPhase phase)
        {
            if (!f.World.IsComponentTypeRegistered<NavigationStatus>() || !f.World.HasComponent<NavigationStatus>(f.Self)) return;
            ref var s = ref f.World.GetComponentRW<NavigationStatus>(f.Self);
            s.IntentId = intent.IntentId;
            s.Result = result;
            s.Phase = phase;
        }

        /// <summary>The heading (radians, X-forward, Z-up) of a rotation.</summary>
        public static float YawOf(Quaternion q)
        {
            var fwd = Vector3.Transform(Vector3.UnitX, q);
            return MathF.Atan2(fwd.Y, fwd.X);
        }

        /// <summary><paramref name="from"/> turned toward <paramref name="to"/> by at most <paramref name="rateDeg"/>·dt.</summary>
        public static float TurnToward(float from, float to, float rateDeg, float dt)
        {
            float diff = MathF.IEEERemainder(to - from, 2f * MathF.PI);
            float max = rateDeg * MathF.PI / 180f * dt;
            return from + Math.Clamp(diff, -max, max);
        }
    }
}
