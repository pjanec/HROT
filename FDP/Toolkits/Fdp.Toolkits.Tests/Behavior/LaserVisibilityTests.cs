using System;
using Fdp.Toolkit.Behavior.Devices;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ SD-S6a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8a D-158 – D-160) — THE one laser-visibility rule: the full band × sensor table, the haze
    /// (monotonic in visibility, raised by humidity), the beam's scatter and fade, the spot's surface return, and 0 B.
    /// </summary>
    public sealed class LaserVisibilityTests
    {
        /// <summary>D-159, every cell: band 0 eye ✓ NV ✓; band 1 nothing; band 2 NV × 0.25 only; band 3 NV only; thermal nothing; an unknown band nothing.</summary>
        [Theory]
        [InlineData(LaserBands.Visible, 1f, 1f, 0f)]
        [InlineData(LaserBands.EyeSafeRangefinder, 0f, 0f, 0f)]
        [InlineData(LaserBands.Designator, 0f, 0.25f, 0f)]
        [InlineData(LaserBands.NearInfrared, 0f, 1f, 0f)]
        [InlineData((byte)9, 0f, 0f, 0f)]
        public void D159_TheBandBySensorTable(byte band, float eye, float nv, float thermal)
        {
            Assert.Equal(eye, LaserVisibility.Table(band, LaserSensor.Eye));
            Assert.Equal(nv, LaserVisibility.Table(band, LaserSensor.NightVision));
            Assert.Equal(thermal, LaserVisibility.Table(band, LaserSensor.Thermal));
            // the beam and the spot follow the table: zero where it is zero, whatever the power, haze or strength
            foreach (var s in new[] { LaserSensor.Eye, LaserSensor.NightVision, LaserSensor.Thermal })
            {
                float t = LaserVisibility.Table(band, s);
                Assert.Equal(t, LaserVisibility.Beam(band, 1f, s, 1f), 5);
                Assert.Equal(t, LaserVisibility.Spot(band, 1f, s), 5);
            }
        }

        /// <summary>D-160 — the haze rises as the visibility falls (never below the floor, never above 1); clean air (≥ 20 km, or no weather) is the floor.</summary>
        [Fact]
        public void D160_TheHaze_IsMonotonicInVisibility()
        {
            float last = 2f;
            foreach (var v in new[] { 10f, 100f, 500f, 1_000f, 2_000f, 5_000f, 10_000f, 15_000f, 20_000f, 50_000f })
            {
                float a = LaserVisibility.Aerosol(v, 0.5f);
                Assert.InRange(a, LaserVisibility.MinAerosol, 1f);
                Assert.True(a <= last, $"haze at {v} m ({a}) must not exceed the haze at less visibility ({last})");
                last = a;
            }
            Assert.Equal(LaserVisibility.MinAerosol, LaserVisibility.Aerosol(float.PositiveInfinity, 0.5f));
            Assert.Equal(LaserVisibility.MinAerosol, LaserVisibility.Aerosol(20_000f, 0.5f));
            Assert.True(LaserVisibility.Aerosol(2_000f, 0.5f) > 5f * LaserVisibility.Aerosol(20_000f, 0.5f), "a hazy 2 km day scatters far more than clean air");
        }

        /// <summary>D-160 — humidity above 80 % adds haze (half the way to 1 at 100 %); at or below 80 % it adds nothing.</summary>
        [Fact]
        public void D160_HumidityAbove80Percent_AddsHaze()
        {
            float dry = LaserVisibility.Aerosol(10_000f, 0.8f);
            Assert.Equal(dry, LaserVisibility.Aerosol(10_000f, 0.3f));
            Assert.True(LaserVisibility.Aerosol(10_000f, 0.9f) > dry);
            Assert.Equal(dry + (1f - dry) * 0.5f, LaserVisibility.Aerosol(10_000f, 1f), 5);
        }

        /// <summary>D-160 — the beam scales with power and haze; it fades along its length with exp(−d / visibility) (no weather: no fade).</summary>
        [Fact]
        public void D160_TheBeam_ScalesWithPowerAndHaze_AndFadesAlongIt()
        {
            Assert.Equal(0.5f * 0.4f, LaserVisibility.Beam(LaserBands.Visible, 0.5f, LaserSensor.Eye, 0.4f), 5);
            Assert.Equal(0.25f * 0.4f, LaserVisibility.Beam(LaserBands.Designator, 1f, LaserSensor.NightVision, 0.4f), 5);
            Assert.Equal(1f, LaserVisibility.Beam(LaserBands.Visible, 3f, LaserSensor.Eye, 2f), 5);   // clamped
            Assert.Equal(1f, LaserVisibility.FadeAlong(0f, 1_000f), 5);
            Assert.Equal(MathF.Exp(-1f), LaserVisibility.FadeAlong(1_000f, 1_000f), 5);
            Assert.True(LaserVisibility.FadeAlong(500f, 1_000f) > LaserVisibility.FadeAlong(900f, 1_000f));
            Assert.Equal(1f, LaserVisibility.FadeAlong(5_000f, float.PositiveInfinity));
        }

        /// <summary>D-160 — the spot is a surface return: its brightness is table × strength, with no haze in it.</summary>
        [Fact]
        public void D160_TheSpot_IsTableTimesStrength()
        {
            float strength = LaserSpots.StrengthAt(1f, 1_000f);   // ½
            Assert.Equal(strength, LaserVisibility.Spot(LaserBands.Visible, strength, LaserSensor.Eye), 5);
            Assert.Equal(0.25f * strength, LaserVisibility.Spot(LaserBands.Designator, strength, LaserSensor.NightVision), 5);
            Assert.Equal(0f, LaserVisibility.Spot(LaserBands.Designator, strength, LaserSensor.Eye));
        }

        /// <summary>0 B: the rule allocates nothing.</summary>
        [Fact]
        public void TheRule_AllocatesNothing()
        {
            float sink = 0f;
            for (int i = 0; i < 100; i++) sink += LaserVisibility.Beam((byte)(i & 3), 1f, (LaserSensor)(i % 3), LaserVisibility.Aerosol(1_000f + i, 0.9f)) + LaserVisibility.FadeAlong(i, 2_000f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
                sink += LaserVisibility.Beam((byte)(i & 3), 1f, (LaserSensor)(i % 3), LaserVisibility.Aerosol(1_000f + i, 0.9f))
                        + LaserVisibility.Spot((byte)(i & 3), 0.5f, (LaserSensor)(i % 3)) + LaserVisibility.FadeAlong(i, 2_000f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(sink > 0f);
        }
    }
}
