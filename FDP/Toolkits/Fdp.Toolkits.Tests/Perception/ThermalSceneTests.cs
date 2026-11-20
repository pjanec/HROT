using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Weather;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1985</c> SV-2 step 1 (docs/designs/sensor-view/DESIGN_Sensor_View.md §5a, §5b, §7 TH-1…TH-4) — the thermal scene's SURFACE MODEL as pure numbers: a scene at 50.0°N 14.4°E on
    /// 2026-06-21, clear unless said, a 2 m/s wind; every comparison is on the APPARENT °C a camera reads (<c>ε·T + (1−ε)·T_sky</c>), the relations the frame rails will pin on the
    /// rendered scene. 🔴 Red-proof: the first-order LAG removed (τ → 0 for every class) ⇒ TH-2's "water warmer than grass at 3 am" fails.
    /// </summary>
    public class ThermalSceneTests
    {
        private readonly ITestOutputHelper _out;
        public ThermalSceneTests(ITestOutputHelper output) => _out = output;

        private const double Lat = 50.0, Lon = 14.4;
        private static readonly DateTime Day = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
        private const float MeanC = 15f;

        private static WeatherSample Sample(double utcHour, float octas, bool rain = false)
        {
            var utc = Day.AddHours(utcHour);
            var sun = SolarPosition.Of(utc, Lat, Lon);
            float amp = 2f + 4f * (1f - octas / 8f);
            float phase = (float)Math.Cos(2 * Math.PI * (sun.SolarHour - 14.5) / 24.0);
            return new WeatherSample
            {
                Utc = utc, GroundTemperatureC = MeanC + amp * phase, TemperatureC = MeanC + amp * phase, DiurnalPhase = phase,
                CloudOctas = octas, CloudOctasAbove = octas, Wind = new Vector2(2f, 0f),
                SunElevationDeg = sun.ElevationDeg, SunAzimuthDeg = sun.AzimuthDeg,
                Precipitation = rain ? PrecipitationKind.Rain : PrecipitationKind.None,
            };
        }

        private static ThermalScene.DayForcing Forcing(float octas) => ThermalScene.DayForcing.Of(Day, Lat, Lon, ThermalScene.CloudCut(octas));

        /// <summary>The apparent °C of every class in a scene.</summary>
        private static float[] Apparent(double utcHour, float octas, bool rain = false)
        {
            var s = Sample(utcHour, octas, rain);
            var flat = new float[ThermalScene.ClassCount]; var gain = new float[ThermalScene.ClassCount]; var eps = new float[ThermalScene.ClassCount];
            ThermalScene.ClassTemperatures(in s, Forcing(octas), flat, gain, eps);
            float sky = ThermalScene.SkyC(in s);
            var app = new float[ThermalScene.ClassCount];
            for (int i = 0; i < app.Length; i++) app[i] = ThermalScene.Apparent(flat[i], eps[i], sky);
            return app;
        }

        private static float C(float[] a, ThermalClassId c) => a[(int)c];

        private const double Noon = 11.0, ThreeAm = 1.0;   // local SOLAR noon at 14.4°E is ≈ 11:03 UTC; 03:00 solar ≈ 02:00 UTC — the 03:00 rail uses 02:00 UTC below

        /// <summary>The 13 terrain classes in order, then the painted hull; names map, unknown ⇒ concrete.</summary>
        [Fact]
        public void SV2_TheClassIds_AreTheTerrainListInOrder_AndNamesMap()
        {
            for (int i = 0; i < TerrainThermalClass.All.Length; i++)
            {
                Assert.Equal((ThermalClassId)i, ThermalScene.FromName(TerrainThermalClass.All[i]));
            }
            Assert.Equal(TerrainThermalClass.All.Length, (int)ThermalClassId.PaintedMetal);
            Assert.Equal(ThermalScene.ClassCount, (int)ThermalClassId.PaintedMetal + 1);
            Assert.Equal(ThermalClassId.Concrete, ThermalScene.FromName("nonsense"));
            Assert.Equal(ThermalClassId.Concrete, ThermalScene.FromName(null));
            Assert.Equal(0.9f, ThermalScene.Emissivity[(int)ThermalClassId.PaintedMetal]);
        }

        /// <summary>A June day at 50°N: a positive mean, the first harmonic peaking within an hour of solar noon; the solar offset is the longitude's.</summary>
        [Fact]
        public void SV2_ADayForcing_HasAPositiveMean_AndPeaksAtSolarNoon()
        {
            var f = Forcing(0f);
            Assert.True(f.MeanWm2 > 100f && f.AmpWm2 > f.MeanWm2 * 0.8f, $"mean {f.MeanWm2}, amp {f.AmpWm2}");
            Assert.InRange(f.PeakSolarHour, 11f, 13f);
            Assert.InRange(f.SolarOffsetHours, 0.9f, 1.1f);   // 14.4°E ≈ +0.96 h
            Assert.True(Forcing(8f).MeanWm2 < f.MeanWm2 * 0.4f, "overcast cuts the day's sun");
            _out.WriteLine($"DayForcing clear: mean {f.MeanWm2:F1} amp {f.AmpWm2:F1} peak {f.PeakSolarHour:F2} h offset {f.SolarOffsetHours:F2} h");
        }

        /// <summary>TH-1 — noon: asphalt &gt; grass &gt; water; a painted hull &gt; grass.</summary>
        [Fact]
        public void TH1_AtNoon_AsphaltIsHotterThanGrass_GrassThanWater_AndAHullThanGrass()
        {
            var a = Apparent(Noon, 0f);
            _out.WriteLine($"TH-1 noon: asphalt {C(a, ThermalClassId.Asphalt):F1} grass {C(a, ThermalClassId.Grass):F1} water {C(a, ThermalClassId.Water):F1} hull {C(a, ThermalClassId.PaintedMetal):F1}");
            Assert.True(C(a, ThermalClassId.Asphalt) > C(a, ThermalClassId.Grass));
            Assert.True(C(a, ThermalClassId.Grass) > C(a, ThermalClassId.Water));
            Assert.True(C(a, ThermalClassId.PaintedMetal) > C(a, ThermalClassId.Grass));
        }

        /// <summary>TH-2 — 03:00 clear: water &gt; grass; asphalt &gt; grass; a painted hull &lt; grass; glass &lt; brick (it mirrors the cold sky).</summary>
        [Fact]
        public void TH2_At3amClear_WaterAndAsphaltAreWarmerThanGrass_AHullAndGlassAreColder()
        {
            var a = Apparent(ThreeAm, 0f);
            _out.WriteLine($"TH-2 3am clear: water {C(a, ThermalClassId.Water):F2} asphalt {C(a, ThermalClassId.Asphalt):F2} grass {C(a, ThermalClassId.Grass):F2} hull {C(a, ThermalClassId.PaintedMetal):F2} glass {C(a, ThermalClassId.Glass):F2} brick {C(a, ThermalClassId.Brick):F2}");
            Assert.True(C(a, ThermalClassId.Water) > C(a, ThermalClassId.Grass), "water holds the day's warmth");
            Assert.True(C(a, ThermalClassId.Asphalt) > C(a, ThermalClassId.Grass));
            Assert.True(C(a, ThermalClassId.PaintedMetal) < C(a, ThermalClassId.Grass));
            Assert.True(C(a, ThermalClassId.Glass) < C(a, ThermalClassId.Brick));
        }

        /// <summary>TH-3 — 03:00 overcast: the asphalt-over-grass difference is less than half of TH-2's (clouds hand the cold sky back).</summary>
        [Fact]
        public void TH3_At3amOvercast_TheContrastCollapses()
        {
            var clear = Apparent(ThreeAm, 0f); var over = Apparent(ThreeAm, 8f);
            float dClear = C(clear, ThermalClassId.Asphalt) - C(clear, ThermalClassId.Grass);
            float dOver = C(over, ThermalClassId.Asphalt) - C(over, ThermalClassId.Grass);
            _out.WriteLine($"TH-3 asphalt − grass: clear {dClear:F2}, overcast {dOver:F2}");
            Assert.True(dClear > 0f && dOver < dClear * 0.5f, $"{dOver} vs {dClear}");
        }

        /// <summary>Rain and wind hold a surface nearer the air: the same noon asphalt is cooler in the rain than dry.</summary>
        [Fact]
        public void SV2_RainHoldsASurfaceNearTheAir()
        {
            float dry = C(Apparent(Noon, 0f), ThermalClassId.Asphalt), wet = C(Apparent(Noon, 0f, rain: true), ThermalClassId.Asphalt);
            Assert.True(wet < dry);
        }

        private static ThermalState Tank() => new() { BaseSignature = 0.3f, RunningHeatPerSecond = 0.05f, CooldownPerSecond = 0.02f, ReferenceSpeed = 5f, IdleHeat = 0.35f, RunningHeat = 0.8f, HasPowerPlant = true };
        private static ThermalState Man() => new() { BaseSignature = 0.4f, RunningHeatPerSecond = 0.05f, CooldownPerSecond = 0.02f, ReferenceSpeed = 3f, IdleHeat = 0.2f, RunningHeat = 0.7f };

        /// <summary>TH-4 — units: a man at 3 am is more than 10 K above the grass; an idling engine's hot end &gt; its body &gt; the grass at night; a fire saturates at ≥ 600 °C.</summary>
        [Fact]
        public void TH4_Units_AManIsWarmerThanGrass_AnEnginesHotEndIsHotterThanItsBody_AFireSaturates()
        {
            var a = Apparent(ThreeAm, 0f);
            float grass = C(a, ThermalClassId.Grass), hull = C(a, ThermalClassId.PaintedMetal);
            var man = Man(); var tank = Tank();
            var standing = HeatSource.Of(HeatSourceState.Standing, (SimInstant)0.0, (SimInstant)0.0);
            var idling = HeatSource.Of(HeatSourceState.Idle, (SimInstant)0.0, (SimInstant)double.NegativeInfinity);
            var m = ThermalScene.Unit(in man, in standing, 0f, 600.0, 0f, 0f, double.NaN, grass);
            var t = ThermalScene.Unit(in tank, in idling, 0f, 600.0, 0f, 0f, double.NaN, hull);
            var fire = ThermalScene.Unit(in tank, in idling, 0f, 600.0, 0f, 1f, double.NaN, hull);
            var barrel = ThermalScene.Unit(in tank, in idling, 0f, 600.0, 0.5f, 0f, double.NaN, hull);
            var dead = ThermalScene.Unit(in man, in standing, 0f, 600.0 + 3600.0, 0f, 0f, 600.0, grass);
            _out.WriteLine($"TH-4: grass {grass:F1} man {m.BodyC:F1} hull {hull:F1} engine body {t.BodyC:F1} hot end {t.HotEndC:F1} barrel {barrel.BarrelC:F1} dead-1h {dead.BodyC:F1}");
            Assert.True(m.BodyC > grass + 10f);
            Assert.True(t.HotEndC > t.BodyC && t.BodyC > grass - 5f);
            Assert.True(t.HotEndC > grass);
            Assert.True(barrel.BarrelC > t.BodyC + 55f);
            Assert.True(fire.BodyC >= 600f && fire.HotEndC >= 600f && fire.BarrelC >= 600f);
            Assert.True(dead.BodyC < m.BodyC && dead.BodyC > grass, "a corpse cools toward the air, slowly");
        }

        /// <summary>ALLOC — ClassTemperatures, Unit and DayForcing.Of allocate nothing once warm.</summary>
        [Fact]
        public void SV2_TheModel_AllocatesNothing()
        {
            var s = Sample(ThreeAm, 2f);
            var f = ThermalScene.DayForcing.Of(Day, Lat, Lon, 0.8f);
            var flat = new float[ThermalScene.ClassCount]; var gain = new float[ThermalScene.ClassCount]; var eps = new float[ThermalScene.ClassCount];
            var tank = Tank(); var src = HeatSource.Of(HeatSourceState.Running, (SimInstant)0.0, (SimInstant)0.0);
            float sink = 0f;
            for (int i = 0; i < 3; i++)
            {
                ThermalScene.ClassTemperatures(in s, in f, flat, gain, eps);
                sink += ThermalScene.Unit(in tank, in src, 3f, 100.0, 0.2f, 0f, double.NaN, 10f).HotEndC;
                sink += ThermalScene.DayForcing.Of(Day, Lat, Lon, 0.8f).MeanWm2;
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
            {
                ThermalScene.ClassTemperatures(in s, in f, flat, gain, eps);
                sink += ThermalScene.Unit(in tank, in src, 3f, 100.0, 0.2f, 0f, double.NaN, 10f).HotEndC;
                sink += ThermalScene.DayForcing.Of(Day, Lat, Lon, 0.8f).MeanWm2;
                sink += ThermalScene.Apparent(flat[0], eps[0], ThermalScene.SkyC(in s));
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0, $"{bytes} B over 50 rounds (sink {sink})");
        }
    }
}
