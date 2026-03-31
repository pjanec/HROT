using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.IG.Abstractions;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.IG.Tools;
using FDP.Toolkit.Replication.Patching;
using FDP.Toolkit.Vis2D;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="MapCommandController"/>.
///
/// Exercises the full session lifecycle:
/// <list type="bullet">
///   <item>Placement tool activation and canvas state.</item>
///   <item>Entity creation delegate forwarding to the DDS writer.</item>
///   <item>CreateEntityAck correlation producing MapCommandAck(Finished).</item>
///   <item>Tool cancellation producing MapCommandAck(Cancelled).</item>
///   <item>Area authoring session commit and cancellation paths.</item>
/// </list>
///
/// Uses <see cref="MapCanvas"/> with no Raylib window (headless); canvas push/pop
/// works without a graphics context because it is pure state management.
/// </summary>
public class MapCommandControllerTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class CapturingDdsWriter<T> : IDdsWriter<T>
    {
        public List<T> Written { get; } = new();
        public void Write(T sample) => Written.Add(sample);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (
        MapCanvas                           canvas,
        CapturingDdsWriter<CreateEntityRequest> entityWriter,
        CapturingDdsWriter<MapCommandAck>       ackWriter,
        MapCommandController                controller)
    BuildController()
    {
        var canvas       = new MapCanvas();
        var entityWriter = new CapturingDdsWriter<CreateEntityRequest>();
        var ackWriter    = new CapturingDdsWriter<MapCommandAck>();
        var controller   = new MapCommandController(canvas, entityWriter, ackWriter);
        return (canvas, entityWriter, ackWriter, controller);
    }

    // ── ActivatePlacementCommand ──────────────────────────────────────────────

    /// <summary>
    /// After activating a placement command the canvas active tool must be a
    /// <see cref="CreationTool"/>.
    /// </summary>
    [Fact]
    public void ActivatePlacementCommand_PushesCreationToolOntoCanvas()
    {
        var (canvas, _, _, ctrl) = BuildController();

        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);

        Assert.IsType<CreationTool>(canvas.ActiveTool);
    }

    /// <summary>
    /// Calling <see cref="MapCommandController.ActivatePlacementCommand"/> twice with
    /// the same (non-empty) context ID and while the session is still active must
    /// be a no-op: the tool on the canvas must not change.
    /// </summary>
    [Fact]
    public void ActivatePlacementCommand_SameContext_IsNoop()
    {
        var (canvas, _, ackWriter, ctrl) = BuildController();
        var contextId = Guid.NewGuid();

        ctrl.ActivatePlacementCommand(Guid.NewGuid(), contextId, 202L, null);
        var toolAfterFirst = canvas.ActiveTool;

        ctrl.ActivatePlacementCommand(Guid.NewGuid(), contextId, 202L, null);

        Assert.Same(toolAfterFirst, canvas.ActiveTool); // identical object
        Assert.Empty(ackWriter.Written);                // no spurious acks
    }

    // ── Left-click → create entity request forwarded ──────────────────────────

    /// <summary>
    /// A left-click on the activated <see cref="CreationTool"/> must forward exactly
    /// one <see cref="CreateEntityRequest"/> to the DDS entity writer.
    /// </summary>
    [Fact]
    public void ActivatePlacementCommand_LeftClick_ForwardsCreateEntityRequest()
    {
        var (canvas, entityWriter, _, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);

        var tool = (CreationTool)canvas.ActiveTool!;
        tool.HandleClick(new Vector2(1f, 2f), MouseButton.Left);

        Assert.Single(entityWriter.Written);
    }

    // ── MapCommandAck(Finished) after tool exit + ack ─────────────────────────

    /// <summary>
    /// When the <see cref="CreationTool"/> auto-pops after a left-click (setting
    /// <c>_toolFinished</c>) and a matching <see cref="CreateEntityAck"/> arrives,
    /// the controller must publish one <see cref="MapCommandAck"/> with
    /// <see cref="MapCommandController.StatusFinished"/>.
    /// </summary>
    [Fact]
    public void OnCreateEntityAck_AfterToolAutoPop_PublishesFinishedAck()
    {
        var (canvas, entityWriter, ackWriter, ctrl) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(requestId, Guid.NewGuid(), 202L, null);

        var tool = (CreationTool)canvas.ActiveTool!;
        tool.HandleClick(new Vector2(1f, 2f), MouseButton.Left);
        // Tool has auto-popped; entityWriter has one request.

        var entityReqId = entityWriter.Written[0].RequestId;
        ctrl.OnCreateEntityAck(new CreateUpdateDeleteEntityAck
        {
            RequestId  = entityReqId,
            EntityId   = 99,
            StatusCode = 0
        });

        Assert.Single(ackWriter.Written);
        Assert.Equal(requestId,                        ackWriter.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusFinished, ackWriter.Written[0].StatusCode);
    }

    /// <summary>
    /// A <see cref="CreateEntityAck"/> whose <c>RequestId</c> does not match any
    /// pending entity request must NOT produce a <see cref="MapCommandAck"/>.
    /// </summary>
    [Fact]
    public void OnCreateEntityAck_UnknownRequestId_IsIgnored()
    {
        var (canvas, _, ackWriter, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);

        var tool = (CreationTool)canvas.ActiveTool!;
        tool.HandleClick(new Vector2(1f, 2f), MouseButton.Left);

        ctrl.OnCreateEntityAck(new CreateUpdateDeleteEntityAck
        {
            RequestId  = Guid.NewGuid(), // deliberate mismatch
            EntityId   = 1,
            StatusCode = 0
        });

        Assert.Empty(ackWriter.Written);
    }

    // ── MapCommandAck(Cancelled) ──────────────────────────────────────────────

    /// <summary>
    /// When the <see cref="CreationTool"/> is right-clicked (cancelled) before any
    /// entity is created, the controller must publish exactly one
    /// <see cref="MapCommandAck"/> with <see cref="MapCommandController.StatusCancelled"/>.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_PublishesCancelledAck()
    {
        var (canvas, entityWriter, ackWriter, ctrl) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(requestId, Guid.NewGuid(), 202L, null);

        var tool = (CreationTool)canvas.ActiveTool!;
        tool.HandleClick(new Vector2(1f, 2f), MouseButton.Right);

        Assert.Empty(entityWriter.Written);              // nothing created
        Assert.Single(ackWriter.Written);
        Assert.Equal(requestId,                          ackWriter.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusCancelled, ackWriter.Written[0].StatusCode);
    }

    // ── MapCommandAck DataJson contains entityId ──────────────────────────────

    /// <summary>
    /// The <see cref="MapCommandAck.DataJson"/> field in a finished ack must contain
    /// the <c>entityId</c> reported by the <see cref="CreateEntityAck"/>.
    /// </summary>
    [Fact]
    public void FinishedAck_DataJsonContainsEntityId()
    {
        var (canvas, entityWriter, ackWriter, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);

        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(1f, 2f), MouseButton.Left);

        ctrl.OnCreateEntityAck(new CreateUpdateDeleteEntityAck
        {
            RequestId  = entityWriter.Written[0].RequestId,
            EntityId   = 42,
            StatusCode = 0
        });

        Assert.Contains("42", ackWriter.Written[0].DataJson);
    }

    // ── Area authoring session ────────────────────────────────────────────────

    /// <summary>
    /// After <see cref="MapCommandController.BeginAreaAuthoringSession"/> + a single
    /// <see cref="MapCommandController.OnAreaEntityCreated"/>(<c>isToolDone=true</c>)
    /// + a matching <see cref="CreateEntityAck"/>, the controller must publish one
    /// <see cref="MapCommandAck"/> with <see cref="MapCommandController.StatusFinished"/>.
    /// </summary>
    [Fact]
    public void AreaAuthoring_CommitAndAck_PublishesFinishedAck()
    {
        var (_, entityWriter, ackWriter, ctrl) = BuildController();
        var requestId = Guid.NewGuid();

        ctrl.BeginAreaAuthoringSession(requestId, Guid.NewGuid());

        var areaRequest = new CreateEntityRequest { RequestId = Guid.NewGuid() };
        ctrl.OnAreaEntityCreated(areaRequest, isToolDone: true);

        // Entity request must have been forwarded.
        Assert.Single(entityWriter.Written);

        ctrl.OnCreateEntityAck(new CreateUpdateDeleteEntityAck
        {
            RequestId  = areaRequest.RequestId,
            EntityId   = 10,
            StatusCode = 0
        });

        Assert.Single(ackWriter.Written);
        Assert.Equal(requestId,                        ackWriter.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusFinished, ackWriter.Written[0].StatusCode);
    }

    /// <summary>
    /// When <see cref="MapCommandController.OnAreaToolCancelled"/> is called before any
    /// entity request has been forwarded, the controller must publish
    /// <see cref="MapCommandAck"/> with <see cref="MapCommandController.StatusCancelled"/>.
    /// </summary>
    [Fact]
    public void AreaAuthoring_CancelledBeforeAnyRequest_PublishesCancelledAck()
    {
        var (_, entityWriter, ackWriter, ctrl) = BuildController();
        var requestId = Guid.NewGuid();

        ctrl.BeginAreaAuthoringSession(requestId, Guid.NewGuid());
        ctrl.OnAreaToolCancelled();

        Assert.Empty(entityWriter.Written);
        Assert.Single(ackWriter.Written);
        Assert.Equal(requestId,                          ackWriter.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusCancelled, ackWriter.Written[0].StatusCode);
    }

    // ── Session isolation ─────────────────────────────────────────────────────

    /// <summary>
    /// An ack delivered when no session is active (no <c>ActivatePlacementCommand</c>
    /// or <c>BeginAreaAuthoringSession</c> has been called) must not produce any
    /// <see cref="MapCommandAck"/>.
    /// </summary>
    [Fact]
    public void OnCreateEntityAck_NoActiveSession_IsIgnored()
    {
        var (_, _, ackWriter, ctrl) = BuildController();

        ctrl.OnCreateEntityAck(new CreateUpdateDeleteEntityAck
        {
            RequestId  = Guid.NewGuid(),
            EntityId   = 1,
            StatusCode = 0
        });

        Assert.Empty(ackWriter.Written);
    }

    // ── ATTR2-DEBT-07: Edge compiler DI wiring ────────────────────────────────

    /// <summary>
    /// Builds a <see cref="MapCommandController"/> that has a non-null
    /// <see cref="JsonToRecordCompiler"/> injected via the constructor.
    /// </summary>
    private static (
        MapCanvas                               canvas,
        CapturingDdsWriter<CreateEntityRequest> entityWriter,
        CapturingDdsWriter<MapCommandAck>       ackWriter,
        MapCommandController                    controller)
    BuildControllerWithEdgeCompiler()
    {
        var canvas       = new MapCanvas();
        var entityWriter = new CapturingDdsWriter<CreateEntityRequest>();
        var ackWriter    = new CapturingDdsWriter<MapCommandAck>();
        var compiler     = new JsonToRecordCompilerBuilder()
            .Register("Name",                  AttributeIds.Name,        AttributeValueType.KindString)
            .Register("GeoPosition.Latitude",  AttributeIds.GeoLat,      AttributeValueType.KindFloat64)
            .Register("GeoPosition.Longitude", AttributeIds.GeoLon,      AttributeValueType.KindFloat64)
            .Register("GeoPosition.Altitude",  AttributeIds.GeoAlt,      AttributeValueType.KindFloat64)
            .Build();
        var controller   = new MapCommandController(canvas, entityWriter, ackWriter, compiler);
        return (canvas, entityWriter, ackWriter, controller);
    }

    /// <summary>
    /// ATTR2-DEBT-07: When <see cref="MapCommandController"/> is constructed with a
    /// non-null <see cref="JsonToRecordCompiler"/>, the <see cref="CreationTool"/> it
    /// pushes must emit <c>InitialAttributeRecords</c> (non-null, non-empty) and
    /// set <c>InitialAttributesJson</c> to <c>null</c> on a left-click.
    ///
    /// This verifies that the edge compiler flows from the DI root through the
    /// controller into the tool, fulfilling the production binary-wire requirement.
    /// </summary>
    [Fact]
    public void ActivatePlacementCommand_WithEdgeCompiler_CreationToolEmitsBinaryRecords()
    {
        var (canvas, entityWriter, _, ctrl) = BuildControllerWithEdgeCompiler();

        ctrl.ActivatePlacementCommand(
            Guid.NewGuid(), Guid.NewGuid(), 202L, null,
            initialPropertiesJson: "{\"Name\":\"AlphaUnit\"}");

        var tool = (CreationTool)canvas.ActiveTool!;
        tool.HandleClick(new Vector2(100f, 200f), MouseButton.Left);

        Assert.Single(entityWriter.Written);
        var req = entityWriter.Written[0];

        // Binary-only wire: InitialAttributesJson must be null.
        Assert.Null(req.InitialAttributesJson);

        // InitialAttributeRecords must be non-null and contain at least one record.
        Assert.NotNull(req.InitialAttributeRecords);
        Assert.NotEmpty(req.InitialAttributeRecords!);
    }

    /// <summary>
    /// ATTR2-DEBT-07: Without an edge compiler (legacy path) the placement tool must
    /// fall back to the JSON wire — <c>InitialAttributesJson</c> is set and
    /// <c>InitialAttributeRecords</c> is null.  This confirms the optional wiring
    /// does not break existing tests or headless scenarios.
    /// </summary>
    [Fact]
    public void ActivatePlacementCommand_WithoutEdgeCompiler_CreationToolUsesJsonPath()
    {
        var (canvas, entityWriter, _, ctrl) = BuildController();

        ctrl.ActivatePlacementCommand(
            Guid.NewGuid(), Guid.NewGuid(), 202L, null,
            initialPropertiesJson: "{\"Name\":\"BetaUnit\"}");

        var tool = (CreationTool)canvas.ActiveTool!;
        tool.HandleClick(new Vector2(100f, 200f), MouseButton.Left);

        Assert.Single(entityWriter.Written);
        var req = entityWriter.Written[0];

        // Legacy wire: InitialAttributesJson must be forwarded verbatim.
        Assert.Equal("{\"Name\":\"BetaUnit\"}", req.InitialAttributesJson);

        // No binary records on the legacy path.
        Assert.Null(req.InitialAttributeRecords);
    }

    // ── OC1-B001 regression: BeginAreaAuthoringSession required before OnAreaEntityCreated ──

    /// <summary>
    /// OC1-B001 regression: Calling <see cref="MapCommandController.OnAreaEntityCreated"/>
    /// without a preceding <see cref="MapCommandController.BeginAreaAuthoringSession"/> must
    /// silently drop the request — the guard <c>if (_sessionRequestId == Guid.Empty) return</c>
    /// prevents writes to the DDS writer when no session is established.
    ///
    /// <para>This test documents the bug condition so it is detectable if the guard is removed.</para>
    /// </summary>
    [Fact]
    public void OnAreaEntityCreated_WithoutBeginSession_IsDropped()
    {
        var (_, entityWriter, _, ctrl) = BuildController();

        ctrl.OnAreaEntityCreated(new CreateEntityRequest { RequestId = Guid.NewGuid() });

        Assert.Empty(entityWriter.Written);
    }

    /// <summary>
    /// OC1-B001 regression: After <see cref="MapCommandController.BeginAreaAuthoringSession"/>
    /// is called with a non-empty requestId, <see cref="MapCommandController.OnAreaEntityCreated"/>
    /// must forward the request to the entity writer.
    ///
    /// <para>This verifies the fix: <c>ActivateRouteAuthoringTool</c> must call
    /// <c>BeginAreaAuthoringSession</c> before pushing the PointSequenceTool so the
    /// session is established and <c>OnAreaEntityCreated</c> does not return early.</para>
    /// </summary>
    [Fact]
    public void OnAreaEntityCreated_AfterBeginSession_WritesRequest()
    {
        var (_, entityWriter, _, ctrl) = BuildController();
        ctrl.BeginAreaAuthoringSession(Guid.NewGuid(), Guid.NewGuid());

        ctrl.OnAreaEntityCreated(new CreateEntityRequest { RequestId = Guid.NewGuid() });

        Assert.Single(entityWriter.Written);
    }
}

