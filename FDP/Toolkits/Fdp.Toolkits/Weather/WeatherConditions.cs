using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Weather
{
    /// <summary>Kind of a cloud layer (a category: the strongest contributor wins a blend, W-2).</summary>
    public enum CloudKind : byte { Stratus = 0, Cumulus = 1, Stratocumulus = 2, Cirrus = 3, Cumulonimbus = 4 }

    /// <summary>
    /// ⭐ CE-3216 S1 (DESIGN_Weather_And_Time.md §6.3) — a cloud layer's cover by its aviation name (METAR): the octas
    /// (<see cref="WeatherConditions.CloudOctas"/>) stay the stored value; this names the bands so an author can say "overcast".
    /// </summary>
    public enum CloudCover : byte
    {
        /// <summary>SKC — 0 octas.</summary>
        Clear = 0,
        /// <summary>FEW — 1–2 octas.</summary>
        Few = 1,
        /// <summary>SCT — 3–4 octas.</summary>
        Scattered = 2,
        /// <summary>BKN — 5–7 octas (a ceiling).</summary>
        Broken = 3,
        /// <summary>OVC — 8 octas (a ceiling).</summary>
        Overcast = 4,
    }

    /// <summary>The octas ↔ <see cref="CloudCover"/> mapping (the METAR bands).</summary>
    public static class CloudCovers
    {
        /// <summary>The octas an author's cover stands for — the band's upper value (FEW 2, SCT 4, BKN 7, OVC 8): conservative for the
        /// light and the ceiling.</summary>
        public static byte Octas(CloudCover cover) => cover switch
        {
            CloudCover.Few => 2, CloudCover.Scattered => 4, CloudCover.Broken => 7, CloudCover.Overcast => 8, _ => 0,
        };

        /// <summary>The band <paramref name="octas"/> falls in.</summary>
        public static CloudCover Of(int octas) => octas switch
        {
            <= 0 => CloudCover.Clear, <= 2 => CloudCover.Few, <= 4 => CloudCover.Scattered, <= 7 => CloudCover.Broken, _ => CloudCover.Overcast,
        };
    }

    /// <summary>Kind of precipitation (a category: the strongest contributor wins a blend, W-2).</summary>
    public enum PrecipitationKind : byte { None = 0, Drizzle = 1, Rain = 2, Snow = 3, Sleet = 4, Hail = 5 }

    /// <summary>
    /// ⭐ CE-3216 S1 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md §1, W-0…W-3, W-7) — AUTHORED weather: the GLOBAL weather on
    /// the World Environment, or a REGIONAL weather on a Weather Region area. ONE type for both, so the blend has one input and the global
    /// weather is just the region that is everywhere. ⛔ No derived state lives here: temperature at a height, wind at a height, the sun
    /// and the light are computed by <see cref="WeatherField"/> on every node.
    /// <para>Fixed-size, so it replicates and saves as one block (a scenario saves it as authored). Wind levels and cloud bases are
    /// metres above sea level (W-3); the fog top is metres above the ground.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeatherConditions)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public unsafe struct WeatherConditions
    {
        public const int MaxWindLevels = 8;
        public const int MaxCloudLayers = 4;

        /// <summary>The DAILY MEAN air temperature at the ground, °C (§4: the diurnal swing is derived).</summary>
        public float GroundTemperatureC;
        /// <summary>Temperature fall with height, K per km (ICAO standard 6.5).</summary>
        public float LapseRateKPerKm;
        /// <summary>Sea-level pressure, hPa (standard 1013.25).</summary>
        public float SeaLevelPressureHpa;
        /// <summary>Relative humidity, 0…1 (W-7: data now, consumers in S2/S4).</summary>
        public float RelativeHumidity;

        /// <summary>How many of the wind levels are used.</summary>
        public byte WindCount;
        /// <summary>Wind level altitude, m above sea level, ascending.</summary>
        public fixed float WindAltitudeM[MaxWindLevels];
        /// <summary>Direction the wind blows FROM, compass degrees (0 = from the north).</summary>
        public fixed float WindFromDeg[MaxWindLevels];
        /// <summary>Mean wind speed, m/s.</summary>
        public fixed float WindSpeedMps[MaxWindLevels];
        /// <summary>Gust speed, m/s (≥ the mean; 0 = no gusts stated).</summary>
        public fixed float WindGustMps[MaxWindLevels];

        /// <summary>How many of the cloud layers are used.</summary>
        public byte CloudCount;
        /// <summary>Cloud base, m above sea level.</summary>
        public fixed float CloudBaseM[MaxCloudLayers];
        /// <summary>Layer thickness, m.</summary>
        public fixed float CloudThicknessM[MaxCloudLayers];
        /// <summary>Cover in eighths of the sky (octas, 0…8).</summary>
        public fixed byte CloudOctas[MaxCloudLayers];
        /// <summary><see cref="Weather.CloudKind"/>.</summary>
        public fixed byte CloudKind[MaxCloudLayers];

        /// <summary>Authored horizontal visibility in clear air, m (the meteorological visibility V).</summary>
        public float VisibilityM;
        /// <summary>Fog layer top above the ground, m (0 = no fog).</summary>
        public float FogTopM;
        /// <summary>Visibility inside the fog layer, m (used below <see cref="FogTopM"/>).</summary>
        public float FogVisibilityM;

        /// <summary><see cref="Weather.PrecipitationKind"/>.</summary>
        public byte Precipitation;
        /// <summary>Precipitation rate, mm/h.</summary>
        public float PrecipitationMmPerHour;

        /// <summary>⭐ W-1 — a REGION's fade past its edge, m (0 = the weight reaches 0 exactly at the edge). Unused on the global weather.</summary>
        public float FeatherM;

        /// <summary>
        /// The built-in weather (C-3, the default World Environment, and what a world without one reads): an ISA day — 15 °C, 6.5 K/km,
        /// 1013.25 hPa, 60 % humidity, CALM, a few cumulus at 1500 m, 20 km visibility, no fog, no precipitation. ⭐ Calm on purpose: the
        /// retired <c>Wind</c> singleton's contract was "absent ⇒ calm", so a scenario that states no weather behaves exactly as before.
        /// </summary>
        public static WeatherConditions Standard
        {
            get
            {
                var w = new WeatherConditions
                {
                    GroundTemperatureC = 15f, LapseRateKPerKm = 6.5f, SeaLevelPressureHpa = 1013.25f, RelativeHumidity = 0.6f,
                    VisibilityM = 20_000f, FogVisibilityM = 200f,
                };
                w.SetCloud(0, 1500f, 600f, 2, Weather.CloudKind.Cumulus);
                w.CloudCount = 1;
                return w;
            }
        }

        /// <summary>
        /// The built-in weather with ONE steady wind at every height, blowing TOWARD <paramref name="toward"/> (m/s, X east, Y north) — the
        /// single-velocity wind the retired <c>Wind</c> singleton was (one level at −1000 m MSL: below any ground, so no surface fall-off).
        /// </summary>
        public static WeatherConditions SteadyWind(Vector2 toward)
        {
            var w = Standard;
            float speed = toward.Length();
            float fromDeg = speed > 0f ? MathF.Atan2(-toward.X, -toward.Y) * 180f / MathF.PI : 0f;
            if (fromDeg < 0f) fromDeg += 360f;
            w.SetWind(0, -1000f, fromDeg, speed, 0f);
            w.WindCount = 1;
            return w;
        }

        /// <summary>Writes wind level <paramref name="i"/> (does not change <see cref="WindCount"/>).</summary>
        public void SetWind(int i, float altitudeM, float fromDeg, float speedMps, float gustMps)
        {
            WindAltitudeM[i] = altitudeM; WindFromDeg[i] = fromDeg; WindSpeedMps[i] = speedMps; WindGustMps[i] = gustMps;
        }

        /// <summary>Writes cloud layer <paramref name="i"/> by its aviation cover ("overcast at 1 500 m") — does not change <see cref="CloudCount"/>.</summary>
        public void SetCloud(int i, float baseM, float thicknessM, CloudCover cover, CloudKind kind)
            => SetCloud(i, baseM, thicknessM, CloudCovers.Octas(cover), kind);

        /// <summary>Writes cloud layer <paramref name="i"/> (does not change <see cref="CloudCount"/>).</summary>
        public void SetCloud(int i, float baseM, float thicknessM, byte octas, CloudKind kind)
        {
            CloudBaseM[i] = baseM; CloudThicknessM[i] = thicknessM; CloudOctas[i] = octas; CloudKind[i] = (byte)kind;
        }
    }
}
