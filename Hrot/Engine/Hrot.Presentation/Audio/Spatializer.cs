using System;
using System.Numerics;

namespace Hrot.UI.Common.Audio
{
    /// <summary>The listener: where the FPS camera is and where it looks (X east, Y north, Z up).</summary>
    public readonly record struct Ear(Vector3 Position, Vector3 Forward);

    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-2, §4) — 3-D sound done by hand, pure: how loud a source is at a distance (full
    /// inside its reference, falling with distance, silent at its range — the AI's hearing range, AU-4), where it sits left / right of the
    /// ear, the Doppler shift of a mover and the speed-of-sound delay.
    /// </summary>
    public static class Spatializer
    {
        public const float SpeedOfSound = 343f;

        /// <summary>A pan never goes fully to one side — a source straight left is still faintly in the right ear.</summary>
        public const float MaxPan = 0.9f;

        /// <summary>The Doppler pitch is held within ±20 %.</summary>
        public const float MaxDoppler = 0.2f;

        /// <summary>The gain (0…1) at <paramref name="distance"/>: r₀ / max(d, r₀), faded smoothly to 0 over the second half of
        /// <paramref name="range"/>; 0 at and beyond it; a zero range is silent.</summary>
        public static float Gain(float distance, float reference, float range)
        {
            if (range <= 0f || distance >= range) return 0f;
            float r0 = MathF.Max(0.1f, reference);
            float near = r0 / MathF.Max(distance, r0);
            float t = Math.Clamp((distance - 0.5f * range) / (0.5f * range), 0f, 1f);
            return near * (1f - t * t * (3f - 2f * t));
        }

        /// <summary>A level in dB as a factor.</summary>
        public static float Db(float db) => MathF.Pow(10f, db / 20f);

        /// <summary>−1 left … +1 right of the ear's forward, in the horizontal plane; 0 straight ahead, behind, or on the ear.</summary>
        public static float Pan(in Ear ear, Vector3 at)
        {
            var fwd = new Vector2(ear.Forward.X, ear.Forward.Y);
            var to = new Vector2(at.X - ear.Position.X, at.Y - ear.Position.Y);
            if (fwd.LengthSquared() < 1e-6f || to.LengthSquared() < 1e-4f) return 0f;
            fwd = Vector2.Normalize(fwd);
            var right = new Vector2(fwd.Y, -fwd.X);
            return Math.Clamp(Vector2.Dot(Vector2.Normalize(to), right), -1f, 1f) * MaxPan;
        }

        /// <summary>The pitch factor of a source moving at <paramref name="velocity"/>: higher coming, lower going.</summary>
        public static float Doppler(in Ear ear, Vector3 at, Vector3 velocity)
        {
            var dir = at - ear.Position;
            float d = dir.Length();
            if (d < 1e-3f) return 1f;
            float away = Vector3.Dot(velocity, dir / d);
            return Math.Clamp(SpeedOfSound / MathF.Max(1f, SpeedOfSound + away), 1f - MaxDoppler, 1f + MaxDoppler);
        }

        /// <summary>How long the sound takes to reach the ear (s).</summary>
        public static double Delay(float distance) => MathF.Max(0f, distance) / SpeedOfSound;

        /// <summary>§4 — a shot's NEAR sound fades out between <paramref name="from"/> and <paramref name="to"/> while its far sound fades
        /// in: the near share (1 close, 0 far).</summary>
        public static float NearShare(float distance, float from = 120f, float to = 180f)
        {
            float t = Math.Clamp((distance - from) / (to - from), 0f, 1f);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>The point of segment a→b nearest <paramref name="p"/>, and its distance.</summary>
        public static (Vector3 Point, float Distance) Closest(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
            var c = a + ab * t;
            return (c, Vector3.Distance(c, p));
        }
    }
}
