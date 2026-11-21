using System;

namespace Fdp.Toolkit.Weather
{
    /// <summary>
    /// ⭐ CE-3216 S1 (📄 docs/designs/weather/DESIGN_Weather_And_Time.md §4) — the sun's position from UTC and a latitude / longitude: the
    /// NOAA solar-position algorithm (the NOAA spreadsheet's formulas — declination, equation of time, true solar time, hour angle).
    /// Pure; no time zone is needed (UTC in, the point's own longitude makes the local solar time). Accurate to well under a degree
    /// for 1800–2100, which is far below anything a sensor or a shadow can tell. ⛔ No atmospheric refraction (≤ 0.6° at the horizon).
    /// </summary>
    public static class SolarPosition
    {
        /// <summary>The sun at <paramref name="utc"/> seen from <paramref name="latDeg"/> / <paramref name="lonDeg"/> (east positive).</summary>
        public static Sun Of(DateTime utc, double latDeg, double lonDeg)
        {
            double jd = utc.Ticks / (double)TimeSpan.TicksPerDay + 1721425.5;   // DateTime's tick 0 (0001-01-01) is JD 1721425.5
            double t = (jd - 2451545.0) / 36525.0;                                // Julian century from J2000

            double l0 = Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360.0);
            double m  = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double e  = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
            double c  = Math.Sin(Rad(m)) * (1.914602 - t * (0.004817 + 0.000014 * t))
                      + Math.Sin(Rad(2 * m)) * (0.019993 - 0.000101 * t)
                      + Math.Sin(Rad(3 * m)) * 0.000289;
            double appLong = l0 + c - 0.00569 - 0.00478 * Math.Sin(Rad(125.04 - 1934.136 * t));
            double obliq0  = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
            double obliq   = obliq0 + 0.00256 * Math.Cos(Rad(125.04 - 1934.136 * t));
            double decl    = Deg(Math.Asin(Math.Sin(Rad(obliq)) * Math.Sin(Rad(appLong))));

            double y = Math.Tan(Rad(obliq / 2)); y *= y;
            double eqTimeMin = 4 * Deg(y * Math.Sin(2 * Rad(l0)) - 2 * e * Math.Sin(Rad(m))
                                       + 4 * e * y * Math.Sin(Rad(m)) * Math.Cos(2 * Rad(l0))
                                       - 0.5 * y * y * Math.Sin(4 * Rad(l0)) - 1.25 * e * e * Math.Sin(2 * Rad(m)));

            double minutesUtc = utc.TimeOfDay.TotalMinutes;
            double trueSolarMin = Mod(minutesUtc + eqTimeMin + 4 * lonDeg, 1440.0);
            double hourAngle = trueSolarMin / 4 < 0 ? trueSolarMin / 4 + 180 : trueSolarMin / 4 - 180;

            double lat = Rad(latDeg);
            double cosZen = Math.Sin(lat) * Math.Sin(Rad(decl)) + Math.Cos(lat) * Math.Cos(Rad(decl)) * Math.Cos(Rad(hourAngle));
            double zen = Deg(Math.Acos(Math.Clamp(cosZen, -1.0, 1.0)));

            double az;
            double denom = Math.Cos(lat) * Math.Sin(Rad(zen));
            if (Math.Abs(denom) < 1e-9) az = 180.0;   // the sun overhead or a pole: azimuth undefined
            else
            {
                double a = Deg(Math.Acos(Math.Clamp((Math.Sin(lat) * Math.Cos(Rad(zen)) - Math.Sin(Rad(decl))) / denom, -1.0, 1.0)));
                az = hourAngle > 0 ? Mod(a + 180, 360) : Mod(540 - a, 360);
            }

            return new Sun(90.0 - zen, az, trueSolarMin / 60.0, decl);
        }

        private static double Rad(double d) => d * Math.PI / 180.0;
        private static double Deg(double r) => r * 180.0 / Math.PI;
        private static double Mod(double a, double n) => ((a % n) + n) % n;
    }

    /// <summary>The sun as seen from one point at one moment.</summary>
    /// <param name="ElevationDeg">Above the horizon, degrees (negative below it).</param>
    /// <param name="AzimuthDeg">Compass bearing, degrees clockwise from north.</param>
    /// <param name="SolarHour">Local apparent solar time, hours (12 = the sun at its highest).</param>
    /// <param name="DeclinationDeg">The sun's declination, degrees.</param>
    public readonly record struct Sun(double ElevationDeg, double AzimuthDeg, double SolarHour, double DeclinationDeg);
}
