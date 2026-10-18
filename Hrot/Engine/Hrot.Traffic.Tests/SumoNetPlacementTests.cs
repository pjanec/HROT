using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.Traffic;
using Sim.Ingest;

namespace Hrot.Traffic.Tests;

/// <summary>
/// AQ93 E1 / E1b (R-4025, R-4052) — where a SUMO net sits on the terrain, and that it rides the terrain load (R-4021).
/// </summary>
public sealed class SumoNetPlacementTests
{
    // ── UTM ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Utm_TheCentralMeridianOnTheEquator_IsTheFalseEasting()
    {
        var (lat, lon) = Utm.ToGeodetic(500000.0, 0.0, zone: 33, south: false);
        Assert.Equal(0.0, lat, 9);
        Assert.Equal(15.0, lon, 9);
    }

    [Theory]
    [InlineData(52.52, 13.405, 33, false)]   // Berlin
    [InlineData(46.2, 6.14, 32, false)]      // Geneva (SumoSharp's georef fixture zone)
    [InlineData(-33.87, 151.21, 56, true)]   // Sydney
    public void Utm_RoundTrips_ToWellUnderAMillimetre(double lat, double lon, int zone, bool south)
    {
        var (e, n) = Utm.FromGeodetic(lat, lon, zone, south);
        var (lat2, lon2) = Utm.ToGeodetic(e, n, zone, south);
        Assert.Equal(lat, lat2, 8);    // 1e-8 deg ≈ 1 mm
        Assert.Equal(lon, lon2, 8);
    }

    [Theory]
    [InlineData("+proj=utm +zone=32 +ellps=WGS84 +datum=WGS84 +units=m +no_defs", 32, false)]
    [InlineData("+proj=utm +zone=56 +south +ellps=WGS84", 56, true)]
    public void Utm_ParsesTheProjString(string proj, int zone, bool south)
    {
        Assert.True(Utm.TryParseProj(proj, out var z, out var s));
        Assert.Equal(zone, z);
        Assert.Equal(south, s);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("+proj=merc +ellps=WGS84")]
    [InlineData("")]
    public void Utm_RefusesAnythingElse(string proj) => Assert.False(Utm.TryParseProj(proj, out _, out _));

    // ── placement ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void E1b_ANonGeoreferencedNet_IsPlacedByTheAnchor()
    {
        var net = NetworkParser.Parse(DemoCityNet());
        var place = SumoNetPlacement.For(net, new TerrainSumoNet("demo-city.net.xml", 2200, 2200), new TerrainGeoOrigin(52.52, 13.405, 0));

        Assert.False(place.IsGeoreferenced);
        Assert.Equal((150.0, -50.0), place.ToLocal(2350.0, 2150.0));
    }

    [Fact]
    public void E1_AUtmNet_IsPlacedThroughTheTerrainOrigin()
    {
        // A net whose (0, 0) is the UTM point of the terrain origin must place (0, 0) at local (0, 0), and 100 m of UTM
        // easting at ~100 m east (UTM scale and grid convergence make it not exactly 100).
        var (e, n) = Utm.FromGeodetic(52.52, 13.405, 33, false);
        string xml = $"<net><location netOffset=\"{-e:R},{-n:R}\" convBoundary=\"0,0,1,1\" origBoundary=\"\" projParameter=\"+proj=utm +zone=33 +ellps=WGS84\"/></net>";
        var net = NetworkParser.ParseXml(xml);
        var place = SumoNetPlacement.For(net, new TerrainSumoNet("x.net.xml", 0, 0), new TerrainGeoOrigin(52.52, 13.405, 0));

        Assert.True(place.IsGeoreferenced);
        var (x0, y0) = place.ToLocal(0, 0);
        Assert.Equal(0.0, x0, 2);
        Assert.Equal(0.0, y0, 2);
        var (x1, y1) = place.ToLocal(100, 0);
        Assert.InRange(x1, 99.5, 100.5);
        Assert.InRange(Math.Abs(y1), 0.0, 3.0);
    }

    /// <summary>⭐ AQ93 §13 F5 — what SUMO is told about our bodies goes back through the exact inverse: ToNet ∘ ToLocal is the identity,
    /// for an anchored net and a UTM one.</summary>
    [Fact]
    public void F5_ToNet_InvertsToLocal_AnchoredAndUtm()
    {
        var anchored = SumoNetPlacement.For(NetworkParser.Parse(DemoCityNet()), new TerrainSumoNet("demo-city.net.xml", 2200, 2200), null);
        var (e, n) = Utm.FromGeodetic(52.52, 13.405, 33, false);
        var utm = SumoNetPlacement.For(NetworkParser.ParseXml(
                $"<net><location netOffset=\"{-e:R},{-n:R}\" convBoundary=\"0,0,1,1\" origBoundary=\"\" projParameter=\"+proj=utm +zone=33 +ellps=WGS84\"/></net>"),
            new TerrainSumoNet("x.net.xml", 0, 0), new TerrainGeoOrigin(52.52, 13.405, 0));

        foreach (var place in new[] { anchored, utm })
            foreach (var (x, y) in new[] { (0.0, 0.0), (386.0, 386.0), (-1500.0, 720.0), (2400.0, -900.0) })
            {
                var (nx, ny) = place.ToNet(x, y);
                var (lx, ly) = place.ToLocal(nx, ny);
                Assert.Equal(x, lx, 1);   // float local frame: decimetre
                Assert.Equal(y, ly, 1);
            }
    }

    [Fact]
    public void E1_AnUnsupportedProjection_IsRefused()
    {
        var net = NetworkParser.ParseXml("<net><location netOffset=\"0,0\" convBoundary=\"0,0,1,1\" projParameter=\"+proj=merc\"/></net>");
        Assert.Throws<InvalidDataException>(() => SumoNetPlacement.For(net, new TerrainSumoNet("x", 0, 0), null));
    }

    // ── it rides the terrain load ────────────────────────────────────────────────────────────

    [Fact]
    public void R4021_TheDemoCityTerrainLoad_PublishesTheNetCache_AndAnUnloadClearsIt()
    {
        using var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());

        residency.Commit(world, residency.Prepare("demo-city"));

        var cache = TrafficNetCache.Of(world);
        Assert.NotNull(cache);
        Assert.Equal("demo-city.net.xml", cache!.File);
        Assert.Equal(16, cache.NetHash.Length);
        Assert.True(cache.Model.TlLogicsById.Count >= 51);

        residency.Unload(world);
        Assert.Null(TrafficNetCache.Of(world));
    }

    private static string TerrainRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Hrot", "Subsystems")))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain");
    }

    private static string DemoCityNet() => Path.Combine(TerrainRoot(), "demo-city", "demo-city.net.xml");
}
