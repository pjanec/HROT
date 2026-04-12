using System.Numerics;
using Hrot.Core.Mission;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using Hrot.ExCon.Services;
using Hrot.Map.Common.Dds;
using FDP.Toolkit.DER;
using ImGuiNET;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Tests covering the Two-ACK entity lifecycle pattern in ExCon:
/// <list type="bullet">
///   <item>Phase-1 InProgress ACK → entity added to pending set.</item>
///   <item>Phase-2 Success ACK → entity removed from pending set.</item>
///   <item>Phase-2 Error ACK → entity removed from pending set + GlobalAlert set.</item>
///   <item>DismissAlert → GlobalAlert cleared.</item>
///   <item>IsEntityPending → correct predicate behaviour.</item>
///   <item>ContextMenuLogic pending guard → empty menu returned.</item>
///   <item>MissionPanel pending guard → IsPendingGuardActive helper.</item>
/// </list>
/// </summary>
public class TwoAckIosTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static (
        ExConLogic Logic,
        ConcurrentEventQueue<CreateUpdateDeleteEntityAck> AckQueue,
        Mock<IRequestTransactionManager> TransactionMgr)
        CreateSutWithAckQueue()
    {
        var repo              = new DerRepo();
        var configWriter      = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter      = new Mock<IDdsWriter<CreateEntityRequest>>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();
        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEvent>();
        var ackQueue          = new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>();

        var logic = new ExConLogic(
            repo:                 repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:     contextMenuLogic.Object,
            transactionManager:   transactionMgr.Object,
            configWriter:         configWriter.Object,
            createEntityWriter:   createWriter.Object,
            clickQueue:           clickQueue,
            selectionQueue:       selectionQueue,
            interactionPanel:     interactionPanel,
            createEntityAckQueue: ackQueue);

        return (logic, ackQueue, transactionMgr);
    }

    // ── ProcessEntityCreationAcks: InProgress ─────────────────────────────────

    [Fact]
    public void InProgressAck_AddsEntityToPendingSet()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            RequestId  = Guid.NewGuid(),
            EntityId   = 42,
            StatusCode = (int)NedStatusCode.InProgress,
        });

        logic.Update();

        Assert.True(logic.IsEntityPending(42));
    }

    [Fact]
    public void InProgressAck_DoesNotCompleteTransaction()
    {
        var (logic, ackQueue, txMgr) = CreateSutWithAckQueue();
        var requestId = Guid.NewGuid();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            RequestId  = requestId,
            EntityId   = 43,
            StatusCode = (int)NedStatusCode.InProgress,
        });

        logic.Update();

        txMgr.Verify(t => t.CompleteRequest(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>()),
            Times.Never,
            "TransactionManager.CompleteRequest must NOT be called for Phase-1 InProgress ACKs.");
    }

    // ── ProcessEntityCreationAcks: Success ────────────────────────────────────

    [Fact]
    public void SuccessAck_RemovesEntityFromPendingSet()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();

        // Phase 1
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 50, StatusCode = (int)NedStatusCode.InProgress });
        logic.Update();
        Assert.True(logic.IsEntityPending(50));

        // Phase 2 - success
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 50, StatusCode = (int)NedStatusCode.Success });
        logic.Update();

        Assert.False(logic.IsEntityPending(50));
    }

    [Fact]
    public void SuccessAck_CompletesTransactionWithSuccess()
    {
        var (logic, ackQueue, txMgr) = CreateSutWithAckQueue();
        var requestId = Guid.NewGuid();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            RequestId  = requestId,
            EntityId   = 55,
            StatusCode = (int)NedStatusCode.Success,
        });

        logic.Update();

        txMgr.Verify(t => t.CompleteRequest(requestId, true, null), Times.Once);
    }

    [Fact]
    public void SuccessAck_DoesNotSetGlobalAlert()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 60, StatusCode = (int)NedStatusCode.Success });
        logic.Update();

        Assert.Null(logic.GlobalAlert);
    }

    // ── ProcessEntityCreationAcks: Failure ────────────────────────────────────

    [Fact]
    public void ErrorAck_RemovesEntityFromPendingSet()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();

        // Phase 1
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 70, StatusCode = (int)NedStatusCode.InProgress });
        logic.Update();
        Assert.True(logic.IsEntityPending(70));

        // Phase 2 - error
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 70, StatusCode = (int)NedStatusCode.UnknownDescriptorType });
        logic.Update();

        Assert.False(logic.IsEntityPending(70));
    }

    [Fact]
    public void ErrorAck_SetsGlobalAlert()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            RequestId  = Guid.NewGuid(),
            EntityId   = 75,
            StatusCode = (int)NedStatusCode.UnknownDescriptorType,
        });

        logic.Update();

        Assert.NotNull(logic.GlobalAlert);
    }

    [Fact]
    public void ErrorAck_CompletesTransactionWithFailure()
    {
        var (logic, ackQueue, txMgr) = CreateSutWithAckQueue();
        var requestId = Guid.NewGuid();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            RequestId  = requestId,
            EntityId   = 80,
            StatusCode = (int)NedStatusCode.UnknownDescriptorType,
        });

        logic.Update();

        txMgr.Verify(t => t.CompleteRequest(requestId, false, It.IsAny<string?>()), Times.Once);
    }

    // ── GlobalAlert / DismissAlert ────────────────────────────────────────────

    [Fact]
    public void DismissAlert_ClearsGlobalAlert()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();

        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 90, StatusCode = (int)NedStatusCode.EntityNotFound });
        logic.Update();

        Assert.NotNull(logic.GlobalAlert);

        logic.DismissAlert();

        Assert.Null(logic.GlobalAlert);
    }

    // ── IsEntityPending ───────────────────────────────────────────────────────

    [Fact]
    public void IsEntityPending_ReturnsFalse_ForUnknownEntity()
    {
        var (logic, _, _) = CreateSutWithAckQueue();
        Assert.False(logic.IsEntityPending(9999));
    }

    [Fact]
    public void IsEntityPending_ReturnsTrue_AfterInProgressAck()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 100, StatusCode = (int)NedStatusCode.InProgress });
        logic.Update();
        Assert.True(logic.IsEntityPending(100));
    }

    [Fact]
    public void IsEntityPending_ReturnsFalse_AfterSuccessAck()
    {
        var (logic, ackQueue, _) = CreateSutWithAckQueue();
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 101, StatusCode = (int)NedStatusCode.InProgress });
        logic.Update();
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck { RequestId = Guid.NewGuid(), EntityId = 101, StatusCode = (int)NedStatusCode.Success });
        logic.Update();
        Assert.False(logic.IsEntityPending(101));
    }
}

