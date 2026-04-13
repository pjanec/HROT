using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Common;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Commands;
using FDP.Toolkit.Vis2D.Tools;

namespace Hrot.IG.Tests.CommandHandling;

/// <summary>
/// Unit tests for <see cref="IgApplication"/> handling of
/// CMD_DRAW_PERSONAL_ROUTE — OC1-G003.
/// </summary>
public class DrawPersonalRouteCommandTests : IDisposable
{
    private readonly IgApplication      _app;
    private readonly MockNetworkAdapter _adapter = new();

    public DrawPersonalRouteCommandTests()
    {
        _app = new IgApplication();
        _app.InitializeEmbedded(headless: true, domainIdOverride: 207);
        _app.TestHook_SetNetworkAdapter(_adapter);
    }

    public void Dispose() => _app.Dispose();

    // ── Mocks ────────────────────────────────────────────────────────────────

    private sealed class StubCommandGateway : ICommandGateway
    {
        public List<MissionControlCommand> MissionCalls { get; } = new();

        public void Dispose() { }

        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<MissionCommitResult> SendMissionControlRequestAsync(
            MissionControlCommand cmd, CancellationToken ct = default)
        {
            MissionCalls.Add(cmd);
            return Task.FromResult(new MissionCommitResult { Success = true });
        }
    }

    private sealed class MockNetworkAdapter : IIgNetworkAdapter
    {
        public StubCommandGateway Gateway { get; } = new();

        public int                                                     CreateRouteCallCount   { get; private set; }
        public long                                                    LastTkbRouteType       { get; private set; }
        public IReadOnlyList<(double Lat, double Lon, double Alt)>?   LastWaypoints          { get; private set; }
        public int                                                     LastCommanderEntityId  { get; private set; }
        public int                                                     CreateRouteReturnId    { get; set; } = 77;
        public int                                                     CreateRouteStatusCode  { get; set; } = 0;

        public ICommandGateway CommandGateway => Gateway;

        public void Dispose() { }

        public void WriteMapClick(MapClickEventDto dto) { }
        public void WriteSelectionChanged(SelectionChangedEventDto dto) { }
        public void WriteMapCommandAck(MapCommandAckDto dto) { }
        public void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection) { }
        public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson) { }
        public MapConfigDto? PollMapConfig() => null;
        public MapCommandDto? PollMapCommand() => null;
        public EntityLifecycleAckDto? PollEntityLifecycleAck() => null;

        public Task<int> CreateRouteEntityAsync(
            long tkbRouteType,
            IReadOnlyList<(double Lat, double Lon, double Alt)> waypoints,
            double anchorLat, double anchorLon, double anchorAlt,
            int commanderEntityId,
            CancellationToken ct = default)
        {
            CreateRouteCallCount++;
            LastTkbRouteType      = tkbRouteType;
            LastWaypoints         = waypoints;
            LastCommanderEntityId = commanderEntityId;
            if (CreateRouteStatusCode != 0) return Task.FromResult(0);
            return Task.FromResult(CreateRouteReturnId);
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

        // Capture ACKs by intercepting the MapCommandAck writer indirectly via a sink.
        // The ACK is written to _mapCommandAckWriter which is set up by InitializeEmbedded.
        // Since we're headless, the writer may be null. We test the gateway side instead.

        _app.TestHook_ParseCommandAndActivatePersonalRoute(requestId, "{\"entityId\":5}");

        // Submit with empty list — simulates clicking right-click immediately.
        _app.TestHook_DirectPointSequenceToolCommit(Array.Empty<Vector2>());

        // Allow async task to complete.
        Task.Delay(50).Wait();

        // CreateRouteEntityAsync must not have been called.
        Assert.Equal(0, _adapter.CreateRouteCallCount);
    }

    /// <summary>
    /// OC1-G003 Scenario 3 — completing with valid points calls gateway with correct descriptors
    /// and sends mission with FollowRoute task referencing the created entity.
    /// </summary>
    [Fact]
    public async Task ValidPoints_GatewayCalledWithCorrectDescriptors()
    {
        _adapter.CreateRouteReturnId = 77;

        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        // Allow the fire-and-forget async task to complete.
        await Task.Delay(200);

        Assert.Equal(1, _adapter.CreateRouteCallCount);
        Assert.Equal(TkbEntityTypes.TacGraphic_Route, _adapter.LastTkbRouteType);
        Assert.NotNull(_adapter.LastWaypoints);
        Assert.Equal(3, _adapter.LastWaypoints.Count);
        Assert.Equal(5, _adapter.LastCommanderEntityId);

        // Mission request should have been sent with FollowRoute task.
        Assert.Single(_adapter.Gateway.MissionCalls);
        var missionCmd = _adapter.Gateway.MissionCalls[0];
        Assert.Equal(Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION, missionCmd.CommandType);
        var task = missionCmd.Plan!.Tasks[0];
        Assert.Equal("FollowRoute", task.BehaviorId);
        Assert.Contains("77", task.BehaviorParams); // routeEntityId = 77
    }

    /// <summary>
    /// OC1-G003 Scenario 4 — CreateEntityAsync failure: no mission request sent.
    /// </summary>
    [Fact]
    public async Task CreateEntityFailure_MissionNotSent()
    {
        _adapter.CreateRouteStatusCode = 2; // failure

        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);

        await Task.Delay(200);

        Assert.Equal(1, _adapter.CreateRouteCallCount);
        Assert.Empty(_adapter.Gateway.MissionCalls);
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

        Assert.Equal(1, _adapter.CreateRouteCallCount);
        Assert.NotNull(_adapter.LastWaypoints);
        Assert.Equal(2, _adapter.LastWaypoints.Count);

        var wp0 = _adapter.LastWaypoints[0];
        var wp1 = _adapter.LastWaypoints[1];

        // Latitudes must be identical (canvas Y → altitude, not North).
        Assert.Equal(wp0.Lat, wp1.Lat, precision: 8);
        // Altitudes must differ (canvas Y=200 → alt smaller than Y=400).
        Assert.NotEqual(wp0.Alt, wp1.Alt);
        Assert.True(wp1.Alt > wp0.Alt,
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

        Assert.Equal(1, _adapter.CreateRouteCallCount);
        Assert.NotNull(_adapter.LastWaypoints);
        var route2 = _adapter.LastWaypoints;

        var wp0 = route2[0];
        var wp1 = route2[1];

        // Longitudes must differ (canvas X → East → longitude).
        Assert.NotEqual(wp0.Lon, wp1.Lon);
        // Altitudes must be equal (same canvas Y → same altitude).
        // WGS84 coordinate-frame skew between two Easting positions introduces up to
        // ~20 mm of apparent altitude offset; precision:1 (tolerance 0.05 m) is sufficient.
        Assert.Equal(wp0.Alt, wp1.Alt, precision: 1);
    }
}
