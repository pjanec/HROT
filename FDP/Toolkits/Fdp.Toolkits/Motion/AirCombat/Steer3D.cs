using System;
using System.Numerics;

namespace Fdp.Toolkit.Motion
{
    /// <summary>⭐ CE-10001 (AC-3) — the state the one fighter integrator steps: position, velocity, attitude (a quaternion — never read back as
    /// Euler angles, so a vertical or inverted aircraft has no gimbal problem).</summary>
    public struct FlightBody
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public Quaternion Rot;
    }

    /// <summary>⭐ CE-10001 (AC-3, §5a) — the airframe's limits the integrator flies within (from <see cref="FlightParams"/>).</summary>
    public readonly struct FighterLimits
    {
        public readonly float MaxG, CornerSpeed, Stall, Top, Thrust, DragK, RollRate;

        public FighterLimits(float maxG, float corner, float stall, float top, float thrust, float dragK, float rollRateRad)
        {
            MaxG = maxG; CornerSpeed = corner; Stall = stall; Top = top; Thrust = thrust; DragK = dragK; RollRate = rollRateRad;
        }

        /// <summary>The limits of a type. Where the type states no fighter numbers: a 6 g, 60°/s airliner-like airframe (the A-10's GCAS).</summary>
        public static FighterLimits Of(in FlightParams p)
        {
            float top = MathF.Max(p.MaxSpeedMps, p.CruiseSpeedMps);
            float stall = MathF.Min(p.StallSpeedMps, top);
            float maxG = p.MaxG > 0f ? p.MaxG : 6f;
            float corner = p.CornerSpeedMps > 0f ? p.CornerSpeedMps : MathF.Max(stall * 1.6f, 1f);
            float thrust = p.AccelMps2 > 0f ? p.AccelMps2 : FixedWingModel.AccelMps2;
            float dragK = p.DragK > 0f ? p.DragK : 0.3f;
            float roll = (p.RollRateDeg > 0f ? p.RollRateDeg : FixedWingModel.RollRateDeg) * MathF.PI / 180f;
            return new FighterLimits(maxG, corner, stall, top, thrust, dragK, roll);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-10001 (AC-3; docs/designs/air-attack/DESIGN_Air_Combat.md §3 AC-3) — THE one fighter integrator: it steers a 3-D velocity
    /// vector toward a desired direction with its OWN attitude. It rolls (at the type's roll rate) to put the lift onto the wanted turn — net of
    /// gravity, so a level turn banks acos(1/n) by itself — and pulls at most the g asked (≤ the type's <c>MaxG</c>, and below corner speed
    /// the speed-limited <c>MaxG·(v/corner)²</c>); the speed is the thrust (<c>AccelMps2</c> × throttle) − parasite drag (zero net at top speed)
    /// − induced drag (<c>DragK·(n²−1)</c>) − g·sin(climb), never below stall. ONE integrator flies the fight AND simulates GCAS's recovery
    /// (<see cref="Gcas"/>). Pure maths on structs: no allocation.
    /// </summary>
    public static class Steer3D
    {
        public const float G = 9.81f;
        /// <summary>The steering's turn-rate gain (1/s): the turn asked = this × the angle still to turn.</summary>
        public const float Gain = 2f;
        /// <summary>The most negative g a pilot pushes.</summary>
        public const float MinG = -1f;

        public static Vector3 Forward(Quaternion q) => Vector3.Transform(Vector3.UnitX, q);
        public static Vector3 Up(Quaternion q) => Vector3.Transform(Vector3.UnitZ, q);

        /// <summary>The attitude whose nose is <paramref name="fwd"/> and whose lift points along <paramref name="up"/> (made perpendicular).</summary>
        public static Quaternion FromBasis(Vector3 fwd, Vector3 up)
        {
            fwd = Vector3.Normalize(fwd);
            up -= fwd * Vector3.Dot(up, fwd);
            if (up.LengthSquared() < 1e-8f) up = WingsLevelUp(fwd);
            up = Vector3.Normalize(up);
            var left = Vector3.Cross(up, fwd);
            var m = new Matrix4x4(fwd.X, fwd.Y, fwd.Z, 0f, left.X, left.Y, left.Z, 0f, up.X, up.Y, up.Z, 0f, 0f, 0f, 0f, 1f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }

        /// <summary>The wings-level lift direction for a nose along <paramref name="fwd"/> (world up made perpendicular; any for a vertical nose).</summary>
        public static Vector3 WingsLevelUp(Vector3 fwd)
        {
            var up = Vector3.UnitZ - fwd * fwd.Z;
            if (up.LengthSquared() < 1e-6f) up = Vector3.UnitX - fwd * fwd.X;
            return Vector3.Normalize(up);
        }

        /// <summary>The g the airframe can pull at <paramref name="speed"/>: <paramref name="maxG"/>, less below corner speed (∝ speed²), never under 1.</summary>
        public static float AvailableG(in FighterLimits l, float speed, float maxG)
        {
            float g = MathF.Min(maxG, l.MaxG);
            if (speed < l.CornerSpeed) g *= (speed / l.CornerSpeed) * (speed / l.CornerSpeed);
            return MathF.Max(1f, g);
        }

        /// <summary>
        /// One step: turn the velocity toward <paramref name="desired"/> (a unit vector), pulling at most <paramref name="maxG"/>, at
        /// <paramref name="throttle"/> (0…1). Returns the load factor flown (1 = level flight).
        /// </summary>
        public static float Step(ref FlightBody b, Vector3 desired, float maxG, float throttle, in FighterLimits l, float dt)
        {
            float v = b.Vel.Length();
            Vector3 f;
            if (v < 1f) { f = Forward(b.Rot); v = MathF.Max(l.Stall, 1f); }
            else f = b.Vel / v;

            var u = Up(b.Rot);
            u -= f * Vector3.Dot(u, f);
            u = u.LengthSquared() < 1e-6f ? WingsLevelUp(f) : Vector3.Normalize(u);

            // the turn asked: toward the desired direction, at a rate ∝ the angle still to turn
            if (desired.LengthSquared() < 1e-8f) desired = f;
            desired = Vector3.Normalize(desired);
            float cos = Math.Clamp(Vector3.Dot(f, desired), -1f, 1f);
            float angle = MathF.Acos(cos);
            var p = desired - f * cos;
            if (p.LengthSquared() < 1e-8f) p = angle > 1f ? u : Vector3.Zero;   // dead behind ⇒ pull through the lift
            else p = Vector3.Normalize(p);
            float aTurn = v * Gain * angle;

            // the lift that turn needs, net of gravity — GRAVITY FIRST: past the g limit the turn gets what is left once the lift holds the
            //   aircraft up (a max-g level turn banks acos(1/n), it does not spiral down)
            var gvec = new Vector3(0f, 0f, -G);
            var gPerp = gvec - f * Vector3.Dot(gvec, f);
            float nAvail = AvailableG(in l, v, maxG);
            float pg = Vector3.Dot(p, gPerp), nG = nAvail * G;
            float disc = pg * pg - gPerp.LengthSquared() + nG * nG;
            float kMax = disc > 0f ? MathF.Max(0f, pg + MathF.Sqrt(disc)) : 0f;
            var lift = p * MathF.Min(aTurn, kMax) - gPerp;
            float liftLen = lift.Length();

            // roll the lift onto it (about the nose, at the roll rate); a tiny lift asked keeps the wings level
            var upWanted = liftLen > 0.1f * G ? lift / liftLen : WingsLevelUp(f);
            float sin = Vector3.Dot(Vector3.Cross(u, upWanted), f);
            float rollTo = MathF.Atan2(sin, Vector3.Dot(u, upWanted));
            float rollStep = Math.Clamp(rollTo, -l.RollRate * dt, l.RollRate * dt);
            u = Vector3.Normalize(Vector3.Transform(u, Quaternion.CreateFromAxisAngle(f, rollStep)));

            // pull: the part of the wanted lift along the wings' lift line, within the airframe's g
            float n = Math.Clamp(Vector3.Dot(lift, u) / G, MinG, nAvail);

            // energy: thrust − parasite (zero net at top speed) − induced (n² − 1) − gravity along the path
            float tangential = l.Thrust * Math.Clamp(throttle, 0f, 1f) - l.Thrust * (v / l.Top) * (v / l.Top)
                               - l.DragK * MathF.Max(0f, n * n - 1f) + Vector3.Dot(gvec, f);
            float speed = Math.Clamp(v + tangential * dt, l.Stall, l.Top);

            var perp = u * (n * G) + gPerp;
            var dir = f * v + perp * dt;
            dir = dir.LengthSquared() < 1e-8f ? f : Vector3.Normalize(dir);
            b.Vel = dir * speed;
            b.Pos += b.Vel * dt;
            b.Rot = FromBasis(dir, u);
            return n;
        }
    }

    /// <summary>
    /// ⭐⭐ CE-10001 (AC-4; docs/designs/air-attack/DESIGN_Air_Combat.md §3 AC-4) — AUTO-GCAS for every AI fixed-wing mode: each frame it
    /// SIMULATES the recovery through <see cref="Steer3D"/> (the same integrator, so the prediction matches the aircraft): <see cref="ReactionS"/>
    /// holding the present line, then roll wings level and pull the airframe's <c>MaxG</c> into a <see cref="RecoveryClimbDeg"/> climb, over the
    /// ground under that curved path for up to <see cref="HorizonS"/>. Below the floor anywhere ⇒ it takes the stick. A ridge the straight climb
    /// cannot clear ⇒ the climbing turn (left or right) that clears best. Allocation-free.
    /// </summary>
    public static class Gcas
    {
        public const float ReactionS = 0.3f, HorizonS = 10f, StepS = 0.1f, RecoveryClimbDeg = 30f, TurnAwayDeg = 120f;
        /// <summary>GCAS holds the stick at least this long, and lets go only with a recovery clearing the floor by this margin (hysteresis).</summary>
        public const float MinHoldS = 1.5f, ReleaseMarginM = 100f;
        /// <summary>The default floor (m above the ground); never below <see cref="MinFloorM"/>.</summary>
        public const float DefaultFloorM = 150f, MinFloorM = 8f;
        /// <summary>A route / point's ordered height leaves this much under it for the floor (AC-4: a 50 m gun run ⇒ a 30 m floor).</summary>
        public const float OrderedMarginM = 20f;

        /// <summary>AC-4's floor: min(150, the ordered height − 20), never below 8.</summary>
        public static float FloorFor(float orderedAgl)
            => orderedAgl <= 0f ? DefaultFloorM : Math.Clamp(orderedAgl - OrderedMarginM, MinFloorM, DefaultFloorM);

        /// <summary>The direction a recovery flies: the present heading turned by <paramref name="turnRad"/> (+ left), climbing <see cref="RecoveryClimbDeg"/>.</summary>
        public static Vector3 RecoveryDirection(in FlightBody b, float turnRad)
        {
            var f = b.Vel.LengthSquared() > 1f ? Vector3.Normalize(b.Vel) : Steer3D.Forward(b.Rot);
            float yaw = MathF.Abs(f.X) + MathF.Abs(f.Y) > 1e-4f ? MathF.Atan2(f.Y, f.X) : MathF.Atan2(Steer3D.Up(b.Rot).Y, Steer3D.Up(b.Rot).X) + MathF.PI;
            yaw += turnRad;
            float c = RecoveryClimbDeg * MathF.PI / 180f;
            return new Vector3(MathF.Cos(yaw) * MathF.Cos(c), MathF.Sin(yaw) * MathF.Cos(c), MathF.Sin(c));
        }

        /// <summary>The lowest height above the ground (m) the recovery turning by <paramref name="turnRad"/> would fly, from <paramref name="b"/>.</summary>
        public static float RecoveryClearance(FlightBody b, in FighterLimits l, float maxG, float throttle, Fdp.Toolkit.World.IWorldQuery? terrain, float turnRad)
        {
            float min = float.MaxValue;
            int react = (int)MathF.Ceiling(ReactionS / StepS), total = (int)(HorizonS / StepS);
            var hold = b.Vel.LengthSquared() > 1f ? Vector3.Normalize(b.Vel) : Steer3D.Forward(b.Rot);
            for (int i = 0; i < total; i++)
            {
                if (i == react) hold = RecoveryDirection(in b, turnRad);   // the recovery's heading is chosen once, when the pull starts
                Steer3D.Step(ref b, hold, maxG, MathF.Max(throttle, 1f), in l, StepS);
                float agl = b.Pos.Z - (terrain?.GroundHeightAt(b.Pos.X, b.Pos.Y) ?? 0f);
                if (agl < min) min = agl;
            }
            return min;
        }

        /// <summary>
        /// The recovery to fly from <paramref name="b"/>: straight ahead, or the climbing turn (± <see cref="TurnAwayDeg"/>) that clears best when
        /// the straight one does not clear <paramref name="floor"/>. Returns its turn (rad) and its clearance.
        /// </summary>
        public static (float Turn, float Clearance) BestRecovery(in FlightBody b, in FighterLimits l, float maxG, Fdp.Toolkit.World.IWorldQuery? terrain, float floor)
        {
            float straight = RecoveryClearance(b, in l, maxG, 1f, terrain, 0f);
            if (straight >= floor) return (0f, straight);
            float turn = TurnAwayDeg * MathF.PI / 180f;
            float left = RecoveryClearance(b, in l, maxG, 1f, terrain, turn);
            float right = RecoveryClearance(b, in l, maxG, 1f, terrain, -turn);
            if (straight >= left && straight >= right) return (0f, straight);
            return left >= right ? (turn, left) : (-turn, right);
        }
    }
}
