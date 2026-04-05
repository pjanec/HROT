using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using Hrot.ExCon.Services;
using Hrot.Common.Events;
using FDP.Toolkit.DER;
using Fdp.Kernel;

namespace Hrot.ExCon.Tests;

// ── Test collection: disable parallelism ──────────────────────────────────────
// Real DDS integration tests must run on separate domain IDs to isolate topic
// traffic.  Even in this in-process suite, serialising the collection prevents
// any future migration to live participants from introducing flaky races.

[CollectionDefinition("Integration", DisableParallelization = true)]
public class IntegrationTestCollection { }

// ── Shared infrastructure for integration tests ───────────────────────────────

/// <summary>
/// Lightweight "IG stub" used in ExCon.9.2 tests: exposes two event queues
/// (inbound from the ExCon, outbound to the ExCon) so the test can assert that
/// configuration is received and click events are forwarded.
/// </summary>
internal sealed class IgStub
{
    /// <summary>Captures MapInteractionConfig messages pushed by the ExCon.</summary>
    public CapturingWriter<MapInteractionConfig> ConfigCapture { get; } = new();

    /// <summary>Click-event queue that the ExCon logic reads from (IG → ExCon).</summary>
    public ConcurrentEventQueue<MapClickEvent> ClickQueue { get; } = new();

    /// <summary>Selection-change queue that the ExCon logic reads from (IG → ExCon).</summary>
    public ConcurrentEventQueue<SelectionChangedEvent> SelectionQueue { get; } = new();
}

/// <summary>
/// Lightweight "SimHost stub" used in ExCon.9.3 tests: owns the ACK queue that
/// feeds the <see cref="MissionEditorService"/> and captures outgoing create
/// requests sent by the ExCon.
/// </summary>
internal sealed class SimHostStub
{
    /// <summary>Captures CreateEntityRequest messages published by the ExCon.</summary>
    public CapturingWriter<CreateEntityRequest> CreateCapture { get; } = new();

    /// <summary>Bus that feeds MissionControlAckEvent back into MissionEditorService (SimHost → ExCon).</summary>
    public FdpEventBus MissionBus { get; } = new();

    /// <summary>
    /// Delivers a successful <see cref="MissionControlAckEvent"/> for the given request ID
    /// (simulating SimHost accepting and processing the commit).
    /// </summary>
    public void DeliverAck(Guid requestId, MissionEditorService svc, long newVersion = 1)
    {
        MissionBus.Publish(new MissionControlAckEvent { RequestId = requestId, ErrorCode = 0, NewVersion = newVersion });
        MissionBus.SwapBuffers();
        svc.Poll();
    }

    /// <summary>
    /// Delivers a version-conflict rejection (simulating SimHost detecting a stale base version).
    /// </summary>
    public void DeliverVersionConflict(Guid requestId, MissionEditorService svc)
    {
        MissionBus.Publish(new MissionControlAckEvent { RequestId = requestId, ErrorCode = 7, NewVersion = 0 });
        MissionBus.SwapBuffers();
        svc.Poll();
    }
}

// ── Test fixture factory ──────────────────────────────────────────────────────

