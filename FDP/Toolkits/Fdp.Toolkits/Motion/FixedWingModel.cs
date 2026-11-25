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
    /// its steepest bank and circles it once there (Arrived). No order: straight and level — ⭐ CE-5046: once an order has ENDED, it holds (circles where it is).
    /// <b>Manual</b> (slice 6, E5): the mouse-aim pilot — see <see cref="Manual"/>.
    /// A dead plane falls; a plane down (<c>Airborne = 0</c>) stays down.
    /// </summary>
    public sealed class FixedWingModel : IMotionModel
    {
        /// <summary>How fast the bank follows its target (deg/s).</summary>
        public const float RollRateDeg = 60f;
        /// <summary>Acceleration (m/s²) where the type states none (<c>Gen.Flight AccelMps2</c>, R-3007).</summary>
        public const float AccelMps2 = 5f;

        /// <summary>The type's acceleration (R-3007), else <see cref="AccelMps2"/>.</summary>
        public static float Accel(in FlightParams p) => p.AccelMps2 > 0f ? p.AccelMps2 : AccelMps2;
        public const float MaxPitchDeg = 25f;

        public LocomotionClass Class => LocomotionClass.FixedWing;

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            var p = Params(f);
            if (IsDown(f)) { f.Velocity.Linear = Vector3.Zero; return; }
            if (!Alive(f)) { Fall(ref f, dt); return; }
            SetAirborne(ref f);
            ref var pilot = ref PilotOf(ref f);

            // ⭐⭐ CE-10002 (AC-9a) — the live threat, every frame; ⭐ AC-9 — the LAST-DITCH reflex: inside 2.5 s to impact it breaks across the
            //   missile's path whatever the brain asked (GCAS, when it already holds the stick, keeps it; a G-LOC'd pilot does nothing)
            ThreatScan.Begin(f.World);
            if (ThreatScan.Scan(f.Self, f.Transform.Position, f.Velocity.Linear, ref pilot, out _, out var threatVel)
                && pilot.ThreatTtiS < ThreatScan.LastDitchS && pilot.GcasActive == 0 && !GLocked(in f) && !LateWarned(in f))
            {
                var now = Fdp.Toolkit.Squad.Command.CommandPictureOps.Now(f.World);
                if (pilot.LastDitchAt == SimInstant.Zero || (now - pilot.LastDitchAt).Seconds > 3f) pilot.LastDitchAt = now;
                var lim = FighterLimits.Of(in p);
                var body = BodyOf(in f, in p);
                // ⭐ CE-10005 (AC-12, R-10019) — the reflex pulls the g THIS pilot will pull (the steer's skill g), never more than the airframe's
                float reflexG = f.World.IsComponentTypeRegistered<AirCombatSteer>() && f.World.HasComponent<AirCombatSteer>(f.Self)
                                && f.World.GetComponentRO<AirCombatSteer>(f.Self).MaxG > 0f
                    ? MathF.Min(lim.MaxG, f.World.GetComponentRO<AirCombatSteer>(f.Self).MaxG) : lim.MaxG;
                float g = Steer3D.Step(ref body, ThreatScan.LastDitchDirection(body.Vel, threatVel), reflexG, 1f, in lim, dt);
                Apply(ref f, in body);
                pilot.Throttle = 1f;
                Track(ref f, ref pilot, g, dt);
                return;
            }

            // ⭐⭐ CE-10001 (AC-2, AC-3) — mode 8: the brain's AirCombatSteer, flown every frame by the one integrator (GCAS inside)
            if (intent.Mode == NavigationMode.AirCombat) { Fight(ref f, in intent, in p, ref pilot, dt); return; }

            if (intent.Mode == NavigationMode.FollowRoute)
            {
                var path = PathOf(f);
                if (path != null && path.IntentId == intent.IntentId && path.Points.Length > 0)
                {
                    // ⭐ CE-10001 (AC-4) — GCAS in route mode: the floor from the leg's height; while it holds the stick the route's place
                    //   still advances (so the path is rejoined about where the recovery left it)
                    ref var fol = ref AirFollowers.Of(ref f);
                    var at = AirRoute.Sample(path, fol.Progress);
                    float floor = Gcas.FloorFor(at.Point.Z - f.Ground(at.Point.X, at.Point.Y));
                    if (GcasHolds(ref f, in p, ref pilot, floor, dt)) { pilot.Rejoin = 1; return; }
                    // ⭐ CE-10001 — after a recovery the plane REJOINS its path (it flies at a point 1 km ahead of the nearest path point until
                    //   within RejoinM of it) — ⛔ never a snap: measured, the kinematic follower jumped hundreds of metres back onto the path and
                    //   its velocity (position difference ÷ dt) read 7 km/s for a frame
                    if (pilot.Rejoin != 0)
                    {
                        float s = NearestS(path, f.Transform.Position, fol.Progress, out float off);
                        fol.Progress = s;
                        if (off > RejoinM)
                        {
                            var ahead = AirRoute.Sample(path, MathF.Min(path.Length, s + RejoinLeadM)).Point;
                            var toward = intent with { Mode = NavigationMode.DirectPoint, FinalDestination = ahead, ArrivalRadius = 0f };
                            ToPoint(ref f, in toward, in p, dt);
                            Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.Following, fol.Progress);
                            Track(ref f, ref pilot, 1f, dt);
                            return;
                        }
                        pilot.Rejoin = 0;
                    }
                    FollowRoute(ref f, in intent, in p, path, dt);
                    Track(ref f, ref pilot, 1f, dt);
                    return;
                }
                Straight(ref f, in p, dt);
                Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.AwaitingPath, 0f);
                Track(ref f, ref pilot, 1f, dt);
                return;
            }
            if (intent.Mode == NavigationMode.None || intent.Mode == NavigationMode.Manual)
            {
                if (GcasHolds(ref f, in p, ref pilot, Gcas.DefaultFloorM, dt)) return;
                // ⭐ CE-5046 (Air Operations §4: an attack ends with "egress route, or orbit (Hold)"; user-approved lean 2026-11-23) — a plane
                //   whose order ENDED, with nothing next, holds: it circles where it is (B9 — planes never stop), so it stays over the battle
                //   for the next task. ⛔ Measured before: the A-10 flew straight and level after its strike, 28 km off by 320 s. The mouse-aim
                //   pilot's Manual (no stick input) and a plane that never had an order (IntentId 0) keep flying straight and level.
                if (intent.Mode == NavigationMode.None && intent.IntentId != 0) Hold(ref f, in intent, in p, dt);
                else Straight(ref f, in p, dt);
                Track(ref f, ref pilot, 1f, dt);
                return;
            }
            // ⭐ AC-4 — a point: the floor from the point's height above its ground; a point ON the ground (an attack's aim point, a gun
            //   run's target) leaves the minimum floor — measured: the 150 m default stopped ua-air-attack's A-10 gun run (the column lead lived)
            float pointAgl = intent.FinalDestination.Z - f.Ground(intent.FinalDestination.X, intent.FinalDestination.Y);
            float pointFloor = pointAgl > Gcas.MinFloorM ? Gcas.FloorFor(pointAgl) : Gcas.MinFloorM;
            // ⭐ R-10010 — the point order is KINEMATIC: it levels at its height by itself. GCAS watches only an order whose own flight meets the
            //   ground (measured: a 50 m gun run, descending onto its height, tripped the recovery prediction and its rounds missed)
            if ((pilot.GcasActive != 0 || !PointOrderSafe(in f, in intent, in p, pointFloor)) && GcasHolds(ref f, in p, ref pilot, pointFloor, dt)) return;
            ToPoint(ref f, in intent, in p, dt);
            Track(ref f, ref pilot, 1f, dt);
        }

        // ── ⭐⭐ CE-10001 — air combat (mode 8) and GCAS ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ R-10010 — does the point order's OWN flight (on along its heading at its speed, its height moving to the point's at the climb
        /// rate — the kinematics <see cref="ToPoint"/> flies) stay above <paramref name="floor"/> for <see cref="Gcas.HorizonS"/>? Allocation-free.
        /// </summary>
        private static bool PointOrderSafe(in MotionFrame f, in NavigationIntent intent, in FlightParams p, float floor)
        {
            var pos = f.Transform.Position;
            var hv = new Vector2(f.Velocity.Linear.X, f.Velocity.Linear.Y);
            float v = hv.Length();
            if (v < 1f) return true;
            hv /= v;
            float z = pos.Z, step = 0.25f;
            for (float t = step; t <= Gcas.HorizonS; t += step)
            {
                z += Math.Clamp(intent.FinalDestination.Z - z, -p.ClimbMps * step, p.ClimbMps * step);
                float x = pos.X + hv.X * v * t, y = pos.Y + hv.Y * v * t;
                if (z - f.Ground(x, y) < floor) return false;
            }
            return true;
        }

        /// <summary>A plane rejoining its route after GCAS is back on it within this (m); it aims this far ahead of the nearest path point.</summary>
        public const float RejoinM = 30f, RejoinLeadM = 1000f;

        /// <summary>The path's place nearest <paramref name="at"/> (horizontally) within 3 km of <paramref name="around"/>, and how far off it is.</summary>
        private static float NearestS(AirPath path, Vector3 at, float around, out float offset)
        {
            float best = float.MaxValue, bestS = around;
            var pts = path.Points; var s = path.S;
            for (int j = 0; j < pts.Length; j++)
            {
                if (s[j] < around - 3000f) continue;
                if (s[j] > around + 3000f) break;
                float dx = pts[j].X - at.X, dy = pts[j].Y - at.Y, d = dx * dx + dy * dy;
                if (d < best) { best = d; bestS = s[j]; }
            }
            offset = MathF.Sqrt(best);
            return bestS;
        }

        /// <summary>How far ahead (s) a lead pursuit leads its target (the gun's ~1 km/s round over a fight's range); a lag pursuit lags.</summary>
        public const float LeadSeconds = 0.8f, LagSeconds = 1.5f;
        /// <summary>⭐ CE-10005 — inside this (m) a lead pursuit leads by the gun round's time of flight at <see cref="GunRoundMps"/>.</summary>
        public const float GunLeadRangeM = 800f, GunRoundMps = 1000f;
        /// <summary>⭐ CE-10005 — how much harder than a pursuit a gun track pulls out its error to the lead (× the error, added).</summary>
        public const float GunTrackBoost = 2f;
        /// <summary>A manoeuvre with no throttle stated flies at this.</summary>
        public const float DefaultThrottle = 0.8f;

        /// <summary>
        /// ⭐⭐ CE-10001 (AC-2, AC-3, AC-5) — mode 8: the manoeuvre the brain's <see cref="AirCombatSteer"/> names, as a desired 3-D direction at
        /// the aircraft's own position against the target's, flown by <see cref="Steer3D"/> within the pilot's g; GCAS first (AC-4, the steer's
        /// floor, a third of it under a fixation lapse). No steer ⇒ straight and level.
        /// </summary>
        private static void Fight(ref MotionFrame f, in NavigationIntent intent, in FlightParams p, ref FighterPilotState pilot, float dt)
        {
            var w = f.World;
            if (!w.IsComponentTypeRegistered<AirCombatSteer>() || !w.HasComponent<AirCombatSteer>(f.Self))
            {
                if (GcasHolds(ref f, in p, ref pilot, Gcas.DefaultFloorM, dt)) return;
                Straight(ref f, in p, dt);
                Track(ref f, ref pilot, 1f, dt);
                return;
            }
            var steer = w.GetComponentRO<AirCombatSteer>(f.Self);
            var limits = FighterLimits.Of(in p);
            float floor = steer.FloorAgl > 0f ? MathF.Max(Gcas.MinFloorM, steer.FloorAgl) : Gcas.DefaultFloorM;
            if (steer.Lapse == PilotLapse.Fixation) floor = MathF.Max(Gcas.MinFloorM, floor / 3f);
            if (GcasHolds(ref f, in p, ref pilot, floor, dt)) return;

            var b = BodyOf(in f, in p);
            float maxG = steer.MaxG > 0f ? MathF.Min(steer.MaxG, limits.MaxG) : limits.MaxG;
            float throttle = steer.Throttle > 0f ? steer.Throttle : DefaultThrottle;
            var desired = Desired(in f, in b, in steer, in pilot, ref maxG, ref throttle);
            float n = Steer3D.Step(ref b, desired, maxG, throttle, in limits, dt);
            Apply(ref f, in b);
            pilot.Throttle = throttle;
            Track(ref f, ref pilot, n, dt);
            Report(ref f, intent, NavigationResult.InProgress, NavigationPhase.Following, 0f);
        }

        /// <summary>⭐ AC-5 — the manoeuvre's desired direction (unit). Geometry only; a manoeuvre may lower the g or set the throttle.</summary>
        internal static Vector3 Desired(in MotionFrame f, in FlightBody b, in AirCombatSteer s, in FighterPilotState pilot, ref float maxG, ref float throttle)
        {
            var fwd = b.Vel.LengthSquared() > 1f ? Vector3.Normalize(b.Vel) : Steer3D.Forward(b.Rot);
            var level = new Vector3(fwd.X, fwd.Y, 0f);
            level = level.LengthSquared() > 1e-6f ? Vector3.Normalize(level) : Vector3.UnitX;
            if (s.Lapse == PilotLapse.GLoc) { maxG = 1f; return level; }   // AC-12 — no control: wings level, no pull

            bool hasTarget = TargetState(f.World, s.Target, out var tp, out var tv);
            var aim = hasTarget ? tp : s.Point;
            switch (s.Manoeuvre)
            {
                case Manoeuvre.PurePursuit:
                case Manoeuvre.Break:
                    if (s.Manoeuvre == Manoeuvre.Break) throttle = 1f;
                    return Toward(b.Pos, aim, level);
                case Manoeuvre.LeadPursuit:
                {
                    // ⭐ CE-10005 (AC-8) — inside the gun's reach the lead is the ROUND's time of flight (r ÷ ~1 km/s), so the nose can meet the
                    //   brain's 2° gun cone; 📐 measured on ac-guns-turning: the fixed 0.8 s lead held the nose ~10° off the gun's lead at 400 m
                    //   and the gun never fired. Beyond GunLeadRangeM the 0.8 s lead (the two agree at that range)
                    float lead = LeadSeconds;
                    if (hasTarget)
                    {
                        float r = Vector3.Distance(b.Pos, aim);
                        // the brain's GUN lead (its round, the target's acceleration) as an offset from the target — the pipper on the bandit
                        if (r < GunLeadRangeM && s.Point != Vector3.Zero)
                        {
                            // the pilot TRACKS the pipper: the error to the lead is pulled out GunTrackBoost × harder than a pursuit's (📐 measured
                            //   on ac-guns-turning: the plain proportional pull left the flight path a steady ~6° behind the lead of a 5 g target)
                            var d = Toward(b.Pos, aim + s.Point, level);
                            var tracked = d + (d - fwd) * GunTrackBoost;
                            return tracked.LengthSquared() > 1e-6f ? Vector3.Normalize(tracked) : d;
                        }
                        if (r < GunLeadRangeM) lead = MathF.Max(0.15f, r / GunRoundMps);
                    }
                    return Toward(b.Pos, hasTarget ? aim + tv * lead : aim, level);
                }
                case Manoeuvre.LagPursuit:
                    return Toward(b.Pos, hasTarget ? aim - tv * LagSeconds : aim, level);
                case Manoeuvre.Climb:
                    return Vector3.Normalize(level + new Vector3(0f, 0f, 1f));
                case Manoeuvre.Extend:
                    maxG = MathF.Min(maxG, 2f); throttle = 1f;
                    return Vector3.Normalize(level + new Vector3(0f, 0f, -0.05f));
                case Manoeuvre.Merge:
                case Manoeuvre.ReturnToArena:
                case Manoeuvre.Disengage:
                    if (s.Manoeuvre == Manoeuvre.Disengage) throttle = 1f;
                    return Toward(b.Pos, new Vector3(aim.X, aim.Y, b.Pos.Z), level);
                case Manoeuvre.Rejoin:
                    if (!hasTarget) return level;
                    var lf = tv.LengthSquared() > 1f ? Vector3.Normalize(tv) : level;
                    return Toward(b.Pos, tp - lf * 200f + new Vector3(-lf.Y, lf.X, 0f) * 150f, level);
                case Manoeuvre.Drag:
                    // ⭐ AC-9 — turn the threat onto the tail and run, a gentle descent (S2 gives the missile its energy to lose)
                    throttle = 1f;
                    return pilot.Inbound > 0 ? Vector3.Normalize(Bearing(pilot.ThreatBearingDeg + 180f) + new Vector3(0f, 0f, -0.1f)) : level;
                case Manoeuvre.Beam:
                {
                    // ⭐ AC-9 / RV-4 — the threat on the 3 or 9 o'clock (the side nearer the present heading), descending toward the notch
                    if (pilot.Inbound == 0) return level;
                    var l = Bearing(pilot.ThreatBearingDeg + 90f); var r = Bearing(pilot.ThreatBearingDeg - 90f);
                    var side = Vector3.Dot(l, level) >= Vector3.Dot(r, level) ? l : r;
                    return Vector3.Normalize(side + new Vector3(0f, 0f, -0.15f));
                }
                case Manoeuvre.LastDitch:
                {
                    maxG = MathF.Max(maxG, 9f);
                    var across = Bearing(pilot.ThreatBearingDeg + 90f);
                    return Vector3.Normalize(Vector3.Dot(across, level) >= 0f ? across : -across);
                }
                default:
                    return level;
            }
        }

        private static Vector3 Toward(Vector3 from, Vector3 to, Vector3 fallback)
        {
            var d = to - from;
            return d.LengthSquared() > 1f ? Vector3.Normalize(d) : fallback;
        }

        /// <summary>True while the pilot is in a G-LOC lapse (AC-12): no reflex, no control.</summary>
        private static bool GLocked(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<AirCombatSteer>() && f.World.HasComponent<AirCombatSteer>(f.Self)
               && f.World.GetComponentRO<AirCombatSteer>(f.Self).Lapse == PilotLapse.GLoc;

        /// <summary>⭐ CE-10004 (AC-12, R-10016) — a pilot in a LateWarning or a Fixation lapse has not seen the missile yet: no last-ditch reflex (his 2 s late
        /// reaction would come after the 2.5 s window anyway).</summary>
        private static bool LateWarned(in MotionFrame f)
            => f.World.IsComponentTypeRegistered<AirCombatSteer>() && f.World.HasComponent<AirCombatSteer>(f.Self)
               && f.World.GetComponentRO<AirCombatSteer>(f.Self).Lapse is PilotLapse.LateWarning or PilotLapse.Fixation;

        /// <summary>A unit horizontal vector along a compass bearing (0 = north = +Y, clockwise).</summary>
        internal static Vector3 Bearing(float deg)
        {
            float r = deg * MathF.PI / 180f;
            return new Vector3(MathF.Sin(r), MathF.Cos(r), 0f);
        }

        /// <summary>The target's position and velocity (Null or gone ⇒ false).</summary>
        private static bool TargetState(EntityRepository w, Entity t, out Vector3 pos, out Vector3 vel)
        {
            pos = default; vel = default;
            if (t.IsNull || !w.IsAlive(t) || !w.HasComponent<SimTransform>(t)) return false;
            if (ThreatScan.TickStart(t, out pos, out vel)) return true;   // ⭐ CE-10003 (RV-6) — the tick-start state, never a half-moved one
            pos = w.GetComponentRO<SimTransform>(t).Position;
            if (w.HasComponent<SimVelocity>(t)) vel = w.GetComponentRO<SimVelocity>(t).Linear;
            return true;
        }

        /// <summary>
        /// ⭐⭐ CE-10001 (AC-4) — Auto-GCAS: true while it holds the stick (this frame was flown by it). Takes over when the simulated
        /// recovery from the present state would pass below <paramref name="floor"/>; lets go once climbing, above the floor and with a clear
        /// recovery ahead.
        /// </summary>
        private static bool GcasHolds(ref MotionFrame f, in FlightParams p, ref FighterPilotState pilot, float floor, float dt)
        {
            var limits = FighterLimits.Of(in p);
            var b = BodyOf(in f, in p);
            float agl = b.Pos.Z - f.Ground(b.Pos.X, b.Pos.Y);
            if (pilot.GcasActive == 0)
            {
                // cheap first: a body far above anything it can reach in the horizon needs no simulation
                float reach = b.Vel.Length() * Gcas.HorizonS;
                if (agl > reach + 500f) return false;
                if (Gcas.RecoveryClearance(b, in limits, limits.MaxG, 1f, f.Terrain, 0f) >= floor) return false;
                var (turn, _) = Gcas.BestRecovery(in b, in limits, limits.MaxG, f.Terrain, floor);
                pilot.GcasActive = 1;
                pilot.GcasCount++;
                pilot.GcasHeldS = 0f;
                pilot.GcasTurn = (sbyte)MathF.Sign(turn);
                var dir = Gcas.RecoveryDirection(in b, turn);
                pilot.GcasHeadingDeg = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
            }
            // ⭐ it lets go only after a minimum hold, climbing, and with a recovery that clears the floor by Gcas.ReleaseMarginM — measured
            //   (ac-gcas-ridge): released at the floor, a kinematic point order dived straight back at the ridge it had just climbed from, and the
            //   second recovery came too late
            else if ((pilot.GcasHeldS += dt) >= Gcas.MinHoldS && b.Vel.Z > 0f && agl >= floor
                     && Gcas.RecoveryClearance(b, in limits, limits.MaxG, 1f, f.Terrain, 0f) >= floor + Gcas.ReleaseMarginM)
            {
                pilot.GcasActive = 0;
                pilot.GcasTurn = 0;
                return false;
            }
            float yaw = pilot.GcasHeadingDeg * MathF.PI / 180f, climb = Gcas.RecoveryClimbDeg * MathF.PI / 180f;
            var want = new Vector3(MathF.Cos(yaw) * MathF.Cos(climb), MathF.Sin(yaw) * MathF.Cos(climb), MathF.Sin(climb));
            float n = Steer3D.Step(ref b, want, limits.MaxG, 1f, in limits, dt);
            Apply(ref f, in b);
            pilot.Throttle = 1f;
            Track(ref f, ref pilot, n, dt);
            return true;
        }

        /// <summary>The integrator's state from the body: a plane that has no velocity yet flies on along its nose at cruise.</summary>
        private static FlightBody BodyOf(in MotionFrame f, in FlightParams p)
        {
            var b = new FlightBody { Pos = f.Transform.Position, Vel = f.Velocity.Linear, Rot = f.Transform.Rotation };
            if (b.Vel.LengthSquared() < 1f) b.Vel = Steer3D.Forward(b.Rot) * Math.Clamp(Cruise(in p), Stall(in p), Top(in p));
            return b;
        }

        private static void Apply(ref MotionFrame f, in FlightBody b)
        {
            f.Transform.Position = b.Pos;
            f.Transform.Rotation = b.Rot;
            f.Velocity.Linear = b.Vel;
            if (f.World.IsComponentTypeRegistered<FlightState>() && f.World.HasComponent<FlightState>(f.Self))
                f.World.GetComponentRW<FlightState>(f.Self).VerticalSpeed = b.Vel.Z;
        }

        /// <summary>The report's steady fields: g, speed, the lowest height flown.</summary>
        private static void Track(ref MotionFrame f, ref FighterPilotState pilot, float g, float dt)
        {
            pilot.GLoad = g;
            pilot.SpeedMps = f.Velocity.Linear.Length();
            float agl = f.Transform.Position.Z - f.Ground(f.Transform.Position.X, f.Transform.Position.Y);
            if (agl < pilot.MinAgl) pilot.MinAgl = agl;
        }

        /// <summary>⭐ CE-10001 — the motion node's report on this aircraft, added (once) with its final starting value (D-31).</summary>
        private static ref FighterPilotState PilotOf(ref MotionFrame f)
        {
            var w = f.World;
            if (!w.IsComponentTypeRegistered<FighterPilotState>()) w.RegisterComponent<FighterPilotState>();
            if (!w.HasComponent<FighterPilotState>(f.Self)) w.AddComponent(f.Self, new FighterPilotState { MinAgl = float.MaxValue, Throttle = DefaultThrottle, GLoad = 1f });
            return ref w.GetComponentRW<FighterPilotState>(f.Self);
        }

        /// <summary>
        /// ⭐⭐ Air Ops slice 6 (E5, §6 B3) — the MOUSE-AIM pilot on the existing <see cref="ManualDrive"/> axes: <c>Yaw</c> = the heading the
        /// camera looks along, <c>Vertical</c> ∈ [−1, 1] = the climb angle asked for (× <see cref="MaxPitchDeg"/>, the climb rate caps it),
        /// <c>Forward</c> ∈ [−1, 1] = the throttle, HELD (R-3007): + speeds it up, − slows it, 0 keeps the speed it has. The plane flies itself: it banks toward the look
        /// (a coordinated turn — the bank IS the turn), levels its wings as the nose comes onto the look, and never flies under its stall
        /// speed whatever it is told.
        /// </summary>
        public void Manual(ref MotionFrame f, in ManualDrive d, float dt)
        {
            var p = Params(f);
            if (IsDown(f)) { f.Velocity.Linear = Vector3.Zero; return; }
            if (!Alive(f)) { Fall(ref f, dt); return; }
            SetAirborne(ref f);

            // ⭐ R-3007 — the throttle is HELD: W speeds it up toward its top speed, S slows it toward its stall speed (at the type's
            //   acceleration × how far the axis is pushed), released it keeps the speed it has — any speed between stall and top
            float throttle = Math.Clamp(d.Forward, -1f, 1f);
            float now = f.Velocity.Linear.Length();
            if (now <= 0.1f) now = Math.Clamp(Cruise(in p), Stall(in p), Top(in p));   // possessed already flying
            float speed = Math.Clamp(now + throttle * Accel(in p) * dt, Stall(in p), Top(in p));

            // the turn: a bank toward the look that fades as the nose comes onto it; the yaw follows the bank (coordinated)
            float yaw = RotorcraftModel.YawOf(f.Transform.Rotation);
            float error = MathF.IEEERemainder(d.Yaw - yaw, 2f * MathF.PI);
            float maxRate = RotorcraftModel.Gravity * MathF.Tan(MaxBank(in p)) / speed;
            float rateWanted = Math.Clamp(error / ManualTurnSeconds, -maxRate, maxRate);
            float roll = EaseRoll(f, -MathF.Atan(speed * rateWanted / RotorcraftModel.Gravity), in p, dt);
            float turned = -RotorcraftModel.Gravity * MathF.Tan(roll) / speed * dt;
            turned = Math.Clamp(turned, MathF.Min(0f, error), MathF.Max(0f, error));   // never past the look, never away from it
            yaw += turned;

            // the climb: the angle asked for, capped by the climb rate, eased
            float pitchMax = MathF.Min(MaxPitchDeg * MathF.PI / 180f, MathF.Asin(Math.Clamp(p.ClimbMps / speed, 0f, 1f)));
            float pitchWanted = Math.Clamp(d.Vertical, -1f, 1f) * pitchMax;
            float pitchNow = RotorcraftModel.TiltOf(f.Transform.Rotation).Pitch;
            float pitchStep = ManualPitchRateDeg * MathF.PI / 180f * dt;
            float pitch = pitchNow + Math.Clamp(pitchWanted - pitchNow, -pitchStep, pitchStep);

            var dir = new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));
            Pose(ref f, f.Transform.Position + dir * speed * dt, yaw, pitch, roll, dt);
            // the held speed is the velocity's own length: write it exactly, not as a position difference (float rounding at
            //   kilometre coordinates would bleed the held speed away frame by frame)
            f.Velocity.Linear = dir * speed;
        }

        /// <summary>The mouse-aim pilot turns at a rate of (heading error ÷ this), up to its steepest bank — a soft approach to the look.</summary>
        public const float ManualTurnSeconds = 1.5f;
        /// <summary>How fast the mouse-aim pilot's nose follows the climb angle asked for (deg/s).</summary>
        public const float ManualPitchRateDeg = 20f;

        // ── a route ───────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void FollowRoute(ref MotionFrame f, in NavigationIntent intent, in FlightParams p, AirPath path, float dt)
        {
            ref var fol = ref AirFollowers.Of(ref f);   // ⭐ CE-4046 ② — the follower beside the immutable path
            float speed = Speed(in p, in intent, fol.Stage == AirStage.Start ? HorizontalSpeed(f) : fol.SpeedNow, dt, AirRoute.Sample(path, fol.Progress).Speed);
            fol.SpeedNow = speed;
            if (fol.Stage == AirStage.Start) fol.Stage = AirStage.Flying;

            if (fol.Stage == AirStage.Done) { Orbit(ref f, in p, ref fol, speed, dt); Report(ref f, intent, NavigationResult.Arrived, NavigationPhase.Completed, fol.Progress); return; }

            fol.Progress += speed * dt;
            if (fol.Progress >= path.Length)
            {
                if (path.Loop && path.Length > 0f) fol.Progress -= path.Length;
                else
                {
                    // the end: a left-hand circle beside the last point, entered with this frame's leftover travel (no short step)
                    float leftover = fol.Progress - path.Length;
                    fol.Progress = path.Length;
                    var end = AirRoute.Sample(path, path.Length);
                    var left = new Vector2(-end.Heading.Y, end.Heading.X);
                    float r = MathF.Max(1f, p.TurnRadius);
                    fol.OrbitCenter = new Vector2(end.Point.X, end.Point.Y) + left * r;
                    fol.OrbitAngle = MathF.Atan2(end.Point.Y - fol.OrbitCenter.Y, end.Point.X - fol.OrbitCenter.X);
                    fol.Stage = AirStage.Done;
                    Orbit(ref f, in p, ref fol, leftover / MathF.Max(dt, 1e-4f), dt);
                    Report(ref f, intent, NavigationResult.Arrived, NavigationPhase.Completed, fol.Progress);
                    return;
                }
            }

            var at = AirRoute.Sample(path, fol.Progress);
            var pos = f.Transform.Position;
            float dz = Math.Clamp(at.Point.Z - pos.Z, -p.ClimbMps * dt, p.ClimbMps * dt);
            var next = new Vector3(at.Point.X, at.Point.Y, pos.Z + dz);
            float yaw = MathF.Atan2(at.Heading.Y, at.Heading.X);
            Bank(ref fol, in p, -MathF.Atan(speed * speed * at.Curvature / RotorcraftModel.Gravity), dt);
            Pose(ref f, next, yaw, MathF.Atan2(dz / MathF.Max(dt, 1e-4f), MathF.Max(speed, 1f)), fol.Roll, dt);
            Report(ref f, intent, fol.Stage == AirStage.Done ? NavigationResult.Arrived : NavigationResult.InProgress,
                   fol.Stage == AirStage.Done ? NavigationPhase.Completed : NavigationPhase.Following, fol.Progress);
        }

        /// <summary>After the route: a left-hand circle of the turn radius beside the last point, at its height.</summary>
        private static void Orbit(ref MotionFrame f, in FlightParams p, ref AirFollower fol, float speed, float dt)
        {
            float r = MathF.Max(1f, p.TurnRadius);
            fol.OrbitAngle += speed / r * dt;
            var c = fol.OrbitCenter;
            var next = new Vector3(c.X + r * MathF.Cos(fol.OrbitAngle), c.Y + r * MathF.Sin(fol.OrbitAngle), f.Transform.Position.Z);
            Bank(ref fol, in p, -MathF.Atan(speed * speed / r / RotorcraftModel.Gravity), dt);
            Pose(ref f, next, fol.OrbitAngle + MathF.PI / 2f, 0f, fol.Roll, dt);
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

        /// <summary>
        /// ⭐ CE-5046 — the no-order hold: a left-hand circle of the turn radius, anchored once per ended intent where the plane IS — the circle
        /// tangent to its heading, so it rolls into the turn with no jump — at its height, at its own speed (stall..top).
        /// </summary>
        private static void Hold(ref MotionFrame f, in NavigationIntent intent, in FlightParams p, float dt)
        {
            ref var fol = ref AirFollowers.Of(ref f);
            if (fol.Stage != AirStage.Holding || fol.HoldIntent != intent.IntentId)
            {
                float yaw = RotorcraftModel.YawOf(f.Transform.Rotation);
                var pos = new Vector2(f.Transform.Position.X, f.Transform.Position.Y);
                var left = new Vector2(-MathF.Sin(yaw), MathF.Cos(yaw));
                fol.OrbitCenter = pos + left * MathF.Max(1f, p.TurnRadius);
                fol.OrbitAngle = MathF.Atan2(pos.Y - fol.OrbitCenter.Y, pos.X - fol.OrbitCenter.X);
                fol.Stage = AirStage.Holding;
                fol.HoldIntent = intent.IntentId;
            }
            float speed = Math.Clamp(HorizontalSpeed(f) > 0.1f ? HorizontalSpeed(f) : Cruise(in p), Stall(in p), Top(in p));
            Orbit(ref f, in p, ref fol, speed, dt);
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
            return Math.Clamp(now + Math.Clamp(want - now, -Accel(in p) * dt, Accel(in p) * dt), Stall(in p), Top(in p));
        }

        private static void Bank(ref AirFollower fol, in FlightParams p, float target, float dt)
        {
            float max = MaxBank(in p), rate = RollRateDeg * MathF.PI / 180f * dt;
            fol.Roll = (Fdp.Core.Angle)(fol.Roll + Math.Clamp(Math.Clamp(target, -max, max) - fol.Roll, -rate, rate));
        }

        private static float EaseRoll(in MotionFrame f, float target, in FlightParams p, float dt)
        {
            float now = RotorcraftModel.TiltOf(f.Transform.Rotation).Roll;   // left side up +
            float max = MaxBank(in p), rate = RollRateDeg * MathF.PI / 180f * dt;
            return now + Math.Clamp(Math.Clamp(target, -max, max) - now, -rate, rate);
        }

        private static void Pose(ref MotionFrame f, Vector3 next, float yaw, float pitch, float roll, float dt)
        {
            float maxPitch = MaxPitchDeg * MathF.PI / 180f;
            f.Velocity.Linear = StepVelocity.Of(f.Transform.Position, next, dt, f.Velocity.Linear);   // ⭐ CE-11057 — a too-short frame keeps the velocity
            f.Transform.Position = next;
            f.Transform.Rotation = BodyGeometry.Rotation(yaw, new RestingPose(0f, (Fdp.Core.Angle)(Math.Clamp(pitch, -maxPitch, maxPitch)), (Fdp.Core.Angle)(roll)));
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
