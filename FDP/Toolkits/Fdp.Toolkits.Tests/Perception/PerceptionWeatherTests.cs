using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Tests;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Weather;
using Fdp.Core.Collections;
using Xunit;

namespace Fdp.Toolkit.Tests.Perception
{
    /// <summary>
    /// ⭐⭐ <c>CE-3216</c> S2 (R-3017; docs/designs/weather/DESIGN_Weather_And_Time.md §8) — the weather and the night reach the sensors
    /// through their own seams: vision's range × the detection light curve, capped by the visibility (S2-1); thermal ignores the light,
    /// fog caps it at twice the visibility, the crossover dims only a PASSIVE base signature (S2-3); wind and rain mask hearing (S2-4); a
    /// world with no World Environment changes nothing (S2-5). The world sits at 0° N 0° E (no geo transform): solar noon ≈ 12:00 UTC.
    /// </summary>
    public sealed class PerceptionWeatherTests
    {
        private static readonly DateTime Noon = new(2025, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Midnight = new(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Crossover = new(2025, 6, 21, 8, 30, 0, DateTimeKind.Utc);   // solar 08:30: the swing crosses its mean

        private static EntityRepository World(WeatherConditions? weather, DateTime start)
        {
            var w = PerceptionTestWorldFactory.Create();
            w.RegisterComponent<ScenarioClock>();
            w.RegisterComponent<WeatherConditions>();
            w.RegisterComponent<ThermalState>();
            w.RegisterComponent<AcousticEmitter>();
            w.RegisterComponent<EqsSensor>();
            w.RegisterManagedComponent<SensorCapability>();
            w.SetSingleton(new GlobalTime { TotalTime = 0 });
            if (weather is { } wx)
            {
                var env = w.CreateEntity();
                w.AddComponent(env, new ScenarioClock { StartUtcTicks = start.Ticks });
                w.AddComponent(env, wx);
            }
            return w;
        }

        private static WeatherConditions Clear()
        {
            var w = WeatherConditions.Standard;
            w.CloudCount = 0;
            return w;
        }

        private static Entity Man(EntityRepository w, Vector3 at, ForceId force)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new EntityInfo { ForceId = force });
            return e;
        }

        private static Entity Sensor(EntityRepository w, SensorEntryDto entry)
        {
            var s = w.CreateEntity();
            w.SetManagedComponent(s, new SensorCapability { Default = entry, Current = entry });
            return s;
        }

        private static float VisionRange(EntityRepository w)
        {
            var unit = Man(w, Vector3.Zero, ForceId.Friend);
            w.AddComponent(unit, new PerceptionReceptor { VisionRange = 150f, FieldOfViewCos = -1f });
            Assert.True(VisualPerception.Optics(w, w.CreateEntity(), unit, out float range, out _));
            return range;
        }

        /// <summary>⭐ S2-1 — the same soldier (a 150 m receptor) sees 150 m at noon, a quarter of it on a moonless night, and no further than
        /// the fog; a world with no World Environment, as today. ✅ Red-proof: the weather line in <c>VisualPerception.Optics</c> removed ⇒
        /// 150 m at night ⇒ red.</summary>
        [Fact]
        public void S2_1_SightShortensAtNight_AndInFog_AndNotWithoutAClock()
        {
            using (var w = World(Clear(), Noon)) Assert.Equal(150f, VisionRange(w), 1);
            using (var w = World(Clear(), Midnight)) Assert.InRange(VisionRange(w), 37.5f, 38.5f);   // 0.25 + a starlit sliver
            var fog = Clear();
            fog.FogTopM = 30f; fog.FogVisibilityM = 50f;
            using (var w = World(fog, Noon)) Assert.Equal(50f, VisionRange(w), 1);
            using (var w = World(null, Midnight)) Assert.Equal(150f, VisionRange(w), 1);
        }

        /// <summary>⭐ S2-3 — thermal: no light term (as far at midnight as at noon), fog caps it at TWICE the visibility; and the crossover
        /// filters a cold hull (base signature only) that passes at noon, while a running engine passes both. ✅ Red-proofs: the fog cap removed
        /// ⇒ 1000 m in fog ⇒ red; the contrast factor removed ⇒ the cold hull passes at the crossover ⇒ red.</summary>
        [Fact]
        public void S2_3_ThermalIgnoresTheNight_FogCapsIt_AndTheCrossoverDimsOnlyAPassiveHull()
        {
            var entry = new SensorEntryDto { Thermal = new ThermalSensorDto { Range = 1000f, FieldOfViewDegrees = 360f, MinSignature = 0.4f } };
            float ThermalRange(EntityRepository w)
            {
                var unit = Man(w, Vector3.Zero, ForceId.Friend);
                Assert.True(ThermalPerception.Optics(w, Sensor(w, entry), unit, out float range, out _));
                return range;
            }
            using (var w = World(Clear(), Midnight)) Assert.Equal(1000f, ThermalRange(w), 1);
            var fog = Clear();
            fog.FogTopM = 30f; fog.FogVisibilityM = 150f;
            using (var w = World(fog, Noon)) Assert.Equal(150f * PerceptionWeather.ThermalFogFactor, ThermalRange(w), 1);

            bool[] Passes(DateTime when)
            {
                using var w = World(Clear(), when);
                var unit = Man(w, Vector3.Zero, ForceId.Friend);
                var sensorEntity = Sensor(w, entry);
                var hull = Man(w, new Vector3(50, 0, 0), ForceId.Hostile);
                w.AddComponent(hull, new ThermalState { BaseSignature = 0.5f, Heat = 0f });     // a parked vehicle: passive only
                var engine = Man(w, new Vector3(60, 0, 0), ForceId.Hostile);
                w.AddComponent(engine, new ThermalState { BaseSignature = 0.5f, Heat = 0.8f }); // its engine running
                var sensor = new EqsSensor { ContextSlot0 = unit };
                Span<EqsResult> c = stackalloc EqsResult[2];
                c[0] = new EqsResult { EntityId = (long)hull.PackedValue };
                c[1] = new EqsResult { EntityId = (long)engine.PackedValue };
                new SignatureFilterTest().ExecuteBatch(sensorEntity, ref sensor, w, c);
                return new[] { c[0].EntityId > 0, c[1].EntityId > 0 };
            }
            Assert.Equal(new[] { true, true }, Passes(new DateTime(2025, 6, 21, 14, 30, 0, DateTimeKind.Utc)));   // the day's warmest: full contrast
            Assert.Equal(new[] { false, true }, Passes(Crossover));
        }

