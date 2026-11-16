using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-2 (docs/designs/animals/DESIGN_Animals.md AN-4, AR-2) — a BIRD (a flock entity or a lone bird): a light point mass that
    /// flies toward its intent's point at its speed, turns with a capped yaw rate (a banked turn — the bank itself is the presentation's), cruises
    /// at <see cref="CruiseAglM"/> while far and comes down onto the point (a tree top to perch on, a field to land on) when near. ⛔ Never an
    /// aircraft (<see cref="Locomotions.IsAircraft"/> excludes <see cref="LocomotionClass.Bird"/>: radar, air contacts and air defence never see it).
    /// ⭐ The hen's flutter is the same model on a short, low hop (AR-21). No intent: it stays where it is (perched / landed: still).
    /// </summary>
    public sealed class BirdFlightModel : IMotionModel
    {
        public LocomotionClass Class => LocomotionClass.Bird;

        /// <summary>Cruise height above the ground while far from the point (m).</summary>
        public const float CruiseAglM = 25f;
        /// <summary>Within this flat distance it comes down onto the point (m).</summary>
        public const float DescendWithinM = 60f;
        /// <summary>The turn-rate cap (rad/s): a banked turn, never a spin on the spot.</summary>
        public const float MaxYawRate = 2.2f;
        /// <summary>Climb / descent cap (m/s).</summary>
        public const float MaxVerticalMps = 4f;
        public const float DefaultSpeedMps = 10f;
        public const float DefaultArrivalM = 1.5f;

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            if (intent.Mode == NavigationMode.None || intent.Mode == NavigationMode.Manual || dt <= 0f)
            {
                f.Velocity.Linear = Vector3.Zero;   // perched / landed: still
                return;
            }
            var here = f.Transform.Position;
            Vector3 target = intent.FinalDestination;
            var flat = new Vector2(target.X - here.X, target.Y - here.Y);
            float dist = flat.Length();
            float arrive = intent.ArrivalRadius > 0f ? intent.ArrivalRadius : DefaultArrivalM;
            if (dist <= arrive && MathF.Abs(target.Z - here.Z) <= 1.5f)
            {
                f.Velocity.Linear = Vector3.Zero;
                f.Transform.Position = target;
                Report(ref f, in intent, NavigationResult.Arrived, NavigationPhase.Completed);
                return;
            }
            float speed = intent.TargetSpeed > 0f ? intent.TargetSpeed : DefaultSpeedMps;
            // heading: turn toward the point at a capped rate (a bird banks round, it does not pivot)
            float yaw = RotorcraftModel.YawOf(f.Transform.Rotation);
            float want = dist > 1e-3f ? MathF.Atan2(flat.Y, flat.X) : yaw;
            float diff = MathF.IEEERemainder(want - yaw, 2f * MathF.PI);
            float turn = Math.Clamp(diff, -MaxYawRate * dt, MaxYawRate * dt);
            yaw += turn;
            // slow into the last metres so it settles on the point
            float hs = MathF.Min(speed, MathF.Max(1.5f, dist / MathF.Max(dt, 1e-3f) * 0.5f));
            if (dist < 8f) hs = MathF.Min(hs, MathF.Max(1f, dist));
            var fwd = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw));
            // height: cruise far away, come down onto the point near it
            float ground = f.Ground(here.X, here.Y);
            float wantZ = dist > DescendWithinM ? MathF.Max(ground + CruiseAglM, target.Z) : target.Z;
            float vz = Math.Clamp((wantZ - here.Z) / MathF.Max(dt, 1e-3f), -MaxVerticalMps, MaxVerticalMps);
            var v = new Vector3(fwd.X * hs, fwd.Y * hs, vz);
            f.Velocity.Linear = v;
            var next = here + v * dt;
            float floor = f.Ground(next.X, next.Y);
            if (next.Z < floor) next.Z = floor;
            f.Transform.Position = next;
            f.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);
            Report(ref f, in intent, NavigationResult.InProgress, NavigationPhase.Following);
        }

        /// <summary>A bird is never flown by hand (no possession of a bird).</summary>
        public void Manual(ref MotionFrame f, in ManualDrive drive, float dt) => f.Velocity.Linear = Vector3.Zero;

        private static void Report(ref MotionFrame f, in NavigationIntent intent, NavigationResult result, NavigationPhase phase)
        {
            if (!f.World.IsComponentTypeRegistered<NavigationStatus>() || !f.World.HasComponent<NavigationStatus>(f.Self)) return;
            ref var s = ref f.World.GetComponentRW<NavigationStatus>(f.Self);
            s.IntentId = intent.IntentId;
            s.Result = result;
            s.Phase = phase;
        }
    }
}
