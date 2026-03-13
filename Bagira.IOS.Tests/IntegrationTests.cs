using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using FDP.Toolkit.DER;

namespace Bagira.IOS.Tests;

// ── Test collection: disable parallelism ──────────────────────────────────────
// Real DDS integration tests must run on separate domain IDs to isolate topic
// traffic.  Even in this in-process suite, serialising the collection prevents
// any future migration to live participants from introducing flaky races.

[CollectionDefinition("Integration", DisableParallelization = true)]
public class IntegrationTestCollection { }

// ── Shared infrastructure for integration tests ───────────────────────────────

/// <summary>
/// Lightweight "IG stub" used in IOS.9.2 tests: exposes two event queues
/// (inbound from the IOS, outbound to the IOS) so the test can assert that
/// configuration is received and click events are forwarded.
/// </summary>
internal sealed class IgStub
{
    /// <summary>Captures MapInteractionConfig messages pushed by the IOS.</summary>
    public CapturingWriter<MapInteractionConfig> ConfigCapture { get; } = new();

    /// <summary>Click-event queue that the IOS logic reads from (IG → IOS).</summary>
    public ConcurrentEventQueue<MapClickEvent> ClickQueue { get; } = new();

    /// <summary>Selection-change queue that the IOS logic reads from (IG → IOS).</summary>
    public ConcurrentEventQueue<SelectionChangedEvent> SelectionQueue { get; } = new();
}

/// <summary>
/// Lightweight "SimHost stub" used in IOS.9.3 tests: owns the ACK queue that
/// feeds the <see cref="MissionEditorService"/> and captures outgoing create
/// requests sent by the IOS.
/// </summary>
internal sealed class SimHostStub
{
    /// <summary>Captures CreateEntityRequest messages published by the IOS.</summary>
    public CapturingWriter<CreateEntityRequest> CreateCapture { get; } = new();

    /// <summary>ACK queue that feeds back into MissionEditorService.Poll() (SimHost → IOS).</summary>
    public ConcurrentEventQueue<MissionControlAck> AckQueue { get; } = new();

    /// <summary>
    /// Delivers a successful MissionControlAck for the given request ID into
    /// the ACK queue (simulating SimHost accepting and processing the commit).
    /// </summary>
    public void DeliverAck(Guid requestId, long newVersion = 1) =>
        AckQueue.Enqueue(new MissionControlAck
        {
            RequestId  = requestId,
            ErrorCode  = 0,
            NewVersion = newVersion
        });

    /// <summary>
    /// Delivers a version-conflict rejection (simulating SimHost detecting a
    /// stale base version).
    /// </summary>
    public void DeliverVersionConflict(Guid requestId) =>
        AckQueue.Enqueue(new MissionControlAck
        {
            RequestId    = requestId,
            ErrorCode    = 7,
            ErrorMessage = "ERR_VERSION_CONFLICT",
            NewVersion   = 0
        });
}

// ── Test fixture factory ──────────────────────────────────────────────────────

