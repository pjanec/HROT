using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <c>CE-3216</c> S2 (R-3017; docs/designs/weather/DESIGN_Weather_And_Time.md §8) — how the weather and the night shorten what a
    /// unit's sensors reach. ONE helper every modality reads, over the ONE light curve the CE-3229 judge already uses
    /// (<see cref="Identification.LightFactor(float, float)"/>) — no second curve, no state: the weather is sampled once per sensor per
    /// solver tick at the OBSERVER (his own Z as the ground, AQ94 §116). A world with no World Environment reads every factor as 1 (S2-5).
    /// </summary>
    public static class PerceptionWeather
    {
        /// <summary>S2-1n — the light factor's floor for DETECTING a man at night (A-4's 0.1 is for identifying what he holds).</summary>
        public const float DetectNightFactor = 0.25f;

        /// <summary>S2-3 — a thermal sensor sees through haze better than the eye: its range is capped at this × the visibility.</summary>
        public const float ThermalFogFactor = 2f;

        /// <summary>S2-3 — the least share of a passive object's BASE signature left at the thermal crossover.</summary>
        public const float CrossoverFloor = 0.2f;

        /// <summary>S2-4 — the wind speed (m/s) in <see cref="HearingFactor(in WeatherSample)"/>'s masking term.</summary>
        public const float WindMaskMps = 8f;

        /// <summary>S2-4 — the rain rate (mm/h) in the same masking term.</summary>
        public const float RainMaskMmPerHour = 10f;

        /// <summary>S2-4 — hearing never falls below this share of the listener's range.</summary>
        public const float HearingFloor = 0.35f;

        /// <summary>The height above his ground at which a man's eyes and ears read the weather, m — ⚠ the wind's log profile is ZERO at the
        /// ground (W-3), so a sample at his feet would never mask his hearing (the trap CE-3258 met for a cloud lying on the ground).</summary>
        public const float SenseHeightM = 1.7f;

        /// <summary>The weather at <paramref name="unit"/>'s eyes and ears (his own Z as the ground, AQ94 §116), or false when the world has
        /// no clock (S2-5) or the unit no position.</summary>
        public static bool TrySample(ISimulationView view, Entity unit, out WeatherSample sample)
        {
            sample = default;
            if (view is not EntityRepository world || !ScenarioTime.TryClock(world, out _)) return false;
            if (unit.IsNull || !world.IsAlive(unit) || !world.HasComponent<SimTransform>(unit)) return false;
            var at = world.GetComponentRO<SimTransform>(unit).Position;
            sample = WeatherField.At(world, at + new System.Numerics.Vector3(0f, 0f, SenseHeightM), ScenarioTime.Utc(world), at.Z);
            return true;
        }

        /// <summary>S2-1 — sight: the range × the detection light factor, capped by the visibility.</summary>
        public static float VisionRange(float range, in WeatherSample w)
            => MathF.Min(range * Identification.LightFactor(w.RelativeLight, DetectNightFactor), w.VisibilityM);

        /// <summary>S2-3 — a thermal sensor ignores the light; fog caps it at <see cref="ThermalFogFactor"/> × the visibility.</summary>
        public static float ThermalRange(float range, in WeatherSample w) => MathF.Min(range, w.VisibilityM * ThermalFogFactor);

        /// <summary>S2-3 — the crossover: the share of a passive object's BASE signature left by the diurnal swing — 1 at the day's
        /// extremes, <see cref="CrossoverFloor"/> where the ground temperature crosses its daily mean (dawn and dusk).</summary>
        public static float BaseContrast(in WeatherSample w) => Math.Clamp(MathF.Abs(w.DiurnalPhase), CrossoverFloor, 1f);

        /// <summary>S2-4 — wind and rain mask hearing: × 1/(1 + wind/8 m/s) × 1/(1 + rain/10 mm/h), never below <see cref="HearingFloor"/>.</summary>
        public static float HearingFactor(in WeatherSample w)
        {
            float wind = w.Wind.Length();
            float f = 1f / (1f + wind / WindMaskMps) / (1f + MathF.Max(0f, w.PrecipitationMmPerHour) / RainMaskMmPerHour);
            return MathF.Max(HearingFloor, f);
        }
    }
}