        /// <summary>⭐ S2-4 — a shot 80 m off is heard by a 100 m listener in calm air, not in a strong wind, nor in heavy rain. ✅ Red-proof:
        /// the hearing factor removed from <c>AcousticSensorGenerator</c> ⇒ heard in the wind ⇒ red.</summary>
        [Fact]
        public void S2_4_WindAndRainMaskHearing()
        {
            int Heard(WeatherConditions? weather)
            {
                using var w = World(weather, Noon);
                var unit = Man(w, Vector3.Zero, ForceId.Friend);
                var ear = Sensor(w, new SensorEntryDto { Acoustic = new AcousticSensorDto { Range = 100f } });
                var shooter = Man(w, new Vector3(80, 0, 0), ForceId.Hostile);
                w.AddComponent(shooter, new AcousticEmitter { FiringAudibleRange = 400f });
                // ⭐ CE-3282 — a shot is heard from the lossless ring (Sensing §8.3), no longer from the emitter's linger
                SoundEmissionRing.Of(w).Append(new SoundEmission { Source = shooter.PackedValue, Force = (byte)ForceId.Hostile,
                    Kind = (byte)SoundKind.Shot, At = new Vector3(80f, 0f, 0f), Carry = 400f });
                w.SetSingleton(new GlobalTime { TotalTime = 0.1 });   // ⭐ CE-3293 — a solve hears what was made BEFORE its own time: the next step
                var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
                try
                {
                    grid.Clear();
                    grid.Add(shooter, new Vector2(80f, 0f));   // the perception grid the broadphase walks
                    var sensor = new EqsSensor { ContextSlot0 = unit };
                    Span<EqsResult> c = stackalloc EqsResult[8];
                    return new AcousticSensorGenerator(grid).Generate(ear, ref sensor, w, c);
                }
                finally { grid.Dispose(); }
            }
            Assert.Equal(1, Heard(Clear()));
            Assert.Equal(1, Heard(null));
            Assert.Equal(0, Heard(WeatherConditions.SteadyWind(new Vector2(12f, 0f))));   // 12 m/s: × 0.4 ⇒ 40 m
            var rain = Clear();
            rain.PrecipitationMmPerHour = 20f;                                               // heavy rain: × 1/3 ⇒ the 0.35 floor ⇒ 35 m
            Assert.Equal(0, Heard(rain));
        }

        /// <summary>⭐ The numbers the user approved (R-3017): 4 m/s ⇒ × 0.67, 10 m/s ⇒ × 0.44, 10 mm/h ⇒ × 0.5; the night floor 0.25; full
        /// light at 0.3.</summary>
        [Fact]
        public void S2_TheApprovedNumbers()
        {
            Assert.Equal(0.667f, PerceptionWeather.HearingFactor(new WeatherSample { Wind = new Vector2(4f, 0f) }), 3);
            Assert.Equal(0.444f, PerceptionWeather.HearingFactor(new WeatherSample { Wind = new Vector2(0f, 10f) }), 3);
            Assert.Equal(0.5f, PerceptionWeather.HearingFactor(new WeatherSample { PrecipitationMmPerHour = 10f }), 3);
            Assert.Equal(PerceptionWeather.HearingFloor, PerceptionWeather.HearingFactor(new WeatherSample { Wind = new Vector2(40f, 0f) }));
            Assert.Equal(150f, PerceptionWeather.VisionRange(150f, new WeatherSample { RelativeLight = 0.3f, VisibilityM = 1e6f }), 3);
            Assert.Equal(37.5f, PerceptionWeather.VisionRange(150f, new WeatherSample { RelativeLight = 0f, VisibilityM = 1e6f }), 3);
            Assert.Equal(PerceptionWeather.CrossoverFloor, PerceptionWeather.BaseContrast(new WeatherSample { DiurnalPhase = 0f }));
        }
    }
}