internal static class IntegrationFactory
{
    /// <summary>
    /// Creates a fully wired IosLogic + IosMock where all DDS writers/readers
    /// are replaced by in-process stubs.  The <paramref name="igStub"/> and
    /// <paramref name="simHostStub"/> carry the queues used in IOS.9.2 and
    /// IOS.9.3 scenarios respectively.
    ///
    /// <para>No live DDS participant is created; tests execute in a single
    /// process without any socket or OS-resource allocation.</para>
    /// </summary>
    public static (IosMock Mock, IosLogic Logic, DerRepo Repo, MissionEditorService MissionSvc, InteractionPanel Log)
        Create(IgStub igStub, SimHostStub simHostStub)
    {
        var repo          = new DerRepo();
        var missionWriter = new CapturingWriter<MissionControlRequest>();
        var missionSvc    = new MissionEditorService(
            repo,
            missionWriter,
            commitTimeoutMs: 200,           // Short timeout keeps tests fast.
            ackQueue:        simHostStub.AckQueue);

        var contextMenuLogic = new ContextMenuLogic(repo, new CapturingWriter<ContextActionsUpdate>());
        var transactionMgr   = new RequestTransactionManager();
        var interactionPanel = new InteractionPanel();

        var logic = new IosLogic(
            repo:                repo,
            missionEditorService: missionSvc,
            contextMenuLogic:    contextMenuLogic,
            transactionManager:  transactionMgr,
            configWriter:        igStub.ConfigCapture,
            createEntityWriter:  simHostStub.CreateCapture,
            clickQueue:          igStub.ClickQueue,
            selectionQueue:      igStub.SelectionQueue,
            interactionPanel:    interactionPanel,
            ingressHandlers:     new[] { (IIngressHandler)missionSvc });

        var mock = new IosMock(
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
// IOS.9.1 — Standalone IOS integration tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 1: Standalone IOS validation.
///
/// Simulates booting <see cref="IosMock"/> and <see cref="IosLogic"/> with no
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
    private static (IosMock Mock, IosLogic Logic, DerRepo Repo, InteractionPanel Log) Create()
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
        hq.SetDescriptor(new EntityInfo { EntityId = 1, Name = "HQ", CommanderId = 0,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });

        var t1 = repo.CreateEntity(2, 101);
        t1.SetDescriptor(new EntityInfo { EntityId = 2, Name = "Tank1", CommanderId = 1,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });

        var t2 = repo.CreateEntity(3, 101);
        t2.SetDescriptor(new EntityInfo { EntityId = 3, Name = "Tank2", CommanderId = 1,
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
        hq.SetDescriptor(new EntityInfo { EntityId = 1, Name = "HQ", CommanderId = 0,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY });
        var t1 = repo.CreateEntity(2, 101);
        t1.SetDescriptor(new EntityInfo { EntityId = 2, Name = "Tank1", CommanderId = 1,
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
            Position             = new GeoPosition { Latitude = 0, Longitude = 0 }
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
// IOS.9.2 — IOS + IG stub integration tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 2: IOS + IG interaction pathways.
///
/// Uses a lightweight <see cref="IgStub"/> to simulate the IG:
/// <list type="bullet">
///   <item>IG emits <see cref="MapClickEvent"/> — IOS converts to
///   <see cref="CreateEntityRequest"/>.</item>
///   <item>IG emits <see cref="SelectionChangedEvent"/> — IOS forwards to
///   <see cref="ContextMenuLogic"/> and logs the interaction.</item>
///   <item>IOS emits <see cref="MapInteractionConfig"/> — IG stub captures
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
            Position             = new GeoPosition { Latitude = 45.0, Longitude = 12.0 }
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
            Position             = new GeoPosition { Latitude = 1.0, Longitude = 1.0 }
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
                Position             = new GeoPosition { Latitude = i, Longitude = i }
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
        var pos = new GeoPosition { Latitude = 51.5, Longitude = -0.1 };
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
            .First(d => d._d == EDescriptorType.dtGeoSpatial)
            .GeoSpatial;
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
// IOS.9.3 — IOS + SimHost stub integration tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 3: IOS + SimHost interaction pathways.
///
/// Uses a lightweight <see cref="SimHostStub"/> to simulate SimHost:
/// <list type="bullet">
///   <item>IOS sends <see cref="CreateEntityRequest"/> — SimHost stub
///   captures it.</item>
///   <item>SimHost delivers <see cref="MissionControlAck"/> via the ingress
///   queue — IOS <see cref="MissionEditorService.Poll"/> resolves pending
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
        var repo          = new DerRepo();
        var requestWriter = new CapturingWriter<MissionControlRequest>();
        var ackQueue      = new ConcurrentEventQueue<MissionControlAck>();
        using var svc     = new MissionEditorService(repo, requestWriter, commitTimeoutMs: 200, ackQueue: ackQueue);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 1);

        var requestId = requestWriter.Written.Single().RequestId;
        ackQueue.Enqueue(new MissionControlAck { RequestId = requestId, ErrorCode = 0, NewVersion = 2 });
        svc.Poll();

        var result = await commitTask;
        Assert.True(result.Success);
        Assert.Equal(2, result.NewVersion);
    }

    [Fact]
    public async Task AckQueue_VersionConflict_ReturnsFailureResult()
    {
        var repo          = new DerRepo();
        var requestWriter = new CapturingWriter<MissionControlRequest>();
        var ackQueue      = new ConcurrentEventQueue<MissionControlAck>();
        using var svc     = new MissionEditorService(repo, requestWriter, commitTimeoutMs: 200, ackQueue: ackQueue);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 5, newPlan: plan, baseVersion: 1);

        var requestId = requestWriter.Written.Single().RequestId;
        ackQueue.Enqueue(new MissionControlAck
        {
            RequestId    = requestId,
            ErrorCode    = 7,
            ErrorMessage = "ERR_VERSION_CONFLICT",
            NewVersion   = 0
        });
        svc.Poll();

        var result = await commitTask;
        Assert.False(result.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", result.ErrorMessage);
    }

    [Fact]
    public async Task AckQueue_MultiplePoll_NoDoubleFire()
    {
        var repo          = new DerRepo();
        var requestWriter = new CapturingWriter<MissionControlRequest>();
        var ackQueue      = new ConcurrentEventQueue<MissionControlAck>();
        using var svc     = new MissionEditorService(repo, requestWriter, commitTimeoutMs: 200, ackQueue: ackQueue);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 3, newPlan: plan, baseVersion: 0);

        var requestId = requestWriter.Written.Single().RequestId;
        ackQueue.Enqueue(new MissionControlAck { RequestId = requestId, ErrorCode = 0, NewVersion = 1 });
        svc.Poll();
        svc.Poll(); // Second poll — queue already empty, must not throw.

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
            Position             = new GeoPosition { Latitude = 48.9, Longitude = 2.3 }
        });
        logic.Update();

        var req = sh.CreateCapture.Written.Single();

        Assert.Equal(tkbType,
            req.InitialDescriptors
                .First(d => d._d == EDescriptorType.dtEntityMaster)
                .EntityMaster.TkbType);
    }
}
