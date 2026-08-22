using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-05 Tier-1 gate — exercises Group M (TKB catalog) and Group N (world/coordinate info)
/// endpoints against the offline <see cref="EditorHarness"/>. No HTTP; runs fast.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch05Tests
{
    private const long TestTkbType = 1L;
    private const long UnknownTkbType = 999_999L;

    // ── Group M — TKB catalog ────────────────────────────────────────────────

    [Fact]
    public void ListTkbTypes_ReturnsNonEmptyList()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.ListTkbTypes().AsArray();
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ListTkbTypes_EachEntryHasRequiredFields()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.ListTkbTypes().AsArray();
        foreach (var item in result)
        {
            Assert.NotNull(item!["tkbType"]);
            Assert.NotNull(item["name"]?.GetValue<string>());
            Assert.NotNull(item["categoryPath"]);
            Assert.NotNull(item["disType"]);
        }
    }

    [Fact]
    public void ListTkbTypes_ContainsTestUnit()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.ListTkbTypes().AsArray();
        bool hasTestUnit = false;
        foreach (var item in result)
        {
            if (item?["tkbType"]?.GetValue<long>() == TestTkbType)
            {
                hasTestUnit = true;
                Assert.Equal("TestUnit", item["name"]?.GetValue<string>());
                break;
            }
        }
        Assert.True(hasTestUnit, "Expected TestUnit (tkbType=1) in list.");
    }

    [Fact]
    public void ListTkbTypes_FilterByCategory_ReturnsEmpty_ForUnknownCategory()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.ListTkbTypes(category: "NoSuchCategory_XYZ").AsArray();
        Assert.Empty(result);
    }

    [Fact]
    public void GetTkbType_UnknownType_ReturnsNull()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetTkbType(UnknownTkbType);
        Assert.Null(result);
    }

    [Fact]
    public void GetTkbType_ValidType_ReturnsObject()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetTkbType(TestTkbType);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetTkbType_ValidType_HasExpectedTopLevelFields()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetTkbType(TestTkbType)!.AsObject();
        Assert.Equal(TestTkbType, result["tkbType"]?.GetValue<long>());
        Assert.Equal("TestUnit", result["name"]?.GetValue<string>());
        Assert.NotNull(result["mandatoryComponents"]);
        Assert.NotNull(result["childBlueprints"]);
        Assert.NotNull(result["descriptors"]);
    }

    [Fact]
    public void GetTkbType_ValidType_MandatoryComponentsIsArray()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetTkbType(TestTkbType)!.AsObject();
        var mandatoryArr = result["mandatoryComponents"]?.AsArray();
        Assert.NotNull(mandatoryArr);
        // TestUnit has no mandatory components — array is empty but present.
    }

    // ── Group N — world/coordinate info ─────────────────────────────────────

    [Fact]
    public void GetWorldInfo_ReturnsObject()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetWorldInfo();
        Assert.NotNull(result);
    }

    [Fact]
    public void GetWorldInfo_GeoOriginIsBerlin()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetWorldInfo().AsObject();
        var geoOrigin = result["geo"]?["origin"];
        Assert.NotNull(geoOrigin);
        double lat = geoOrigin!["lat"]!.GetValue<double>();
        double lon = geoOrigin["lon"]!.GetValue<double>();
        Assert.Equal(52.52, lat, precision: 2);
        Assert.Equal(13.405, lon, precision: 3);
    }

    [Fact]
    public void GetWorldInfo_SpatialGridHasExpectedShape()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetWorldInfo().AsObject();
        var grid = result["spatialGrid"];
        Assert.NotNull(grid);
        Assert.Equal(5.0f,  grid!["cellSize"]!.GetValue<float>(), precision: 3);
        Assert.Equal(200,   grid["width"]!.GetValue<int>());
        Assert.Equal(200,   grid["height"]!.GetValue<int>());
        Assert.NotNull(grid["extent"]);
    }

    [Fact]
    public void GetWorldInfo_GridExtentIsComputedCorrectly()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetWorldInfo().AsObject();
        var extent = result["spatialGrid"]?["extent"];
        Assert.NotNull(extent);
        // originX=0, width=200, cellSize=5 → maxX = 1000
        Assert.Equal(0f,    extent!["minX"]!.GetValue<float>(), precision: 1);
        Assert.Equal(1000f, extent["maxX"]!.GetValue<float>(), precision: 1);
        Assert.Equal(0f,    extent["minY"]!.GetValue<float>(), precision: 1);
        Assert.Equal(1000f, extent["maxY"]!.GetValue<float>(), precision: 1);
    }

    [Fact]
    public void GetWorldInfo_TerrainAndNavmeshAreNull()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GetWorldInfo().AsObject();
        // terrain and navmesh keys must be present but their values must serialize as JSON null.
        Assert.True(result.ContainsKey("terrain"), "terrain key missing");
        Assert.True(result.ContainsKey("navmesh"), "navmesh key missing");
    }

    [Fact]
    public void GeoToLocal_AtOrigin_ReturnsNearZero()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Berlin origin: lat=52.52, lon=13.405, alt=0
        var result = svc.GeoToLocal(52.52, 13.405, 0.0, headingDeg: null).AsObject();
        float x = result["x"]!.GetValue<float>();
        float y = result["y"]!.GetValue<float>();
        float z = result["z"]!.GetValue<float>();

        Assert.True(MathF.Abs(x) < 1.0f, $"x={x} expected near 0");
        Assert.True(MathF.Abs(y) < 1.0f, $"y={y} expected near 0");
        Assert.True(MathF.Abs(z) < 1.0f, $"z={z} expected near 0");
    }

    [Fact]
    public void GeoToLocal_WithHeadingDeg_IncludesRotation()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GeoToLocal(52.52, 13.405, 0.0, headingDeg: 90f).AsObject();
        Assert.NotNull(result["rotation"]);
        var rot = result["rotation"]!.AsObject();
        Assert.NotNull(rot["x"]);
        Assert.NotNull(rot["y"]);
        Assert.NotNull(rot["z"]);
        Assert.NotNull(rot["w"]);
    }

    [Fact]
    public void GeoToLocal_WithoutHeadingDeg_NoRotationField()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.GeoToLocal(52.52, 13.405, 0.0, headingDeg: null).AsObject();
        Assert.False(result.ContainsKey("rotation"), "rotation key should not be present when headingDeg is null");
    }

    [Fact]
    public void LocalToGeo_AtOriginLocalCoords_ReturnsBerlinApprox()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.LocalToGeo(0f, 0f, 0f, rotation: null).AsObject();
        double lat = result["lat"]!.GetValue<double>();
        double lon = result["lon"]!.GetValue<double>();
        Assert.Equal(52.52, lat, precision: 2);
        Assert.Equal(13.405, lon, precision: 3);
    }

    [Fact]
    public void RoundTrip_GeoToLocal_ThenLocalToGeo_RecoverOriginalCoords()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        double srcLat = 52.53;
        double srcLon = 13.42;
        double srcAlt = 50.0;

        // Forward: geo → local
        var localResult = svc.GeoToLocal(srcLat, srcLon, srcAlt, headingDeg: null).AsObject();
        float x = localResult["x"]!.GetValue<float>();
        float y = localResult["y"]!.GetValue<float>();
        float z = localResult["z"]!.GetValue<float>();

        // Inverse: local → geo
        var geoResult = svc.LocalToGeo(x, y, z, rotation: null).AsObject();
        double recoveredLat = geoResult["lat"]!.GetValue<double>();
        double recoveredLon = geoResult["lon"]!.GetValue<double>();
        double recoveredAlt = geoResult["alt"]!.GetValue<double>();

        // Allow 1m / ~0.00001 deg tolerance
        Assert.Equal(srcLat, recoveredLat, precision: 4);
        Assert.Equal(srcLon, recoveredLon, precision: 4);
        Assert.Equal(srcAlt, recoveredAlt, precision: 0);
    }

    [Fact]
    public void RoundTrip_Heading90_RoundTripsApprox()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        float srcHeading = 90f;

        // Convert heading to rotation
        var geoResult = svc.GeoToLocal(52.52, 13.405, 0.0, headingDeg: srcHeading).AsObject();
        var rotObj = geoResult["rotation"]!.AsObject();
        float rx = rotObj["x"]!.GetValue<float>();
        float ry = rotObj["y"]!.GetValue<float>();
        float rz = rotObj["z"]!.GetValue<float>();
        float rw = rotObj["w"]!.GetValue<float>();

        // Convert rotation back to heading
        var localResult = svc.LocalToGeo(0f, 0f, 0f,
            rotation: new System.Numerics.Quaternion(rx, ry, rz, rw)).AsObject();
        float recoveredHeading = localResult["headingDeg"]!.GetValue<float>();

        Assert.Equal(srcHeading, recoveredHeading, precision: 1);
    }
}