internal static class IntegrationFactory
{
    /// <summary>
    /// Creates a fully wired ExConLogic + IosMock where all DDS writers/readers
    /// are replaced by in-process stubs.  The <paramref name="igStub"/> and
    /// <paramref name="simHostStub"/> carry the queues used in ExCon.9.2 and
    /// ExCon.9.3 scenarios respectively.
    ///
    /// <para>No live DDS participant is created; tests execute in a single
    /// process without any socket or OS-resource allocation.</para>
    /// </summary>
    public static (ExConMock Mock, ExConLogic Logic, DerRepo Repo, MissionEditorService MissionSvc, InteractionPanel Log)
        Create(IgStub igStub, SimHostStub simHostStub)
    {
        var repo       = new DerRepo();
        var missionSvc = new MissionEditorService(
            repo,
            simHostStub.MissionBus,
            commitTimeoutMs: 200);          // Short timeout keeps tests fast.

        var contextMenuLogic = new ContextMenuLogic(repo, new CapturingWriter<ContextActionsUpdate>());
        var transactionMgr   = new RequestTransactionManager();
        var interactionPanel = new InteractionPanel();

        var logic = new ExConLogic(
            repo:                repo,
            missionEditorService: missionSvc,
            contextMenuLogic:    contextMenuLogic,
            transactionManager:  transactionMgr,
            configWriter:        igStub.ConfigCapture,
            createEntityWriter:  simHostStub.CreateCapture,
            clickQueue:          igStub.ClickQueue,
            selectionQueue:      igStub.SelectionQueue,
            interactionPanel:    interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>(),
            ingressHandlers:     new[] { (IIngressHandler)missionSvc });

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
// ExCon.9.1 — Standalone ExCon integration tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 1: Standalone ExCon validation.
///
/// Simulates booting <see cref="ExConMock"/> and <see cref="ExConLogic"/> with no
/// live network.  Verifies that:
/// <list type="bullet">
///   <item>Multiple update frames do not throw.</item>
///   <item>Panel hierarchy queries return correct results from an empty or
///   pre-populated repo without null-reference errors.</item>
///   <item>Imperative commands (<c>SelectEntity</c>,
///   <c>StartPlacementMode</c>) execute correctly in isolation.</item>
/// </list>
/// </summary>
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

        // DrawUI bodies are Phase P9 stubs — always safe to call.
        var ex = Record.Exception(() => mock.DrawUI());

        Assert.Null(ex);
    }

    [Fact]
    public void Boot_NoSpontaneousWritesWithoutOperatorAction()
    {
        var ig  = new IgStub();
        var sh  = new SimHostStub();
        var (mock, _, _, _, _) = IntegrationFactory.Create(ig, sh);

        for (int i = 0; i < 3; i++) mock.Update(0f);

        Assert.Empty(ig.ConfigCapture.Written);
        Assert.Empty(sh.CreateCapture.Written);
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

        // HQ → [Tank1, Tank2]
        var hq = repo.CreateEntity(1, 100);
        hq.SetDescriptor(new Hrot.NED.Descriptors.EntityInfo { EntityId = 1, Name = "HQ", CommanderId = 0,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });

        var t1 = repo.CreateEntity(2, 101);
        t1.SetDescriptor(new Hrot.NED.Descriptors.EntityInfo { EntityId = 2, Name = "Tank1", CommanderId = 1,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });

        var t2 = repo.CreateEntity(3, 101);
        t2.SetDescriptor(new Hrot.NED.Descriptors.EntityInfo { EntityId = 3, Name = "Tank2", CommanderId = 1,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });

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
        hq.SetDescriptor(new Hrot.NED.Descriptors.EntityInfo { EntityId = 1, Name = "HQ", CommanderId = 0,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });
        var t1 = repo.CreateEntity(2, 101);
        t1.SetDescriptor(new Hrot.NED.Descriptors.EntityInfo { EntityId = 2, Name = "Tank1", CommanderId = 1,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });

        var orbat = new OrbatPanel();
        // HQ not expanded — child must not appear.
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
        Assert.Single(ig.ConfigCapture.Written);
        Assert.Equal(logic.ActiveContextId, ig.ConfigCapture.Written[0].ActiveContextId);
    }

    [Fact]
    public void Standalone_ClickWithNoPlacementType_IsDroppedGracefully()
    {
        var ig  = new IgStub();
        var sh  = new SimHostStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, sh);

        // Enqueue a click before any placement mode is set — must be dropped.
        ig.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = Guid.NewGuid(),
            Position             = new GeoPoint { Latitude = 0, Longitude = 0 }
        });
        logic.Update();

        Assert.Empty(sh.CreateCapture.Written);
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
// ExCon.9.2 — ExCon + IG stub integration tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 2: ExCon + IG interaction pathways.
///
/// Uses a lightweight <see cref="IgStub"/> to simulate the IG:
/// <list type="bullet">
///   <item>IG emits <see cref="MapClickEvent"/> — ExCon converts to
///   <see cref="CreateEntityRequest"/>.</item>
///   <item>IG emits <see cref="SelectionChangedEvent"/> — ExCon forwards to
///   <see cref="ContextMenuLogic"/> and logs the interaction.</item>
///   <item>ExCon emits <see cref="MapInteractionConfig"/> — IG stub captures
///   it for assertion.</item>
/// </list>
/// </summary>
[Collection("Integration")]
public class IosIgIntegrationTests
{
    // ── Click event → CreateEntityRequest ────────────────────────────────────

