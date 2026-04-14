using Hrot.ExCon.Logic;
using Hrot.Core.Network;
using Hrot.Core.Mission;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using Hrot.ExCon.Services;
using Fdp.Toolkit.DER;
using Moq;

namespace Hrot.ExCon.Tests;

// ── Test collection: disable parallelism ──────────────────────────────────────

[CollectionDefinition("Integration", DisableParallelization = true)]
public class IntegrationTestCollection { }

// ── Shared infrastructure for integration tests ───────────────────────────────

/// <summary>
/// Lightweight "IG stub" used in integration tests: exposes a capturing egress
/// writer (inbound from the ExCon) and two event queues (outbound from the IG).
/// </summary>
internal sealed class IgStub
{
    /// <summary>Captures all egress messages published by the ExCon.</summary>
    public CapturingEgressWriters EgressCapture { get; } = new();

    /// <summary>Click-event queue that the ExCon logic reads from (IG -> ExCon).</summary>
    public ConcurrentEventQueue<MapClickEventDto> ClickQueue { get; } = new();

    /// <summary>Selection-change queue that the ExCon logic reads from (IG -> ExCon).</summary>
    public ConcurrentEventQueue<SelectionChangedEventDto> SelectionQueue { get; } = new();
}

/// <summary>
/// Lightweight "SimHost stub" used in integration tests: exposes a
/// <see cref="ICommandGateway"/> mock for controlling commit results.
/// </summary>
internal sealed class SimHostStub
{
    /// <summary>Mock gateway for commit/control requests.</summary>
    public Mock<ICommandGateway> Gateway { get; } = new();
}

// ── Test fixture factory ──────────────────────────────────────────────────────

internal static class IntegrationFactory
{
    /// <summary>
    /// Creates a fully wired ExConLogic + ExConMock where all DDS writers/readers
    /// are replaced by in-process stubs.
    /// </summary>
    public static (ExConMock Mock, ExConLogic Logic, DerRepo Repo, MissionEditorService MissionSvc, InteractionPanel Log)
        Create(IgStub igStub, SimHostStub simHostStub)
    {
        var repo       = new DerRepo();
        var missionSvc = new MissionEditorService(
            repo,
            simHostStub.Gateway.Object,
            commitTimeoutMs: 200);

        var contextMenuLogic = new ContextMenuLogic(repo, igStub.EgressCapture);
        var transactionMgr   = new RequestTransactionManager();
        var interactionPanel = new InteractionPanel();

        var logic = new ExConLogic(
            repo:                repo,
            missionEditorService: missionSvc,
            contextMenuLogic:    contextMenuLogic,
            transactionManager:  transactionMgr,
            egressWriters:       igStub.EgressCapture,
            clickQueue:          igStub.ClickQueue,
            selectionQueue:      igStub.SelectionQueue,
            interactionPanel:    interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<EntityLifecycleAckDto>());

        var mock = new ExConMock(
            logic:            logic,
            configPanel:      new ConfigPanel(),
            orbatPanel:       new OrbatPanel(),
            missionPanel:     new MissionPanel(),
            interactionPanel: interactionPanel,
            spawnerPanel:     new SpawnerPanel());

        return (mock, logic, repo, missionSvc, interactionPanel);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// ExCon.9.1 -- Standalone ExCon integration tests
// ──────────────────────────────────────────────────────────────────────────────

[Collection("Integration")]
public class StandaloneIosTests
{
    private static (ExConMock Mock, ExConLogic Logic, DerRepo Repo, InteractionPanel Log) Create()
    {
        var (mock, logic, repo, _, log) = IntegrationFactory.Create(new IgStub(), new SimHostStub());
        return (mock, logic, repo, log);
    }

    // ── Boot and update cycle ─────────────────────────────────────────────────

    [Fact]
    public void Boot_MultipleUpdateFrames_NeverThrow()
    {
        var (mock, _, _, _) = Create();

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 10; i++) mock.Update(0f);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Boot_DrawUI_DoesNotThrow()
    {
        var (mock, _, _, _) = Create();

        var ex = Record.Exception(() => mock.DrawUI());

        Assert.Null(ex);
    }

    [Fact]
    public void Boot_NoSpontaneousWritesWithoutOperatorAction()
    {
        var ig  = new IgStub();
        var (mock, _, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        for (int i = 0; i < 3; i++) mock.Update(0f);

        Assert.Empty(ig.EgressCapture.WrittenConfigs);
        Assert.Empty(ig.EgressCapture.WrittenCreateCommands);
    }

    // ── ORBAT panel hierarchy ─────────────────────────────────────────────────

    [Fact]
    public void Standalone_OrbatPanel_EmptyRepo_ReturnsNoNodes()
    {
        var (_, _, repo, _) = Create();
        var orbat = new OrbatPanel();

        Assert.Empty(orbat.GetVisibleNodes(repo));
    }

    [Fact]
    public void Standalone_OrbatPanel_WithHierarchy_ReturnsCorrectDepths()
    {
        var (_, _, repo, _) = Create();

        // HQ -> [Tank1, Tank2]
        var hq = repo.CreateEntity(1, 100);
        hq.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "HQ", CommanderId = 0,
            Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString() });

        var t1 = repo.CreateEntity(2, 101);
        t1.SetDescriptor(new EntityInfoDescriptor { EntityId = 2, Name = "Tank1", CommanderId = 1,
            Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString() });

        var t2 = repo.CreateEntity(3, 101);
        t2.SetDescriptor(new EntityInfoDescriptor { EntityId = 3, Name = "Tank2", CommanderId = 1,
            Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString() });

        var orbat = new OrbatPanel();
        orbat.ToggleExpanded(1); // Expand HQ so children are visible.

        var nodes = orbat.GetVisibleNodes(repo);

        Assert.Equal(3, nodes.Count);
        Assert.Equal("HQ",    nodes[0].Name); Assert.Equal(0, nodes[0].Depth);
        Assert.Equal("Tank1", nodes[1].Name); Assert.Equal(1, nodes[1].Depth);
        Assert.Equal("Tank2", nodes[2].Name); Assert.Equal(1, nodes[2].Depth);
    }

    [Fact]
    public void Standalone_OrbatPanel_CollapsedNode_HidesChildren()
    {
        var (_, _, repo, _) = Create();
        var hq = repo.CreateEntity(1, 100);
        hq.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "HQ", CommanderId = 0,
            Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString() });
        var t1 = repo.CreateEntity(2, 101);
        t1.SetDescriptor(new EntityInfoDescriptor { EntityId = 2, Name = "Tank1", CommanderId = 1,
            Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString() });

