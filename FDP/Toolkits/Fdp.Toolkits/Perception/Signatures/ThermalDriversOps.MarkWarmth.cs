using System;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-4646</c> (📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §4) — THE thermal calculator: static, pure, 0 B — every reader computes the heat from the
    /// replicated DRIVERS (TD-1), never from a sent temperature. ⚠ PARTIAL: this file is terrain's part (<c>CE-4664</c>, the ground heat marks); backend's <c>CE-4657</c>
    /// adds <c>Signature</c> and <c>BarrelHeat</c> in its own file of the same class (NOTE_Terrain_To_Backend_Ground_Heat_Marks.md).
    /// </summary>
    public static partial class ThermalDriversOps
    {
        /// <summary>The engine time that warms the ground under a hull most of the way (s): 1 − e⁻¹ after this long.</summary>
        public const double GroundWarmUpSeconds = 600.0;

        /// <summary>
        /// ⭐ <c>CE-4664</c> (TD-14, §4b) — how warm a ground heat mark is at <paramref name="now"/>, 0 … 1 (1 = the ground under a hull whose engine ran long): the stand warms
        /// it by the engine time (<c>1 − e^(−on / GroundWarmUpSeconds)</c>), and it cools from when the vehicle left, by its power class — ~0.05 at
        /// <see cref="GroundHeatMarkRules.ColdAfterSeconds"/> in calm dry weather (an e-folding of a third of it), faster in wind (+10 % per m/s of the lowest wind level) and in
        /// rain or snow (× 2). 0 before the vehicle left, and once cold. Starter values; the external image generator solves its own from the same drivers.
        /// ⛔ No allocation, no state.
        /// </summary>
        public static unsafe float MarkWarmth(in HeatMark mark, double now, in WeatherConditions weather)
        {
            if (mark.Kind == HeatMarkKind.NoMark || mark.EngineOnSeconds <= 0f) return 0f;
            double since = now - mark.LeftAt.Seconds;
            if (since < 0.0 || GroundHeatMarkRules.IsCold(in mark, now)) return 0f;
            double warmed = 1.0 - Math.Exp(-mark.EngineOnSeconds / GroundWarmUpSeconds);
            double rate = 3.0 / GroundHeatMarkRules.ColdAfterSeconds(mark.PowerClass);
            if (weather.WindCount > 0) rate *= 1.0 + 0.1 * Math.Max(0f, weather.WindSpeedMps[0]);
            if (weather.Precipitation != 0 && weather.PrecipitationMmPerHour > 0f) rate *= 2.0;
            return (float)(warmed * Math.Exp(-rate * since));
        }
    }
}
