using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Weather;
using Xunit;

namespace Fdp.Toolkit.Tests.Weather
{
    /// <summary>
    /// ⭐ CE-3216 S1 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md §3 W-1…W-4, §4, §5) — the pure weather math: a region's weight
    /// (W-1), the wind as a vector (W-2), the wind profile (W-3), the sun (NOAA), and the derived quantities. The region blend across real
    /// area entities is railed in <c>Hrot.Core.Tests</c> (<c>WeatherRegionBlendTests</c>), where the area outline type lives.
    /// </summary>
    public sealed class WeatherFieldTests
    {
        private static readonly List<Vector2> Square100 = new() { new(-100, -100), new(100, -100), new(100, 100), new(-100, 100) };

        // ── W-1 ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ W-1 — 1 at the centroid, 0 at the edge, smooth and falling between; along the ray, not a circle (a corner is
        /// farther than a side's middle, and both are the edge). 🔴 Red-proof: a circle of the inscribed radius ⇒ the corner region at
        /// (90, 90) reads 0, not ≈ 0.15.</summary>
        [Fact]
        public void W1_ARegionWeighs1AtItsCentroid_0AtItsEdge_AndFallsSmoothlyBetween()
        {
            var c = WeatherField.Centroid(Square100);
            Assert.Equal(Vector2.Zero, c);
            Assert.Equal(1f, WeatherField.RegionWeight(Square100, c, 0f, c), 5);
            Assert.Equal(0f, WeatherField.RegionWeight(Square100, c, 0f, new Vector2(100, 0)), 5);       // a side's middle
            Assert.Equal(0f, WeatherField.RegionWeight(Square100, c, 0f, new Vector2(100, 100)), 5);     // a corner: also the edge
            Assert.Equal(0f, WeatherField.RegionWeight(Square100, c, 0f, new Vector2(150, 0)), 5);       // outside
            Assert.Equal(0.5f, WeatherField.RegionWeight(Square100, c, 0f, new Vector2(50, 0)), 5);      // half way: smoothstep(0.5)
            Assert.Equal(0.5f, WeatherField.RegionWeight(Square100, c, 0f, new Vector2(50, 50)), 5);     // half way to the corner: the same
            Assert.True(WeatherField.RegionWeight(Square100, c, 0f, new Vector2(90, 90)) > 0.02f, "the ray reaches the corner; a circle would not");

            float prev = 1.1f;
            for (int i = 0; i <= 20; i++)
            {
                float w = WeatherField.RegionWeight(Square100, c, 0f, new Vector2(i * 5f, 0));
                Assert.True(w <= prev + 1e-6f, $"not monotone at x={i * 5}");
                if (i > 0) Assert.True(prev - w < 0.12f, $"a step of {prev - w:0.000} at x={i * 5} — not smooth");
                prev = w;
            }
        }

        /// <summary>⭐ W-1 — a feather carries the fade past the edge: at the edge the region still counts, and it reaches 0 a feather out.</summary>
        [Fact]
        public void W1_AFeather_CarriesTheFadePastTheEdge()
        {
            var c = Vector2.Zero;
            Assert.True(WeatherField.RegionWeight(Square100, c, 50f, new Vector2(100, 0)) > 0.05f);
            Assert.Equal(0f, WeatherField.RegionWeight(Square100, c, 50f, new Vector2(150, 0)), 5);
        }

        // ── W-2 / W-3: the wind ─────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ W-2 — a wind FROM 350° and one FROM 10°, half and half, blow from the NORTH at nearly full strength — never from
        /// the south (averaging the angles would give 180°). 🔴 Red-proof: blend the angles ⇒ a southerly.</summary>
        [Fact]
        public void W2_TheWindBlendsAsAVector_AcrossNorth()
        {
            var a = WeatherField.Toward(350f, 10f);
            var b = WeatherField.Toward(10f, 10f);
            var mix = 0.5f * a + 0.5f * b;
            Assert.True(mix.Y < -9.5f, $"a northerly blows south (−Y): {mix}");
            Assert.InRange(mix.X, -0.01f, 0.01f);
        }