        var orbat = new OrbatPanel();
        // HQ not expanded -- child must not appear.
        var nodes = orbat.GetVisibleNodes(repo);

        Assert.Single(nodes);
        Assert.Equal("HQ", nodes[0].Name);
    }

    // ── SelectEntity ──────────────────────────────────────────────────────────

    [Fact]
    public void Standalone_SelectNonExistentEntity_DoesNotThrow()
    {
        var (mock, logic, _, _) = Create();

        var ex = Record.Exception(() =>
        {
            logic.SelectEntity(9999);
            mock.Update(0f);
        });

        Assert.Null(ex);
        Assert.Equal(9999, logic.SelectedEntityId);
    }

    // ── StartPlacementMode ────────────────────────────────────────────────────

    [Fact]
    public void Standalone_StartPlacementMode_SetsContextIdAndWritesConfig()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        logic.StartPlacementMode(100L);

        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
        Assert.Single(ig.EgressCapture.WrittenMapCommands);
        Assert.Contains(logic.ActiveContextId.ToString("N"),
            ig.EgressCapture.WrittenMapCommands[0].CommandArgsJson);
    }

    [Fact]
    public void Standalone_ClickWithNoPlacementType_IsDroppedGracefully()
    {
        var ig  = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        // Enqueue a click before any placement mode is set -- must be dropped.
        ig.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = Guid.NewGuid(),
        });
        logic.Update();

        Assert.Empty(ig.EgressCapture.WrittenCreateCommands);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Standalone_Dispose_ThenUpdate_Throws()
    {
        var (mock, _, _, _) = Create();
        mock.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mock.Update(0f));
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// ExCon.9.2 -- ExCon + IG stub integration tests
// ──────────────────────────────────────────────────────────────────────────────

[Collection("Integration")]
public class IosIgIntegrationTests
{
    // ── Click event -> CreateEntityCommand ────────────────────────────────────

    [Fact]
    public void ClickEvent_WithMatchingContext_ProducesCreateEntityCommand()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        logic.StartPlacementMode(100L);