    [Fact]
    public void ClickEvent_WithMatchingContext_ProducesCreateEntityRequest()
    {
        var ig = new IgStub();
        var sh = new SimHostStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, sh);

        logic.StartPlacementMode(100L);

        ig.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position             = new GeoPoint { Latitude = 45.0, Longitude = 12.0 }
        });
        logic.Update();

        Assert.Single(sh.CreateCapture.Written);
    }

    [Fact]
    public void ClickEvent_WithStaleContext_IsDropped()
    {
        var ig = new IgStub();
        var sh = new SimHostStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, sh);

        logic.StartPlacementMode(100L);

        ig.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = Guid.NewGuid(), // Does not match active context.
            Position             = new GeoPoint { Latitude = 1.0, Longitude = 1.0 }
        });
        logic.Update();

        Assert.Empty(sh.CreateCapture.Written);
    }

    [Fact]
    public void ClickEvent_ThreeConsecutive_ProduceThreeCreateRequests()
    {
        var ig = new IgStub();
        var sh = new SimHostStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, sh);

        logic.StartPlacementMode(100L);
        var ctx = logic.ActiveContextId;

        for (int i = 0; i < 3; i++)
        {
            ig.ClickQueue.Enqueue(new MapClickEvent
            {
                InteractionContextId = ctx,
                Position             = new GeoPoint { Latitude = i, Longitude = i }
            });
        }
        logic.Update();

        Assert.Equal(3, sh.CreateCapture.Written.Count);
    }

    [Fact]
    public void ClickEvent_CreateRequest_CarriesCorrectTkbTypeAndPosition()
    {
        var ig = new IgStub();
        var sh = new SimHostStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, sh);
        const long tkbType = 105L;

        logic.StartPlacementMode(tkbType);
        var pos = new GeoPoint { Latitude = 51.5, Longitude = -0.1 };
        ig.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position             = pos
        });
        logic.Update();

        var req = sh.CreateCapture.Written.Single();

        var masterDesc = req.InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityMaster)
            .EntityMaster;
        Assert.Equal(tkbType, masterDesc.TkbType);

        var geoDesc = req.InitialDescriptors
            .First(d => d._d == EDescriptorType.dtWorldPos)
            .WorldPos;
        Assert.Equal(pos.Latitude,  geoDesc.Pos.Latitude,  precision: 5);
        Assert.Equal(pos.Longitude, geoDesc.Pos.Longitude, precision: 5);
    }

    // ── SelectionChangedEvent forwarding ─────────────────────────────────────

    [Fact]
    public void SelectionChanged_IsLoggedInInteractionPanel()
    {
        var ig  = new IgStub();
        var (_, logic, _, _, log) = IntegrationFactory.Create(ig, new SimHostStub());

        ig.SelectionQueue.Enqueue(new SelectionChangedEvent
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

        ig.SelectionQueue.Enqueue(new SelectionChangedEvent
        {
            MapId             = 1,
            SelectedEntityIds = new List<int> { 10, 11, 12 }
        });

        var ex = Record.Exception(() => logic.Update());

        Assert.Null(ex);
    }

    // ── Config push → IG captures it ─────────────────────────────────────────

    [Fact]
    public void ConfigPatch_IsReceivedByIgStub_WithCorrectContent()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());
        const string patch = @"{""view"":{""layers"":{""satellite"":true}}}";

        logic.SendConfigPatch(patch);

        Assert.Single(ig.ConfigCapture.Written);
        Assert.Equal(patch, ig.ConfigCapture.Written[0].ConfigurationJson);
    }

    [Fact]
    public void ConfigPatch_PlacementModeActivation_ContainsPlacementTool()
    {
        var ig = new IgStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, new SimHostStub());

        logic.StartPlacementMode(100L);

        Assert.Single(ig.ConfigCapture.Written);
        Assert.Contains("PLACEMENT", ig.ConfigCapture.Written[0].ConfigurationJson);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// ExCon.9.3 — ExCon + SimHost stub integration tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 3: ExCon + SimHost interaction pathways.
