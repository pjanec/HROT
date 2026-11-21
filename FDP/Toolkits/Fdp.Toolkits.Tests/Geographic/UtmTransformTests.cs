using System;
using System.Numerics;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkits.Tests.Geographic;

/// <summary>
/// ⭐ CE-4394 — the exact UTM frame of an imported terrain (docs/DESIGN_Terrain_Import_UTM.md §12.2): <see cref="UtmTransform"/>, the switchable
/// one-per-node <see cref="SwitchableGeoTransform"/>, the grid convergence, and <c>frame.utm</c> in the terrain definition.
/// </summary>
public sealed class UtmTransformTests
{
    // real-1's frame: the other simulator's origin, zone 32N (UTM §2 / §5.2)
    private const int Zone = 32;
    private const double E0 = 372_000.0, N0 = 6_043_000.0;

    private static double MetresBetween((double lat, double lon) a, (double lat, double lon) b)
    {
        double dLat = (a.lat - b.lat) * 111_320.0;
        double dLon = (a.lon - b.lon) * 111_320.0 * Math.Cos(a.lat * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    [Fact]
    public void Real1s_corners_convert_to_the_source_grids_lat_lon_to_a_tenth_of_a_metre()
    {
        var utm = new UtmTransform(Zone, south: false, E0, N0);
        foreach (var (x, y) in new[] { (0.0, 0.0), (5500.0, 0.0), (0.0, 5500.0), (5500.0, 5500.0), (2750.0, 1234.5) })
        {
            var (lat, lon, _) = utm.ToGeodetic(new Vector3((float)x, (float)y, 0f));
            var expected = Utm.ToGeodetic(E0 + x, N0 + y, Zone, south: false);
            Assert.True(MetresBetween((lat, lon), (expected.LatDeg, expected.LonDeg)) < 0.1, $"corner ({x}, {y})");
        }
        Assert.InRange(utm.Origin.lat, 54.0, 55.0);   // north Germany
    }

    /// <summary>📐 Measured: inside a zone the round trip is sub-millimetre; 4.4° from the central meridian (1.4° past the zone's edge) the
    /// Snyder series' truncation leaves ≈ 7 mm — a terrain that crosses a zone edge reaches ≈ 3–3.5°, so a centimetre bound there is honest.</summary>
    [Theory]
    [InlineData(32, false, 372_000.0, 6_043_000.0, 0.001)]   // real-1
    [InlineData(33, true, 600_000.0, 6_200_000.0, 0.001)]    // a southern-hemisphere box (≈ 34° S)
    [InlineData(32, false, 830_000.0, 5_500_000.0, 0.01)]    // past zone 32's eastern edge: ≈ 13.4° E (zone 33's band), still in 32
    public void Local_to_geodetic_to_local_round_trips_in_any_zone_hemisphere_and_past_the_zone_edge(int zone, bool south, double e0, double n0, double tolerance)
    {
        var utm = new UtmTransform(zone, south, e0, n0);
        for (float x = -2000f; x <= 6000f; x += 1000f)
            for (float y = -2000f; y <= 6000f; y += 1000f)
            {
                var p = new Vector3(x, y, 37.5f);
                var (lat, lon, alt) = utm.ToGeodetic(p);
                if (south) Assert.True(lat < 0);
                var back = utm.ToCartesian(lat, lon, alt);
                Assert.True(Vector3.Distance(p, back) < tolerance, $"({x}, {y}) came back {back}");
            }
    }

    [Fact]
    public void The_grid_convergence_matches_the_first_order_formula_and_real1s_stated_value()
    {
        // at 45° N, 3° east of the central meridian: γ ≈ Δλ · sin φ = 2.1213° (the ellipsoidal terms are far below 0.01°)
        Assert.Equal(2.1213, Utm.ConvergenceDeg(45.0, Utm.CentralMeridianDeg(31) + 3.0, 31), 2);
        Assert.Equal(0.0, Utm.ConvergenceDeg(45.0, Utm.CentralMeridianDeg(31), 31), 9);   // on the meridian: none
        // real-1 lies west of zone 32's meridian (9° E): γ negative, about −1.6 … −1.7° (UTM §2)
        double g = new UtmTransform(Zone, false, E0, N0).GridConvergenceDeg(new Vector3(2750f, 2750f, 0f));
        Assert.InRange(g, -1.75, -1.55);
        // a local-only frame has none (and every test double)
        Assert.Equal(0.0, ((IGeographicTransform)new LocalOnlyFrame()).GridConvergenceDeg(new Vector3(2750f, 2750f, 0f)));
    }

    [Fact]
    public void The_one_instance_switches_between_utm_and_local_only_and_holders_follow()
    {
        var geo = new SwitchableGeoTransform();
        IGeographicTransform holder = geo;                     // what a translator, a module, the debug API keep
        Assert.False(holder.HasFrame);                         // before any terrain: local-only
        geo.UseUtm(Zone, false, E0, N0);
        Assert.True(holder.HasFrame);
        var (lat, lon, _) = holder.ToGeodetic(Vector3.Zero);
        var expected = Utm.ToGeodetic(E0, N0, Zone, false);
        Assert.True(MetresBetween((lat, lon), (expected.LatDeg, expected.LonDeg)) < 0.01);
        geo.UseOrigin(52.52, 13.405, 0);                       // the rule: zone 33 N at that point
        Assert.Equal(33, Assert.IsType<UtmTransform>(geo.Inner).Zone);
        Assert.Equal(52.52, holder.Origin.lat, 8);
        geo.UseLocalOnly();
        Assert.False(holder.HasFrame);
        Assert.Throws<NoGeoFrameException>(() => holder.ToGeodetic(Vector3.Zero));
    }

    [Fact]
    public void A_terrain_definition_reads_frame_utm_and_its_corner_as_the_frame_of_local_zero()
    {
        var def = TerrainDefinitionParser.Parse("""
            { "schemaVersion": 2, "name": "real-1", "world": "real-1.world.geojson",
              "origin": { "lat": 54.5, "lon": 7.0, "alt": 0 },
              "frame": { "utm": { "zone": 32, "hemisphere": "N", "e0": 372000, "n0": 6043000 }, "corner": [ -120.5, 40 ] } }
            """);
        Assert.Equal(new TerrainUtmFrame(32, false, 371_879.5, 6_043_040.0), def.Utm);
        Assert.Null(TerrainDefinitionParser.Parse("""{ "schemaVersion": 2, "name": "x", "origin": { "lat": 1, "lon": 2 } }""").Utm);
        Assert.Throws<ArgumentException>(() => TerrainDefinitionParser.Parse("""{ "schemaVersion": 2, "name": "x", "frame": { "utm": { "zone": 61, "e0": 1, "n0": 2 } } }"""));
    }
}
