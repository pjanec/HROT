using Hrot.ExCon.Logic;
using Hrot.Core.Network;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using FDP.Toolkit.DER;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for OC1-B004 — Entity Deletion Not Reflected in ExCon Inspector.
///
/// <para>Verifies that <see cref="ExConLogic.SelectedEntityId"/> is cleared when
/// the selected entity is deleted from the <see cref="IDerRepo"/>, and that the
/// inspector is unaffected when an unrelated entity is deleted.</para>
/// </summary>
public class ExConLogicEntityDeletionTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static (ExConLogic Logic, DerRepo Repo) CreateSut()
    {
        var repo              = new DerRepo();
        var egressWriters     = new Mock<IExConEgressWriters>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();
        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEventDto>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEventDto>();

        var logic = new ExConLogic(
            repo:                repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:    contextMenuLogic.Object,
            transactionManager:  transactionMgr.Object,
            egressWriters:       egressWriters.Object,
            clickQueue:          clickQueue,
            selectionQueue:      selectionQueue,
            interactionPanel:    interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<EntityLifecycleAckDto>());

        return (logic, repo);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// OC1-B004 SC1: When the currently-selected entity is deleted from the DER repo,
    /// <see cref="ExConLogic.SelectedEntityId"/> must be reset to 0 (no selection).
    /// </summary>
    [Fact]
    public void EntityDeleted_MatchingSelectedEntity_ClearsSelectedEntityId()
    {
        var (logic, repo) = CreateSut();
        repo.CreateEntity(42, tkbType: 100);
        logic.SelectEntity(42);
        Assert.Equal(42, logic.SelectedEntityId);

        repo.DeleteEntity(42);

        Assert.Equal(0, logic.SelectedEntityId);
    }

    /// <summary>
    /// OC1-B004 SC2: When an entity different from the currently-selected one is deleted,
    /// <see cref="ExConLogic.SelectedEntityId"/> must remain unchanged.
    /// </summary>
    [Fact]
    public void EntityDeleted_DifferentEntity_DoesNotClearSelectedEntityId()
    {
        var (logic, repo) = CreateSut();
        repo.CreateEntity(42, tkbType: 100);
        repo.CreateEntity(7,  tkbType: 100);
        logic.SelectEntity(42);

        repo.DeleteEntity(7);

        Assert.Equal(42, logic.SelectedEntityId);
    }

    /// <summary>
    /// OC1-B004 SC3: When no entity is selected (SelectedEntityId == 0) and an entity
    /// is deleted, no exception must be thrown.
    /// </summary>
    [Fact]
    public void EntityDeleted_NoEntitySelected_DoesNotThrow()
    {
        var (logic, repo) = CreateSut();
        repo.CreateEntity(42, tkbType: 100);
        // No SelectEntity call → SelectedEntityId == 0

        var ex = Record.Exception(() => repo.DeleteEntity(42));

        Assert.Null(ex);
        Assert.Equal(0, logic.SelectedEntityId);
    }

    /// <summary>
    /// After the ExConLogic is disposed, the EntityDeleted subscription must be removed
    /// so subsequent repo deletions do not access the disposed instance.
    /// </summary>
    [Fact]
    public void Dispose_RemovesEntityDeletedSubscription_NoExceptionAfterDispose()
    {
        var (logic, repo) = CreateSut();
        repo.CreateEntity(42, tkbType: 100);
        logic.SelectEntity(42);
        logic.Dispose();

        // Should not throw even though logic is disposed
        var ex = Record.Exception(() => repo.DeleteEntity(42));

        Assert.Null(ex);
    }
}
