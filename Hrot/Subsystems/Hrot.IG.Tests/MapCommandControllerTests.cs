using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Core.Network;
using Hrot.IG.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Core;

namespace Hrot.IG.Tests;

public class MapCommandControllerTests
{
    private sealed class CapturingAckCallback
    {
        public List<MapCommandAckDto> Written { get; } = new();
        public Action<MapCommandAckDto> Callback => dto => Written.Add(dto);
    }

    private static (
        MapCanvas                canvas,
        FdpEventBus              bus,
        CapturingAckCallback     ackCapture,
        GlobalGizmoManager       manager,
        EntityRepository         repo,
        MapCommandController     controller,
        ScenarioEntityCreationRequestSource requests)
    BuildController()
    {
        var canvas     = new MapCanvas();
        var bus        = new FdpEventBus();
        var ackCapture = new CapturingAckCallback();
        var buffer     = new DebugPrimitiveBuffer();
        var manager    = new GlobalGizmoManager(buffer);
        var repo       = new EntityRepository();
        repo.RegisterEvent<GizmoDragUpdateEvent>();
        repo.RegisterEvent<GizmoMouseEvent>();
        repo.RegisterEvent<GizmoKeyEvent>();
        var requests   = new ScenarioEntityCreationRequestSource();
        var ctrl       = new MapCommandController(canvas, bus, ackCapture.Callback, requests, globalGizmoManager: manager);
        return (canvas, bus, ackCapture, manager, repo, ctrl, requests);
    }

    // Publishes a left-mouse-released event and drives a single Execute tick,
    // which routes the event to the focused gizmo synchronously.
    private static void SimulateLeftClick(GlobalGizmoManager manager, EntityRepository repo, float x = 1f, float y = 2f)
    {
        repo.Bus.Publish(new GizmoMouseEvent
        {
            Button    = MapMouseButton.Left,
            IsPressed = false,
            WorldPos  = new Vector3(x, y, 0f),
        });
        repo.Bus.SwapBuffers();
        manager.Execute(repo, 0f);
    }

    // Publishes a right-mouse-pressed event (= cancel) and drives a single Execute tick.
    private static void SimulateRightClick(GlobalGizmoManager manager, EntityRepository repo, float x = 1f, float y = 2f)
    {
        repo.Bus.Publish(new GizmoMouseEvent
        {
            Button    = MapMouseButton.Right,
            IsPressed = true,
            WorldPos  = new Vector3(x, y, 0f),
        });
        repo.Bus.SwapBuffers();
        manager.Execute(repo, 0f);
    }

    /// ⭐ RE-HOMED (host (f), 2026-09-02): the controller posts an INTENT onto the shared request seam
    /// instead of publishing a node-local SpawnEntityCommand ORDER onto the bus. The claims below are
    /// unchanged — "one gesture produces exactly one creation" — only their observation point moved.
    /// 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4b.
    private static IReadOnlyList<EntityCreationRequest> DrainRequests(ScenarioEntityCreationRequestSource requests)
    {
        var drained = new List<EntityCreationRequest>();
        requests.ProcessRequests(drained.Add);
        return drained;
    }

