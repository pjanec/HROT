using System;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>⭐ SD-S6a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8a D-158) — what looks at a laser: the naked eye, an image-intensifier
    /// night-vision tube, a thermal (8–12 µm) imager. A map maps its <c>SceneSensor</c> onto this; AI perception and the IG will too.</summary>
    public enum LaserSensor : byte { Eye = 0, NightVision = 1, Thermal = 2 }

    /// <summary>⭐ SD-S6a (§3.8a D-159) — the values of <see cref="LaserPointing.Band"/>.</summary>
    public static class LaserBands
    {
        /// <summary>A visible laser (≈ 532 / 650 nm): the eye and night vision see it.</summary>
        public const byte Visible = 0;
        /// <summary>An eye-safe rangefinder (1 540 nm): NO view sees it — only the device's own HUD knows it fired.</summary>
        public const byte EyeSafeRangefinder = 1;
        /// <summary>A designator (1 064 nm): night vision only, faint (the tube's tail).</summary>
        public const byte Designator = 2;
        /// <summary>A near-infrared pointer / illuminator (≈ 850 nm): night vision only, bright.</summary>
        public const byte NearInfrared = 3;
    }

    /// <summary>
    /// ⭐⭐ SD-S6a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8a D-158 – D-160) — THE ONE RULE for who sees a laser and how bright: the band ×
    /// sensor table, the beam's scatter off the air's haze, and the spot's surface return. Pure and allocation-free. ⭐ Callers: the 3-D map's
    /// <c>LaserLayer3D</c> now; AI perception of a spot (LS-5) and the external IG (D-36) later — ⛔ never a second table.
    /// <para>A beam is seen only by its SCATTER off aerosols (dust, haze, mist): faint in clean air, strong in haze; the SPOT is a diffuse surface return
    /// and needs no haze. Thermal sees no laser of these bands.</para>
    /// </summary>
    public static class LaserVisibility
    {
        /// <summary>The visibility (m) at and above which the air counts as clean (the aerosol floor).</summary>
        public const float ClearVisibilityM = 20_000f;

        /// <summary>The aerosol of the cleanest air: a beam never vanishes completely at full power.</summary>
        public const float MinAerosol = 0.05f;

        /// <summary>Relative humidity (0…1) above which droplets add to the haze.</summary>
        public const float HumidHazeFrom = 0.8f;

        /// <summary>
        /// D-159 — how well <paramref name="sensor"/> sees <paramref name="band"/>, 0…1: band 0 (visible) eye 1 · NV 1; band 1 (eye-safe rangefinder)
        /// nothing; band 2 (designator) NV 0.25 only; band 3 (near-IR pointer) NV 1 only; thermal nothing. An unknown band: nothing.
        /// </summary>
        public static float Table(byte band, LaserSensor sensor)
        {
            if (sensor == LaserSensor.Thermal) return 0f;
            bool nv = sensor == LaserSensor.NightVision;
            return band switch
            {
                LaserBands.Visible => 1f,
                LaserBands.Designator => nv ? 0.25f : 0f,
                LaserBands.NearInfrared => nv ? 1f : 0f,
                _ => 0f,   // LaserBands.EyeSafeRangefinder and anything unknown
            };
        }

        /// <summary>
        /// D-160 — the air's haze, <see cref="MinAerosol"/>…1: <c>clamp(1 − visibility / 20 km, 0.05, 1)</c>, raised by humidity above 80 % (half the way to 1
        /// at 100 %). A fog is already in <paramref name="visibilityM"/> (the sample inside a fog layer reads the fog's visibility). No weather (∞) ⇒ the floor.
        /// </summary>
        public static float Aerosol(float visibilityM, float relativeHumidity)
        {
            float a = float.IsNaN(visibilityM) || visibilityM <= 0f ? 1f : 1f - visibilityM / ClearVisibilityM;
            a = Math.Clamp(a, MinAerosol, 1f);
            if (relativeHumidity > HumidHazeFrom)
                a += (1f - a) * 0.5f * Math.Clamp((relativeHumidity - HumidHazeFrom) / (1f - HumidHazeFrom), 0f, 1f);
            return Math.Clamp(a, MinAerosol, 1f);
        }

        /// <summary>D-160 — the beam's scatter at its origin, 0…1: <c>table × power × aerosol</c>.</summary>
        public static float Beam(byte band, float power, LaserSensor sensor, float aerosol)
            => Table(band, sensor) * Math.Clamp(power, 0f, 1f) * Math.Clamp(aerosol, 0f, 1f);

        /// <summary>D-160 — how much of the beam's scatter is left <paramref name="distanceM"/> along it: <c>exp(−d / visibility)</c> (1 with no weather).</summary>
        public static float FadeAlong(float distanceM, float visibilityM)
            => visibilityM > 0f && !float.IsInfinity(visibilityM) ? MathF.Exp(-MathF.Max(0f, distanceM) / visibilityM) : 1f;

        /// <summary>D-160 — the spot's brightness, 0…1: <c>table × strength</c> (<see cref="LaserSpotPoint.Strength"/>) — a surface return, no aerosol.</summary>
        public static float Spot(byte band, float strength, LaserSensor sensor)
            => Table(band, sensor) * Math.Clamp(strength, 0f, 1f);
    }
}
