using Hrot.ExCon.Logic;
using Hrot.Core.Network;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using FDP.Toolkit.DER;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for OC1-I002 through OC1-I005 -- ExConLogic context-menu action methods.
/// </summary>
public class ExConLogicContextCommandTests
{
    // -- Factory ---------------------------------------------------------------

    private static (
        ExConLogic Logic,
        Mock<IExConEgressWriters> EgressWriters,
        ConcurrentEventQueue<EntityLifecycleAckDto> AckQueue)
        CreateSut()
    {
        var repo              = new DerRepo();
        var egressWriters     = new Mock<IExConEgressWriters>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();
        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEventDto>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEventDto>();
        var ackQueue          = new ConcurrentEventQueue<EntityLifecycleAckDto>();

        var logic = new ExConLogic(
            repo:                 repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:     contextMenuLogic.Object,
            transactionManager:   transactionMgr.Object,
            egressWriters:        egressWriters.Object,
            clickQueue:           clickQueue,
            selectionQueue:       selectionQueue,
            interactionPanel:     interactionPanel,
            createEntityAckQueue: ackQueue);

        return (logic, egressWriters, ackQueue);
    }

    // -- OC1-I002: SendSetSelection --------------------------------------------

    /// <summary>OC1-I002 SC1: SendSetSelection applies local selection immediately.</summary>
    [Fact]
    public void SendSetSelection_SetsSelectedEntityIdLocally()
    {
        var (logic, _, _) = CreateSut();
        logic.SendSetSelection(42);
        Assert.Equal(42, logic.SelectedEntityId);
    }

