using System;
using System.Numerics;
using CarKinem.Core;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3286</c> (R-3025; 📄 docs/designs/motion-models/DESIGN_Motion_Domains.md §5f) — a ground vehicle the wall guard holds BACKS OUT:
    /// against the held motion, on full lock with the nose swinging toward its path, until <see cref="HullClearance.WayOnClear"/> says the
    /// follower can drive on — the reverse leg of a three-point turn. Its state is the watchdog's own <see cref="FrustrationTicks"/> (the
    /// recovery is the watchdog's remedy for a held hull); <c>CarKinematicsSystem</c> drives it, <c>NavigationExecutionSystem</c> does not count
    /// a recovering hull as stuck. Bounded: <see cref="MaxRecoveries"/> tries a move, then the held hull is the watchdog's again.
    /// </summary>
    public static class HullRecovery
    {
        /// <summary>How long the guard holds a hull before it backs out (s).</summary>
        public const float HoldSeconds = 0.5f;

        /// <summary>The recovery's speed (m/s), capped by the hull's <see cref="VehicleParams.MaxSpeedRev"/> when it backs and has one.</summary>
        public const float SpeedMps = 2f;

        /// <summary>The longest one try drives, in hull lengths.</summary>
        public const float MaxHullLengths = 2f;

        /// <summary>Tries a move — then the watchdog's replan / <c>FailedBlocked</c> as before.</summary>
        public const byte MaxRecoveries = 3;

        /// <summary>How far a try drives between two exit tests (m).</summary>
        public const float CheckEveryM = 0.25f;

        /// <summary>Below this (m/s) a hull is standing — a standing start on a new move is checked before it drives.</summary>
        public const float StandingSpeed = 0.1f;

        /// <summary>Is this a move the hull is driving (a path or a point, not arrived, not held at a door)?</summary>
        public static bool Driving(in NavState nav)
            => (nav.Mode == KinematicsMode.CustomTrajectory || nav.Mode == KinematicsMode.Direct || nav.Mode == KinematicsMode.None)
               && nav.TargetSpeed > 0f && nav.HasArrived == 0 && nav.IsBlocked == 0;

        /// <summary>A key for the move (its path and destination), never 0 — so a standing start is checked once per move.</summary>
        public static int MoveKey(in NavState nav)
            => Fdp.Core.StableHash.Combine(nav.TrajectoryId, (int)MathF.Round(nav.FinalDestination.X * 10f), (int)MathF.Round(nav.FinalDestination.Y * 10f)) | 1;   // ⭐ CE-3303 — it lands in FrustrationTicks: not HashCode (seeded per process)

        /// <summary>
        /// Start a try: drive <paramref name="dir"/> (−1 back, +1 forward — against the held motion) with the wheels locked so the nose swings
        /// toward <paramref name="toAim"/>; every other try steers the other way.
        /// </summary>
        public static void Begin(ref FrustrationTicks ft, sbyte dir, Vector2 fwd, Vector2 toAim)
        {
            float cross = fwd.X * toAim.Y - fwd.Y * toAim.X;
            int nose = cross >= 0f ? 1 : -1;                  // +1: the nose should swing left (counter-clockwise)
            if ((ft.Recoveries & 1) == 1) nose = -nose;
            ft.RecoverDir = dir;
            ft.RecoverSteer = (sbyte)(dir < 0 ? -nose : nose);   // backing, the yaw rate has the speed's sign: steer the other way
            ft.RecoverMetres = 0f;
            ft.RecoverCheckedMetres = 0f;
            ft.WallHeldSeconds = 0f;
        }

        /// <summary>End a try (the way is clear, it is blocked, or it drove its limit).</summary>
        public static void End(ref FrustrationTicks ft)
        {
            ft.RecoverDir = 0;
            ft.RecoverSteer = 0;
            ft.WallHeldSeconds = 0f;
            if (ft.Recoveries < byte.MaxValue) ft.Recoveries++;
        }

        /// <summary>The speed of a try (m/s, signed).</summary>
        public static float Speed(in VehicleParams p, sbyte dir)
            => dir < 0 ? -(p.MaxSpeedRev > 0f ? MathF.Min(SpeedMps, p.MaxSpeedRev) : SpeedMps)
                       : (p.MaxSpeedFwd > 0f ? MathF.Min(SpeedMps, p.MaxSpeedFwd) : SpeedMps);
    }
}
