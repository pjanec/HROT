using System;

namespace Hrot.NED.Descriptors.Orchestration
{
    /// <summary>
    /// ⭐ <c>AQ107 I</c> — the ONE place that says how an ECS value in a non-SI unit becomes its SI form on the NED wire and back.
    /// 📐 The rule: <b>SI on the wire</b> — metres, seconds, radians, rad/s, m/s, kelvin, pascal, and 0…1 fractions. The single exception is
    /// <c>GeoReference.OriginLatitudeDeg/OriginLongitudeDeg</c> (geodetic coordinates are conventionally degrees; the name says so).
    /// The ECS components that hold another unit (compass degrees on <c>EngagementArea</c>, °C / hPa / mm per h on <c>WeatherConditions</c>,
    /// a percent byte on <c>WeaponAmmo</c>, minutes on <c>ScenarioClock</c>) are NOT changed — the translators convert here, at the boundary.
    /// ⚠ Every conversion goes through <c>double</c> and is rounded to the wire's <c>float</c> once, so a value round-trips to within one
    /// float step (≈6e-8 relative), not bit-exactly.
    /// 🔗 Lives beside <see cref="NedWire"/> in the lowest NED project, so every NED/orchestration translator reaches it.
    /// </summary>
    public static class WireUnits
    {
        private const double DegPerRad = 180.0 / Math.PI;
        private const double RadPerDeg = Math.PI / 180.0;
        private const double KelvinAtZeroCelsius = 273.15;

        /// <summary>Degrees → radians.</summary>
        public static float DegToRad(float degrees) => (float)(degrees * RadPerDeg);

        /// <summary>Radians → degrees.</summary>
        public static float RadToDeg(float radians) => (float)(radians * DegPerRad);

        /// <summary>°C → kelvin.</summary>
        public static float CelsiusToKelvin(float celsius) => (float)(celsius + KelvinAtZeroCelsius);

        /// <summary>Kelvin → °C.</summary>
        public static float KelvinToCelsius(float kelvin) => (float)(kelvin - KelvinAtZeroCelsius);

        /// <summary>K per km → K per metre (a lapse rate).</summary>
        public static float PerKilometreToPerMetre(float perKilometre) => (float)(perKilometre / 1000.0);

        /// <summary>K per metre → K per km.</summary>
        public static float PerMetreToPerKilometre(float perMetre) => (float)(perMetre * 1000.0);

        /// <summary>hPa → pascal.</summary>
        public static float HectopascalToPascal(float hectopascal) => (float)(hectopascal * 100.0);

        /// <summary>Pascal → hPa.</summary>
        public static float PascalToHectopascal(float pascal) => (float)(pascal / 100.0);

        /// <summary>mm per hour → metres per second (a precipitation rate).</summary>
        public static float MillimetresPerHourToMetresPerSecond(float mmPerHour) => (float)(mmPerHour / 3_600_000.0);

        /// <summary>Metres per second → mm per hour.</summary>
        public static float MetresPerSecondToMillimetresPerHour(float metresPerSecond) => (float)(metresPerSecond * 3_600_000.0);

        /// <summary>Minutes → seconds (a UTC offset).</summary>
        public static int MinutesToSeconds(int minutes) => minutes * 60;

        /// <summary>Seconds → whole minutes (a UTC offset; rounds toward zero — zones are whole minutes).</summary>
        public static int SecondsToMinutes(int seconds) => seconds / 60;

        /// <summary>Percent (0…100) → a 0…1 fraction.</summary>
        public static float PercentToFraction(float percent) => (float)(percent / 100.0);

        /// <summary>A 0…1 fraction → percent (0…100).</summary>
        public static float FractionToPercent(float fraction) => (float)(fraction * 100.0);
    }
}
