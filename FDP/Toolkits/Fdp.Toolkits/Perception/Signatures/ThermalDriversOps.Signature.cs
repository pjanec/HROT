using System;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-4657</c> (TD-S1; 📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §4) — backend's part of THE thermal calculator: an entity's heat and a barrel's heat
    /// in CLOSED FORM from the replicated drivers (the decision + its times), so every node — a late joiner included — computes the same value without integrating
    /// frames. Terrain's part (<c>MarkWarmth</c>) is in <c>ThermalDriversOps.MarkWarmth.cs</c>. ⛔ No allocation, no state. Starter values: the rates are the TKB's
    /// existing ones read as time constants (warm-up 1 / <c>RunningHeatPerSecond</c>, cooling 1 / <c>CooldownPerSecond</c>); the external image generator solves
    /// its own physics from the same drivers.
    /// </summary>
    public static partial class ThermalDriversOps
    {
        /// <summary>
        /// ⭐ What a thermal sensor sees of an entity at <paramref name="now"/>, 0..1: <c>Base + max(source, barrel, fire) × (1 − Base)</c> — the body / engine, the gun and a
        /// fire are separate sources and the sensor sees the hottest (the old <see cref="ThermalState.Signature"/> rule, R-205).
        /// <list type="bullet">
        /// <item>An ENGINE (<see cref="ThermalState.HasPowerPlant"/>): Off cools from idle heat since <see cref="HeatSource.Since"/>; Idle warms toward idle heat and keeps
        /// the residue of the last drive (cooling since <see cref="HeatSource.MotionSince"/>, when it stopped); Running warms toward the load heat (idle → running by
        /// speed / reference speed); Dead cools like Off; None is cold.</item>
        /// <item>A BODY: Standing is the resting heat, or the residue of the last run cooling since it stopped (<see cref="HeatSource.MotionSince"/>); Moving warms by
        /// the exertion (speed / reference speed) since it started.</item>
        /// </list>
        /// ⭐ A time at −∞ (<see cref="Fdp.Core.SimInstant"/> of <c>double.NegativeInfinity</c>) means "never" — an engine that never ran, a body that never moved — and
        /// contributes nothing. <paramref name="speed"/> is the speed every node already has (m/s); <paramref name="barrelHeat"/> 0..1 (<see cref="BarrelHeat"/>);
        /// <paramref name="fire"/> 0..1 (the strongest burning spot, <see cref="FireState.Intensity01"/>).
        /// </summary>
        public static float Signature(in ThermalState type, in HeatSource source, float speed, double now, float barrelHeat = 0f, float fire = 0f)
        {
            float heat = SourceHeat(in type, in source, speed, now);
            float hottest = MathF.Max(heat, MathF.Max(barrelHeat, fire));
            hottest = Math.Clamp(hottest, 0f, 1f);
            return type.BaseSignature + hottest * (1f - type.BaseSignature);
        }

        /// <summary>
        /// ⭐ <c>CE-4661</c> (TD-7) — how fast a dead BODY's own heat fades, per second: a time constant of 30 min (starter; a corpse loses its warmth over hours,
        /// not over the ~50 s a living body sheds a run). ⛔ An engine's Dead keeps its own <see cref="HeatSource.Since"/> and rate.
        /// </summary>
        public const double CorpseCoolPerSecond = 1.0 / 1800.0;

        /// <summary>The heat source's own heat, 0..1 (see <see cref="Signature"/>). ⭐ <c>CE-4661</c> — <paramref name="diedAt"/>: a BODY's time of death
        /// (<c>Health.StateSince</c> of a dead one; NaN = alive): its heat at death fades from then at <see cref="CorpseCoolPerSecond"/>.</summary>
        public static float SourceHeat(in ThermalState type, in HeatSource source, float speed, double now, double diedAt = double.NaN)
        {
            float idle = type.IdleHeat, running = type.RunningHeat;
            float reference = type.ReferenceSpeed > 0f ? type.ReferenceSpeed : 5f;
            float load = Math.Clamp(speed / reference, 0f, 1f);
            double warmRate = type.RunningHeatPerSecond > 0f ? type.RunningHeatPerSecond : 0.05;
            double coolRate = type.CooldownPerSecond > 0f ? type.CooldownPerSecond : 0.02;
            double since = now - source.Since.Seconds, motion = now - source.MotionSince.Seconds;

            float heat;
            switch (source.State)
            {
                case HeatSourceState.Off:
                case HeatSourceState.Dead:
                    heat = idle * Cool(since, coolRate);
                    break;
                case HeatSourceState.Idle:
                    heat = MathF.Max(idle * Warm(since, warmRate), running * Cool(motion, coolRate));
                    break;
                case HeatSourceState.Running:
                    heat = (idle + (running - idle) * load) * Warm(since, warmRate);
                    break;
                case HeatSourceState.Standing:
                    heat = MathF.Max(idle, running * Cool(motion, coolRate));
                    break;
                case HeatSourceState.Moving:
                    heat = idle + (running - idle) * load * Warm(motion, warmRate);
                    break;
                default:
                    heat = 0f;
                    break;
            }
            if (!source.IsEngine && !double.IsNaN(diedAt)) heat *= Cool(now - diedAt, CorpseCoolPerSecond);
            return Math.Clamp(heat, 0f, 1f);
        }

        /// <summary>
        /// ⭐ A barrel's heat at <paramref name="now"/>, 0..1, from its LAST shot (TD-5, §3d: the record a burst's end leaves) — the existing barrel rule
        /// (<c>Combat.BarrelHeat.Tick</c>: linear cooling at <paramref name="coolPerSecond"/>) in closed form. 0 before the shot or once cooled.
        /// </summary>
        public static float BarrelHeat(float heatAtLastShot, double lastShotAt, float coolPerSecond, double now)
            => Fdp.Toolkit.Combat.BarrelHeat.At(heatAtLastShot, lastShotAt, coolPerSecond, now);   // ⭐ CE-4659 — the ONE barrel rule

        /// <summary>1 − e^(−t·rate): 0 at t ≤ 0 (or a time of "never").</summary>
        private static float Warm(double t, double rate) => t <= 0.0 || double.IsNaN(t) ? 0f : (float)(1.0 - Math.Exp(-t * rate));

        /// <summary>e^(−t·rate): 1 at t ≤ 0, 0 for "never" (t = +∞).</summary>
        private static float Cool(double t, double rate) => double.IsNaN(t) ? 0f : t <= 0.0 ? 1f : (float)Math.Exp(-t * rate);
    }
}
