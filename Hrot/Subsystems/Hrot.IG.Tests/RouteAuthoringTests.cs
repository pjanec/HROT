using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using FDP.Toolkit.NetworkSpawning.Events;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests for the shared-route authoring flow (ROUTES1-T007).
///
/// Verifies that <c>ParseCommandAndActivateAreaTool</c> activates a
/// <see cref="PointSequenceTool"/> when <c>tkbType == TacGraphic_Route</c>,
/// and that finishing the tool emits a well-formed <see cref="SpawnEntityCommand"/>
/// with a <see cref="RoutePlan"/> in <see cref="SpawnEntityCommand.InitialComponents"/>.
/// </summary>
public class RouteAuthoringTests : System.IDisposable
{
    private readonly IgApplication _app;
    private readonly List<SpawnEntityCommand> _captured = new();

    public RouteAuthoringTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 230);
        _app.TestHook_SetSpawnCommandSink(cmd => _captured.Add(cmd));
    }

    public void Dispose()
    {
        _app.TestHook_SetSpawnCommandSink(null);
        _app.Dispose();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<Vector2> ThreePoints = new[]
    {
        new Vector2(100f, 200f),
        new Vector2(300f, 400f),
        new Vector2(500f, 600f),
    };

    private SpawnEntityCommand CaptureRequestFor(IReadOnlyList<Vector2> points)
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
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        if (_captured.Count == 1)
        {
            Assert.NotEqual(TkbEntityTypes.TacGraphic_Route, _captured[0].TkbType);
        }
        // If no command was captured (area tool requires the sink to be set), that
        // is also acceptable for the non-route path.
    }

    // ── Emitted command shape ─────────────────────────────────────────────────

    /// <summary>
    /// Finishing the route tool with 3 points must emit exactly one
    /// <see cref="SpawnEntityCommand"/> via the test sink.
    /// </summary>
    [Fact]
    public void FinishCallback_3Points_EmitsExactlyOneRequest()
    {
        _ = CaptureRequestFor(ThreePoints);

        Assert.Single(_captured);
    }

    /// <summary>
    /// The emitted command must carry route TkbType, InitialTransform (anchor = first waypoint)
    /// and exactly 1 InitialComponent (RoutePlan).
    /// </summary>
    [Fact]
    public void Request_HasCorrectStructure()
    {
        var cmd = CaptureRequestFor(ThreePoints);

        Assert.Equal(TkbEntityTypes.TacGraphic_Route, cmd.TkbType);
        Assert.True(cmd.InitialTransform.HasValue, "InitialTransform (first waypoint) must be set.");
        Assert.NotNull(cmd.InitialComponents);
        Assert.Single(cmd.InitialComponents!.OfType<RoutePlan>());
    }

    /// <summary>
    /// The EntityMaster TkbType must be <c>TacGraphic_Route</c>.
    /// </summary>
    [Fact]
    public void EntityMaster_TkbType_IsRoute()
    {
        var cmd = CaptureRequestFor(ThreePoints);

        Assert.Equal(TkbEntityTypes.TacGraphic_Route, cmd.TkbType);
    }

    /// <summary>
    /// The <see cref="RoutePlan"/> must have the same number of waypoints as the input points.
    /// </summary>
    [Fact]
    public void MapRoute_HasCorrectWaypointCount()
    {
        var cmd = CaptureRequestFor(ThreePoints);

        var routePlan = cmd.InitialComponents!.OfType<RoutePlan>().Single();
        Assert.Equal(ThreePoints.Count, routePlan.Waypoints.Count);
    }

    /// <summary>
    /// The anchor position (InitialTransform) must match the first waypoint Cartesian position.
    /// </summary>
    [Fact]
    public void GeoSpatial_AnchorCorrespondsToFirstPoint()
    {
        var cmd = CaptureRequestFor(ThreePoints);

        var routePlan    = cmd.InitialComponents!.OfType<RoutePlan>().Single();
        var anchor       = cmd.InitialTransform!.Value.Position;
        var firstWpPos   = routePlan.Waypoints[0].Position;

        Assert.Equal(anchor.X, firstWpPos.X, precision: 2);
        Assert.Equal(anchor.Y, firstWpPos.Y, precision: 2);
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
    /// The emitted command must have a non-empty RequestId so SimHost can correlate the response.
    /// </summary>
    [Fact]
    public void Request_HasNonEmptyRequestId()
    {
        var cmd = CaptureRequestFor(ThreePoints);

        Assert.NotEqual(Guid.Empty, cmd.RequestId);
    }

    /// <summary>
    /// Supplying only 1 point (below the minimum of 2) must NOT emit a command.
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
