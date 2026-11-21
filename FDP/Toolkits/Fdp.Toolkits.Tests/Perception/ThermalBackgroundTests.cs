using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1987</c> (R-1058; docs/DESIGN_Thermal_And_Acoustic_Sensing.md §9 C-2, C-5) — the thermal BACKGROUND of a world: the surface under a point by the ONE
    /// shared grid (<see cref="GroundSurfaces"/>) — a field reads grass, bare ground soil, a road asphalt, a flyer the sky — at the frame's class temperatures,
    /// prepared once per sim time and read allocation-free. 🔴 Red-proof: <c>SurfaceClassGrid.Build</c> not stamping Open surfaces ⇒ the field reads soil.
    /// </summary>
    public sealed class ThermalBackgroundTests
    {
        private readonly ITestOutputHelper _out;
        public ThermalBackgroundTests(ITestOutputHelper output) => _out = output;

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            var field = new TerrainSurface
            {
                Type = TerrainSurfaceType.Open,
                Polygon = new[] { new Vector2(0, 185), new Vector2(600, 185), new Vector2(600, 215), new Vector2(0, 215) },
                Min = new Vector2(0, 185), Max = new Vector2(600, 215),
            };
            w.SetSingletonManaged(new TerrainWorld { Surfaces = new[] { field } });
            w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
            return w;
        }

        [Fact]
        public void CE1987_AFieldReadsGrass_BareGroundSoil_AFlyerTheSky_AndTheyDifferAtNoon()
        {
            using var w = World();
            var bg = ThermalBackground.Prepare(w);
            Assert.Equal(ThermalClassId.Grass, bg.GroundClassAt(300, 200));
            Assert.Equal(ThermalClassId.SoilDry, bg.GroundClassAt(300, 320));
            float grass = bg.BackgroundC(new Vector3(300, 200, 0)), soil = bg.BackgroundC(new Vector3(300, 320, 0)), sky = bg.BackgroundC(new Vector3(300, 320, 500));
            _out.WriteLine($"noon (no clock): grass {grass:0.0} C, soil {soil:0.0} C, sky {sky:0.0} C, hull {bg.HullC:0.0} C, air {bg.AirC:0.0} C");
            Assert.Equal(bg.SkyC, sky);
            Assert.True(soil > grass + 4f, $"bare soil is hotter than grass at noon: {soil:0.0} vs {grass:0.0}");
            Assert.True(MathF.Abs(bg.HullC - grass) > 4f, "a sun-heated hull stands out against grass");
        }

        [Fact]
        public void CE1987_PreparedOncePerSimTime_AndReadAllocationFree()
        {
            using var w = World();
            var bg = ThermalBackground.Prepare(w);
            Assert.Same(bg, ThermalBackground.Of(w));
            double at = bg.PreparedAt;
            float sink = 0f;
            for (int i = 0; i < 4; i++) { ThermalBackground.Prepare(w); sink += bg.BackgroundC(new Vector3(i, 200, 0)); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { ThermalBackground.Prepare(w); sink += bg.BackgroundC(new Vector3(i, 200 + i, 0)); }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(at, bg.PreparedAt);
            Assert.True(bytes == 0, $"{bytes} B over 200 reads (sink {sink})");
        }
    }
}
