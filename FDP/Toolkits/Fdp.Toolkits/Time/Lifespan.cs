using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// ⭐ W1 (docs/DESIGN_Visual_Effects.md §7 VE-Q, R-269) — <b>a timed entity:</b> when it was made, in SIMULATION seconds, and how
    /// long it lives. <see cref="LifespanSystem"/> removes it once sim time passes <c>BornAt + Seconds</c> — one collector for every
    /// timed entity, not a timer per feature (U7: <i>"some garbage collection system removes those … maybe this could be a generic
    /// component"</i>).
    /// <para>Its age is never stored: it is <c>now − BornAt</c>, read from the sim clock, so a paused sim freezes it mid-phase (VE-G) and
    /// a replay jump lands on the right phase with no catch-up (VE-R).</para>
    /// <para>⭐ RECORDED, never saved to a scenario (VE-R, U8: <i>"If recorded, replay will show effects properly after jump in
    /// time"</i>), and not copied into a preview.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Lifespan)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoPreview)]
    public struct Lifespan
    {
        /// <summary>The sim time it was made (<see cref="ISimClock.TotalTime"/>).</summary>
        public double BornAtSimSeconds;

        /// <summary>How long it lives (s).</summary>
        public float Seconds;

        /// <summary>A lifespan starting at <paramref name="now"/>.</summary>
        public static Lifespan Starting(double now, float seconds) => new() { BornAtSimSeconds = now, Seconds = seconds };

        /// <summary>Seconds since it was made (0 before it was made — a jump back).</summary>
        public readonly float Age(double now) => (float)Math.Max(0.0, now - BornAtSimSeconds);

        /// <summary>Seconds left (0 once over).</summary>
        public readonly float Remaining(double now) => Math.Max(0f, Seconds - Age(now));

        /// <summary>How far through its life: 0 when made, 1 when it ends.</summary>
        public readonly float Phase(double now) => Seconds > 0f ? Math.Clamp(Age(now) / Seconds, 0f, 1f) : 1f;

        /// <summary>Over: its time has passed, or sim time is BEFORE it was made (a jump back to before it existed).</summary>
        public readonly bool IsOver(double now) => now >= BornAtSimSeconds + Seconds || now < BornAtSimSeconds;
    }
}
