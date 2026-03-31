using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Hrot.Map.Common.Dds;
using FDP.Toolkit.DER;
using ImGuiNET;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="ExConMock"/>.
///
/// <para>Verifies the orchestration contract:
/// <list type="bullet">
///   <item>Per-frame <see cref="ExConMock.Update"/> drives <see cref="ExConLogic.Update"/> and
///   propagates state correctly down to the panels.</item>
///   <item><see cref="ExConMock.Dispose"/> cleans up without error and prevents
///   subsequent calls.</item>
/// </list>
/// </para>
///
/// <para>No Raylib window or ImGui context is required — <see cref="ExConMock.DrawUI"/>
/// contains only commented-out stubs (Phase P9 placeholder) and is therefore
/// not tested here.</para>
/// </summary>
public class IosMockTests
{
    // ── Test factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fully wired <see cref="ExConMock"/> backed by in-process stubs.
    /// Returns the mock together with the inner <see cref="ExConLogic"/> and the
    /// event queues so individual tests can drive state from outside.
    /// </summary>
    private static (
        ExConMock                                  Mock,
        ExConLogic                                 Logic,
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

        var logic = new ExConLogic(
            repo:                new DerRepo(),
            missionEditorService: missionSvc.Object,
            contextMenuLogic:    contextMenuLogic.Object,
            transactionManager:  transactionMgr.Object,
            configWriter:        configWriter.Object,
            createEntityWriter:  createWriter.Object,
            clickQueue:          clickQueue,
            selectionQueue:      selectionQueue,
            interactionPanel:    interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>());

        var configPanel  = new ConfigPanel();
        var orbatPanel   = new OrbatPanel();
        var missionPanel = new MissionPanel();
        var spawnerPanel = new SpawnerPanel();

        var mock = new ExConMock(
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

/// <summary>
/// Tests that verify <see cref="ExConMock.DrawUI"/> opens the GlobalAlert modal
/// when <see cref="ExConLogic.GlobalAlert"/> is non-null.
///
/// <para>These tests run against a real headless ImGui context and are
/// serialised via the "ImGui Sequential" collection.</para>
/// </summary>
[Collection("ImGui Sequential")]
public class IosMockUITests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="ExConMock"/> whose <see cref="ExConLogic.GlobalAlert"/>
    /// is non-null, ready to trigger the "Entity Error" popup in
    /// <see cref="ExConMock.DrawUI"/>.
    /// </summary>
    private static ExConMock CreateMockWithGlobalAlert()
    {
        var repo            = new DerRepo();
        var configWriter    = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter    = new Mock<IDdsWriter<CreateEntityRequest>>();
        var transactionMgr  = new Mock<IRequestTransactionManager>();
        var missionSvc      = new Mock<IMissionEditorService>();
        var contextMenuLogic = new Mock<IContextMenuLogic>();
        var interactionPanel = new InteractionPanel();
        var clickQueue      = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue  = new ConcurrentEventQueue<SelectionChangedEvent>();
        var ackQueue        = new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>();

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

        // Enqueue a Phase-2 error ACK so that Update() sets _globalAlert.
        ackQueue.Enqueue(new CreateUpdateDeleteEntityAck
        {
            RequestId  = Guid.NewGuid(),
            EntityId   = 1,
            StatusCode = (int)NedStatusCode.EntityNotFound,
        });
        logic.Update(); // processes the ACK → _globalAlert is now non-null

        return new ExConMock(
            logic:            logic,
            configPanel:      new ConfigPanel(),
            orbatPanel:       new OrbatPanel(),
            missionPanel:     new MissionPanel(),
            interactionPanel: interactionPanel,
            spawnerPanel:     new SpawnerPanel(),
            useDockSpace:     false);    // no dockspace in headless tests
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>GlobalAlert</c> is non-null, <see cref="ExConMock.DrawUI"/> must
    /// call <c>ImGui.OpenPopup("Entity Error")</c>.  This is verified by
    /// checking that the named popup is open immediately after the draw call,
    /// before the frame is consumed by <c>Render()</c>.
    /// </summary>
    [Fact]
    public void DrawUI_WhenGlobalAlertIsSet_EntityErrorPopupIsOpen()
    {
        using var mock = CreateMockWithGlobalAlert();

        Assert.NotNull(mock.Logic.GlobalAlert); // pre-condition

        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1024, 768);
        io.DeltaTime   = 1.0f / 60.0f;
        io.Fonts.AddFontDefault();
        io.Fonts.Build();

        bool popupOpen = false;
        try
        {
            ImGui.NewFrame();
            mock.DrawUI();  // calls OpenPopup("Entity Error") then BeginPopupModal

            // Check popup state BEFORE Render() — the popup is open in the
            // ImGui stack from the OpenPopup call made inside DrawUI().
            popupOpen = ImGui.IsPopupOpen("Entity Error");

            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(ctx);
        }

        Assert.True(popupOpen,
            "ImGui.OpenPopup(\"Entity Error\") must be called when GlobalAlert is non-null, " +
            "making the popup visible in the same frame.");
    }

    /// <summary>
    /// Confirms the entire DrawUI() call with an active alert completes
    /// without exception (i.e. the popup Begin/End pair is balanced).
    /// </summary>
    [Fact]
    public void DrawUI_WhenGlobalAlertIsSet_DrawCompletesWithoutException()
    {
        using var mock = CreateMockWithGlobalAlert();

        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1024, 768);
        io.DeltaTime   = 1.0f / 60.0f;
        io.Fonts.AddFontDefault();
        io.Fonts.Build();

        Exception? ex = null;
        try
        {
            ImGui.NewFrame();
            ex = Record.Exception(() => mock.DrawUI());
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(ctx);
        }

        Assert.Null(ex);
    }
}
