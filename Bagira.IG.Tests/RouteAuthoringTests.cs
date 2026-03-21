using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using Xunit;

namespace Bagira.IG.Tests;

/// <summary>
/// Integration tests for the shared-route authoring flow (ROUTES1-T007).
///
/// Verifies that <c>ParseCommandAndActivateAreaTool</c> activates a
/// <see cref="PointSequenceTool"/> when <c>tkbType == TacGraphic_Route</c>,
/// and that finishing the tool emits a well-formed <see cref="CreateEntityRequest"/>
/// containing the three required descriptors.
/// </summary>
public class RouteAuthoringTests : System.IDisposable
{
    private readonly IgApplication _app;
    private readonly List<CreateEntityRequest> _captured = new();

    public RouteAuthoringTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 230);
        _app.TestHook_SetCreateEntityRequestSink(req => _captured.Add(req));
    }

    public void Dispose()
    {
        _app.TestHook_SetCreateEntityRequestSink(null);
        _app.Dispose();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<Vector2> ThreePoints = new[]
    {
        new Vector2(100f, 200f),
        new Vector2(300f, 400f),
        new Vector2(500f, 600f),
    };

    private CreateEntityRequest CaptureRequestFor(IReadOnlyList<Vector2> points)
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(Guid.NewGuid(), "{\"tkbType\":8802}");
        _app.TestHook_DirectPointSequenceToolCommit(points);
        Assert.Single(_captured);
        return _captured[0];
    }

    // ── Tool activation ───────────────────────────────────────────────────────

    /// <summary>
    /// A ParseCommand call with <c>tkbType == TacGraphic_Route</c> must push a
    /// PointSequenceTool, making <see cref="IgApplication.TestHook_IsPointSequenceToolActive"/> true.
    /// </summary>
    [Fact]
    public void ParseCommand_WithRouteTkbType_PushesPointSequenceTool()
    {
        _app.TestHook_ParseCommandAndActivateAreaTool(Guid.NewGuid(), "{\"tkbType\":8802}");

        Assert.True(_app.TestHook_IsPointSequenceToolActive);
    }

    /// <summary>
    /// A ParseCommand call with a non-route TkbType (e.g. area) must NOT push
    /// the route PointSequenceTool path — the area authoring path activates instead.
    /// </summary>
    [Fact]
    public void ParseCommand_WithNonRouteTkbType_DoesNotUseRouteToolPath()
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(Guid.NewGuid(), "{\"tkbType\":8803}");
        // No route tool is expected; do not assert IsPointSequenceToolActive since
        // area authoring also uses PointSequenceTool — distinguish by the emitted request content.
        // Finish with 3 points and check the EntityMaster descriptor type.
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        if (_captured.Count == 1)
        {
            var master = _captured[0].InitialDescriptors
                .Find(d => d._d == EDescriptorType.dtEntityMaster);
            Assert.NotEqual(TkbEntityTypes.TacGraphic_Route, master.EntityMaster.TkbType);
        }
        // If no request was captured (area tool requires DDS and returns early), that
        // is also acceptable for the non-route path — the key constraint is that
        // the route-specific code was NOT reached.
    }

    // ── Emitted request shape ─────────────────────────────────────────────────

    /// <summary>
    /// Finishing the route tool with 3 points must emit exactly one
    /// <see cref="CreateEntityRequest"/> via the test sink.
    /// </summary>
    [Fact]
    public void FinishCallback_3Points_EmitsExactlyOneRequest()
    {
        _ = CaptureRequestFor(ThreePoints);

        Assert.Single(_captured);
    }

    /// <summary>
    /// The emitted request must carry exactly 3 initial descriptors:
    /// EntityMaster, GeoSpatial, and MapRoute.
    /// </summary>
    [Fact]
    public void Request_HasExactlyThreeDescriptors()
    {
        var req = CaptureRequestFor(ThreePoints);

        Assert.Equal(3, req.InitialDescriptors.Count);
    }

    /// <summary>
    /// The EntityMaster descriptor must advertise <c>TkbType == TacGraphic_Route</c>.
    /// </summary>
    [Fact]
    public void EntityMaster_TkbType_IsRoute()
    {
        var req = CaptureRequestFor(ThreePoints);

        var master = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(TkbEntityTypes.TacGraphic_Route, master.EntityMaster.TkbType);
    }

    /// <summary>
    /// The MapRoute descriptor must have the same number of waypoints as the input
    /// points (one waypoint per canvas point, not filtered).
    /// </summary>
    [Fact]
    public void MapRoute_HasCorrectWaypointCount()
    {
        var req = CaptureRequestFor(ThreePoints);

        var mapRoute = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtMapRoute);
        Assert.Equal(ThreePoints.Count, mapRoute.MapRoute.Points.Count);
    }

    /// <summary>
    /// The GeoSpatial descriptor's anchor position must equal the first waypoint's position
    /// in the MapRoute descriptor — both derive from the same first input point.
    /// </summary>
    [Fact]
    public void GeoSpatial_AnchorCorrespondsToFirstPoint()
    {
        var req = CaptureRequestFor(ThreePoints);

        var geoSpatial = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtGeoSpatial);
        var mapRoute   = req.InitialDescriptors.Find(d => d._d == EDescriptorType.dtMapRoute);

        var anchor        = geoSpatial.GeoSpatial.Pos;
        var firstWaypoint = mapRoute.MapRoute.Points[0].Position;

        Assert.Equal(anchor.Latitude,  firstWaypoint.Latitude,  precision: 6);
        Assert.Equal(anchor.Longitude, firstWaypoint.Longitude, precision: 6);
    }

    /// <summary>
    /// After the tool finishes (callback fires), it must pop itself so that
    /// <see cref="IgApplication.TestHook_IsPointSequenceToolActive"/> returns false.
    /// </summary>
    [Fact]
    public void AfterFinish_PointSequenceToolIsNoLongerActive()
    {
        _app.TestHook_ParseCommandAndActivateAreaTool(Guid.NewGuid(), "{\"tkbType\":8802}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        Assert.False(_app.TestHook_IsPointSequenceToolActive);
    }

    /// <summary>
    /// The emitted request must have a non-empty <see cref="CreateEntityRequest.RequestId"/>
    /// so the SimHost can correlate the response.
    /// </summary>
    [Fact]
    public void Request_HasNonEmptyRequestId()
    {
        var req = CaptureRequestFor(ThreePoints);

        Assert.NotEqual(Guid.Empty, req.RequestId);
    }

    /// <summary>
    /// Supplying only 1 point (below the minimum of 2) must NOT emit a request —
    /// the callback guard must reject the insufficient input silently.
    /// </summary>
    [Fact]
    public void FinishCallback_1Point_DoesNotEmitRequest()
    {
        _captured.Clear();
        _app.TestHook_ParseCommandAndActivateAreaTool(Guid.NewGuid(), "{\"tkbType\":8802}");
        _app.TestHook_DirectPointSequenceToolCommit(new[] { new Vector2(10f, 20f) });

        Assert.Empty(_captured);
    }
}
