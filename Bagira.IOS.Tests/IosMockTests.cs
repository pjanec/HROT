using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using FDP.Toolkit.DER;
using Moq;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="IosMock"/>.
///
/// <para>Verifies the orchestration contract:
/// <list type="bullet">
///   <item>Per-frame <see cref="IosMock.Update"/> drives <see cref="IosLogic.Update"/> and
///   propagates state correctly down to the panels.</item>
///   <item><see cref="IosMock.Dispose"/> cleans up without error and prevents
///   subsequent calls.</item>
/// </list>
/// </para>
///
/// <para>No Raylib window or ImGui context is required — <see cref="IosMock.DrawUI"/>
/// contains only commented-out stubs (Phase P9 placeholder) and is therefore
/// not tested here.</para>
/// </summary>
public class IosMockTests
{
    // ── Test factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fully wired <see cref="IosMock"/> backed by in-process stubs.
    /// Returns the mock together with the inner <see cref="IosLogic"/> and the
    /// event queues so individual tests can drive state from outside.
    /// </summary>
    private static (
        IosMock                                  Mock,
        IosLogic                                 Logic,
        ConcurrentEventQueue<MapClickEvent>       ClickQueue,
        ConcurrentEventQueue<SelectionChangedEvent> SelectionQueue,
        MissionPanel                             MissionPanel,
        SpawnerPanel                             SpawnerPanel,
        InteractionPanel                         InteractionPanel)
        CreateSut()
    {
        // All DDS writers are no-ops in tests.
        var configWriter      = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter      = new Mock<IDdsWriter<CreateEntityRequest>>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();

        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEvent>();

        var logic = new IosLogic(
            repo:                new DerRepo(),
            missionEditorService: missionSvc.Object,
            contextMenuLogic:    contextMenuLogic.Object,
            transactionManager:  transactionMgr.Object,
            configWriter:        configWriter.Object,
            createEntityWriter:  createWriter.Object,
            clickQueue:          clickQueue,
            selectionQueue:      selectionQueue,
            interactionPanel:    interactionPanel);

        var configPanel  = new ConfigPanel();
        var orbatPanel   = new OrbatPanel();
        var missionPanel = new MissionPanel();
        var spawnerPanel = new SpawnerPanel();

        var mock = new IosMock(
            logic:            logic,
            configPanel:      configPanel,
            orbatPanel:       orbatPanel,
            missionPanel:     missionPanel,
            interactionPanel: interactionPanel,
            spawnerPanel:     spawnerPanel);

        return (mock, logic, clickQueue, selectionQueue, missionPanel, spawnerPanel, interactionPanel);
    }

    // ── Smoke / lifecycle ─────────────────────────────────────────────────────

    [Fact]
    public void Update_WithMinimalSetup_DoesNotThrow()
    {
        var (mock, _, _, _, _, _, _) = CreateSut();

        var ex = Record.Exception(() => mock.Update(0f));

        Assert.Null(ex);
    }

    [Fact]
    public void Update_CalledMultipleTimes_DoesNotThrow()
    {
        var (mock, _, _, _, _, _, _) = CreateSut();

        var ex = Record.Exception(() =>
        {
            for (var i = 0; i < 10; i++)
                mock.Update(0.016f);
        });

        Assert.Null(ex);
    }

    // ── Selected entity propagation ───────────────────────────────────────────

    [Fact]
    public void Update_PropagatesSelectedEntityId_ToMissionPanel()
    {
        var (mock, logic, _, _, missionPanel, _, _) = CreateSut();

        logic.SelectEntity(42);
        mock.Update(0f);

        Assert.Equal(42, missionPanel.SelectedEntityId);
    }

    [Fact]
    public void Update_SelectionChanged_PropagatesNewId_ToMissionPanel()
    {
        var (mock, logic, _, _, missionPanel, _, _) = CreateSut();

        logic.SelectEntity(1);
        mock.Update(0f);
        Assert.Equal(1, missionPanel.SelectedEntityId);

        logic.SelectEntity(99);
        mock.Update(0f);
        Assert.Equal(99, missionPanel.SelectedEntityId);
    }

    // ── Spawner flag consumption ──────────────────────────────────────────────

    [Fact]
    public void Update_SpawnerRequested_IsClearedAfterUpdate()
    {
        var (mock, logic, _, _, _, _, _) = CreateSut();

        logic.OpenSpawner();
        Assert.True(logic.SpawnerRequested);

        mock.Update(0f);

        Assert.False(logic.SpawnerRequested);
    }

    [Fact]
    public void Update_SpawnerNotRequested_FlagRemainsOff()
    {
        var (mock, logic, _, _, _, _, _) = CreateSut();

        mock.Update(0f);

        Assert.False(logic.SpawnerRequested);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledOnce_DoesNotThrow()
    {
        var (mock, _, _, _, _, _, _) = CreateSut();

        var ex = Record.Exception(() => mock.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var (mock, _, _, _, _, _, _) = CreateSut();

        mock.Dispose();
        var ex = Record.Exception(() => mock.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Update_AfterDispose_ThrowsObjectDisposedException()
    {
        var (mock, _, _, _, _, _, _) = CreateSut();

        mock.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mock.Update(0f));
    }

    [Fact]
    public void DrawUI_AfterDispose_ThrowsObjectDisposedException()
    {
        var (mock, _, _, _, _, _, _) = CreateSut();

        mock.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mock.DrawUI());
    }

    // ── Logic property ────────────────────────────────────────────────────────

    [Fact]
    public void Logic_Property_ReturnsInjectedInstance()
    {
        var (mock, logic, _, _, _, _, _) = CreateSut();

        Assert.Same(logic, mock.Logic);
    }
}
