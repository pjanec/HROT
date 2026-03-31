using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests for the area (tactical-shape) authoring tool (OC1-B003).
///
/// Verifies that <c>ParseCommandAndActivateAreaTool</c> activates a
/// <see cref="PointSequenceTool"/> when <c>tkbType != TacGraphic_Route</c> (i.e., area shapes),
/// and that finishing the tool emits a well-formed <see cref="CreateEntityRequest"/>
/// with correct descriptor types and relative-geo offsets.
/// </summary>
public class AreaAuthoringTests : System.IDisposable
{
    private readonly IgApplication _app;
    private readonly List<CreateEntityRequest> _captured = new();

    // Use a TkbType that triggers the area authoring path (not route = 8802).
    private const long AreaTkbType = 8803L; // TacGraphic_Area

    public AreaAuthoringTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 231);
        _app.TestHook_SetCreateEntityRequestSink(req => _captured.Add(req));
    }

    public void Dispose()
    {
        _app.TestHook_SetCreateEntityRequestSink(null);
        _app.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<Vector2> ThreePoints = new[]
    {
        new Vector2(100f, 200f),
        new Vector2(300f, 400f),
        new Vector2(500f, 600f),
    };

    private CreateEntityRequest CaptureAreaRequest(IReadOnlyList<Vector2> points)
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");
        _app.TestHook_DirectPointSequenceToolCommit(points);
        Assert.Single(_captured);
        return _captured[0];
    }

    // ── Tool activation ───────────────────────────────────────────────────────

    /// <summary>
    /// OC1-B003: A ParseCommand call with area TkbType must push a PointSequenceTool.
    /// </summary>
    [Fact]
    public void ParseCommand_WithAreaTkbType_PushesPointSequenceTool()
    {
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");

        Assert.True(_app.TestHook_IsPointSequenceToolActive);
    }

    // ── Request shape ─────────────────────────────────────────────────────────

    /// <summary>
    /// OC1-B003: Finishing the area tool with 3 points must emit exactly one
    /// <see cref="CreateEntityRequest"/> via the test sink.
    /// This verifies the <c>_testCreateEntityRequestSink</c> path was added to
    /// <c>ActivateAreaAuthoringTool</c>.
    /// </summary>
    [Fact]
    public void AreaTool_3Points_EmitsExactlyOneRequest()
    {
        _ = CaptureAreaRequest(ThreePoints);

        Assert.Single(_captured);
    }

    /// <summary>
    /// The emitted request must carry <c>dtEntityMaster</c>, <c>dtGeoSpatial</c>, and
    /// <c>dtMapVisualOverlay</c> descriptors — exactly 3 total.
    /// </summary>
    [Fact]
    public void AreaRequest_HasThreeDescriptors()
    {
        var req = CaptureAreaRequest(ThreePoints);

        Assert.Equal(3, req.InitialDescriptors.Count);
    }

    /// <summary>
    /// The <c>dtEntityMaster</c> descriptor must advertise the area TKB type (not route).
    /// </summary>
    [Fact]
    public void AreaRequest_EntityMaster_TkbType_IsArea()
    {
        var req = CaptureAreaRequest(ThreePoints);

        var master = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(AreaTkbType, master.EntityMaster.TkbType);
    }

    /// <summary>
    /// OC1-B003 — coordinate contract:
    /// The <c>dtGeoSpatial</c> centroid must be the arithmetic mean of all vertex
    /// absolute positions. This means the sum of all relative-geo offsets must be zero
    /// (the offsets are centered on the centroid). This verifies the "relative offset"
    /// contract is maintained regardless of the underlying geo-transform.
    /// </summary>
    [Fact]
    public void AreaRequest_Overlay_PointsAreRelativeOffsets_FromCentroid()
    {
        var req = CaptureAreaRequest(ThreePoints);

        var overlay = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtMapVisualOverlay);

        Assert.NotNull(overlay.MapVisualOverlay.Points);
        Assert.Equal(ThreePoints.Count, overlay.MapVisualOverlay.Points!.Count);

        // The mean of all relative latitude offsets must be 0 (centroid is the mean).
        double sumLat = 0, sumLon = 0;
        foreach (var pt in overlay.MapVisualOverlay.Points)
        {
            sumLat += pt.Latitude;
            sumLon += pt.Longitude;
        }
        double meanLatOffset = sumLat / overlay.MapVisualOverlay.Points.Count;
        double meanLonOffset = sumLon / overlay.MapVisualOverlay.Points.Count;

        Assert.Equal(0.0, meanLatOffset, precision: 6);
        Assert.Equal(0.0, meanLonOffset, precision: 6);

        // At least one point must be non-zero offset (shape is not degenerate).
        bool anyNonZero = overlay.MapVisualOverlay.Points.Any(
            pt => Math.Abs(pt.Latitude) > 1e-9 || Math.Abs(pt.Longitude) > 1e-9);
        Assert.True(anyNonZero, "All overlay points are at centroid — polygon is degenerate.");
    }

    /// <summary>
    /// After commit, the PointSequenceTool must be popped from the canvas.
    /// </summary>
    [Fact]
    public void AreaTool_AfterCommit_ToolIsPopped()
    {
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        Assert.False(_app.TestHook_IsPointSequenceToolActive);
    }

    /// <summary>
    /// Committing with fewer than 3 points (below the area-minimum) must NOT emit a request.
    /// </summary>
    [Fact]
    public void AreaTool_2Points_DoesNotEmitRequest()
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(
            Guid.NewGuid(),
            $"{{\"contextId\":\"{Guid.NewGuid():N}\",\"tkbType\":{AreaTkbType}}}");
        _app.TestHook_DirectPointSequenceToolCommit(
            new[] { new Vector2(10f, 20f), new Vector2(30f, 40f) });

        Assert.Empty(_captured);
    }

    // ── Coordinate-fix regression tests ──────────────────────────────────────

    /// <summary>
    /// After the area-authoring coordinate fix, canvas Y should be encoded as geodetic
    /// latitude/North (the "overlay XY-plane convention"), NOT as altitude.
    ///
    /// Three points that form a vertical strip (same X, different Y) must produce
    /// overlay vertices with differing latitudes and near-zero altitudes.
    ///
    /// This test requires a geo-transform to be wired; IgApplication always creates
    /// one unconditionally (Berlin origin), so these assertions are valid headlessly.
    /// </summary>
    [Fact]
    public void AreaTool_CanvasYEncodedAsLatitude_NotAltitude()
    {
        // Three points with identical X but different Y (vertical strip).
        var sameXDifferentY = new[]
        {
            new Vector2(0f, 100f),
            new Vector2(0f, 500f),
            new Vector2(0f, 800f),
        };

        var req = CaptureAreaRequest(sameXDifferentY);
        var overlay = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtMapVisualOverlay);
        Assert.NotNull(overlay.MapVisualOverlay.Points);

        // Because points have the same canvas X and different canvas Y, and the origin is
        // the map centroid, at least some relative-latitude offsets should be non-zero
        // (different canvas Y → different ENU-North → different latitudes).
        bool anyNonZeroLat = overlay.MapVisualOverlay.Points!.Any(p => Math.Abs(p.Latitude) > 1e-9);
        Assert.True(anyNonZeroLat,
            "All relative latitudes are zero — canvas Y is NOT being encoded as North/latitude "
            + "(coordinate fix regression: should use (X,Y,0), not (X,0,Y)).");

        // All altitudes should be near zero because canvas Y → North, not Up.
        // Allow up to 100 mm of geodetic curvature error (WGS84 ellipsoid bends ~50 mm
        // over an 800 m North arc, so relative values stay well within 0.1 m).
        bool allZeroAlt = overlay.MapVisualOverlay.Points!.All(p => Math.Abs(p.Altitude) < 0.1);
        Assert.True(allZeroAlt,
            "Relative altitudes are non-zero — canvas Y is being incorrectly encoded as altitude.");
    }

    /// <summary>
    /// Three co-horizontal points (same Y, different X) must produce relative offsets with
    /// near-zero latitude differences (all on the same east-west line) and varied longitudes.
    /// </summary>
    [Fact]
    public void AreaTool_CanvasXEncodesLongitude_CanvasYEncodesLatitude()
    {
        var sameYDifferentX = new[]
        {
            new Vector2(100f, 0f),
            new Vector2(400f, 0f),
            new Vector2(700f, 0f),
        };

        var req = CaptureAreaRequest(sameYDifferentX);
        var overlay = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtMapVisualOverlay);

        // All have same canvas Y → all relative latitudes should be (near) zero.
        // Allow 1e-6 ° tolerance: WGS84 second-order curvature over 700 m East
        // introduces ~1e-7 ° latitude coupling, well within this margin.
        bool allZeroLat = overlay.MapVisualOverlay.Points!.All(p => Math.Abs(p.Latitude) < 1e-6);
        Assert.True(allZeroLat,
            "Same-Y canvas points produced differing latitudes — unexpected.");

        // Different X → relative longitudes should differ.
        bool anyNonZeroLon = overlay.MapVisualOverlay.Points!.Any(p => Math.Abs(p.Longitude) > 1e-9);
        Assert.True(anyNonZeroLon,
            "Different-X canvas points produced identical longitudes — canvas X not encoding longitude.");
    }
}
