using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Weather;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.Core.Tests.Weather
{
    /// <summary>
    /// ⭐ CE-3216 S1 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md §3 W-1/W-2, §5) — the region blend over REAL area entities:
    /// a Weather Region is an area (<see cref="EditablePolyline"/> relative to its <see cref="SimTransform"/>) carrying
    /// <see cref="WeatherConditions"/>; <see cref="WeatherField"/> reads its outline through <see cref="Fdp.Toolkit.Terrain.IAreaOutline"/>.
    /// </summary>
    public sealed class WeatherRegionBlendTests : IDisposable
    {
        private readonly EntityRepository _w = new();

        public WeatherRegionBlendTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<EditablePolyline>();
            _w.RegisterComponent<ScenarioClock>();
            _w.RegisterComponent<WeatherConditions>();
            var env = _w.CreateEntity();
            _w.AddComponent(env, ScenarioClock.Default);
            _w.AddComponent(env, Weather(10f, 270f, 0f));   // the global: 10 °C, calm
        }

        public void Dispose() => _w.Dispose();

        /// <summary>
        /// ⭐ W-2 — inside a region at its centroid only the region counts; half way to its edge it shares with the global; outside every
        /// region only the global counts; where two regions overlap past 1 they normalise among themselves and the global gets nothing.
        /// 🔴 Red-proof: drop the normalisation ⇒ the overlap's weights sum to 1.69.
        /// </summary>
        [Fact]
        public void W2_RegionsAdd_TheGlobalFillsTheRest_AndAnOverlapPast1Normalises()
        {
            var a = Region(new Vector2(0, 0), 100f, Weather(30f, 350f, 10f));

            AssertWeights(new Vector2(0, 0), (a, 1f), (Entity.Null, 0f));
            AssertWeights(new Vector2(50, 0), (a, 0.5f), (Entity.Null, 0.5f));
            AssertWeights(new Vector2(-300, 0), (Entity.Null, 1f));
            Assert.Equal(30f, Mean(new Vector2(0, 0)), 3);
            Assert.Equal(20f, Mean(new Vector2(50, 0)), 3);
            Assert.Equal(10f, Mean(new Vector2(-300, 0)), 3);

            var b = Region(new Vector2(50, 0), 100f, Weather(20f, 10f, 10f));
            // at (25, 0) each region weighs smoothstep(0.75) = 0.84375 ⇒ Σ 1.6875 > 1 ⇒ 0.5 / 0.5, the global 0
            AssertWeights(new Vector2(25, 0), (a, 0.5f), (b, 0.5f), (Entity.Null, 0f));
            Assert.Equal(25f, Mean(new Vector2(25, 0)), 3);
        }

        /// <summary>⭐ W-2 through the whole path — one region's wind FROM 350° and the other's FROM 10°, half and half, blow from the north at
        /// nearly full strength at their shared point (never a southerly). 🔴 Red-proof: blending the angles ⇒ the sample's wind points north.</summary>
        [Fact]
        public void W2_TheWindAcrossTwoRegions_BlendsAsAVector()
        {
            Region(new Vector2(0, 0), 100f, Weather(15f, 350f, 10f));
            Region(new Vector2(50, 0), 100f, Weather(15f, 10f, 10f));
            var wind = WeatherField.At(_w, new Vector3(25, 0, 500)).Wind;
            Assert.True(wind.Y < -9.5f, $"a northerly blows south (−Y): {wind}");
            Assert.Equal(wind, WeatherField.WindAt(_w, new Vector3(25, 0, 500)));
        }

        /// <summary>⭐ An area without weather (a tactical area, a terrain zone) and weather without an area are no region.</summary>
        [Fact]
        public void AnAreaWithoutWeather_AndWeatherWithoutAnArea_AreNoRegion()
        {
            var plain = _w.CreateEntity();
            _w.AddComponent(plain, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.SetManagedComponent(plain, new EditablePolyline { Points = Square(100f) });
            var shapeless = _w.CreateEntity();
            _w.AddComponent(shapeless, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.AddComponent(shapeless, Weather(40f, 0f, 0f));
            AssertWeights(new Vector2(0, 0), (Entity.Null, 1f));
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────────

        private Entity Region(Vector2 centre, float half, WeatherConditions weather)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(centre, 0), Rotation = Quaternion.Identity });
            _w.SetManagedComponent(e, new EditablePolyline { Points = Square(half) });
            _w.AddComponent(e, weather);
            return e;
        }

        private static List<Vector2> Square(float half)
            => new() { new(-half, -half), new(half, -half), new(half, half), new(-half, half) };

        private static WeatherConditions Weather(float meanC, float windFrom, float windSpeed)
        {
            var w = WeatherConditions.Standard;
            w.GroundTemperatureC = meanC;
            w.SetWind(0, -1000f, windFrom, windSpeed, 0f);
            w.WindCount = 1;
            return w;
        }

        private float Mean(Vector2 p)
        {
            Span<WeatherField.Contribution> c = stackalloc WeatherField.Contribution[WeatherField.MaxContributors];
            int n = WeatherField.Contributors(_w, p, c);
            float m = 0f;
            for (int i = 0; i < n; i++) m += c[i].Weight * c[i].Weather.GroundTemperatureC;
            return m;
        }

        /// <summary>The contributors at <paramref name="p"/>; <see cref="Entity.Null"/> stands for the global weather (the World Environment).</summary>
        private void AssertWeights(Vector2 p, params (Entity Source, float Weight)[] expected)
        {
            Span<WeatherField.Contribution> c = stackalloc WeatherField.Contribution[WeatherField.MaxContributors];
            int n = WeatherField.Contributors(_w, p, c);
            float sum = 0f;
            for (int i = 0; i < n; i++) sum += c[i].Weight;
            Assert.Equal(1f, sum, 4);
            foreach (var (source, weight) in expected)
            {
                float got = 0f; bool found = false;
                for (int i = 0; i < n; i++)
                {
                    bool isGlobal = _w.HasComponent<ScenarioClock>(c[i].Source);
                    if (source.IsNull ? isGlobal : c[i].Source == source) { got = c[i].Weight; found = true; }
                }
                Assert.True(found, $"no contribution from {(source.IsNull ? "the global weather" : source.ToString())} at {p}");
                Assert.Equal(weight, got, 4);
            }
        }
    }
}
