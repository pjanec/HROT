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
            if (Dead(in f)) { Fall(ref f, dt); return; }   // ⭐ CE-4649 part C ② (AN-20a) — a shot bird falls whatever it was told, and lies where it lands
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
            float wantZ = dist > DescendWithinM ? MathF.Max(ground + CruiseOf(in f), target.Z) : target.Z;
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

        /// <summary>⭐ CE-4649 part C ② — a dead bird (<c>Health</c> ≤ 0): the same fall a dead rotorcraft takes (<see cref="RotorcraftModel.Gravity"/>, its forward speed decaying), then it lies on the ground still.</summary>
        private static void Fall(ref MotionFrame f, float dt)
        {
            var here = f.Transform.Position;
            float floor = f.Ground(here.X, here.Y);
            if (dt <= 0f || here.Z <= floor + 1e-3f) { f.Velocity.Linear = Vector3.Zero; if (here.Z < floor) f.Transform.Position = new Vector3(here.X, here.Y, floor); return; }
            var v = f.Velocity.Linear;
            float decay = MathF.Max(0f, 1f - 1.5f * dt);
            v = new Vector3(v.X * decay, v.Y * decay, v.Z - RotorcraftModel.Gravity * dt);
            var next = here + v * dt;
            if (next.Z <= floor) { next.Z = floor; v = Vector3.Zero; }
            f.Velocity.Linear = v;
            f.Transform.Position = next;
        }

        private static bool Dead(in MotionFrame f)
        {
            var w = f.World;
            return w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(f.Self)
                   && w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(f.Self).Current <= 0f;
        }

        /// <summary>⭐ CE-4649 part C ④ (AN-20c) — a DUCK flies LOW over the water and the fields (m above the ground); every other bird cruises at <see cref="CruiseAglM"/>.</summary>
        public const float DuckCruiseAglM = 8f;

        private static float CruiseOf(in MotionFrame f)
        {
            var w = f.World;
            if (w.IsComponentTypeRegistered<Fdp.Toolkit.Animals.AnimalState>() && w.HasComponent<Fdp.Toolkit.Animals.AnimalState>(f.Self)
                && w.GetComponentRO<Fdp.Toolkit.Animals.AnimalState>(f.Self).Species is Fdp.Toolkit.Animals.AnimalSpecies.Duck or Fdp.Toolkit.Animals.AnimalSpecies.DuckFlock) return DuckCruiseAglM;
            return CruiseAglM;
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
