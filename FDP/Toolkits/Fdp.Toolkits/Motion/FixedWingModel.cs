using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §6, A1/B2/B9; Motion Domains MD-H's "fail loudly" row, now
    /// built) — a PLANE, "faked to look believable": it never stops and never flies slower than <c>StallSpeedMps</c>.
    /// <b>Autonomous</b> — a route (<c>FollowRoute</c>): its <see cref="AirPath"/> (the same compiled path a helicopter flies, rounded to
    /// cruise² / (g·tan max bank)) flown kinematically at <c>CruiseSpeedMps</c> (or the route's / the intent's speed, clamped to
    /// [stall, top]), banked atan(v²κ/g) eased by a roll rate, pitched to its climb; at the end it orbits the last point (planes do not
    /// land in v1 — a Ground waypoint is flown over, <see cref="AirMotionSystem"/> warns). A point (<c>MoveTo</c>): it turns toward it at
    /// its steepest bank and circles it once there (Arrived). No order: straight and level.
    /// <b>Manual</b>: slice 6 (the mouse-aim pilot, E5) — until then it flies straight and level at cruise.
    /// A dead plane falls; a plane down (<c>Airborne = 0</c>) stays down.
    /// </summary>
    public sealed class FixedWingModel : IMotionModel
    {
        /// <summary>How fast the bank follows its target (deg/s).</summary>
        public const float RollRateDeg = 60f;
        /// <summary>Along-path acceleration (m/s²).</summary>
        public const float AccelMps2 = 5f;
        public const float MaxPitchDeg = 25f;

        public LocomotionClass Class => LocomotionClass.FixedWing;

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            var p = Params(f);
            if (IsDown(f)) { f.Velocity.Linear = Vector3.Zero; return; }
            if (!Alive(f)) { Fall(ref f, dt); return; }
            SetAirborne(ref f);

            if (intent.Mode == NavigationMode.FollowRoute)
            {
                var path = PathOf(f);
                if (path != null && path.IntentId == intent.IntentId && path.Points.Length > 0) { FollowRoute(ref f, in intent, in p, path, dt); return; }
                Straight(ref f, in p, dt);
                Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.AwaitingPath, 0f);
                return;
            }
            if (intent.Mode == NavigationMode.None || intent.Mode == NavigationMode.Manual) { Straight(ref f, in p, dt); return; }
            ToPoint(ref f, in intent, in p, dt);
        }

        public void Manual(ref MotionFrame f, in ManualDrive d, float dt)
        {
            var p = Params(f);
            if (IsDown(f)) { f.Velocity.Linear = Vector3.Zero; return; }
            if (!Alive(f)) { Fall(ref f, dt); return; }
            Straight(ref f, in p, dt);   // ⏸ slice 6 (E5): the mouse-aim pilot
        }

        // ── a route ───────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void FollowRoute(ref MotionFrame f, in NavigationIntent intent, in FlightParams p, AirPath path, float dt)
        {
            float speed = Speed(in p, in intent, path.Stage == AirStage.Start ? HorizontalSpeed(f) : path.SpeedNow, dt, AirRoute.Sample(path, path.Progress).Speed);
            path.SpeedNow = speed;
            if (path.Stage == AirStage.Start) path.Stage = AirStage.Flying;

            if (path.Stage == AirStage.Done) { Orbit(ref f, in p, path, speed, dt); Report(ref f, intent, NavigationResult.Arrived, NavigationPhase.Completed, path.Progress); return; }

            path.Progress += speed * dt;
            if (path.Progress >= path.Length)
            {
                if (path.Loop && path.Length > 0f) path.Progress -= path.Length;
                else
                {
                    // the end: a left-hand circle beside the last point, entered with this frame's leftover travel (no short step)
                    float leftover = path.Progress - path.Length;
                    path.Progress = path.Length;
                    var end = AirRoute.Sample(path, path.Length);
                    var left = new Vector2(-end.Heading.Y, end.Heading.X);
                    float r = MathF.Max(1f, p.TurnRadius);
                    path.OrbitCenter = new Vector2(end.Point.X, end.Point.Y) + left * r;
                    path.OrbitAngle = MathF.Atan2(end.Point.Y - path.OrbitCenter.Y, end.Point.X - path.OrbitCenter.X);
                    path.Stage = AirStage.Done;
                    Orbit(ref f, in p, path, leftover / MathF.Max(dt, 1e-4f), dt);
                    Report(ref f, intent, NavigationResult.Arrived, NavigationPhase.Completed, path.Progress);
                    return;
                }
            }

            var at = AirRoute.Sample(path, path.Progress);
            var pos = f.Transform.Position;
            float dz = Math.Clamp(at.Point.Z - pos.Z, -p.ClimbMps * dt, p.ClimbMps * dt);
            var next = new Vector3(at.Point.X, at.Point.Y, pos.Z + dz);
            float yaw = MathF.Atan2(at.Heading.Y, at.Heading.X);
            Bank(path, in p, -MathF.Atan(speed * speed * at.Curvature / RotorcraftModel.Gravity), dt);
            Pose(ref f, next, yaw, MathF.Atan2(dz / MathF.Max(dt, 1e-4f), MathF.Max(speed, 1f)), path.Roll, dt);
            Report(ref f, intent, path.Stage == AirStage.Done ? NavigationResult.Arrived : NavigationResult.InProgress,
                   path.Stage == AirStage.Done ? NavigationPhase.Completed : NavigationPhase.Following, path.Progress);
        }

        /// <summary>After the route: a left-hand circle of the turn radius beside the last point, at its height.</summary>
        private static void Orbit(ref MotionFrame f, in FlightParams p, AirPath path, float speed, float dt)
        {
            float r = MathF.Max(1f, p.TurnRadius);
            path.OrbitAngle += speed / r * dt;
            var c = path.OrbitCenter;
            var next = new Vector3(c.X + r * MathF.Cos(path.OrbitAngle), c.Y + r * MathF.Sin(path.OrbitAngle), f.Transform.Position.Z);
            Bank(path, in p, -MathF.Atan(speed * speed / r / RotorcraftModel.Gravity), dt);
            Pose(ref f, next, path.OrbitAngle + MathF.PI / 2f, 0f, path.Roll, dt);
        }

        // ── a point, or nothing ───────────────────────────────────────────────────────────────────────────────────────────────

        private static void ToPoint(ref MotionFrame f, in NavigationIntent intent, in FlightParams p, float dt)
        {
            var pos = f.Transform.Position;
            float speed = Speed(in p, in intent, HorizontalSpeed(f), dt, 0f);
            float maxBank = MaxBank(in p);
            float rate = RotorcraftModel.Gravity * MathF.Tan(maxBank) / MathF.Max(speed, 1f);   // rad/s at the steepest bank
            float yaw = RotorcraftModel.YawOf(f.Transform.Rotation);
            var to = new Vector2(intent.FinalDestination.X - pos.X, intent.FinalDestination.Y - pos.Y);
            bool arrived = to.Length() <= MathF.Max(intent.ArrivalRadius, p.TurnRadius);
            float want = arrived ? yaw + MathF.PI / 2f : MathF.Atan2(to.Y, to.X);   // circle left once there
            float newYaw = RotorcraftModel.TurnToward(yaw, want, rate * 180f / MathF.PI, dt);
            float turnRate = (newYaw - yaw) / MathF.Max(dt, 1e-4f);
            float roll = -MathF.Atan(speed * turnRate / RotorcraftModel.Gravity);
            float dz = Math.Clamp(intent.FinalDestination.Z - pos.Z, -p.ClimbMps * dt, p.ClimbMps * dt);
            var next = pos + new Vector3(MathF.Cos(newYaw) * speed * dt, MathF.Sin(newYaw) * speed * dt, dz);
            Pose(ref f, next, newYaw, MathF.Atan2(dz / MathF.Max(dt, 1e-4f), speed), EaseRoll(f, roll, in p, dt), dt);
            Report(ref f, intent, arrived ? NavigationResult.Arrived : NavigationResult.InProgress,
                   arrived ? NavigationPhase.Completed : NavigationPhase.Following, 0f);
        }

        private static void Straight(ref MotionFrame f, in FlightParams p, float dt)
        {
            float speed = Math.Clamp(HorizontalSpeed(f) > 0.1f ? HorizontalSpeed(f) : Cruise(in p), Stall(in p), Top(in p));
            float yaw = RotorcraftModel.YawOf(f.Transform.Rotation);
            var next = f.Transform.Position + new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * speed * dt;
            Pose(ref f, next, yaw, 0f, EaseRoll(f, 0f, in p, dt), dt);
        }

        // ── shared ────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The speed this frame: the route's, else the intent's, else cruise — never below stall nor above top, eased.</summary>
        private static float Speed(in FlightParams p, in NavigationIntent intent, float now, float dt, float routeSpeed)
        {
            float want = routeSpeed > 0f ? routeSpeed : intent.TargetSpeed > 0f ? intent.TargetSpeed : Cruise(in p);
            want = Math.Clamp(want, Stall(in p), Top(in p));
            if (now <= 0.1f) now = want;   // a plane starts the order already flying
            return Math.Clamp(now + Math.Clamp(want - now, -AccelMps2 * dt, AccelMps2 * dt), Stall(in p), Top(in p));
        }

        private static void Bank(AirPath path, in FlightParams p, float target, float dt)
        {
            float max = MaxBank(in p), rate = RollRateDeg * MathF.PI / 180f * dt;
            path.Roll += Math.Clamp(Math.Clamp(target, -max, max) - path.Roll, -rate, rate);
        }

        private static float EaseRoll(in MotionFrame f, float target, in FlightParams p, float dt)
        {
            var left = Vector3.Transform(Vector3.UnitY, f.Transform.Rotation);
            float now = MathF.Asin(Math.Clamp(left.Z, -1f, 1f));   // left side up +
            float max = MaxBank(in p), rate = RollRateDeg * MathF.PI / 180f * dt;
            return now + Math.Clamp(Math.Clamp(target, -max, max) - now, -rate, rate);
        }

        private static void Pose(ref MotionFrame f, Vector3 next, float yaw, float pitch, float roll, float dt)
        {
            float maxPitch = MaxPitchDeg * MathF.PI / 180f;
            f.Velocity.Linear = (next - f.Transform.Position) / MathF.Max(dt, 1e-4f);
            f.Transform.Position = next;
            f.Transform.Rotation = BodyGeometry.Rotation(yaw, new RestingPose(0f, Math.Clamp(pitch, -maxPitch, maxPitch), roll));
            if (f.World.IsComponentTypeRegistered<FlightState>() && f.World.HasComponent<FlightState>(f.Self))
                f.World.GetComponentRW<FlightState>(f.Self).VerticalSpeed = f.Velocity.Linear.Z;
        }

        private static void Fall(ref MotionFrame f, float dt)
        {
            var v = f.Velocity.Linear;
            v.Z -= RotorcraftModel.Gravity * dt;
            f.Velocity.Linear = v;
            f.Transform.Position += v * dt;
        }

        private static float Cruise(in FlightParams p) => p.CruiseSpeedMps > 0f ? p.CruiseSpeedMps : p.MaxSpeedMps;
        private static float Stall(in FlightParams p) => MathF.Min(p.StallSpeedMps, Top(in p));
        private static float Top(in FlightParams p) => MathF.Max(p.MaxSpeedMps, p.CruiseSpeedMps);
        private static float MaxBank(in FlightParams p) => (p.MaxBankDeg > 0f ? p.MaxBankDeg : 60f) * MathF.PI / 180f;

        private static float HorizontalSpeed(in MotionFrame f) => new Vector2(f.Velocity.Linear.X, f.Velocity.Linear.Y).Length();

        private static bool IsDown(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<FlightState>() && f.World.HasComponent<FlightState>(f.Self)
               && f.World.GetComponentRO<FlightState>(f.Self).Airborne == 0;

        private static void SetAirborne(ref MotionFrame f)
        {
            if (f.World.IsComponentTypeRegistered<FlightState>() && f.World.HasComponent<FlightState>(f.Self))
                f.World.GetComponentRW<FlightState>(f.Self).Airborne = 1;
        }

        private static bool Alive(in MotionFrame f)
        {
            var w = f.World;
            return !w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || !w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(f.Self)
                   || w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(f.Self).Current > 0f;
        }

        private static AirPath? PathOf(in MotionFrame f)
            => f.World.TryGetTable(typeof(AirPath), out _) && f.World.HasManagedComponent<AirPath>(f.Self)
               ? ((ISimulationView)f.World).GetManagedComponentRO<AirPath>(f.Self) : null;

        private static FlightParams Params(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<FlightParams>() && f.World.HasComponent<FlightParams>(f.Self)
                ? f.World.GetComponentRO<FlightParams>(f.Self)
                : FlightParams.From(new FlightParamsDto());

        private static void Report(ref MotionFrame f, in NavigationIntent intent, NavigationResult result, NavigationPhase phase, float progress)
        {
            if (!f.World.IsComponentTypeRegistered<NavigationStatus>() || !f.World.HasComponent<NavigationStatus>(f.Self)) return;
            ref var s = ref f.World.GetComponentRW<NavigationStatus>(f.Self);
            s.IntentId = intent.IntentId;
            s.Result = result;
            s.Phase = phase;
            s.ProgressS = progress;
        }
    }
}
