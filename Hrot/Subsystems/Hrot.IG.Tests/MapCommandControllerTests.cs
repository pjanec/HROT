using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Core.Network;
using Hrot.IG.Systems;
using Hrot.ScenarioEditor.Tools;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Core;
using Raylib_cs;

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
        MapCommandController     controller)
    BuildController()
    {
        var canvas     = new MapCanvas();
        var bus        = new FdpEventBus();
        var ackCapture = new CapturingAckCallback();
        var ctrl       = new MapCommandController(canvas, bus, ackCapture.Callback);
        return (canvas, bus, ackCapture, ctrl);
    }

    private static IReadOnlyList<SpawnEntityCommand> DrainSpawnCmds(FdpEventBus bus)
    {
        bus.SwapBuffers();
        return bus.ReadManaged<SpawnEntityCommand>();
    }

    [Fact]
    public void ActivatePlacementCommand_PushesCreationToolOntoCanvas()
    {
        var (canvas, _, _, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        Assert.IsType<CreationTool>(canvas.ActiveTool);
    }

    [Fact]
    public void ActivatePlacementCommand_SameContext_IsNoop()
    {
        var (canvas, _, ackCapture, ctrl) = BuildController();
        var contextId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), contextId, 202L, null);
        var toolAfterFirst = canvas.ActiveTool;
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), contextId, 202L, null);
        Assert.Same(toolAfterFirst, canvas.ActiveTool);
        Assert.Empty(ackCapture.Written);
    }

    [Fact]
    public void ActivatePlacementCommand_LeftClick_PublishesSpawnCommandOnBus()
    {
        var (canvas, bus, _, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(1f, 2f), MouseButton.Left);
        Assert.Single(DrainSpawnCmds(bus));
    }

    [Fact]
    public void OnCreateEntityAck_AfterToolAutoPop_PublishesFinishedAck()
    {
        var (canvas, bus, ackCapture, ctrl) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(requestId, Guid.NewGuid(), 202L, null);
        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(1f, 2f), MouseButton.Left);
        var entityReqId = DrainSpawnCmds(bus)[0].RequestId;
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = entityReqId, EntityId = 99, StatusCode = 0 });
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusFinished, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void OnCreateEntityAck_UnknownRequestId_IsIgnored()
    {
        var (canvas, bus, ackCapture, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(1f, 2f), MouseButton.Left);
        bus.SwapBuffers();
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = Guid.NewGuid(), EntityId = 1, StatusCode = 0 });
        Assert.Empty(ackCapture.Written);
    }

    [Fact]
    public void HandleClick_RightClick_PublishesCancelledAck()
    {
        var (canvas, bus, ackCapture, ctrl) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.ActivatePlacementCommand(requestId, Guid.NewGuid(), 202L, null);
        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(1f, 2f), MouseButton.Right);
        Assert.Empty(bus.ReadManaged<SpawnEntityCommand>());
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusCancelled, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void FinishedAck_DataJsonContainsEntityId()
    {
        var (canvas, bus, ackCapture, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null);
        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(1f, 2f), MouseButton.Left);
        var entityReqId = DrainSpawnCmds(bus)[0].RequestId;
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = entityReqId, EntityId = 42, StatusCode = 0 });
        Assert.Contains("42", ackCapture.Written[0].DataJson);
    }

    [Fact]
    public void AreaAuthoring_CommitAndAck_PublishesFinishedAck()
    {
        var (_, bus, ackCapture, ctrl) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.BeginAreaAuthoringSession(requestId, Guid.NewGuid());
        var areaCmd = new SpawnEntityCommand { RequestId = Guid.NewGuid() };
        ctrl.OnAreaEntityCreated(areaCmd, isToolDone: true);
        Assert.Single(DrainSpawnCmds(bus));
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = areaCmd.RequestId, EntityId = 10, StatusCode = 0 });
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusFinished, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void AreaAuthoring_CancelledBeforeAnyRequest_PublishesCancelledAck()
    {
        var (_, bus, ackCapture, ctrl) = BuildController();
        var requestId = Guid.NewGuid();
        ctrl.BeginAreaAuthoringSession(requestId, Guid.NewGuid());
        ctrl.OnAreaToolCancelled();
        Assert.Empty(bus.ReadManaged<SpawnEntityCommand>());
        Assert.Single(ackCapture.Written);
        Assert.Equal(requestId, ackCapture.Written[0].RequestId);
        Assert.Equal(MapCommandController.StatusCancelled, ackCapture.Written[0].StatusCode);
    }

    [Fact]
    public void OnCreateEntityAck_NoActiveSession_IsIgnored()
    {
        var (_, _, ackCapture, ctrl) = BuildController();
        ctrl.OnCreateEntityAck(new EntityLifecycleAckDto { RequestId = Guid.NewGuid(), EntityId = 1, StatusCode = 0 });
        Assert.Empty(ackCapture.Written);
    }

    [Fact]
    public void ActivatePlacementCommand_WithInitialJson_SpawnCommandCarriesJson()
    {
        var (canvas, bus, _, ctrl) = BuildController();
        ctrl.ActivatePlacementCommand(Guid.NewGuid(), Guid.NewGuid(), 202L, null, initialPropertiesJson: "{\"Name\":\"BetaUnit\"}");
        ((CreationTool)canvas.ActiveTool!).HandleClick(new Vector2(100f, 200f), MouseButton.Left);
        var cmds = DrainSpawnCmds(bus);
        Assert.Single(cmds);
        Assert.Equal("{\"Name\":\"BetaUnit\"}", cmds[0].InitialAttributesJson);
    }

    [Fact]
    public void OnAreaEntityCreated_WithoutBeginSession_IsDropped()
    {
        var (_, bus, _, ctrl) = BuildController();
        ctrl.OnAreaEntityCreated(new SpawnEntityCommand { RequestId = Guid.NewGuid() });
        Assert.Empty(bus.ReadManaged<SpawnEntityCommand>());
    }

    [Fact]
    public void OnAreaEntityCreated_AfterBeginSession_PublishesSpawnCommand()
    {
        var (_, bus, _, ctrl) = BuildController();
        ctrl.BeginAreaAuthoringSession(Guid.NewGuid(), Guid.NewGuid());
        ctrl.OnAreaEntityCreated(new SpawnEntityCommand { RequestId = Guid.NewGuid() });
        Assert.Single(DrainSpawnCmds(bus));
    }
}