        /// <summary>⭐ W-3 — the wind between levels is interpolated; above the top it is the top's; below the lowest it falls off
        /// logarithmically to zero at the ground (a 10 m level, a man at 2 m feels ~ 0.7 of it).</summary>
        [Fact]
        public void W3_TheWindProfile_InterpolatesLevels_AndFallsOffLogarithmicallyToTheGround()
        {
            var w = WeatherConditions.Standard;   // calm, then: 3 m/s from 270° at 10 m, 8 m/s at 1000 m
            Assert.Equal(Vector2.Zero, WeatherField.WindOf(w, 500f, 0f, out _));
            w.SetWind(0, 10f, 270f, 3f, 0f);
            w.SetWind(1, 1000f, 270f, 8f, 0f);
            w.WindCount = 2;
            Assert.Equal(3f, WeatherField.WindOf(w, 10f, 0f, out _).Length(), 3);
            Assert.Equal(8f, WeatherField.WindOf(w, 1000f, 0f, out _).Length(), 3);
            Assert.Equal(8f, WeatherField.WindOf(w, 5000f, 0f, out _).Length(), 3);
            Assert.Equal(5.5f, WeatherField.WindOf(w, 505f, 0f, out _).Length(), 2);
            var toward = WeatherField.WindOf(w, 1000f, 0f, out _);
            Assert.True(toward.X > 7.9f, $"a westerly blows EAST: {toward}");
            float at2m = WeatherField.WindOf(w, 2f, 0f, out _).Length();
            Assert.InRange(at2m / 3f, 0.6f, 0.8f);
            Assert.True(WeatherField.WindOf(w, 0.01f, 0f, out _).Length() < 0.01f, "no wind at the ground");
            // the fall-off is measured from THE GROUND BELOW: a lowest level at 510 m over a 500 m hill, a man 2 m above the hill
            var hill = w; hill.SetWind(0, 510f, 270f, 3f, 0f);
            Assert.InRange(WeatherField.WindOf(hill, 502f, 500f, out _).Length() / 3f, 0.6f, 0.8f);
            // ⚠ as built: ABOVE the lowest level there is no fall-off — 2 m above that hill under a 10 m level reads the interpolated profile,
            //   and a level at or below the ground means the author's wind reaches the ground (WeatherConditions.SteadyWind relies on it)
            Assert.InRange(WeatherField.WindOf(w, 502f, 500f, out _).Length(), 5.47f, 5.50f);   // 3 + (502 − 10) / 990 × 5
        }

        // ── the sun (NOAA) ──────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ §4 — the NOAA solar position: London at the June solstice is ~62° up at solar noon and ~15° below the horizon at
        /// solar midnight; the equator at the March equinox has the sun overhead at noon; the azimuth is south at a northern noon.</summary>
        [Fact]
        public void TheSun_IsWhereTheAlmanacPutsIt()
        {
            var noon = SolarPosition.Of(new DateTime(2025, 6, 21, 12, 2, 0, DateTimeKind.Utc), 51.5, 0.0);
            Assert.InRange(noon.ElevationDeg, 61.5, 62.4);
            Assert.InRange(noon.AzimuthDeg, 175.0, 185.0);
            var midnight = SolarPosition.Of(new DateTime(2025, 6, 22, 0, 2, 0, DateTimeKind.Utc), 51.5, 0.0);
            Assert.InRange(midnight.ElevationDeg, -15.5, -14.5);
            var equinox = SolarPosition.Of(new DateTime(2024, 3, 20, 12, 8, 0, DateTimeKind.Utc), 0.0, 0.0);
            Assert.InRange(equinox.ElevationDeg, 89.0, 90.0);
            // the longitude makes the local time: 90° east the sun is up at 06:00 UTC on that day
            var east = SolarPosition.Of(new DateTime(2024, 3, 20, 6, 8, 0, DateTimeKind.Utc), 0.0, 90.0);
            Assert.InRange(east.ElevationDeg, 89.0, 90.0);
        }

        /// <summary>⭐ §4 — the light bands are the standard twilight limits, and cloud dims the light (Kasten–Czeplak).</summary>
        [Fact]
        public void TheLight_FollowsTheTwilightBands_AndCloudDimsIt()
        {
            Assert.Equal(LightBand.Day, WeatherField.LightOf(10, 1f).Band);
            Assert.Equal(LightBand.CivilTwilight, WeatherField.LightOf(-3, 1f).Band);
            Assert.Equal(LightBand.NauticalTwilight, WeatherField.LightOf(-9, 1f).Band);
            Assert.Equal(LightBand.AstronomicalTwilight, WeatherField.LightOf(-15, 1f).Band);
            Assert.Equal(LightBand.Night, WeatherField.LightOf(-25, 1f).Band);
            Assert.True(WeatherField.LightOf(30, 0.25f).Relative < WeatherField.LightOf(30, 1f).Relative);
        }

        // ── the derived quantities at a point (§4), on a world with only the World Environment ─────────────────────────────

