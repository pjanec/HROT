using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Toolkit.World;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3286</c> (R-3025; 📄 docs/designs/motion-models/DESIGN_Motion_Domains.md §5f) — the HULL against the world's closed barriers:
    /// a ground vehicle's step is traced for its four corners and its centre (<see cref="StepBlocked"/>), and the way on from a pose is a
    /// rollout of the path follower itself (<see cref="WayOnClear"/>). 📌 Live: the CE-2198 guard traced only the centre, so the front half of
    /// a tank stood inside a building, and a hull that turns only while driving forward could not leave it.
    /// </summary>
    public static class HullClearance
    {
        /// <summary>A crossing that begins further along the line than this (of its length) ENTERS a barrier; one at 0 began inside it.</summary>
        public const float EnterEpsilon = 1e-3f;

        /// <summary>The rollout's step (m).</summary>
        public const float RolloutStepM = 0.5f;

        /// <summary>The rollout is done once the hull heads within this of its aim (rad) and has driven a hull length.</summary>
        public const float AlignedRad = 0.26f;

        [ThreadStatic] private static List<TraceCrossing>? t_crossings;

        /// <summary>Does this vehicle have a hull to trace (a length and a width; a person has none)?</summary>
        public static bool HasHull(in VehicleParams p) => p.Class != VehicleClass.Pedestrian && p.Length > 0f && p.Width > 0f;

        /// <summary>The smallest turning radius (m) — the bicycle model's, at full lock.</summary>
        public static float TurnRadius(in VehicleParams p)
            => p.WheelBase > 0f && p.MaxSteerAngle > 0f ? p.WheelBase / MathF.Sin(MathF.Min(p.MaxSteerAngle, 1.5f)) : 0f;

        /// <summary>
        /// Does the step from (<paramref name="from"/>, <paramref name="fromFwd"/>) to (<paramref name="to"/>, <paramref name="toFwd"/>) carry the
        /// centre or a corner of a <paramref name="length"/> × <paramref name="width"/> hull INTO a closed barrier (a building's wall, a closed door —
        /// R-252's meaning) at <see cref="CarKinem.Systems.CarKinematicsSystem.HullProbeM"/> above its wheels? A point already inside may move — so a
        /// hull left half inside can still leave. A hull with no size is traced at its centre only (the CE-2198 rule).
        /// </summary>
        public static bool StepBlocked(IWorldQuery world, Vector2 from, Vector2 fromFwd, float fromZ, Vector2 to, Vector2 toFwd, float toZ,
                                       float length, float width)
        {
            if (Enters(world, from, fromZ, to, toZ)) return true;
            if (length <= 0f || width <= 0f) return false;
            float a = length * 0.5f, b = width * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float along = (i & 1) == 0 ? a : -a, side = (i & 2) == 0 ? b : -b;
                if (Enters(world, Corner(from, fromFwd, along, side), fromZ, Corner(to, toFwd, along, side), toZ)) return true;
            }
            return false;
        }

        /// <summary>A point of the hull: <paramref name="along"/> ahead of the centre, <paramref name="side"/> to its left.</summary>
        public static Vector2 Corner(Vector2 centre, Vector2 fwd, float along, float side)
            => centre + fwd * along + new Vector2(-fwd.Y, fwd.X) * side;

        /// <summary>
        /// Does the point's step from <paramref name="a"/> to <paramref name="b"/> go into a wall — CE-2198's two tests, per point: ① the
        /// surface under <paramref name="b"/> RISES like a wall (inside a solid block the sampler offers only its roof; the stand-in's block is
        /// not a "closed barrier", which is a blast meaning — W-7′) where it did not under <paramref name="a"/>; ② the line ENTERS a closed
        /// barrier (a building's wall panel, a closed door).
        /// </summary>
        private static bool Enters(IWorldQuery world, Vector2 a, float za, Vector2 b, float zb)
        {
            if (Vector2.DistanceSquared(a, b) < 1e-8f) return false;
            float step = Vector2.Distance(a, b);
            float rise = world.SurfaceZ(b.X, b.Y, za) - za;
            if (rise > CarKinem.Systems.CarKinematicsSystem.VehicleMaxStepM && rise > CarKinem.Systems.CarKinematicsSystem.VehicleMaxClimbSlope * step
                && world.SurfaceZ(a.X, a.Y, za) - za <= CarKinem.Systems.CarKinematicsSystem.VehicleMaxStepM)
                return true;
            var crossings = t_crossings ??= new List<TraceCrossing>();   // ⭐ one per thread: the kinematics runs ForEachParallel
            float probe = CarKinem.Systems.CarKinematicsSystem.HullProbeM;
            world.Trace(new Vector3(a, za + probe), new Vector3(b, zb + probe), TracePurpose.Fire, crossings);
            foreach (var c in crossings)
                if (c.ClosedBarrier && c.T > EnterEpsilon) return true;
            return false;
        }

        /// <summary>
        /// ⭐⭐ The EXIT TEST of a recovery — "smart" is here (R-3025): can the hull drive on from (<paramref name="pos"/>, <paramref name="fwd"/>)?
        /// The path follower is rolled out — pure pursuit at the path's lookahead (the same <see cref="PurePursuitController"/>,
        /// <see cref="CarKinem.Systems.CarKinematicsSystem.PathLookahead"/> and chord cap as the live follower), the bicycle model in
        /// <see cref="RolloutStepM"/> steps, every step through <see cref="StepBlocked"/> — over a quarter turn at full lock plus one hull length,
        /// or until it heads along its aim having driven a hull length, or reaches the path's end. ⛔ Never a fixed back-up distance: one metre
        /// is "the same situation one metre away".
        /// </summary>
        public static bool WayOnClear(IWorldQuery world, TrajectoryPoolManager? pool, in NavState nav, in VehicleParams p,
                                      Vector2 pos, Vector2 fwd, float z)
        {
            bool path = nav.Mode == KinematicsMode.CustomTrajectory && pool != null && pool.TryGetTrajectory(nav.TrajectoryId, out _);
            Vector2 end = EndPoint(pool, in nav, path);
            float speed = nav.TargetSpeed > 0f ? nav.TargetSpeed : 5f;
            float tol = nav.ArrivalRadius > 0f ? nav.ArrivalRadius : 2f;
            float horizon = MathF.PI * 0.5f * TurnRadius(in p) + p.Length;
            float s = nav.ProgressS, travelled = 0f;
            var at = pos;
            var f = fwd;
            while (travelled < horizon)
            {
                if (Vector2.Distance(at, end) <= tol) return true;
                var aim = AimPoint(pool, in nav, in p, path, s, speed, at);
                var to = aim - at;
                if (to.LengthSquared() < 1e-6f) return true;
                float steer = PurePursuitController.CalculateSteering(at, f, Vector2.Normalize(to) * speed, speed, p.WheelBase,
                    p.LookaheadTimeMin, p.LookaheadTimeMax, p.MaxSteerAngle, false);
                float turn = p.WheelBase > 0f ? RolloutStepM / p.WheelBase * MathF.Tan(steer) : 0f;
                float c = MathF.Cos(turn), sn = MathF.Sin(turn);
                var nf = Vector2.Normalize(new Vector2(f.X * c - f.Y * sn, f.X * sn + f.Y * c));
                var nat = at + nf * RolloutStepM;
                float nz = world.SurfaceZ(nat.X, nat.Y, z);
                if (StepBlocked(world, at, f, z, nat, nf, nz, p.Length, p.Width)) return false;
                if (path)
                {
                    var (_, tangent, _) = pool!.SampleTrajectory(nav.TrajectoryId, s);
                    s += RolloutStepM * MathF.Max(0f, Vector2.Dot(nf, tangent));
                }
                at = nat; f = nf; z = nz;
                travelled += RolloutStepM;
                if (travelled >= p.Length && MathF.Abs(VectorMath.SignedAngle(f, aim - at)) <= AlignedRad) return true;
            }
            return true;
        }

        /// <summary>Where the follower aims from progress <paramref name="s"/>: the path point a (chord-capped) lookahead on, else the destination.</summary>
        public static Vector2 AimPoint(TrajectoryPoolManager? pool, in NavState nav, in VehicleParams p, bool path, float s, float speed, Vector2 at)
        {
            if (!path) return new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y);
            float ld = CarKinem.Systems.CarKinematicsSystem.CapLookaheadByChord(pool!, nav.TrajectoryId, s,
                CarKinem.Systems.CarKinematicsSystem.PathLookahead(in p, speed),
                MathF.Max(CarKinem.Systems.CarKinematicsSystem.MinPathLookaheadMetres, p.WheelBase * CarKinem.Systems.CarKinematicsSystem.ChordCapFloorWheelBases));
            var (ahead, _, _) = pool!.SampleTrajectory(nav.TrajectoryId, s + ld);
            return new Vector2(ahead.X, ahead.Y);
        }

        /// <summary>The move's end: the path's last point, else its destination.</summary>
        public static Vector2 EndPoint(TrajectoryPoolManager? pool, in NavState nav, bool path)
        {
            if (path && pool!.TryGetTrajectory(nav.TrajectoryId, out var traj) && traj.IsLooped == 0 && traj.Waypoints.Length > 0)
            {
                var last = traj.Waypoints[traj.Waypoints.Length - 1].Position;
                return new Vector2(last.X, last.Y);
            }
            return new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y);
        }
    }
}