    /// <summary>OC1-I002 SC2: SendSetSelection publishes CMD_SET_SELECTION with correct entity ID.</summary>
    [Fact]
    public void SendSetSelection_PublishesCmdSetSelection()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.SendSetSelection(42);
        egressWriters.Verify(w => w.WriteMapCommand(It.Is<MapCommandDto>(r =>
            r.CommandType == "CMD_SET_SELECTION" &&
            r.CommandArgsJson.Contains("\"entityId\":42"))),
            Times.Once);
    }

    /// <summary>OC1-I002 SC3: SendSetSelection does not crash when there is no command writer.</summary>
    [Fact]
    public void SendSetSelection_NullWriter_NoException()
    {
        var (logic, _, _) = CreateSut();

        var ex = Record.Exception(() => logic.SendSetSelection(7));

        Assert.Null(ex);
        Assert.Equal(7, logic.SelectedEntityId);
    }

    // -- OC1-I003: CenterOnEntity ---------------------------------------------

    /// <summary>OC1-I003 SC1: CenterOnEntity publishes CMD_SET_VIEW with the entity ID.</summary>
    [Fact]
    public void CenterOnEntity_PublishesCmdSetView()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.CenterOnEntity(15);
        egressWriters.Verify(w => w.WriteMapCommand(It.Is<MapCommandDto>(r =>
            r.CommandType == "CMD_SET_VIEW" &&
            r.CommandArgsJson.Contains("\"entityId\":15"))),
            Times.Once);
    }

    /// <summary>OC1-I003 SC2: CenterOnEntity payload must NOT contain geo-coordinates.</summary>
    [Fact]
    public void CenterOnEntity_PayloadHasNoCoordinates()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.CenterOnEntity(15);
        egressWriters.Verify(w => w.WriteMapCommand(It.Is<MapCommandDto>(r =>
            !r.CommandArgsJson.Contains("\"lat\"") &&
            !r.CommandArgsJson.Contains("\"lon\""))),
            Times.Once);
    }

    /// <summary>OC1-I003 SC3: CenterOnEntity does not crash when there is no command writer.</summary>
    [Fact]
    public void CenterOnEntity_NullWriter_NoException()
    {
        var (logic, _, _) = CreateSut();
        var ex = Record.Exception(() => logic.CenterOnEntity(15));
        Assert.Null(ex);
    }

    // -- OC1-I004: DeleteEntity -----------------------------------------------

    /// <summary>OC1-I004 SC1: DeleteEntity calls WriteDeleteEntity with correct entity ID.</summary>
    [Fact]
    public void DeleteEntity_PublishesDeleteEntityRequest()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.DeleteEntity(5);
        egressWriters.Verify(w => w.WriteDeleteEntity(5), Times.Once);
    }

    /// <summary>OC1-I004 SC2: DeleteEntity marks entity as pending delete.</summary>
    [Fact]
    public void DeleteEntity_MarksEntityPendingDelete()
    {
        var (logic, _, _) = CreateSut();
        logic.DeleteEntity(5);
        Assert.True(logic.IsEntityPendingDelete(5));
    }

    /// <summary>OC1-I004 SC3: Success ACK clears the pending-delete flag.</summary>
    [Fact]
    public void DeleteEntity_SuccessAck_ClearsPendingFlag()
    {
        var (logic, _, ackQueue) = CreateSut();
        logic.DeleteEntity(5);

        ackQueue.Enqueue(new EntityLifecycleAckDto
        {
            EntityId   = 5,
            StatusCode = EntityLifecycleAckDto.StatusSuccess,
        });
        logic.Update();

        Assert.False(logic.IsEntityPendingDelete(5));
        Assert.Null(logic.GlobalAlert);
    }

    /// <summary>OC1-I004 SC4: Failure ACK clears pending flag and sets a GlobalAlert.</summary>
    [Fact]
    public void DeleteEntity_FailureAck_ClearsPendingAndSetsAlert()
    {
        var (logic, _, ackQueue) = CreateSut();
        logic.DeleteEntity(5);

        ackQueue.Enqueue(new EntityLifecycleAckDto
        {
            EntityId   = 5,
            StatusCode = EntityLifecycleAckDto.StatusFailureMin, // any failure code
        });
        logic.Update();

        Assert.False(logic.IsEntityPendingDelete(5));
        Assert.NotNull(logic.GlobalAlert);
    }

    /// <summary>OC1-I004 SC5: ACK for an entity that was never in pending-delete is ignored.</summary>
    [Fact]
    public void DeleteEntity_UnrelatedAck_Ignored()
    {
        var (logic, _, ackQueue) = CreateSut();

        ackQueue.Enqueue(new EntityLifecycleAckDto
        {
            EntityId   = 99,
            StatusCode = EntityLifecycleAckDto.StatusSuccess,
        });
        logic.Update();

        Assert.False(logic.IsEntityPendingDelete(99));
        Assert.Null(logic.GlobalAlert);
    }

    // -- OC1-I005: StartPersonalRouteAuthoring --------------------------------

    /// <summary>OC1-I005 SC1: StartPersonalRouteAuthoring publishes CMD_DRAW_PERSONAL_ROUTE.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_PublishesCmdDrawPersonalRoute()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        egressWriters.Verify(w => w.WriteMapCommand(It.Is<MapCommandDto>(r =>
            r.CommandType == "CMD_DRAW_PERSONAL_ROUTE")),
            Times.Once);
    }

    /// <summary>OC1-I005 SC2: payload must contain the entity ID.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_PayloadContainsEntityId()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        egressWriters.Verify(w => w.WriteMapCommand(It.Is<MapCommandDto>(r =>
            r.CommandArgsJson.Contains("\"entityId\":10"))),
            Times.Once);
    }

    /// <summary>OC1-I005 SC3: payload must contain a non-empty contextId.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_PayloadContainsContextId()
    {
        var (logic, egressWriters, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        egressWriters.Verify(w => w.WriteMapCommand(It.Is<MapCommandDto>(r =>
            r.CommandArgsJson.Contains("\"contextId\"") &&
            r.CommandArgsJson.Length > 20)),
            Times.Once);
    }

    /// <summary>OC1-I005 SC4: ActiveContextId is updated to a non-empty GUID.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_UpdatesActiveContextId()
    {
        var (logic, _, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
    }
}