    [Fact]
    public void ActivatePlacementCommand_RegistersGizmoWithManager()
    {
        var (_, _, _, manager, _, ctrl, requests) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public void ActivatePlacementCommand_SameContext_IsNoop()
    {
        var (_, _, ackCapture, manager, _, ctrl, requests) = BuildController();
        var contextId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), contextId, 202L, null);
        Assert.Equal(1, manager.ActiveCount);
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), contextId, 202L, null);
        Assert.Equal(1, manager.ActiveCount);
        Assert.Empty(ackCapture.Written);
    }

    [Fact]
    public void ActivatePlacementCommand_LeftClick_PublishesSpawnCommandOnBus()
    {
        var (_, bus, _, manager, repo, ctrl, requests) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        SimulateLeftClick(manager, repo);
        Assert.Single(DrainRequests(requests));
    }

    [Fact]
    public void OnCreateEntityAck_AfterToolAutoPop_PublishesFinishedAck()
    {
        var (_, bus, ackCapture, manager, repo, ctrl, requests) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(requestId, Guid.NewGuid(), 202L, null);
        SimulateLeftClick(manager, repo);
        var entityReqId = DrainRequests(requests)[0].RequestId;
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = entityReqId, EntityId = 99, StatusCode = 0 });
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusFinished, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void OnCreateEntityAck_UnknownRequestId_IsIgnored()
    {
        var (_, bus, ackCapture, manager, repo, ctrl, requests) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        SimulateLeftClick(manager, repo);
        bus.SwapBuffers();
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = Guid.NewGuid(), EntityId = 1, StatusCode = 0 });
        Assert.Empty(ackCapture.Written);
    }

    [Fact]
    public void HandleClick_RightClick_PublishesCancelledAck()
    {
        var (_, bus, ackCapture, manager, repo, ctrl, requests) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(requestId, Guid.NewGuid(), 202L, null);
        SimulateRightClick(manager, repo);
        Assert.Empty(DrainRequests(requests));
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusCancelled, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void FinishedAck_DataJsonContainsEntityId()
    {
        var (_, bus, ackCapture, manager, repo, ctrl, requests) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        SimulateLeftClick(manager, repo);
        var entityReqId = DrainRequests(requests)[0].RequestId;
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = entityReqId, EntityId = 42, StatusCode = 0 });
        Assert.Contains("42", ackCapture.Written[0].DataJson);
    }

    [Fact]
    public void AreaAuthoring_CommitAndAck_PublishesFinishedAck()
    {
        var (_, bus, ackCapture, _, _, ctrl, requests) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.BeginAreaAuthoringSession(requestId, Guid.NewGuid());
        var areaCmd = new SpawnEntityCommand { RequestId = Guid.NewGuid() };
        ctrl.OnAreaEntityCreated(areaCmd, isToolDone: true);
        Assert.Single(DrainRequests(requests));
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = areaCmd.RequestId, EntityId = 10, StatusCode = 0 });
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusFinished, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void AreaAuthoring_CancelledBeforeAnyRequest_PublishesCancelledAck()
    {
        var (_, bus, ackCapture, _, _, ctrl, requests) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.BeginAreaAuthoringSession(requestId, Guid.NewGuid());
        ctrl.OnAreaToolCancelled();
        Assert.Empty(DrainRequests(requests));
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusCancelled, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void OnCreateEntityAck_NoActiveSession_IsIgnored()
    {
        var (_, _, ackCapture, _, _, ctrl, requests) = BuildController();
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = Guid.NewGuid(), EntityId = 1, StatusCode = 0 });
        Assert.Empty(ackCapture.Written);
    }

    [Fact]
    public void ActivatePlacementCommand_WithInitialJson_SpawnCommandCarriesJson()
    {
        var (_, bus, _, manager, repo, ctrl, requests) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null, initialPropertiesJson: "{\"Name\":\"BetaUnit\"}");
        SimulateLeftClick(manager, repo, 100f, 200f);
        var cmds = DrainRequests(requests);
        Assert.Single(cmds);
        Assert.Equal("{\"Name\":\"BetaUnit\"}", cmds[0].InitialAttributesJson);
    }

    [Fact]
    public void OnAreaEntityCreated_WithoutBeginSession_IsDropped()
    {
        var (_, bus, _, _, _, ctrl, requests) = BuildController();
        ctrl.OnAreaEntityCreated(new SpawnEntityCommand { RequestId = Guid.NewGuid() });
        Assert.Empty(DrainRequests(requests));
    }

    [Fact]
    public void OnAreaEntityCreated_AfterBeginSession_PublishesSpawnCommand()
    {
        var (_, bus, _, _, _, ctrl, requests) = BuildController();
        ctrl.BeginAreaAuthoringSession(Guid.NewGuid(), Guid.NewGuid());
        ctrl.OnAreaEntityCreated(new SpawnEntityCommand { RequestId = Guid.NewGuid() });
        Assert.Single(DrainRequests(requests));
    }
}