///
/// Uses a lightweight <see cref="SimHostStub"/> to simulate SimHost:
/// <list type="bullet">
///   <item>ExCon sends <see cref="CreateEntityRequest"/> — SimHost stub
///   captures it.</item>
///   <item>SimHost delivers <see cref="MissionControlAck"/> via the ingress
///   queue — ExCon <see cref="MissionEditorService.Poll"/> resolves pending
///   commits.</item>
///   <item>Entity appears in the DER repo (simulating the SimHost publishing
///   its entity topics) — ORBAT panel reflects the update.</item>
/// </list>
/// </summary>
[Collection("Integration")]
public class IosSimHostIntegrationTests
{
    // ── MissionControlAck via Poll ────────────────────────────────────────────

    [Fact]
    public async Task AckQueue_SuccessfulAck_ResolvesPendingCommit()
    {
        // Build a minimal, self-contained wiring for this test.
        var repo = new DerRepo();
        var bus  = new FdpEventBus();
        using var svc = new MissionEditorService(repo, bus, commitTimeoutMs: 200);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 1);

        bus.SwapBuffers();
        var intent    = bus.ConsumeManaged<MissionControlIntent>().Single();
        bus.Publish(new MissionControlAckEvent { RequestId = intent.RequestId, ErrorCode = 0, NewVersion = 2 });
        bus.SwapBuffers();
        svc.Poll();

        var result = await commitTask;
        Assert.True(result.Success);
        Assert.Equal(2, result.NewVersion);
    }

    [Fact]
    public async Task AckQueue_VersionConflict_ReturnsFailureResult()
    {
        var repo = new DerRepo();
        var bus  = new FdpEventBus();
        using var svc = new MissionEditorService(repo, bus, commitTimeoutMs: 200);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 5, newPlan: plan, baseVersion: 1);

        bus.SwapBuffers();
        var intent = bus.ConsumeManaged<MissionControlIntent>().Single();
        bus.Publish(new MissionControlAckEvent { RequestId = intent.RequestId, ErrorCode = 7, NewVersion = 0 });
        bus.SwapBuffers();
        svc.Poll();

        var result = await commitTask;
        Assert.False(result.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", result.ErrorMessage);
    }

    [Fact]
    public async Task AckQueue_MultiplePoll_NoDoubleFire()
    {
        var repo = new DerRepo();
        var bus  = new FdpEventBus();
        using var svc = new MissionEditorService(repo, bus, commitTimeoutMs: 200);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 3, newPlan: plan, baseVersion: 0);

        bus.SwapBuffers();
        var intent = bus.ConsumeManaged<MissionControlIntent>().Single();
        bus.Publish(new MissionControlAckEvent { RequestId = intent.RequestId, ErrorCode = 0, NewVersion = 1 });
        bus.SwapBuffers();
        svc.Poll();
        svc.Poll(); // Second poll — bus empty, must not throw.

        var result = await commitTask;
        Assert.True(result.Success);
    }

    // ── Entity appears in ORBAT after repo update ─────────────────────────────

    [Fact]
    public void EntityAddedToRepo_AppearsInOrbatOnNextQuery()
    {
        var (_, _, repo, _, _) = IntegrationFactory.Create(new IgStub(), new SimHostStub());
        var orbat = new OrbatPanel();

        Assert.Empty(orbat.GetVisibleNodes(repo));

        // Simulate SimHost publishing an entity (production: via MasterIngressHandler).
        var entity = repo.CreateEntity(200, 100);
        entity.SetDescriptor(new EntityInfo
        {
            EntityId        = 200,
            Name            = "T-72#1",
            CommanderId     = 0,
            ForceIdentifier = eForceIdentifier.FORCE_OPPOSING
        });

        var nodes = orbat.GetVisibleNodes(repo);
        Assert.Single(nodes);
        Assert.Equal("T-72#1", nodes[0].Name);
    }

    [Fact]
    public void CreateEntityRequest_SendsCorrectPayload_ToSimHostCapture()
    {
        var ig  = new IgStub();
        var sh  = new SimHostStub();
        var (_, logic, _, _, _) = IntegrationFactory.Create(ig, sh);
        const long tkbType = 102L;

        logic.StartPlacementMode(tkbType);
        ig.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position             = new GeoPoint { Latitude = 48.9, Longitude = 2.3 }
        });
        logic.Update();

        var req = sh.CreateCapture.Written.Single();

        Assert.Equal(tkbType,
            req.InitialDescriptors
                .First(d => d._d == EDescriptorType.dtEntityMaster)
                .EntityMaster.TkbType);
    }
}