        ig.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
            Latitude             = 45.0,
            Longitude            = 12.0
        });
        logic.Update();

        Assert.Single(ig.EgressCapture.WrittenCreateCommands);
    }

    [Fact]
    public void ClickEvent_WithStaleContext_IsDropped()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        logic.StartPlacementMode(100L);

        ig.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = Guid.NewGuid(), // Does not match active context.
            Latitude             = 1.0,
            Longitude            = 1.0
        });
        logic.Update();

        Assert.Empty(ig.EgressCapture.WrittenCreateCommands);
    }

    [Fact]
    public void ClickEvent_ThreeConsecutive_ProduceThreeCreateCommands()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        logic.StartPlacementMode(100L);
        var ctx = logic.ActiveContextId;

        for (int i = 0; i < 3; i++)
        {
            ig.ClickQueue.Enqueue(new MapClickEventDto
            {
                InteractionContextId = ctx,
                Latitude             = i,
                Longitude            = i
            });
        }
        logic.Update();

        Assert.Equal(3, ig.EgressCapture.WrittenCreateCommands.Count);
    }

    [Fact]
    public void ClickEvent_CreateCommand_CarriesCorrectTkbTypeAndPosition()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());
        const long tkbType = 105L;
        const double lat = 51.5;
        const double lon = -0.1;

        logic.StartPlacementMode(tkbType);
        ig.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
            Latitude             = lat,
            Longitude            = lon
        });
        logic.Update();

        var cmd = ig.EgressCapture.WrittenCreateCommands.Single();
        Assert.Equal(tkbType, cmd.TkbType);
        Assert.Equal(lat, cmd.Latitude,  5);
        Assert.Equal(lon, cmd.Longitude, 5);
    }

    // ── SelectionChangedEventDto forwarding ───────────────────────────────────

    [Fact]
    public void SelectionChanged_IsLoggedInInteractionPanel()
    {
        var ig  = new IgStub();
        var (_, logic, _, _, log) = IntegrationFactory.Create(ig, new SimHostStub());

        ig.SelectionQueue.Enqueue(new SelectionChangedEventDto
        {
            MapId             = 1,
            SelectedEntityIds = new List<int> { 42 }
        });
        logic.Update();
        // Log entries queued during ProcessSelectionEvents (frame N) are drained
        // by DrainPendingLogs in the next Update call (frame N+1).
        logic.Update();

        Assert.Contains(log.Entries, e => e.Topic.Contains("Selection"));
    }

    [Fact]
    public void SelectionChanged_MultipleEntities_AllProcessedWithoutError()
    {
        var ig  = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        ig.SelectionQueue.Enqueue(new SelectionChangedEventDto
        {
            MapId             = 1,
            SelectedEntityIds = new List<int> { 10, 11, 12 }
        });

        var ex = Record.Exception(() => logic.Update());

        Assert.Null(ex);
    }

    // ── Config push -> IG captures it ─────────────────────────────────────────

    [Fact]
    public void ConfigPatch_IsReceivedByIgStub_WithCorrectContent()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());
        const string patch = @"{""view"":{""layers"":{""satellite"":true}}}";

        logic.SendConfigPatch(patch);

        Assert.Single(ig.EgressCapture.WrittenConfigs);
        Assert.Equal(patch, ig.EgressCapture.WrittenConfigs[0].ConfigJson);
    }

    [Fact]
    public void ConfigPatch_PlacementModeActivation_ContainsPlacementTool()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        logic.StartPlacementMode(100L);

        Assert.Single(ig.EgressCapture.WrittenMapCommands);
        Assert.Contains("PLACE", ig.EgressCapture.WrittenMapCommands[0].CommandType);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// ExCon.9.3 -- ExCon + SimHost stub integration tests
// ──────────────────────────────────────────────────────────────────────────────

[Collection("Integration")]
public class IosSimHostIntegrationTests
{
    // ── CommitMissionAsync via ICommandGateway ────────────────────────────────

    [Fact]
    public async Task CommitMissionAsync_GatewayReturnsSuccess_ResolvesPendingCommit()
    {
        var repo    = new DerRepo();
        var gateway = new Mock<ICommandGateway>();
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = true, NewVersion = 2 });
        using var svc = new MissionEditorService(repo, gateway.Object, commitTimeoutMs: 200);

        var plan   = new MissionPlan { Tasks = new List<MissionTask>() };
        var result = await svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 1);

        Assert.True(result.Success);
        Assert.Equal(2, result.NewVersion);
    }

    [Fact]
    public async Task CommitMissionAsync_GatewayReturnsVersionConflict_ReturnsFailureResult()
    {
        var repo    = new DerRepo();
        var gateway = new Mock<ICommandGateway>();
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = false, ErrorMessage = "ERR_VERSION_CONFLICT" });
        using var svc = new MissionEditorService(repo, gateway.Object, commitTimeoutMs: 200);

        var plan   = new MissionPlan { Tasks = new List<MissionTask>() };
        var result = await svc.CommitMissionAsync(entityId: 5, newPlan: plan, baseVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", result.ErrorMessage);
    }

    // ── Entity appears in ORBAT after repo update ─────────────────────────────

    [Fact]
    public void EntityAddedToRepo_AppearsInOrbatOnNextQuery()
    {
        var (_, _, repo, _, _) = IntegrationFactory.Create(new IgStub(), new SimHostStub());
        var orbat = new OrbatPanel();

        Assert.Empty(orbat.GetVisibleNodes(repo));

        // Simulate SimHost publishing an entity (production: via bridging handler).
        var entity = repo.CreateEntity(200, 100);
        entity.SetDescriptor(new EntityInfoDescriptor
        {
            EntityId    = 200,
            Name        = "T-72#1",
            CommanderId = 0,
            Affiliation = eForceIdentifier.FORCE_OPPOSING.ToString()
        });

        var nodes = orbat.GetVisibleNodes(repo);
        Assert.Single(nodes);
        Assert.Equal("T-72#1", nodes[0].Name);
    }

    [Fact]
    public void CreateEntityCommand_SendsCorrectPayload_ToEgressCapture()
    {
        var ig  = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());
        const long tkbType = 102L;

        logic.StartPlacementMode(tkbType);
        ig.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
            Latitude             = 48.9,
            Longitude            = 2.3
        });
        logic.Update();

        var cmd = ig.EgressCapture.WrittenCreateCommands.Single();
        Assert.Equal(tkbType, cmd.TkbType);
    }
}