        /// <summary>
        /// ⭐ §4 — one World Environment (global weather + clock), no regions: at noon the temperature peaks after solar noon and the night
        /// is cooler; the air cools 6.5 K/km and thins with height (≈ 899 hPa at 1 km, ISA); irradiance is ~ 1361·sin(el)·0.75 at a clear
        /// noon and 0 at night; overcast cuts it and narrows the day–night swing; fog cuts the visibility inside the layer only.
        /// </summary>
        [Fact]
        public void TheDerivedQuantities_FollowSection4()
        {
            using var world = WorldWith(Clear(), new DateTime(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            var noon = WeatherField.At(world, Vector3.Zero, new DateTime(2025, 6, 21, 14, 30, 0, DateTimeKind.Utc));
            var night = WeatherField.At(world, Vector3.Zero, new DateTime(2025, 6, 21, 2, 30, 0, DateTimeKind.Utc));
            Assert.Equal(21f, noon.GroundTemperatureC, 1);     // the 15 °C mean + the clear-sky 6 K swing at its peak
            Assert.Equal(9f, night.GroundTemperatureC, 1);
            var up = WeatherField.At(world, new Vector3(0, 0, 1000), new DateTime(2025, 6, 21, 14, 30, 0, DateTimeKind.Utc));
            Assert.Equal(noon.TemperatureC - 6.5f, up.TemperatureC, 2);
            Assert.InRange(noon.PressureHpa, 1013f, 1013.5f);
            Assert.InRange(up.PressureHpa, 895f, 905f);
            Assert.InRange(noon.DensityKgM3, 1.15f, 1.25f);

            var clearNoon = WeatherField.At(world, Vector3.Zero, new DateTime(2025, 3, 20, 12, 8, 0, DateTimeKind.Utc));
            Assert.InRange(clearNoon.IrradianceWm2, 1000f, 1025f);    // the sun overhead at 0°, 0°: 1361 × 0.75
            Assert.Equal(0f, night.IrradianceWm2);
            Assert.Equal(LightBand.Night, night.Light);
            Assert.Equal(LightBand.Day, noon.Light);

            using var grey = WorldWith(Overcast(), new DateTime(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            var greyNoon = WeatherField.At(grey, Vector3.Zero, new DateTime(2025, 3, 20, 12, 8, 0, DateTimeKind.Utc));
            Assert.InRange(greyNoon.IrradianceWm2, 240f, 270f);        // × (1 − 0.75): the Kasten–Czeplak cut at 8 octas
            var greyPeak = WeatherField.At(grey, Vector3.Zero, new DateTime(2025, 6, 21, 14, 30, 0, DateTimeKind.Utc));
            Assert.Equal(17f, greyPeak.GroundTemperatureC, 1);         // ±2 K under overcast
            Assert.Equal(1500f, greyPeak.CeilingM);

            using var foggy = WorldWith(Fog(), new DateTime(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(150f, WeatherField.At(foggy, new Vector3(0, 0, 2)).VisibilityM, 0);
            Assert.Equal(20_000f, WeatherField.At(foggy, new Vector3(0, 0, 200)).VisibilityM, 0);
            Assert.Equal(3.912f / 150f, WeatherField.At(foggy, new Vector3(0, 0, 2)).ExtinctionPerM, 5);
        }

        // ── §6.3 — clouds: aviation cover, cover above the point, the deck ──────────────────────────────────────────────────

        /// <summary>⭐ §6.3 — a layer can be written by its aviation name, and the octas map back to the METAR bands (SKC 0 · FEW 1–2 · SCT
        /// 3–4 · BKN 5–7 · OVC 8); broken and overcast make a ceiling, scattered does not.</summary>
        [Fact]
        public void Clouds_AnAuthorCanSayOvercast_AndTheOctasMapToTheMetarBands()
        {
            Assert.Equal(new[] { CloudCover.Clear, CloudCover.Few, CloudCover.Few, CloudCover.Scattered, CloudCover.Scattered,
                                 CloudCover.Broken, CloudCover.Broken, CloudCover.Broken, CloudCover.Overcast },
                         new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }.Select(CloudCovers.Of));
            foreach (CloudCover c in Enum.GetValues<CloudCover>()) Assert.Equal(c, CloudCovers.Of(CloudCovers.Octas(c)));

            var w = Clear();
            w.SetCloud(0, 1500f, 600f, CloudCover.Scattered, CloudKind.Cumulus);
            w.CloudCount = 1;
            Assert.Equal(float.PositiveInfinity, WeatherField.CeilingOf(w));
            w.SetCloud(1, 2500f, 400f, CloudCover.Overcast, CloudKind.Stratus);
            w.CloudCount = 2;
            Assert.Equal((byte)8, CloudDeck.Of(w).Layer(1).Octas);
            Assert.Equal(2500f, WeatherField.CeilingOf(w));
        }

        /// <summary>
        /// ⭐ §6.3 — an overcast deck at 1 500–2 300 m shades the ground (the Kasten–Czeplak cut, a quarter of the clear sun) and a point
        /// INSIDE it, but not an aircraft above it: there the sun is the clear-sky sun, though the sky (seen from the ground) is still
        /// overcast. 🔴 Red-proof: cut by the total cover ⇒ the aircraft above the deck reads ~255 W/m², not ~1020.
        /// </summary>
        [Fact]
        public void Clouds_AnOvercastDeck_ShadesBelowAndInside_NotAbove()
        {
            using var world = WorldWith(Overcast(), new DateTime(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            var noon = new DateTime(2024, 3, 20, 12, 8, 0, DateTimeKind.Utc);   // the sun overhead at 0°, 0°
            var ground = WeatherField.At(world, Vector3.Zero, noon);
            var inside = WeatherField.At(world, new Vector3(0, 0, 1800), noon);
            var above = WeatherField.At(world, new Vector3(0, 0, 3000), noon);
            Assert.InRange(ground.IrradianceWm2, 240f, 270f);
            Assert.InRange(inside.IrradianceWm2, 240f, 270f);
            Assert.InRange(above.IrradianceWm2, 1000f, 1025f);
            Assert.Equal(8f, above.CloudOctas, 3);           // the sky is still overcast
            Assert.Equal(0f, above.CloudOctasAbove, 3);      // nothing over the aircraft
            Assert.True(above.RelativeLight > ground.RelativeLight);
        }

        /// <summary>⭐ §6.3 — the sample carries the deck as authored, layer by layer (a renderer draws it): a broken cumulus layer under an
        /// overcast cirrus deck.</summary>
        [Fact]
        public void Clouds_TheSampleCarriesTheLayers()
        {
            var w = Clear();
            w.SetCloud(0, 1500f, 800f, CloudCover.Broken, CloudKind.Cumulus);
            w.SetCloud(1, 8000f, 500f, CloudCover.Overcast, CloudKind.Cirrus);
            w.CloudCount = 2;
            using var world = WorldWith(w, new DateTime(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            var deck = WeatherField.At(world, Vector3.Zero).Clouds;
            Assert.Equal(2, deck.Count);
            Assert.Equal((1500f, 800f, (byte)7, CloudCover.Broken, CloudKind.Cumulus), deck.Layer(0));
            Assert.Equal((8000f, 500f, (byte)8, CloudCover.Overcast, CloudKind.Cirrus), deck.Layer(1));
        }

        /// <summary>⭐ W-4 — UTC is the start time + the sim time; ⛔ no day rate. And a world with no World Environment reads the C-3 default.</summary>
        [Fact]
        public void W4_Utc_IsTheStartTimePlusTheSimTime()
        {
            var start = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            using var world = WorldWith(Clear(), start);
            world.SetSingleton(new GlobalTime { TotalTime = 3600.5 });
            Assert.Equal(start.AddSeconds(3600.5), ScenarioTime.Utc(world));

            using var bare = new EntityRepository();
            bare.SetSingleton(new GlobalTime { TotalTime = 60 });
            Assert.Equal(ScenarioClock.DefaultStartUtc.AddSeconds(60), ScenarioTime.Utc(bare));
        }

        // ── fixtures ────────────────────────────────────────────────────────────────────────────────────────────────────────

        internal static EntityRepository WorldWith(WeatherConditions global, DateTime start)
        {
            var w = new EntityRepository();
            w.RegisterComponent<ScenarioClock>();
            w.RegisterComponent<WeatherConditions>();
            var env = w.CreateEntity();
            w.AddComponent(env, new ScenarioClock { StartUtcTicks = start.Ticks });
            w.AddComponent(env, global);
            return w;
        }

        private static WeatherConditions Clear()
        {
            var w = WeatherConditions.Standard;
            w.CloudCount = 0;
            return w;
        }

        private static WeatherConditions Overcast()
        {
            var w = WeatherConditions.Standard;
            w.SetCloud(0, 1500f, 800f, 8, CloudKind.Stratus);
            w.CloudCount = 1;
            return w;
        }

        private static WeatherConditions Fog()
        {
            var w = Clear();
            w.FogTopM = 30f; w.FogVisibilityM = 150f;
            return w;
        }
    }
}
