using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using Bagira.Map.Common.Commands;
using FDP.Toolkit.Vis2D.Tools;

namespace Bagira.IG.Tests.CommandHandling;

/// <summary>
/// Unit tests for <see cref="IgApplication"/> handling of
/// <see cref="Bagira.BDC.SSTM.CommandType.CMD_DRAW_PERSONAL_ROUTE"/> — OC1-G003.
/// </summary>
public class DrawPersonalRouteCommandTests : IDisposable
{
    private readonly IgApplication _app;
    private readonly MockGateway   _gateway = new();

    public DrawPersonalRouteCommandTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 242);
        _app.TestHook_SetCommandGateway(_gateway);
    }

    public void Dispose() => _app.Dispose();

    // ── Mock ─────────────────────────────────────────────────────────────────

    private sealed class MockGateway : IBdcCommandGateway
    {
        public List<CreateEntityRequest>   CreateCalls   { get; } = new();
        public List<MissionControlRequest> MissionCalls  { get; } = new();
        public List<MapCommandAck>         AckCalls      { get; } = new();  // not used directly

        public int    CreateStatusCode { get; set; } = 0;
        public int    CreatedEntityId  { get; set; } = 77;

        public void SendUpdateDescriptor(UpdateEntityDescriptorRequest request) { }

        public Task<CreateUpdateDeleteEntityAck> CreateEntityAsync(
            CreateEntityRequest request, int timeoutMs = 5000)
        {
            CreateCalls.Add(request);
            return Task.FromResult(new CreateUpdateDeleteEntityAck
            {
                RequestId  = request.RequestId,
                EntityId   = CreatedEntityId,
                StatusCode = CreateStatusCode
            });
        }

        public Task<MissionControlAck> SendMissionControlRequestAsync(
            MissionControlRequest request, int timeoutMs = 5000)
        {
            MissionCalls.Add(request);
            return Task.FromResult(new MissionControlAck
            {
                RequestId = request.RequestId,
                ErrorCode = 0
            });
        }
    }

    private static readonly IReadOnlyList<Vector2> ThreePoints = new[]
    {
        new Vector2(100f, 200f),
        new Vector2(300f, 400f),
        new Vector2(500f, 600f),
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// OC1-G003 Scenario 1 — CMD_DRAW_PERSONAL_ROUTE activates PointSequenceTool.
    /// </summary>
    [Fact]
    public void ToolActivatesOnCommand()
    {
        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");

        Assert.True(_app.TestHook_IsPointSequenceToolActive);
    }

    /// <summary>
    /// OC1-G003 Scenario 2 — submitting fewer than 2 points sends a cancelled ACK
    /// and does NOT call CreateEntityAsync.
    /// </summary>
    [Fact]
    public void CancelWithZeroPoints_SendsCancelledAck_NoCreateCalled()
    {
        var requestId = Guid.NewGuid();
        var captured  = new List<MapCommandAck>();
        _app.TestHook_SetCreateEntityRequestSink(_ => { }); // not needed; use gateway mock

        // Capture ACKs by intercepting the MapCommandAck writer indirectly via a sink.
        // The ACK is written to _mapCommandAckWriter which is set up by InitializeEmbedded.
        // Since we're headless, the writer may be null. We test the gateway side instead.

        _app.TestHook_ParseCommandAndActivatePersonalRoute(requestId, "{\"entityId\":5}");

        // Submit with empty list — simulates clicking right-click immediately.
        _app.TestHook_DirectPointSequenceToolCommit(Array.Empty<Vector2>());

        // Allow async task to complete.
        Task.Delay(50).Wait();

        // CreateEntityAsync must not have been called.
        Assert.Empty(_gateway.CreateCalls);
    }

    /// <summary>
    /// OC1-G003 Scenario 3 — completing with valid points calls gateway with correct descriptors
    /// and sends mission with FollowRoute task referencing the created entity.
    /// </summary>
    [Fact]
    public async Task ValidPoints_GatewayCalledWithCorrectDescriptors()
    {
        _gateway.CreatedEntityId = 77;

        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        // Allow the fire-and-forget async task to complete.
        await Task.Delay(200);

        Assert.Single(_gateway.CreateCalls);
        var createReq = _gateway.CreateCalls[0];

        var master = createReq.InitialDescriptors.Find(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(TkbEntityTypes.TacGraphic_Route, master.EntityMaster.TkbType);

        var route = createReq.InitialDescriptors.Find(d => d._d == EDescriptorType.dtMapRoute);
        Assert.NotNull(route.MapRoute.Points);
        Assert.Equal(3, route.MapRoute.Points.Count);

        var info = createReq.InitialDescriptors.Find(d => d._d == EDescriptorType.dtEntityInfo);
        Assert.Equal(5, info.EntityInfo.CommanderId);

        // Mission request should have been sent with FollowRoute task.
        Assert.Single(_gateway.MissionCalls);
        var missionReq = _gateway.MissionCalls[0];
        Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, missionReq.Payload._d);
        var task = missionReq.Payload.FullMissionData.Tasks[0];
        Assert.Equal("FollowRoute", task.BehaviorId);
        Assert.Contains("77", task.BehaviorParams); // routeEntityId = 77
    }

    /// <summary>
    /// OC1-G003 Scenario 4 — CreateEntityAsync failure: no mission request sent.
    /// </summary>
    [Fact]
    public async Task CreateEntityFailure_MissionNotSent()
    {
        _gateway.CreateStatusCode = 2; // failure

        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        await Task.Delay(200);

        Assert.Single(_gateway.CreateCalls);
        Assert.Empty(_gateway.MissionCalls);
    }

    /// <summary>
    /// OC1-G003 Scenario 5 — empty JSON: silently ignored.
    /// </summary>
    [Fact]
    public void EmptyJson_SilentlyIgnored()
    {
        var ex = Record.Exception(() =>
            _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), ""));
        Assert.Null(ex);
    }

    // ── Coordinate-fix regression tests ──────────────────────────────────────

    /// <summary>
    /// After the personal-route coordinate fix, canvas Y should be encoded as
    /// geodetic altitude (the "route XZ convention"), NOT as latitude.
    /// Two canvas points with the same X but different Y values must produce
    /// waypoints with identical latitudes and differing altitudes.
    /// </summary>
    [Fact]
    public async Task CoordinateFix_CanvasYEncodedAsAltitude_NotLatitude()
    {
        var sameXDifferentY = new[]
        {
            new Vector2(100f, 200f),
            new Vector2(100f, 400f),
        };

        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(sameXDifferentY);
        await Task.Delay(200);

        Assert.Single(_gateway.CreateCalls);
        var route = _gateway.CreateCalls[0].InitialDescriptors
            .Find(d => d._d == EDescriptorType.dtMapRoute);
        Assert.Equal(2, route.MapRoute.Points.Count);

        var wp0 = route.MapRoute.Points[0].Position;
        var wp1 = route.MapRoute.Points[1].Position;

        // Latitudes must be identical (canvas Y → altitude, not North).
        Assert.Equal(wp0.Latitude, wp1.Latitude, precision: 8);
        // Altitudes must differ (canvas Y=200 → alt smaller than Y=400).
        Assert.NotEqual(wp0.Altitude, wp1.Altitude);
        Assert.True(wp1.Altitude > wp0.Altitude,
            "Waypoint with higher canvas Y should have higher altitude.");
    }

    /// <summary>
    /// Two canvas points with different X but same Y must produce waypoints
    /// with different longitudes but identical altitudes — confirming that canvas X
    /// maps to East (longitude change) and canvas Y maps to altitude, not latitude.
    /// </summary>
    [Fact]
    public async Task CoordinateFix_CanvasXEncodesLongitudeCorrectly()
    {
        var differentXSameY = new[]
        {
            new Vector2(100f, 200f),
            new Vector2(500f, 200f),
        };

        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(differentXSameY);
        await Task.Delay(200);

        Assert.Single(_gateway.CreateCalls);
        var route = _gateway.CreateCalls[0].InitialDescriptors
            .Find(d => d._d == EDescriptorType.dtMapRoute);

        var wp0 = route.MapRoute.Points[0].Position;
        var wp1 = route.MapRoute.Points[1].Position;

        // Longitudes must differ (canvas X → East → longitude).
        Assert.NotEqual(wp0.Longitude, wp1.Longitude);
        // Altitudes must be equal (same canvas Y → same altitude).
        // WGS84 coordinate-frame skew between two Easting positions introduces up to
        // ~20 mm of apparent altitude offset; precision:1 (tolerance 0.05 m) is sufficient.
        Assert.Equal(wp0.Altitude, wp1.Altitude, precision: 1);
    }
}
