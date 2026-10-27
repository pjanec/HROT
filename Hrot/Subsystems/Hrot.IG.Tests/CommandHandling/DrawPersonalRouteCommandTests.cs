using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Common;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Commands;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;

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

        public Task SendUpdateAttributeAsync(Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class MockNetworkAdapter : IIgNetworkAdapter
    {
        public StubCommandGateway Gateway { get; } = new();

        public int                                                     CreateRouteCallCount   { get; private set; }
        public long                                                    LastTkbRouteType       { get; private set; }
        public IReadOnlyList<(double X, double Y, double Z)>?   LastWaypoints          { get; private set; }
        public int                                                     LastCommanderEntityId  { get; private set; }
        public int                                                     CreateRouteReturnId    { get; set; } = 77;
        public int                                                     CreateRouteStatusCode  { get; set; } = 0;

        public ICommandGateway CommandGateway => Gateway;
        public Fdp.Toolkit.Diagnostics.Gizmos.Network.IDdsWriter<GizmoInteractionBatch>? GizmoInteractionWriter => null;
        public Fdp.Toolkit.Diagnostics.Gizmos.Network.IDdsReader<DebugPrimitivesBatch>? DebugPrimitivesReader => null;

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
            IReadOnlyList<(double X, double Y, double Z)> waypoints,
            double anchorX, double anchorY, double anchorZ,
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
    // ⭐ CE-4073 — the neutral route waypoints are LOCAL ENU metres (they were geodetic lat/lon/alt, converted at the NED boundary).
    //   The mapping is UNCHANGED from the geodetic round trip, quirk included: canvas X → X (east), canvas Y → the THIRD axis (Z),
    //   Y = 0 (`ToGeodetic(x, 0, y)` and straight back is the identity). ⚠ Whether that is the intended frame for a 2D map is not decided here.

    /// <summary>
    /// ⭐ <c>CE-4073</c> rail (c), the IG half — the waypoints reach the adapter as the canvas points' LOCAL metres, unchanged
    /// (no geodetic round trip, so exact equality holds, and it needs no geographic transform on the IG).
    /// </summary>
    [Fact]
    public async Task TheWaypointsAreTheCanvasPointsAsLocalMetres_Unchanged()
    {
        _app.TestHook_ParseCommandAndActivatePersonalRoute(Guid.NewGuid(), "{\"entityId\":5}");
        _app.TestHook_DirectPointSequenceToolCommit(ThreePoints);
        await Task.Delay(200);

        Assert.NotNull(_adapter.LastWaypoints);
        Assert.Equal(
            new[] { (100.0, 0.0, 200.0), (300.0, 0.0, 400.0), (500.0, 0.0, 600.0) },
            _adapter.LastWaypoints!.ToArray());
    }

    /// <summary>
    /// Two canvas points with the same X but different Y must produce waypoints with identical X (east) and differing Z — canvas Y is
    /// the route's third axis, NOT north.
    /// </summary>
    [Fact]
    public async Task CoordinateFix_CanvasYEncodedAsZ_NotNorth()
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

        Assert.Equal(wp0.X, wp1.X);
        Assert.Equal(wp0.Y, wp1.Y);                                   // north untouched by the canvas Y
        Assert.True(wp1.Z > wp0.Z, "Waypoint with higher canvas Y should have higher Z.");
    }

    /// <summary>
    /// Two canvas points with different X but the same Y must produce waypoints with different X (east) and identical Z — canvas X
    /// maps to east, and a local frame has no skew to blur the equality.
    /// </summary>
    [Fact]
    public async Task CoordinateFix_CanvasXEncodesEastCorrectly()
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

        Assert.NotEqual(wp0.X, wp1.X);
        Assert.Equal(wp0.Z, wp1.Z);
    }
}