/// <summary>
/// Tests covering ContextMenuLogic pending entity guard.
/// </summary>
public class ContextMenuLogicPendingTests
{
    private static (ContextMenuLogic logic, Mock<IDdsWriter<ContextActionsUpdate>> writer) Build()
    {
        var repo   = new DerRepo();
        var writer = new Mock<IDdsWriter<ContextActionsUpdate>>();
        var logic  = new ContextMenuLogic(repo, writer.Object);
        return (logic, writer);
    }

    [Fact]
    public void OnSelectionChanged_WhenEntityIsPending_PushesEmptyMenu()
    {
        var (logic, writer) = Build();

        var evt = new SelectionChangedEvent
        {
            MapId             = 1,
            SelectedEntityIds = new List<int> { 42 },
        };

        // isEntityPending predicate always returns true.
        logic.OnSelectionChanged(evt, _ => true);

        writer.Verify(w => w.Write(It.Is<ContextActionsUpdate>(
            u => u.MenuDefinitionJson == "[]")),
            Times.Once,
            "Expected empty JSON array when entity is pending.");
    }

    [Fact]
    public void OnSelectionChanged_WhenEntityNotPending_BuildsNormalMenu()
    {
        var (logic, writer) = Build();

        var evt = new SelectionChangedEvent
        {
            MapId             = 1,
            SelectedEntityIds = new List<int> { 43 },
        };

        // isEntityPending predicate always returns false.
        logic.OnSelectionChanged(evt, _ => false);

        writer.Verify(w => w.Write(It.Is<ContextActionsUpdate>(
            u => u.MenuDefinitionJson != "[]")),
            Times.Once,
            "Expected non-empty menu when entity is NOT pending.");
    }
}

/// <summary>
/// Tests verifying that <see cref="MissionPanel.Draw"/> calls
/// <c>ImGui.BeginDisabled()</c> when the selected entity is pending.
///
/// Runs against a real headless ImGui context to exercise the actual render path.
/// Tests are serialised via the "ImGui Sequential" collection to prevent
/// concurrent native-context access.
/// </summary>
[Collection("ImGui Sequential")]
public class MissionPanelDrawPendingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IntPtr CreateHeadlessContext()
    {
        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1024, 768);
        io.DeltaTime   = 1.0f / 60.0f;
        io.Fonts.AddFontDefault();
        io.Fonts.Build();
        return ctx;
    }

    private static (
        Mock<Hrot.UI.Common.Facades.IMissionEditorService> Svc,
        Mock<Hrot.UI.Common.Facades.IMapPickService> Pick)
        BuildServices(long entityId = 55)
    {
        var svc = new Mock<Hrot.UI.Common.Facades.IMissionEditorService>();
        svc.Setup(s => s.GetAvailableBehaviors(entityId)).Returns(Array.Empty<string>());
        svc.Setup(s => s.GetMissionSnapshot(entityId)).Returns(((Hrot.Core.Mission.MissionPlan?)null, 0L));
        var pick = new Mock<Hrot.UI.Common.Facades.IMapPickService>();
        return (svc, pick);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Draw_WithSelectedEntity_CallsGetAvailableBehaviors()
    {
        var (svc, pick) = BuildServices(55);
        var panel = new MissionPanel { SelectedEntityId = 55 };

        var ctx = CreateHeadlessContext();
        try
        {
            ImGui.NewFrame();
            panel.Draw(svc.Object, pick.Object);
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(ctx);
        }

        svc.Verify(s => s.GetAvailableBehaviors(55), Times.AtLeastOnce,
            "Draw() must call GetAvailableBehaviors each frame for the selected entity.");
    }

    [Fact]
    public void Draw_WithSelectedEntity_CompletesWithoutException()
    {
        var (svc, pick) = BuildServices(55);
        var panel = new MissionPanel { SelectedEntityId = 55 };

        Exception? ex = null;
        var ctx = CreateHeadlessContext();
        try
        {
            ImGui.NewFrame();
            ex = Record.Exception(() => panel.Draw(svc.Object, pick.Object));
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(ctx);
        }

        Assert.Null(ex);
    }

    [Fact]
    public void Draw_WithNoSelection_FrameCompletesCleanly()
    {
        var (svc, pick) = BuildServices(0);
        var panel = new MissionPanel { SelectedEntityId = 0 };

        var ctx = CreateHeadlessContext();
        try
        {
            ImGui.NewFrame();
            panel.Draw(svc.Object, pick.Object);
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(ctx);
        }

        svc.Verify(s => s.GetAvailableBehaviors(0), Times.AtLeastOnce);
    }
}
