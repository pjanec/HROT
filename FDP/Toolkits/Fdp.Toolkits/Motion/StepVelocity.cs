using System.Numerics;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-11057</c> — the ONE rule for a motion model that writes its velocity as the step it just moved ÷ the frame's dt: a frame shorter
    /// than <see cref="MinDtSeconds"/> carries no measurable motion, so the velocity it had is KEPT. ⛔ Before, the models divided by
    /// <c>max(dt, 1e-4)</c>: a 3 µs frame (the master clock's resume frame) wrote 150 m/s × 3 µs ÷ 0.1 ms = 4.9 m/s, and the next frame's
    /// route follower read the plane as "already flying" at 4.9 m/s — it was clamped to its stall speed and spent ~20 s accelerating
    /// (📌 <c>fl-napalm-strike</c> live: the A-10's release 5.7 s late).
    /// </summary>
    public static class StepVelocity
    {
        /// <summary>A frame shorter than this (s) measures no velocity.</summary>
        public const float MinDtSeconds = 1e-4f;

        /// <summary>The velocity of a step from <paramref name="from"/> to <paramref name="to"/> in <paramref name="dt"/>; <paramref name="previous"/> on a frame too short to measure.</summary>
        public static Vector3 Of(Vector3 from, Vector3 to, float dt, Vector3 previous) => dt < MinDtSeconds ? previous : (to - from) / dt;
    }
}
