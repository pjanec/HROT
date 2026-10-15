using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
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
    /// Vertical × climb rate; no axis ⇒ it holds its hover; it tilts as a route draws it (nose down forward, rolled into a strafe or a
    /// turn — slice 6, E5). A DEAD rotorcraft (<c>Health</c> ≤ 0) falls under gravity whatever it is told. It owns its Z (MD-G): it is
    /// never clamped to the ground. No aerodynamics.
    /// <para>⭐⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, §6, as-built §5b) — <b>a route</b>
    /// (<c>FollowRoute</c>): it flies its <see cref="AirPath"/> exactly, its speed eased by an acceleration limit, slowed for the curves
    /// and for the next stop; the nose pitches against the acceleration and it rolls into the turn. A body with landing gear
    /// (<c>Body.Geometry</c> ground contacts) takes off from the ground, lands on a Ground waypoint onto its gear
    /// (<see cref="BodyGeometry.RestingPose"/>, <c>Airborne = 0</c>), and holds where the route says: Seconds timed here, Unload / Pickup
    /// until its Brain sets <see cref="NavigationIntent.ReleaseHold"/>. A body without gear that is down stays down (a spent drone).</para>
    /// </summary>
    public sealed class RotorcraftModel : IMotionModel
    {
        public const float DefaultArrivalRadiusM = 2f;
        public const float Gravity = 9.81f;

        /// <summary>Acceleration and braking, m/s², where the type states none (<c>Gen.Flight AccelMps2</c>, R-3007) — a helicopter eases
        /// into and out of its speed; an FPV drone states its own (15).</summary>
        public const float AccelMps2 = 3f;

        /// <summary>The type's acceleration (R-3007), else <see cref="AccelMps2"/>.</summary>
        public static float Accel(in FlightParams p) => p.AccelMps2 > 0f ? p.AccelMps2 : AccelMps2;
        /// <summary>The bank a route's curves are flown at most — it sets the speed for a curve: v² κ ≤ g tan(bank).</summary>
        public const float MaxBankDeg = 30f;
        public const float MaxPitchDeg = 20f;
        /// <summary>How fast the nose and the roll follow their targets.</summary>
        public const float TiltRateDeg = 40f;
        /// <summary>The final descent onto the gear, m/s (or the climb rate when slower).</summary>
        public const float LandingMps = 2.5f;

        public LocomotionClass Class => LocomotionClass.Rotorcraft;

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            var p = Params(f);
            if (StaysDown(ref f)) return;
            if (Falling(ref f, dt)) return;
            if (intent.Mode == NavigationMode.None || intent.Mode == NavigationMode.Manual) { Hover(ref f, p, dt); return; }

            if (intent.Mode == NavigationMode.FollowRoute)
            {
                var path = PathOf(f);
                if (path != null && path.IntentId == intent.IntentId) FollowRoute(ref f, in intent, in p, path, dt);
                else { Hover(ref f, p, dt); Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.AwaitingPath, 0f); }
                return;
            }

            // a point move from the ground: straight up to the hover height first
            if (OnGear(ref f)) { SetAirborne(ref f, 1); Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.TakingOff, 0f); }
            if (Status(f).IntentId == intent.IntentId && Status(f).Phase == NavigationPhase.TakingOff)
            {
                float top = f.Ground(f.Transform.Position.X, f.Transform.Position.Y) + p.HoverHeightM;
                if (f.Transform.Position.Z < top - 0.05f)
                {
                    float vzUp = MathF.Min(p.ClimbMps, (top - f.Transform.Position.Z) / MathF.Max(dt, 1e-4f));
                    Fly(ref f, YawOf(f.Transform.Rotation), new Vector3(0f, 0f, vzUp), dt);
                    return;
                }
            }

            var to = intent.FinalDestination - f.Transform.Position;
            float arrive = intent.ArrivalRadius > 0f ? intent.ArrivalRadius : DefaultArrivalRadiusM;
            if (to.Length() <= arrive)
            {
                f.Velocity.Linear = Vector3.Zero;
                SetVertical(ref f, 0f);
                Report(ref f, intent, NavigationResult.Arrived, NavigationPhase.Completed, 0f);
                return;
            }

            float yaw = YawOf(f.Transform.Rotation);
            var flat = new Vector2(to.X, to.Y);
            if (flat.LengthSquared() > 1e-6f) yaw = TurnToward(yaw, MathF.Atan2(flat.Y, flat.X), p.TurnRateDeg, dt);
            float speed = MathF.Min(intent.TargetSpeed > 0f ? intent.TargetSpeed : p.MaxSpeedMps, p.MaxSpeedMps);
            // a point mass flies along its nose; it does not overshoot a point closer than one frame's travel. ⭐ R-3007 — it eases
            //   into its speed and brakes for the point at the type's acceleration (√(2·a·d)), climbing and descending likewise
            float a = Accel(in p), step = a * dt, inv = 1f / MathF.Max(dt, 1e-4f);
            var now = f.Velocity.Linear;
            float alongWant = MathF.Min(MathF.Min(speed, MathF.Sqrt(2f * a * flat.Length())), flat.Length() * inv);
            float hNow = new Vector2(now.X, now.Y).Length();
            float along = MathF.Min(hNow + Math.Clamp(alongWant - hNow, -step, step), flat.Length() * inv);
            float vzWant = Math.Clamp(to.Z * inv, -p.ClimbMps, p.ClimbMps);
            vzWant = MathF.Sign(vzWant) * MathF.Min(MathF.Abs(vzWant), MathF.Sqrt(2f * a * MathF.Abs(to.Z)));
            float vz = now.Z + Math.Clamp(vzWant - now.Z, -step, step);
            if (MathF.Abs(vz) > MathF.Abs(to.Z) * inv) vz = to.Z * inv;
            Fly(ref f, yaw, new Vector3(MathF.Cos(yaw) * along, MathF.Sin(yaw) * along, vz), dt);
            Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.Following, 0f);
        }

        public void Manual(ref MotionFrame f, in ManualDrive d, float dt)
        {
            var p = Params(f);
            if (StaysDown(ref f)) return;
            if (Falling(ref f, dt)) return;
            if (OnGear(ref f))
            {
                if (d.Vertical <= 0f) { f.Velocity.Linear = Vector3.Zero; return; }   // on the ground until the collective comes up
                SetAirborne(ref f, 1);
            }
            float before = YawOf(f.Transform.Rotation);
            float yaw = TurnToward(before, d.Yaw, p.TurnRateDeg, dt);
            float fwdWant = Math.Clamp(d.Forward, -1f, 1f) * p.MaxSpeedMps, latWant = Math.Clamp(d.Lateral, -1f, 1f) * p.MaxSpeedMps;
            var want = new Vector3(MathF.Cos(yaw) * fwdWant - MathF.Sin(yaw) * latWant, MathF.Sin(yaw) * fwdWant + MathF.Cos(yaw) * latWant,
                                   Math.Clamp(d.Vertical, -1f, 1f) * p.ClimbMps);
            // ⭐ R-3007 — the axes ask for a velocity; the body reaches it at the type's acceleration (a UH-60 3 m/s², an FPV drone 15).
            //   Keys released ⇒ it brakes to a hover and holds it there (the hover hold)
            var v = f.Velocity.Linear;
            float step = Accel(in p) * dt;
            var dh = new Vector2(want.X - v.X, want.Y - v.Y);
            if (dh.Length() > step) dh *= step / dh.Length();
            v = new Vector3(v.X + dh.X, v.Y + dh.Y, v.Z + Math.Clamp(want.Z - v.Z, -step, step));
            // the body-frame speeds the tilt is drawn from
            float fwd = MathF.Cos(yaw) * v.X + MathF.Sin(yaw) * v.Y, lat = -MathF.Sin(yaw) * v.X + MathF.Cos(yaw) * v.Y;
            // ⭐ Air Ops slice 6 (E5, §6 B3) — the same tilt a route draws: the nose down as it flies forward, the roll into a strafe
            //   and into the turn; level again in the hover the released keys leave it in
            float yawRate = MathF.IEEERemainder(yaw - before, 2f * MathF.PI) / MathF.Max(dt, 1e-4f);
            float top = MathF.Max(p.MaxSpeedMps, 1f);
            float pitchTarget = -MaxPitchDeg * MathF.PI / 180f * fwd / top;
            float rollTarget = -MaxBankDeg * MathF.PI / 180f * lat / top - MathF.Atan(fwd * yawRate / Gravity);
            var (pitchNow, rollNow) = TiltOf(f.Transform.Rotation);
            float maxPitch = MaxPitchDeg * MathF.PI / 180f, maxRoll = MaxBankDeg * MathF.PI / 180f, tiltStep = TiltRateDeg * MathF.PI / 180f * dt;
            float pitch = pitchNow + Math.Clamp(Math.Clamp(pitchTarget, -maxPitch, maxPitch) - pitchNow, -tiltStep, tiltStep);
            float roll = rollNow + Math.Clamp(Math.Clamp(rollTarget, -maxRoll, maxRoll) - rollNow, -tiltStep, tiltStep);
            Fly(ref f, yaw, v, dt);
            f.Transform.Rotation = BodyGeometry.Rotation(yaw, new RestingPose(0f, pitch, roll));
            // a body with gear that comes down onto the ground sets down on it (no crash for a gentle landing)
            if (v.Z < 0f && Geometry(f) is { } geo)
            {
                var pose = RestOn(ref f, geo, yaw);
                if (f.Transform.Position.Z <= pose.Z) TouchDown(ref f, yaw, pose);
            }
        }

        // ── the route ─────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void FollowRoute(ref MotionFrame f, in NavigationIntent intent, in FlightParams p, AirPath path, float dt)
        {
            if (!intent.ReleaseHold) path.ReleaseArmed = true;
            float yaw = YawOf(f.Transform.Rotation);

            if (path.Stage == AirStage.Start)
            {
                bool onGround = OnGear(ref f);
                if (path.Stops.Length > 0 && path.Stops[0].S <= 0.5f && AirRoute.IsFlownThrough(in path.Stops[0]) && !onGround)
                {
                    path.Dropping = true;   // ⭐ B-3 — the route starts on its DZ leg
                    path.ReleaseArmed = !intent.ReleaseHold;
                    path.NextStop = 1;
                    path.Stage = AirStage.Flying;
                }
                else if (path.Stops.Length > 0 && path.Stops[0].S <= 0.5f)
                    path.Stage = onGround || path.Stops[0].Kind != RouteWaypointKind.Ground ? BeginHold(path, path.Stops[0]) : AirStage.Landing;
                else path.Stage = onGround ? AirStage.TakingOff : AirStage.Flying;
            }

            switch (path.Stage)
            {
                case AirStage.TakingOff:
                {
                    SetAirborne(ref f, 1);
                    float top = AirRoute.Sample(path, path.Progress).Point.Z;
                    float vz = MathF.Min(p.ClimbMps, MathF.Max(0f, top - f.Transform.Position.Z) / MathF.Max(dt, 1e-4f));
                    Level(path, dt);
                    Fly(ref f, yaw, new Vector3(0f, 0f, vz), dt, path);
                    if (f.Transform.Position.Z >= top - 0.05f) path.Stage = AirStage.Flying;
                    Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.TakingOff, path.Progress);
                    return;
                }
                case AirStage.Landing:
                {
                    var geo = Geometry(f);
                    var pose = geo != null ? RestOn(ref f, geo, yaw) : new RestingPose(f.Ground(f.Transform.Position.X, f.Transform.Position.Y), 0f, 0f);
                    Level(path, dt);
                    float down = MathF.Min(LandingMps, p.ClimbMps);
                    Fly(ref f, yaw, new Vector3(0f, 0f, -MathF.Min(down, MathF.Max(0f, f.Transform.Position.Z - pose.Z) / MathF.Max(dt, 1e-4f))), dt, path);
                    if (f.Transform.Position.Z <= pose.Z + 1e-3f)
                    {
                        TouchDown(ref f, yaw, pose);
                        path.Pitch = path.Roll = 0f;
                        path.Stage = BeginHold(path, path.Stops[path.NextStop]);
                    }
                    Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.Landing, path.Progress);
                    return;
                }
                case AirStage.Holding:
                {
                    var stop = path.Stops[path.NextStop];
                    f.Velocity.Linear = Vector3.Zero;
                    SetVertical(ref f, 0f);
                    bool released = stop.Hold switch
                    {
                        RouteHold.Seconds => (path.HoldLeft -= dt) <= 0f,
                        RouteHold.Unload or RouteHold.Pickup or RouteHold.Rope => intent.ReleaseHold && path.ReleaseArmed,
                        _ => true,
                    };
                    bool onGround = OnGear(ref f);
                    if (released)
                    {
                        if (stop.Hold is RouteHold.Unload or RouteHold.Pickup or RouteHold.Rope) path.ReleaseArmed = false;
                        if (stop.Last) path.Stage = AirStage.Done;
                        else { path.NextStop++; path.Stage = onGround ? AirStage.TakingOff : AirStage.Flying; }
                    }
                    if (path.Stage == AirStage.Done) { Report(ref f, intent, NavigationResult.Arrived, onGround ? NavigationPhase.Landed : NavigationPhase.Completed, path.Progress); return; }
                    var phase = stop.Hold switch
                    {
                        RouteHold.Unload => NavigationPhase.Unloading,
                        RouteHold.Pickup => NavigationPhase.Loading,
                        RouteHold.Rope => NavigationPhase.Roping,
                        RouteHold.Drop when path.Dropping => NavigationPhase.Dropping,   // a Drop at the route's end: the hover goes on dropping
                        _ => onGround ? NavigationPhase.Landed : NavigationPhase.Following,
                    };
                    Report(ref f, intent, NavigationResult.InProgress, released ? NavigationPhase.Following : phase, path.Progress);
                    return;
                }
                case AirStage.Done:
                    f.Velocity.Linear = Vector3.Zero;
                    SetVertical(ref f, 0f);
                    ReleaseDrop(in intent, path);
                    Report(ref f, intent, NavigationResult.Arrived,
                           path.Dropping ? NavigationPhase.Dropping : OnGear(ref f) ? NavigationPhase.Landed : NavigationPhase.Completed, path.Progress);
                    return;
            }
            ReleaseDrop(in intent, path);

            // ── Flying: along the path, the speed eased; slowed for the curve ahead and for the next stop ──
            bool hasStop = path.NextStop < path.Stops.Length;
            var next = hasStop ? path.Stops[path.NextStop] : default;
            var here = AirRoute.Sample(path, path.Progress);
            float vt = here.Speed > 0f ? here.Speed : (intent.TargetSpeed > 0f ? intent.TargetSpeed : p.MaxSpeedMps);
            vt = MathF.Min(vt, p.MaxSpeedMps);
            float lateral = Gravity * MathF.Tan(MaxBankDeg * MathF.PI / 180f);
            float accelMax = Accel(in p);
            float look = path.SpeedNow * path.SpeedNow / (2f * accelMax) + 5f;
            float kAhead = AirRoute.MaxCurvature(path, path.Progress, path.Progress + look);
            if (kAhead > 1e-5f) vt = MathF.Min(vt, MathF.Sqrt(lateral / kAhead));
            if (hasStop && !AirRoute.IsFlownThrough(in next)) vt = MathF.Min(vt, MathF.Sqrt(2f * accelMax * MathF.Max(0f, next.S - path.Progress)));
            float dv = Math.Clamp(vt - path.SpeedNow, -2f * accelMax * dt, accelMax * dt);
            float accel = dv / MathF.Max(dt, 1e-4f);
            path.SpeedNow = MathF.Max(0f, path.SpeedNow + dv);
            path.Progress += path.SpeedNow * dt;

            bool arrived = false;
            if (hasStop && path.Progress >= next.S - 0.05f) { path.Progress = next.S; arrived = true; }
            else if (!hasStop && path.Progress >= path.Length)
            {
                if (path.Loop && path.Length > 0f) { path.Progress -= path.Length; path.NextStop = 0; }
                else path.Progress = path.Length;
            }

            var at = AirRoute.Sample(path, path.Progress);
            var pos = f.Transform.Position;
            float dz = Math.Clamp(at.Point.Z - pos.Z, -p.ClimbMps * dt, p.ClimbMps * dt);
            var newPos = new Vector3(at.Point.X, at.Point.Y, pos.Z + dz);
            yaw = MathF.Atan2(at.Heading.Y, at.Heading.X);

            // the attitude: the nose down while it accelerates, the roll into the turn (left turn ⇒ left side down)
            float pitchTarget = -MathF.Atan(accel / Gravity);
            float rollTarget = -MathF.Atan(path.SpeedNow * path.SpeedNow * at.Curvature / Gravity);
            Tilt(path, pitchTarget, rollTarget, dt);

            f.Velocity.Linear = (newPos - pos) / MathF.Max(dt, 1e-4f);
            f.Transform.Position = newPos;
            f.Transform.Rotation = BodyGeometry.Rotation(yaw, new RestingPose(0f, path.Pitch, path.Roll));
            SetVertical(ref f, f.Velocity.Linear.Z);

            if (arrived && AirRoute.IsFlownThrough(in next))
            {
                // ⭐ airborne insertion B-3 (D-7) — the DZ leg begins: flown on, at its speed, reporting Dropping until the Brain releases it
                path.Dropping = true;
                path.ReleaseArmed = !intent.ReleaseHold;
                path.NextStop++;
            }
            else if (arrived)
            {
                path.SpeedNow = 0f;
                path.Dropping = next.Hold == RouteHold.Drop;   // a real stop ends the DZ leg; a route ending on its DZ hovers there, still dropping
                path.Stage = next.Kind == RouteWaypointKind.Ground ? AirStage.Landing : BeginHold(path, next);
            }
            Report(ref f, intent, NavigationResult.InProgress, path.Dropping ? NavigationPhase.Dropping : NavigationPhase.Following, path.Progress);
        }

        /// <summary>⭐ Airborne insertion B-3 — the Brain released the DZ leg (nobody aboard): the route goes on as a plain flight.</summary>
        private static void ReleaseDrop(in NavigationIntent intent, AirPath path)
        {
            if (!path.Dropping || !intent.ReleaseHold || !path.ReleaseArmed) return;
            path.Dropping = false;
            path.ReleaseArmed = false;
        }

        private static AirStage BeginHold(AirPath path, in AirStop stop)
        {
            path.HoldLeft = stop.Hold == RouteHold.Seconds ? stop.HoldSeconds : 0f;
            return AirStage.Holding;
        }

        private static void Tilt(AirPath path, float pitchTarget, float rollTarget, float dt)
        {
            float maxPitch = MaxPitchDeg * MathF.PI / 180f, maxRoll = MaxBankDeg * MathF.PI / 180f, rate = TiltRateDeg * MathF.PI / 180f * dt;
            path.Pitch += Math.Clamp(Math.Clamp(pitchTarget, -maxPitch, maxPitch) - path.Pitch, -rate, rate);
            path.Roll += Math.Clamp(Math.Clamp(rollTarget, -maxRoll, maxRoll) - path.Roll, -rate, rate);
        }

        private static void Level(AirPath path, float dt) => Tilt(path, 0f, 0f, dt);

        private static void TouchDown(ref MotionFrame f, float yaw, RestingPose pose)
        {
            var pos = f.Transform.Position;
            f.Transform.Position = new Vector3(pos.X, pos.Y, pose.Z);
            f.Transform.Rotation = BodyGeometry.Rotation(yaw, pose);
            f.Velocity.Linear = Vector3.Zero;
            SetAirborne(ref f, 0);
            SetVertical(ref f, 0f);
        }

        private static RestingPose RestOn(ref MotionFrame f, BodyGeometryDto geometry, float yaw)
        {
            var terrain = f.Terrain;
            return BodyGeometry.RestingPose(geometry, new Vector2(f.Transform.Position.X, f.Transform.Position.Y), yaw,
                (x, y) => terrain?.GroundHeightAt(x, y) ?? 0f);
        }

        // ── shared ────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Hover(ref MotionFrame f, in FlightParams p, float dt)
        {
            f.Velocity.Linear = Vector3.Zero;
            SetVertical(ref f, 0f);
        }

        private static void Fly(ref MotionFrame f, float yaw, Vector3 v, float dt, AirPath? tilt = null)
        {
            f.Velocity.Linear = v;
            f.Transform.Position += v * dt;
            f.Transform.Rotation = tilt == null ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw)
                                                : BodyGeometry.Rotation(yaw, new RestingPose(0f, tilt.Pitch, tilt.Roll));
            SetVertical(ref f, v.Z);
        }

        /// <summary>A dead rotorcraft falls (its horizontal speed decays); true when it took over the frame.</summary>
        private static bool Falling(ref MotionFrame f, float dt)
        {
            if (Alive(f)) return false;
            var v = f.Velocity.Linear;
            float decay = MathF.Max(0f, 1f - 0.5f * dt);
            v = new Vector3(v.X * decay, v.Y * decay, v.Z - Gravity * dt);
            f.Velocity.Linear = v;
            f.Transform.Position += v * dt;
            SetVertical(ref f, v.Z);
            return true;
        }

        /// <summary>A flyer that is down (<c>Airborne = 0</c>) and cannot take off again — dead, or without gear (a drone that has met
        /// something, <c>DroneImpactSystem</c>) — stays where it is.</summary>
        private static bool StaysDown(ref MotionFrame f)
        {
            if (!IsDown(f) || (Alive(f) && Geometry(f) != null)) return false;
            f.Velocity.Linear = Vector3.Zero;
            return true;
        }

        /// <summary>A live body with gear standing on the ground.</summary>
        private static bool OnGear(ref MotionFrame f) => IsDown(f) && Alive(f) && Geometry(f) != null;

        private static bool IsDown(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<FlightState>() && f.World.HasComponent<FlightState>(f.Self)
               && f.World.GetComponentRO<FlightState>(f.Self).Airborne == 0;

        private static bool Alive(in MotionFrame f)
        {
            var w = f.World;
            return !w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || !w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(f.Self)
                   || w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(f.Self).Current > 0f;
        }

        /// <summary>The body's geometry when it has landing gear (ground contacts), else null.</summary>
        private static BodyGeometryDto? Geometry(in MotionFrame f)
            => CombatTkb.TryGetTemplate(f.World, f.Self, out var t) && t.GetDescriptor<BodyGeometryDto>() is { GroundContacts.Count: > 0 } g ? g : null;

        private static AirPath? PathOf(in MotionFrame f)
            => f.World.TryGetTable(typeof(AirPath), out _) && f.World.HasManagedComponent<AirPath>(f.Self)
               ? ((ISimulationView)f.World).GetManagedComponentRO<AirPath>(f.Self) : null;

        private static NavigationStatus Status(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<NavigationStatus>() && f.World.HasComponent<NavigationStatus>(f.Self)
               ? f.World.GetComponentRO<NavigationStatus>(f.Self) : default;

        private static FlightParams Params(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<FlightParams>() && f.World.HasComponent<FlightParams>(f.Self)
                ? f.World.GetComponentRO<FlightParams>(f.Self)
                : FlightParams.From(new FlightParamsDto());

        private static void SetAirborne(ref MotionFrame f, byte airborne)
        {
            if (!f.World.IsComponentTypeRegistered<FlightState>() || !f.World.HasComponent<FlightState>(f.Self)) return;
            f.World.GetComponentRW<FlightState>(f.Self).Airborne = airborne;
        }

        private static void SetVertical(ref MotionFrame f, float vz)
        {
            if (!f.World.IsComponentTypeRegistered<FlightState>() || !f.World.HasComponent<FlightState>(f.Self)) return;
            ref var s = ref f.World.GetComponentRW<FlightState>(f.Self);
            s.VerticalSpeed = vz;
        }

        private static void Report(ref MotionFrame f, in NavigationIntent intent, NavigationResult result, NavigationPhase phase, float progress)
        {
            if (!f.World.IsComponentTypeRegistered<NavigationStatus>() || !f.World.HasComponent<NavigationStatus>(f.Self)) return;
            ref var s = ref f.World.GetComponentRW<NavigationStatus>(f.Self);
            s.IntentId = intent.IntentId;
            s.Result = result;
            s.Phase = phase;
            s.ProgressS = progress;
        }

        /// <summary>The heading (radians, X-forward, Z-up) of a rotation.</summary>
        public static float YawOf(Quaternion q)
        {
            var fwd = Vector3.Transform(Vector3.UnitX, q);
            return MathF.Atan2(fwd.Y, fwd.X);
        }

        /// <summary>The nose pitch (up +) and the roll (left side up +) of a rotation built by <see cref="BodyGeometry.Rotation"/>.</summary>
        public static (float Pitch, float Roll) TiltOf(Quaternion q)
        {
            float sinPitch = Math.Clamp(Vector3.Transform(Vector3.UnitX, q).Z, -1f, 1f);
            float cosPitch = MathF.Max(0.1f, MathF.Sqrt(1f - sinPitch * sinPitch));
            return (MathF.Asin(sinPitch), MathF.Asin(Math.Clamp(Vector3.Transform(Vector3.UnitY, q).Z / cosPitch, -1f, 1f)));
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
