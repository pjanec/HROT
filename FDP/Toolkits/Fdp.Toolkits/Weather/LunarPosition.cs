using System;

namespace Fdp.Toolkit.Weather
{
    /// <summary>
    /// ⭐ <c>CE-1984</c> (📄 docs/designs/sensor-view/DESIGN_Sensor_View.md §4a, R-1056 — *"Some simple moon"*) — the moon's position and lit fraction
    /// from UTC and a latitude / longitude: the Astronomical Almanac's LOW-PRECISION lunar formulae (six longitude and four latitude terms; ~0.3° in
    /// the sky, geocentric — the ≤ 1° parallax is ignored) and the elongation from the sun for the phase. Pure, allocation-free; the twin of
    /// <see cref="SolarPosition"/>, in the same frame (elevation above the horizon, azimuth clockwise from north). Enough to light a scene and to
    /// place a moon in the sky — ⛔ not for eclipses or occultations.
    /// </summary>
    public static class LunarPosition
    {
        /// <summary>The moon at <paramref name="utc"/> seen from <paramref name="latDeg"/> / <paramref name="lonDeg"/> (east positive).</summary>
        public static Moon Of(DateTime utc, double latDeg, double lonDeg)
        {
            double jd = utc.Ticks / (double)TimeSpan.TicksPerDay + 1721425.5;   // DateTime's tick 0 (0001-01-01) is JD 1721425.5
            double d = jd - 2451545.0;
            double t = d / 36525.0;

            // the moon's ecliptic longitude / latitude (Astronomical Almanac, low precision)
            double lambda = 218.32 + 481267.881 * t
                          + 6.29 * Sin(135.0 + 477198.87 * t) - 1.27 * Sin(259.3 - 413335.36 * t)
                          + 0.66 * Sin(235.7 + 890534.22 * t) + 0.21 * Sin(269.9 + 954397.74 * t)
                          - 0.19 * Sin(357.5 + 35999.05 * t) - 0.11 * Sin(186.5 + 966404.03 * t);
            double beta = 5.13 * Sin(93.3 + 483202.02 * t) + 0.28 * Sin(228.2 + 960400.89 * t)
                        - 0.28 * Sin(318.3 + 6003.15 * t) - 0.17 * Sin(217.6 - 407332.21 * t);

            // the sun's ecliptic longitude (for the phase)
            double g = 357.528 + 0.9856003 * d;
            double sunLambda = 280.460 + 0.9856474 * d + 1.915 * Sin(g) + 0.020 * Sin(2 * g);
            double cosElong = Cos(beta) * Cos(lambda - sunLambda);
            double lit = (1.0 - cosElong) / 2.0;                                  // 0 new … 1 full
            bool waxing = Mod(lambda - sunLambda, 360.0) < 180.0;

            // ecliptic → equatorial
            double eps = 23.439 - 0.0000004 * d;
            double x = Cos(beta) * Cos(lambda);
            double y = Cos(eps) * Cos(beta) * Sin(lambda) - Sin(eps) * Sin(beta);
            double z = Sin(eps) * Cos(beta) * Sin(lambda) + Cos(eps) * Sin(beta);
            double ra = Deg(Math.Atan2(y, x));
            double dec = Deg(Math.Asin(Math.Clamp(z, -1.0, 1.0)));

            // the local hour angle from Greenwich mean sidereal time
            double gmst = 280.46061837 + 360.98564736629 * d;
            double h = Mod(gmst + lonDeg - ra, 360.0);

            double sinEl = Sin(latDeg) * Sin(dec) + Cos(latDeg) * Cos(dec) * Cos(h);
            double el = Deg(Math.Asin(Math.Clamp(sinEl, -1.0, 1.0)));
            double az = Mod(Deg(Math.Atan2(-Sin(h) * Cos(dec), Cos(latDeg) * Sin(dec) - Sin(latDeg) * Cos(dec) * Cos(h))), 360.0);
            return new Moon(el, az, lit, waxing);
        }

        private static double Sin(double deg) => Math.Sin(deg * Math.PI / 180.0);
        private static double Cos(double deg) => Math.Cos(deg * Math.PI / 180.0);
        private static double Deg(double r) => r * 180.0 / Math.PI;
        private static double Mod(double a, double n) => ((a % n) + n) % n;
    }

    /// <summary>The moon as seen from one point at one moment.</summary>
    /// <param name="ElevationDeg">Above the horizon, degrees (negative below it).</param>
    /// <param name="AzimuthDeg">Compass bearing, degrees clockwise from north.</param>
    /// <param name="Illumination">The lit fraction of its disc, 0 (new) … 1 (full).</param>
    /// <param name="Waxing">True between new and full.</param>
    public readonly record struct Moon(double ElevationDeg, double AzimuthDeg, double Illumination, bool Waxing);

    /// <summary>
    /// ⭐ <c>CE-1984</c> (DESIGN_Sensor_View §4a) — the NATURAL light on the ground, in lux: the sun (from the irradiance, ~110 lm/W; twilight by the
    /// sun's elevation), the MOON (a full moon high in a clear sky ≈ 0.25 lux) and the stars + airglow (≈ 0.002 lux), each cut by the cloud above.
    /// ⛔ It does NOT change <see cref="WeatherSample.RelativeLight"/> — the AI's sight reads that, and a moonlit night seeing further is a behaviour
    /// change of its own (CE-1986). Pure, allocation-free.
    /// </summary>
    public static class NaturalLight
    {
        /// <summary>A full moon at the zenith in a clear sky, lux.</summary>
        public const float FullMoonLux = 0.25f;
        /// <summary>A clear moonless night (stars + airglow), lux.</summary>
        public const float StarlightLux = 0.002f;
        /// <summary>Luminous efficacy of daylight, lm/W.</summary>
        public const float SunLumensPerWatt = 110f;

        /// <summary>The light on a horizontal surface at the sample's point and moment, lux.</summary>
        public static float Lux(in WeatherSample s)
        {
            float cover = Math.Clamp(s.CloudOctasAbove / 8f, 0f, 1f);
            float cloudCut = 1f - 0.75f * MathF.Pow(cover, 3.4f);                // the irradiance's Kasten–Czeplak cut (WeatherField)
            float sun = s.IrradianceWm2 * SunLumensPerWatt;
            if (s.SunElevationDeg <= 0 && s.SunElevationDeg > -18)             // twilight: ~400 lux at sunset to the stars at −18° (log-linear)
                sun = MathF.Max(sun, MathF.Pow(10f, 2.6f + (float)s.SunElevationDeg * (2.6f - MathF.Log10(StarlightLux)) / 18f) * cloudCut);
            float moon = s.MoonElevationDeg > 0
                ? FullMoonLux * MoonPhaseBrightness(s.MoonIllumination) * (float)Math.Sin(s.MoonElevationDeg * Math.PI / 180.0) * cloudCut : 0f;
            float stars = StarlightLux * (1f - 0.85f * cover);
            return sun + moon + stars;
        }

        /// <summary>A half moon is ~1/10 as bright as a full one (the opposition surge): brightness ≈ lit fraction^2.5 (starter fit).</summary>
        public static float MoonPhaseBrightness(float illumination) => MathF.Pow(Math.Clamp(illumination, 0f, 1f), 2.5f);
    }
}
