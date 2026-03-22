using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Map.Common.Dds;
using FDP.Toolkit.DER;
using Moq;
using Xunit;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for OC1-I002 through OC1-I005 — IosLogic context-menu action methods.
///
/// <para>Covers:
/// <list type="bullet">
///   <item>OC1-I002: <see cref="IosLogic.SendSetSelection"/> publishes CMD_SET_SELECTION and selects locally.</item>
///   <item>OC1-I003: <see cref="IosLogic.CenterOnEntity"/> publishes CMD_SET_VIEW.</item>
///   <item>OC1-I004: <see cref="IosLogic.DeleteEntity"/> publishes DeleteEntityRequest and tracks pending deletes.</item>
///   <item>OC1-I005: <see cref="IosLogic.StartPersonalRouteAuthoring"/> publishes CMD_DRAW_PERSONAL_ROUTE.</item>
/// </list>
/// </para>
/// </summary>
public class IosLogicContextCommandTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static (
        IosLogic Logic,
        Mock<IDdsWriter<MapCommandRequest>> CommandWriter,
        Mock<IDdsWriter<Bagira.BDC.SSTM.DeleteEntityRequest>> DeleteWriter,
        ConcurrentEventQueue<CreateUpdateDeleteEntityAck> AckQueue)
        CreateSut()
    {
        var repo              = new DerRepo();
        var configWriter      = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter      = new Mock<IDdsWriter<CreateEntityRequest>>();
        var commandWriter     = new Mock<IDdsWriter<MapCommandRequest>>();
        var deleteWriter      = new Mock<IDdsWriter<Bagira.BDC.SSTM.DeleteEntityRequest>>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();
        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEvent>();
        var ackQueue          = new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>();

        var logic = new IosLogic(
            repo:                 repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:     contextMenuLogic.Object,
            transactionManager:   transactionMgr.Object,
            configWriter:         configWriter.Object,
            createEntityWriter:   createWriter.Object,
            clickQueue:           clickQueue,
            selectionQueue:       selectionQueue,
            interactionPanel:     interactionPanel,
            createEntityAckQueue: ackQueue,
            commandWriter:        commandWriter.Object,
            deleteEntityWriter:   deleteWriter.Object);

        return (logic, commandWriter, deleteWriter, ackQueue);
    }

    // ── OC1-I002: SendSetSelection ────────────────────────────────────────────

    /// <summary>OC1-I002 SC1: SendSetSelection applies local selection immediately.</summary>
    [Fact]
    public void SendSetSelection_SetsSelectedEntityIdLocally()
    {
        var (logic, _, _, _) = CreateSut();
        logic.SendSetSelection(42);
        Assert.Equal(42, logic.SelectedEntityId);
    }

    /// <summary>OC1-I002 SC2: SendSetSelection publishes CMD_SET_SELECTION with correct entity ID.</summary>
    [Fact]
    public void SendSetSelection_PublishesCmdSetSelection()
    {
        var (logic, cmdWriter, _, _) = CreateSut();
        logic.SendSetSelection(42);
        cmdWriter.Verify(w => w.Write(It.Is<MapCommandRequest>(r =>
            r.Type == CommandType.CMD_SET_SELECTION &&
            r.CommandArgsJson.Contains("\"entityId\":42"))),
            Times.Once);
    }

    /// <summary>OC1-I002 SC3: SendSetSelection does not crash when CommandWriter is null.</summary>
    [Fact]
    public void SendSetSelection_NullWriter_NoException()
    {
        // Build logic without a command writer.
        var repo             = new DerRepo();
        var configWriter     = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter     = new Mock<IDdsWriter<CreateEntityRequest>>();
        var transactionMgr   = new Mock<IRequestTransactionManager>();
        var missionSvc       = new Mock<IMissionEditorService>();
        var contextMenuLogic = new Mock<IContextMenuLogic>();
        var interactionPanel = new InteractionPanel();
        var logic = new IosLogic(
            repo:                 repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:     contextMenuLogic.Object,
            transactionManager:   transactionMgr.Object,
            configWriter:         configWriter.Object,
            createEntityWriter:   createWriter.Object,
            clickQueue:           new ConcurrentEventQueue<MapClickEvent>(),
            selectionQueue:       new ConcurrentEventQueue<SelectionChangedEvent>(),
            interactionPanel:     interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>());

        var ex = Record.Exception(() => logic.SendSetSelection(7));

        Assert.Null(ex);
        Assert.Equal(7, logic.SelectedEntityId);
    }

    // ── OC1-I003: CenterOnEntity ──────────────────────────────────────────────

    /// <summary>OC1-I003 SC1: CenterOnEntity publishes CMD_SET_VIEW with the entity ID.</summary>
    [Fact]
    public void CenterOnEntity_PublishesCmdSetView()
    {
        var (logic, cmdWriter, _, _) = CreateSut();
        logic.CenterOnEntity(15);
        cmdWriter.Verify(w => w.Write(It.Is<MapCommandRequest>(r =>
            r.Type == CommandType.CMD_SET_VIEW &&
            r.CommandArgsJson.Contains("\"entityId\":15"))),
            Times.Once);
    }

    /// <summary>OC1-I003 SC2: CenterOnEntity payload must NOT contain geo-coordinates.</summary>
    [Fact]
    public void CenterOnEntity_PayloadHasNoCoordinates()
    {
        var (logic, cmdWriter, _, _) = CreateSut();
        logic.CenterOnEntity(15);
        cmdWriter.Verify(w => w.Write(It.Is<MapCommandRequest>(r =>
            !r.CommandArgsJson.Contains("\"lat\"") &&
            !r.CommandArgsJson.Contains("\"lon\""))),
            Times.Once);
    }

    /// <summary>OC1-I003 SC3: CenterOnEntity does not crash when CommandWriter is null.</summary>
    [Fact]
    public void CenterOnEntity_NullWriter_NoException()
    {
        var repo             = new DerRepo();
        var configWriter     = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter     = new Mock<IDdsWriter<CreateEntityRequest>>();
        var transactionMgr   = new Mock<IRequestTransactionManager>();
        var missionSvc       = new Mock<IMissionEditorService>();
        var contextMenuLogic = new Mock<IContextMenuLogic>();
        var logic = new IosLogic(
            repo:                 repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:     contextMenuLogic.Object,
            transactionManager:   transactionMgr.Object,
            configWriter:         configWriter.Object,
            createEntityWriter:   createWriter.Object,
            clickQueue:           new ConcurrentEventQueue<MapClickEvent>(),
            selectionQueue:       new ConcurrentEventQueue<SelectionChangedEvent>(),
            interactionPanel:     new InteractionPanel(),
            createEntityAckQueue: new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>());

        var ex = Record.Exception(() => logic.CenterOnEntity(15));
        Assert.Null(ex);
    }

    // ── OC1-I004: DeleteEntity ────────────────────────────────────────────────

    /// <summary>OC1-I004 SC1: DeleteEntity publishes DeleteEntityRequest with correct entity ID.</summary>
    [Fact]
    public void DeleteEntity_PublishesDeleteEntityRequest()
    {
        var (logic, _, deleteWriter, _) = CreateSut();
        logic.DeleteEntity(5);
        deleteWriter.Verify(w => w.Write(It.Is<Bagira.BDC.SSTM.DeleteEntityRequest>(r =>
            r.EntityId == 5)),
            Times.Once);
    }

    /// <summary>OC1-I004 SC2: DeleteEntity marks entity as pending delete.</summary>
    [Fact]
    public void DeleteEntity_MarksEntityPendingDelete()
    {
        var (logic, _, _, _) = CreateSut();
        logic.DeleteEntity(5);
        Assert.True(logic.IsEntityPendingDelete(5));
    }

    /// <summary>OC1-I004 SC3: Success ACK clears the pending-delete flag.</summary>
    [Fact]
    public void DeleteEntity_SuccessAck_ClearsPendingFlag()
    {
        var (logic, _, _, ackQueue) = CreateSut();
        logic.DeleteEntity(5);

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            EntityId   = 5,
            StatusCode = (int)SstStatusCode.Success,
        });
        logic.Update();

        Assert.False(logic.IsEntityPendingDelete(5));
        Assert.Null(logic.GlobalAlert);
    }

    /// <summary>OC1-I004 SC4: Failure ACK clears pending flag and sets a GlobalAlert.</summary>
    [Fact]
    public void DeleteEntity_FailureAck_ClearsPendingAndSetsAlert()
    {
        var (logic, _, _, ackQueue) = CreateSut();
        logic.DeleteEntity(5);

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            EntityId   = 5,
            StatusCode = (int)SstStatusCode.UnknownDescriptorType, // any failure code
        });
        logic.Update();

        Assert.False(logic.IsEntityPendingDelete(5));
        Assert.NotNull(logic.GlobalAlert);
    }

    /// <summary>OC1-I004 SC5: ACK for an entity that was never in pending-delete is ignored.</summary>
    [Fact]
    public void DeleteEntity_UnrelatedAck_Ignored()
    {
        var (logic, _, _, ackQueue) = CreateSut();
        // No DeleteEntity call.

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            EntityId   = 99,
            StatusCode = (int)SstStatusCode.Success,
        });
        logic.Update();

        Assert.False(logic.IsEntityPendingDelete(99));
        Assert.Null(logic.GlobalAlert);
    }

    // ── OC1-I005: StartPersonalRouteAuthoring ─────────────────────────────────

    /// <summary>OC1-I005 SC1: StartPersonalRouteAuthoring publishes CMD_DRAW_PERSONAL_ROUTE.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_PublishesCmdDrawPersonalRoute()
    {
        var (logic, cmdWriter, _, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        cmdWriter.Verify(w => w.Write(It.Is<MapCommandRequest>(r =>
            r.Type == CommandType.CMD_DRAW_PERSONAL_ROUTE)),
            Times.Once);
    }

    /// <summary>OC1-I005 SC2: payload must contain the entity ID.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_PayloadContainsEntityId()
    {
        var (logic, cmdWriter, _, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        cmdWriter.Verify(w => w.Write(It.Is<MapCommandRequest>(r =>
            r.CommandArgsJson.Contains("\"entityId\":10"))),
            Times.Once);
    }

    /// <summary>OC1-I005 SC3: payload must contain a non-empty contextId.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_PayloadContainsContextId()
    {
        var (logic, cmdWriter, _, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        cmdWriter.Verify(w => w.Write(It.Is<MapCommandRequest>(r =>
            r.CommandArgsJson.Contains("\"contextId\"") &&
            r.CommandArgsJson.Length > 20)),
            Times.Once);
    }

    /// <summary>OC1-I005 SC4: ActiveContextId is updated to a non-empty GUID.</summary>
    [Fact]
    public void StartPersonalRouteAuthoring_UpdatesActiveContextId()
    {
        var (logic, _, _, _) = CreateSut();
        logic.StartPersonalRouteAuthoring(10);
        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
    }
}
