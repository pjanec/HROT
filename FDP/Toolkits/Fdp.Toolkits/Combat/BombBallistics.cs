using System;
using System.Numerics;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §8, A3) — where a bomb dropped now lands, and when to drop
    /// it. ONE predictor that steps EXACTLY what flies the bomb: each frame <c>BallisticsSystem</c> takes gravity off its velocity
    /// (<see cref="CombatConstants.Gravity"/>), then <c>LinearKinematicsSystem</c> moves it by velocity × dt — so at the sim's dt the
    /// predicted impact IS the flown one (no drag on either side; adding drag later keeps them identical).
    /// </summary>
    public static class BombBallistics
    {
        /// <summary>A bomb leaves its rack at the carrier's velocity plus this, downward (m/s).</summary>
        public const float EjectMps = 1f;

        /// <summary>The Release profile drops only when the predicted impact passes the target within this, across the track (m).</summary>
        public const float CrossTrackToleranceM = 10f;

        /// <summary>The step the prediction takes: the sim's fixed frame.</summary>
        public const float DefaultDt = 1f / 60f;

        /// <summary>The longest fall predicted (s) — past it the prediction gives up (no ground below).</summary>
        public const float MaxFallSeconds = 120f;

        /// <summary>The velocity a bomb leaves the carrier with.</summary>
        public static Vector3 Launch(Vector3 carrierVelocity) => carrierVelocity + new Vector3(0f, 0f, -EjectMps);

        /// <summary>
        /// Where a bomb at <paramref name="p"/> moving at <paramref name="v"/> meets the ground (<paramref name="ground"/> null ⇒ flat at 0),
        /// and how long it falls. The step crossing the ground is interpolated like <c>BallisticsSystem</c> does.
        /// </summary>
        public static (Vector3 Impact, float Seconds) Impact(Vector3 p, Vector3 v, Func<float, float, float>? ground, float dt = DefaultDt)
        {
            ground ??= static (_, _) => 0f;
            int maxSteps = (int)(MaxFallSeconds / dt);
            for (int i = 1; i <= maxSteps; i++)
            {
                v.Z -= CombatConstants.Gravity * dt;   // BallisticsSystem
                var next = p + v * dt;                 // LinearKinematicsSystem
                float g = ground(next.X, next.Y);
                if (next.Z <= g)
                {
                    float t = p.Z - next.Z > 1e-6f ? Math.Clamp((p.Z - g) / (p.Z - next.Z), 0f, 1f) : 1f;
                    return (Vector3.Lerp(p, next, t), (i - 1 + t) * dt);
                }
                p = next;
            }
            return (p, MaxFallSeconds);
        }

        /// <summary>
        /// The release cue for a carrier at <paramref name="p"/> with velocity <paramref name="v"/> against <paramref name="target"/>:
        /// how far the target lies ahead of (+) or behind (−) the impact a bomb dropped NOW would make, how far to the side, and the
        /// seconds until that impact reaches the target at the carrier's ground speed (≤ 0 ⇒ drop now).
        /// </summary>
        public static (float AlongTrack, float CrossTrack, float SecondsToRelease) Cue(Vector3 p, Vector3 v, Vector3 target,
                                                                                       Func<float, float, float>? ground, float dt = DefaultDt)
        {
            var (impact, _) = Impact(p, Launch(v), ground, dt);
            var track = new Vector2(v.X, v.Y);
            float speed = track.Length();
            var miss = new Vector2(target.X - impact.X, target.Y - impact.Y);
            if (speed < 1e-3f) return (miss.Length(), 0f, float.PositiveInfinity);
            var dir = track / speed;
            float along = Vector2.Dot(miss, dir);
            float cross = dir.X * miss.Y - dir.Y * miss.X;
            return (along, cross, along / speed);
        }

        /// <summary>A carrier more than this past its release point has missed the pass (it does not drop late).</summary>
        public const float MissedPassSeconds = 0.5f;

        /// <summary>The Release profile's test: the cue says NOW — the impact has reached the target along the track (within half a
        /// step ahead, so the drop lands centred on it), but not more than <see cref="MissedPassSeconds"/> ago — and the target is
        /// within <see cref="CrossTrackToleranceM"/> to the side. One bomb per pass is the action's round count (or its cooldown).</summary>
        public static bool ReleaseNow(Vector3 p, Vector3 v, Vector3 target, Func<float, float, float>? ground, float dt = DefaultDt)
        {
            var (_, cross, seconds) = Cue(p, v, target, ground, dt);
            return seconds <= 0.5f * dt && seconds > -MissedPassSeconds && MathF.Abs(cross) <= CrossTrackToleranceM;
        }
    }
}
