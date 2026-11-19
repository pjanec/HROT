using System;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>The thermal classes the 3-D map's thermal view knows: the 13 of <see cref="Terrain.TerrainThermalClass.All"/>, IN THAT ORDER, then the internal
    /// <see cref="PaintedMetal"/> (a vehicle hull — not a terrain class).</summary>
    public enum ThermalClassId : byte
    {
        Asphalt = 0, Concrete, Brick, Metal, Glass, Wood, SoilDry, SoilWet, Sand, Grass, DenseVegetation, Water, Snow,
        /// <summary>A vehicle hull: painted metal, ε 0.9 (unlike bare <see cref="Metal"/>, ε 0.3), τ 0.2 h, α 0.8.</summary>
        PaintedMetal,
    }

    /// <summary>
    /// ⭐ <c>CE-1985</c> SV-2 step 1 (docs/designs/sensor-view/DESIGN_Sensor_View.md §5a, §5b) — the thermal view's SURFACE-TEMPERATURE MODEL, pure and allocation-free: per thermal
    /// class the true surface °C of a horizontal surface now (a first-order lag of the steady daily cycle of sun and air, minus night radiative cooling, pinned toward the air by
    /// evaporation, damped by wind and rain), its per-unit-facing sun gain and its emissivity; the apparent (radiometric) temperature against the sky; and the heat of a unit (engine, body,
    /// barrel, fire). The shader and the body layer only READ it. Culture-invariant (numbers only). Spans in, numbers out — no LINQ, no arrays per call.
    /// <para>⭐ Heat SOURCES are not copied: <see cref="ThermalDriversOps.SourceHeat"/> (engine / body heat 0..1), <see cref="ThermalDriversOps.CorpseCoolPerSecond"/> and
    /// <see cref="ThermalDriversOps.BarrelHeat"/> remain the one owner.</para>
    /// </summary>
    public static class ThermalScene
    {
        /// <summary>The number of <see cref="ThermalClassId"/> entries — the length of every per-class span.</summary>
        public const int ClassCount = 14;

        // ---- the per-class constants (§5a table; starter values, tuned on the §7 TH rails) --------------------------------------------------------------------------------------
        /// <summary>α — how much of the sun's heating the surface keeps.</summary>
        public static ReadOnlySpan<float> Absorption => s_alpha;
        /// <summary>τ — the thermal inertia, hours (a first-order lag).</summary>
        public static ReadOnlySpan<float> InertiaHours => s_tau;
        /// <summary>ε — emissivity.</summary>
        public static ReadOnlySpan<float> Emissivity => s_eps;
        /// <summary>pin — 0..1, how hard evaporation holds the surface at the air temperature.</summary>
        public static ReadOnlySpan<float> Pin => s_pin;

        //                                   asph  conc  brick metal glass wood  soilD soilW sand  grass dense water snow  paint
        private static readonly float[] s_alpha = { 0.90f, 0.70f, 0.70f, 0.80f, 0.30f, 0.60f, 0.75f, 0.80f, 0.65f, 0.50f, 0.50f, 0.50f, 0.20f, 0.80f };
        private static readonly float[] s_tau   = { 3.0f,  4.0f,  4.0f,  0.2f,  0.5f,  1.0f,  1.5f,  2.0f,  1.5f,  0.5f,  0.5f,  48f,   2.0f,  0.2f };
        private static readonly float[] s_eps   = { 0.95f, 0.92f, 0.92f, 0.30f, 0.20f, 0.90f, 0.92f, 0.95f, 0.92f, 0.98f, 0.98f, 0.98f, 0.98f, 0.90f };
        private static readonly float[] s_pin   = { 0f,    0f,    0f,    0f,    0f,    0f,    0f,    0.5f,  0f,    0.8f,  0.9f,  0.3f,  0f,    0f };

        /// <summary>The model's tunables (reported with the rails): the sun's heating per W/m² absorbed (K), the clear-night radiative cooling (K), the wind's damping (per m/s), the rain's factor.</summary>
        public const float SunGainKPerWm2 = 0.04f, NightCoolK = 3f, NightPinShare = 0.5f, WindDamping = 0.15f, RainFactor = 0.4f;

        /// <summary>The thermal class of a <c>TerrainThermalClass</c> name (case-sensitive, the names of <c>TerrainThermalClass.All</c>); unknown or null ⇒ concrete.</summary>
        public static ThermalClassId FromName(string? name)
        {
            switch (name)
            {
                case "asphalt": return ThermalClassId.Asphalt;
                case "concrete": return ThermalClassId.Concrete;
                case "brick": return ThermalClassId.Brick;
                case "metal": return ThermalClassId.Metal;
                case "glass": return ThermalClassId.Glass;
                case "wood": return ThermalClassId.Wood;
                case "soil-dry": return ThermalClassId.SoilDry;
                case "soil-wet": return ThermalClassId.SoilWet;
                case "sand": return ThermalClassId.Sand;
                case "grass": return ThermalClassId.Grass;
                case "dense-vegetation": return ThermalClassId.DenseVegetation;
                case "water": return ThermalClassId.Water;
                case "snow": return ThermalClassId.Snow;
                default: return ThermalClassId.Concrete;
            }
        }

        // ---- the day's forcing ---------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The sun's heating over a UTC day as a mean plus its first harmonic (what a first-order lag needs): computed ONCE per sim day by the caller. <see cref="SolarOffsetHours"/> turns a UTC
        /// time of day into the point's local SOLAR hour (longitude + the equation of time, taken at the day's noon).
        /// </summary>
        public readonly struct DayForcing
        {
            /// <summary>The day's mean horizontal irradiance (W/m²).</summary>
            public readonly float MeanWm2;
            /// <summary>The amplitude of its first harmonic (W/m²).</summary>
            public readonly float AmpWm2;
            /// <summary>The solar hour (0..24) at which the first harmonic peaks.</summary>
            public readonly float PeakSolarHour;
            /// <summary>Solar hour − UTC hour (h) at this point, this day.</summary>
            public readonly float SolarOffsetHours;
            /// <summary>The clear-sky direct-beam irradiance with this day's cloud cut (W/m²): 1361 · 0.75 · cloudCut.</summary>
            public readonly float DniWm2;
            /// <summary>The sun's latitude / longitude this was made for (info).</summary>
            public readonly float LatDeg, LonDeg;

            public DayForcing(float mean, float amp, float peak, float offset, float dni, float lat, float lon)
            { MeanWm2 = mean; AmpWm2 = amp; PeakSolarHour = peak; SolarOffsetHours = offset; DniWm2 = dni; LatDeg = lat; LonDeg = lon; }

            /// <summary>The local solar hour (0..24) at <paramref name="utc"/>.</summary>
            public float SolarHourAt(DateTime utc)
            {
                double h = utc.TimeOfDay.TotalHours + SolarOffsetHours;
                h %= 24.0; if (h < 0) h += 24.0;
                return (float)h;
            }

            /// <summary>
            /// The day of <paramref name="utc"/> (UTC midnight to midnight) at the point: samples <see cref="Weather.SolarPosition.Of"/> 24 times, horizontal irradiance
            /// 1361 · max(0, sin elevation) · 0.75 · <paramref name="cloudCut"/> (<c>WeatherField</c>'s formula), then the closed-form Fourier mean and first harmonic. 0 B.
            /// </summary>
            public static DayForcing Of(DateTime utc, double latDeg, double lonDeg, float cloudCut)
            {
                var day = utc.Date;
                double sum = 0, c = 0, s = 0;
                const int n = 24;
                for (int i = 0; i < n; i++)
                {
                    var sun = Weather.SolarPosition.Of(day.AddHours(i + 0.5), latDeg, lonDeg);   // the middle of each hour
                    double sinEl = Math.Sin(sun.ElevationDeg * Math.PI / 180.0);
                    double g = sinEl > 0 ? Weather.WeatherField.SolarConstant * sinEl * Weather.WeatherField.ClearSkyTransmittance * cloudCut : 0.0;
                    double hour = sun.SolarHour;
                    sum += g;
                    c += g * Math.Cos(2 * Math.PI * hour / 24.0);
                    s += g * Math.Sin(2 * Math.PI * hour / 24.0);
                }
                double mean = sum / n;
                double a = 2.0 * c / n, b = 2.0 * s / n;   // g ≈ mean + a cos(wh) + b sin(wh) = mean + amp cos(w(h − peak))
                double amp = Math.Sqrt(a * a + b * b);
                double peak = amp > 1e-9 ? Math.Atan2(b, a) * 24.0 / (2 * Math.PI) : 12.0;
                if (peak < 0) peak += 24.0;
                var noon = Weather.SolarPosition.Of(day.AddHours(12), latDeg, lonDeg);
                double offset = noon.SolarHour - 12.0;
                return new DayForcing((float)mean, (float)amp, (float)peak, (float)offset,
                                      Weather.WeatherField.SolarConstant * Weather.WeatherField.ClearSkyTransmittance * cloudCut, (float)latDeg, (float)lonDeg);
            }
        }

        /// <summary>The Kasten–Czeplak cloud cut of the cover ABOVE the point — <c>WeatherField</c>'s irradiance factor: 1 − 0.75 (N/8)^3.4.</summary>
        public static float CloudCut(float octasAbove) => 1f - 0.75f * MathF.Pow(Math.Clamp(octasAbove / 8f, 0f, 1f), 3.4f);

        // ---- the class temperatures ---------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Per class (index = <see cref="ThermalClassId"/>): <paramref name="flatC"/> the TRUE surface °C of a HORIZONTAL surface now, <paramref name="sunGainC"/> the gain per unit of facing
        /// (a pixel: <c>T = flat + sunGain · (max(0, N·sun) − sinEl)</c>), <paramref name="emissivity"/> ε. Each span must hold at least <see cref="ClassCount"/> entries. 0 B.
        /// <para>Per class: the daily cycle lagged by a first-order system (<c>ω = 2π/24 h</c>, <c>A = 1/√(1+(ωτ)²)</c>, <c>φ = atan ωτ</c>) — the AIR (mean + A·amp·cos(ω(h − 14.5) − φ)) plus
        /// <c>α·G·(sun mean + A·sun harmonic)</c> (never below the heat the mean leaves, so a night is not colder for the sun's absence than the lag allows) minus the night's radiative cooling
        /// (clear sky, ε, the less the inertia) — the whole DEPARTURE from the air damped by wind and rain and pinned toward the air by evaporation.</para>
        /// </summary>
        public static void ClassTemperatures(in Weather.WeatherSample s, in DayForcing f, Span<float> flatC, Span<float> sunGainC, Span<float> emissivity)
        {
            float octas = Math.Clamp(s.CloudOctas, 0f, 8f);
            float cover = octas / 8f;
            float cloudCut = CloudCut(s.CloudOctasAbove);
            float amp = 2f + 4f * (1f - cover);                                       // WeatherField's diurnal amplitude
            float mean = s.GroundTemperatureC - amp * s.DiurnalPhase;                 // … recovered from the sample
            float h = f.SolarHourAt(s.Utc);
            float airNow = s.GroundTemperatureC;
            const float w = 2f * MathF.PI / 24f;
            float sinEl = MathF.Sin((float)(s.SunElevationDeg * Math.PI / 180.0));
            float night = Math.Clamp(0.5f - sinEl * 8f, 0f, 1f);                       // 0 with the sun up, 1 below about −7°: smooth over twilight
            float windK = 1f / (1f + WindDamping * MathF.Sqrt(s.Wind.X * s.Wind.X + s.Wind.Y * s.Wind.Y));
            float rainK = s.Precipitation != Weather.PrecipitationKind.None ? RainFactor : 1f;
            float dni = sinEl > 0f ? f.DniWm2 * (cloudCut > 0f ? 1f : 0f) : 0f;       // the day's forcing already carries the day's cloud cut
            for (int i = 0; i < ClassCount; i++)
            {
                float tau = s_tau[i];
                float wt = w * tau;
                float a = 1f / MathF.Sqrt(1f + wt * wt), phi = MathF.Atan(wt);
                float airLag = mean + a * amp * MathF.Cos(w * (h - 14.5f) - phi);
                float sun = f.MeanWm2 + a * f.AmpWm2 * MathF.Cos(w * (h - f.PeakSolarHour) - phi);
                sun = MathF.Max(sun, f.MeanWm2 * (1f - a));
                float warm = airLag + s_alpha[i] * SunGainKPerWm2 * sun - airNow;          // the lagged air and the sun's heat, as a departure from the air now
                float cool = NightCoolK * (1f - cover) * s_eps[i] * (0.5f + 0.5f * a) * night;
                // evaporation pins the heat toward the air; the radiative cooling it only half — a clear night leaves wet grass the coldest ground there is
                float departure = (warm * (1f - s_pin[i]) - cool * (1f - NightPinShare * s_pin[i])) * windK * rainK;
                float flat = airNow + departure;
                if (i == (int)ThermalClassId.Snow) flat = MathF.Min(flat, 0f);          // snow is at most melting
                flatC[i] = flat;
                sunGainC[i] = s_alpha[i] * SunGainKPerWm2 * dni * a * windK * rainK * (1f - s_pin[i]);
                emissivity[i] = s_eps[i];
            }
        }

        /// <summary>The effective sky temperature (°C): clear ≈ air − 25 K, overcast ≈ air − 3 K.</summary>
        public static float SkyC(in Weather.WeatherSample s)
        {
            float cover = Math.Clamp(s.CloudOctas, 0f, 8f) / 8f;
            return s.GroundTemperatureC - 25f * (1f - cover) - 3f * cover;
        }

        /// <summary>The temperature a camera reads: <c>ε·T + (1−ε)·T_sky</c> (a linear °C approximation — enough for a debug view).</summary>
        public static float Apparent(float surfaceC, float eps, float skyC) => eps * surfaceC + (1f - eps) * skyC;

        // ---- units ---------------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>The temperatures of a unit's body, its hot end and its barrel (°C).</summary>
        public readonly struct UnitHeat
        {
            public readonly float BodyC, HotEndC, BarrelC;
            public UnitHeat(float body, float hotEnd, float barrel) { BodyC = body; HotEndC = hotEnd; BarrelC = barrel; }
        }

        /// <summary>A person's clothing temperature at rest (°C), the rise at full source heat (K), the engine's hot-end rise (K), the barrel's rise (K), the temperature of a fire (°C), a body's temperature at death (°C).</summary>
        public const float ClothingC = 28f, BodyRiseK = 6f, EngineRiseK = 60f, BarrelRiseK = 120f, FireC = 600f, CoreC = 34f;

        /// <summary>
        /// A unit's temperatures (§5b). An ENGINE (<c>type.HasPowerPlant</c>): the body is the painted hull at <paramref name="surfaceC"/>, the hot end <c>surfaceC + SourceHeat·60 K</c>; a BODY (a person, an animal): clothing
        /// 28 °C + <c>SourceHeat·6 K</c>, a dead one cooling from 34 °C toward <paramref name="surfaceC"/> (the air) at <see cref="ThermalDriversOps.CorpseCoolPerSecond"/> since <paramref name="diedAt"/> (NaN = alive); the barrel
        /// is the body + <c>barrelHeat01·120 K</c>; <paramref name="fire01"/> &gt; 0 ⇒ every value at least <c>600·fire01</c>. 0 B.
        /// </summary>
        public static UnitHeat Unit(in ThermalState type, in HeatSource src, float speed, double now, float barrelHeat01, float fire01, double diedAt, float surfaceC)
        {
            float heat = ThermalDriversOps.SourceHeat(in type, in src, speed, now, diedAt);
            float body, hot;
            if (type.HasPowerPlant)
            {
                body = surfaceC;
                hot = surfaceC + heat * EngineRiseK;
            }
            else if (!double.IsNaN(diedAt))
            {
                float k = (float)Math.Exp(-(now - diedAt) * ThermalDriversOps.CorpseCoolPerSecond);   // the corpse's warmth fades (SourceHeat's own factor covers the rest)
                body = surfaceC + (CoreC - surfaceC) * k;
                hot = body;
            }
            else
            {
                body = ClothingC + heat * BodyRiseK;
                hot = body;
            }
            float barrel = body + Math.Clamp(barrelHeat01, 0f, 1f) * BarrelRiseK;
            if (fire01 > 0f)
            {
                float fire = FireC * Math.Min(fire01, 1f);
                body = MathF.Max(body, fire); hot = MathF.Max(hot, fire); barrel = MathF.Max(barrel, fire);
            }
            return new UnitHeat(body, hot, barrel);
        }
    }
}
