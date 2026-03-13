using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using FDP.Toolkit.DER;

namespace Bagira.IOS.Tests;

// ──────────────────────────────────────────────────────────────────────────────
// IOS.9.4 — Workflow sanity checks
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 4: Full-stack placement + mission modification trace.
///
/// Simulates a multi-step operator workflow:
/// <list type="number">
///   <item>Operator activates the placement tool.</item>
///   <item>IG stub delivers a <see cref="MapClickEvent"/>; IOS emits
///   <see cref="CreateEntityRequest"/> to SimHost.</item>
///   <item>SimHost "spawns" the entity by inserting it into the DER repo
///   and makes it visible in the ORBAT panel.</item>
///   <item>Operator selects the entity.</item>
///   <item>Operator commits a new mission plan; SimHost delivers the
///   <see cref="MissionControlAck"/> via the ACK queue.</item>
///   <item>MissionEditorService resolves the commit successfully.</item>
/// </list>
///
/// <para>No <see cref="System.Threading.Thread.Sleep"/> is used; the
/// <see cref="MissionEditorService"/> is constructed with a short
/// <c>commitTimeoutMs</c> and ACKs are delivered synchronously via the
/// injected <see cref="ConcurrentEventQueue{T}"/>.</para>
/// </summary>
[Collection("Integration")]
public class FullStackWorkflowTests
{
    // ── Test fixture ──────────────────────────────────────────────────────────

    private sealed class WorkflowFixture : IDisposable
    {
        public DerRepo Repo { get; } = new DerRepo();
        public CapturingWriter<MapInteractionConfig>  ConfigCapture  { get; } = new();
        public CapturingWriter<CreateEntityRequest>   CreateCapture  { get; } = new();
        public CapturingWriter<MissionControlRequest> MissionCapture { get; } = new();
        public ConcurrentEventQueue<MissionControlAck> AckQueue      { get; } = new();
        public ConcurrentEventQueue<MapClickEvent>    ClickQueue     { get; } = new();
        public ConcurrentEventQueue<SelectionChangedEvent> SelectionQueue { get; } = new();
        public InteractionPanel Log { get; } = new();

        public MissionEditorService MissionSvc { get; }
        public IosLogic             Logic      { get; }
        public IosMock              Mock       { get; }

        public WorkflowFixture()
        {
            MissionSvc = new MissionEditorService(
                Repo, MissionCapture, commitTimeoutMs: 200, ackQueue: AckQueue);

            var contextMenuLogic = new ContextMenuLogic(
                Repo,
                new CapturingWriter<ContextActionsUpdate>());

            Logic = new IosLogic(
                repo:                Repo,
                missionEditorService: MissionSvc,
                contextMenuLogic:    contextMenuLogic,
                transactionManager:  new RequestTransactionManager(),
                configWriter:        ConfigCapture,
                createEntityWriter:  CreateCapture,
                clickQueue:          ClickQueue,
                selectionQueue:      SelectionQueue,
                interactionPanel:    Log,
                ingressHandlers:     new[] { (IIngressHandler)MissionSvc });

            Mock = new IosMock(
                logic:            Logic,
                configPanel:      new ConfigPanel(),
                orbatPanel:       new OrbatPanel(),
                missionPanel:     new MissionPanel(),
                interactionPanel: Log,
                spawnerPanel:     new SpawnerPanel());
        }

        /// <summary>
        /// Simulates SimHost spawning an entity into the shared DER repo in
        /// response to the most recent <see cref="CreateEntityRequest"/> sent by
        /// the IOS.  Returns the new entity.
        /// </summary>
        public IDerEntity SimHostSpawnEntity(int entityId, string name, long tkbType = 100)
        {
            var entity = Repo.CreateEntity(entityId, tkbType);
            entity.SetDescriptor(new EntityInfo
            {
                EntityId        = entityId,
                Name            = name,
                CommanderId     = 0,
                ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY
            });
            entity.SetDescriptor(new EntityMission
            {
                EntityId = entityId,
                Plan     = new MissionPlan { Tasks = new List<MissionTask>() }
            });
            entity.SetDescriptor(new DescriptorOptimisticLock
            {
                EntityId       = entityId,
                CurrentVersion = 1
            });
            return entity;
        }

        public void Dispose() => Mock.Dispose();
    }

    // ── Full multi-step workflow ───────────────────────────────────────────────

    [Fact]
    public async Task FullWorkflow_PlacementToMissionCommit_CompletesSuccessfully()
    {
        using var f = new WorkflowFixture();

        // ── Step 1: Activate placement tool ──────────────────────────────────
        f.Logic.StartPlacementMode(100L);
        Assert.NotEqual(Guid.Empty, f.Logic.ActiveContextId);
        Assert.Single(f.ConfigCapture.Written);

        // ── Step 2: IG delivers a placement click ─────────────────────────────
        f.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = f.Logic.ActiveContextId,
            Position             = new GeoPosition { Latitude = 50.0, Longitude = 14.4 }
        });
        f.Mock.Update(0f); // Processes the click → writes CreateEntityRequest.

        Assert.Single(f.CreateCapture.Written);
        Assert.Equal(100L,
            f.CreateCapture.Written[0].InitialDescriptors
                .First(d => d._d == EDescriptorType.dtEntityMaster)
                .EntityMaster.TkbType);

        // ── Step 3: SimHost spawns entity; IOS repo is updated ────────────────
        var entity = f.SimHostSpawnEntity(entityId: 1001, name: "Alpha-1");

        var orbat = new OrbatPanel();
        var nodes = orbat.GetVisibleNodes(f.Repo);
        Assert.Single(nodes);
        Assert.Equal("Alpha-1", nodes[0].Name);

        // ── Step 4: Operator selects the entity ───────────────────────────────
        f.Logic.SelectEntity(1001);
        f.Mock.Update(0f);
        Assert.Equal(1001, f.Logic.SelectedEntityId);

        // ── Step 5: Operator commits a mission plan ───────────────────────────
        var newPlan = new MissionPlan
        {
            ActiveTaskId = Guid.NewGuid(),
            Tasks        = new List<MissionTask>
            {
                new() { TaskId = Guid.NewGuid(), BehaviorId = "MoveToWaypoint",
                        BehaviorParams = @"{""wp"":1}", State = eTaskState.TASK_PLANNED,
                        ExecutingEngine = "CGFX", Triggers = new List<MissionTrigger>() }
            }
        };

        var commitTask = f.MissionSvc.CommitMissionAsync(
            entityId: 1001, newPlan: newPlan, baseVersion: 1);

        Assert.False(commitTask.IsCompleted, "Commit must be pending until ACK is delivered.");
        Assert.Single(f.MissionCapture.Written);

        // ── Step 6: SimHost delivers ACK; IOS resolves the commit ─────────────
        var requestId = f.MissionCapture.Written[0].RequestId;
        f.AckQueue.Enqueue(new MissionControlAck
        {
            RequestId  = requestId,
            ErrorCode  = 0,
            NewVersion = 2
        });
        f.Mock.Update(0f); // IIngressHandler.Poll() is called inside Update.

        // Await the commit — continuation runs on thread pool after TrySetResult.
        var commitResult = await commitTask;
        Assert.True(commitResult.Success);
        Assert.Equal(2, commitResult.NewVersion);
    }

    [Fact]
    public void FullWorkflow_OrbatFilterAfterSpawn_FindsNewEntity()
    {
        using var f = new WorkflowFixture();

        f.SimHostSpawnEntity(1001, "Alpha-1");
        f.SimHostSpawnEntity(1002, "Bravo-1");
        f.SimHostSpawnEntity(1003, "Charlie-1");

        var orbat = new OrbatPanel { FilterText = "bravo" };
        var nodes = orbat.GetVisibleNodes(f.Repo);

        Assert.Single(nodes);
        Assert.Equal("Bravo-1", nodes[0].Name);
    }

    [Fact]
    public void FullWorkflow_MultipleClicks_AllTrackedByTransactionManager()
    {
        using var f = new WorkflowFixture();
        f.Logic.StartPlacementMode(100L);
        var ctx = f.Logic.ActiveContextId;

        for (int i = 0; i < 5; i++)
        {
            f.ClickQueue.Enqueue(new MapClickEvent
            {
                InteractionContextId = ctx,
                Position             = new GeoPosition { Latitude = i, Longitude = i }
            });
        }
        f.Mock.Update(0f);

        Assert.Equal(5, f.CreateCapture.Written.Count);
    }

    [Fact]
    public void FullWorkflow_LogContainsExpectedEntries_AfterWorkflow()
    {
        using var f = new WorkflowFixture();

        // Placement mode → TX config entry.
        f.Logic.StartPlacementMode(100L);

        // Click → TX create entry.
        f.ClickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = f.Logic.ActiveContextId,
            Position             = new GeoPosition { Latitude = 1.0, Longitude = 1.0 }
        });
        f.Mock.Update(0f);
        // Log entries queued during ProcessClickEvents (frame N) are drained
        // by DrainPendingLogs in the next Update call (frame N+1).
        f.Mock.Update(0f);

        var entries = f.Log.Entries.ToList();

        Assert.Contains(entries, e => e.Direction == "TX" && e.Topic.Contains("Config"));
        Assert.Contains(entries, e => e.Direction == "TX" && e.Topic.Contains("Create"));
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// IOS.9.4 — Conflict detection workflow tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 5: Optimistic-lock conflict detection.
///
/// Validates that two concurrent IOS clients editing the same mission correctly
/// resolve the conflict: the first commit wins; the second receives a version
/// conflict rejection from SimHost.
///
/// <para>Two independent <see cref="MissionEditorService"/> instances share the
/// same DER repository but each has its own request writer and ACK queue,
/// mirroring two separate IOS operator stations connected to the same DDS
/// domain.</para>
/// </summary>
[Collection("Integration")]
public class ConflictDetectionWorkflowTests
{
    // ── Two-operator fixture ──────────────────────────────────────────────────

    private sealed class TwoOperatorFixture : IDisposable
    {
        // Shared DER repo — both operators observe the same entity state.
        public DerRepo Repo { get; } = new DerRepo();

        // Operator A
        public CapturingWriter<MissionControlRequest> WriterA  { get; } = new();
        public ConcurrentEventQueue<MissionControlAck> AckQueueA { get; } = new();
        public MissionEditorService SvcA { get; }

        // Operator B
        public CapturingWriter<MissionControlRequest> WriterB  { get; } = new();
        public ConcurrentEventQueue<MissionControlAck> AckQueueB { get; } = new();
        public MissionEditorService SvcB { get; }

        public TwoOperatorFixture()
        {
            SvcA = new MissionEditorService(Repo, WriterA, commitTimeoutMs: 200, ackQueue: AckQueueA);
            SvcB = new MissionEditorService(Repo, WriterB, commitTimeoutMs: 200, ackQueue: AckQueueB);
        }

        public IDerEntity CreateEntityWithVersion(int entityId, int version)
        {
            var entity = Repo.CreateEntity(entityId, 100);
            entity.SetDescriptor(new EntityMission
            {
                EntityId = entityId,
                Plan     = new MissionPlan { Tasks = new List<MissionTask>() }
            });
            entity.SetDescriptor(new DescriptorOptimisticLock
            {
                EntityId       = entityId,
                CurrentVersion = version
            });
            return entity;
        }

        public void Dispose()
        {
            SvcA.Dispose();
            SvcB.Dispose();
        }
    }

    // ── Conflict detection tests ──────────────────────────────────────────────

    [Fact]
    public async Task ConflictDetection_FirstCommitSucceeds_SecondReceivesVersionConflict()
    {
        using var f = new TwoOperatorFixture();
        f.CreateEntityWithVersion(entityId: 42, version: 5);

        var planA = new MissionPlan { ActiveTaskId = Guid.NewGuid(), Tasks = new List<MissionTask>() };
        var planB = new MissionPlan { ActiveTaskId = Guid.NewGuid(), Tasks = new List<MissionTask>() };

        // Both operators read version 5 and start editing simultaneously.
        var commitA = f.SvcA.CommitMissionAsync(entityId: 42, newPlan: planA, baseVersion: 5);
        var commitB = f.SvcB.CommitMissionAsync(entityId: 42, newPlan: planB, baseVersion: 5);

        // ── Operator A wins: SimHost accepts the commit ───────────────────────
        f.AckQueueA.Enqueue(new MissionControlAck
        {
            RequestId  = f.WriterA.Written.Single().RequestId,
            ErrorCode  = 0,
            NewVersion = 6
        });
        f.SvcA.Poll();

        // Await the result — continuation runs on thread pool after TrySetResult.
        var resultA = await commitA;
        Assert.True(resultA.Success);
        Assert.Equal(6, resultA.NewVersion);

        // ── Operator B loses: SimHost detects version conflict ────────────────
        f.AckQueueB.Enqueue(new MissionControlAck
        {
            RequestId    = f.WriterB.Written.Single().RequestId,
            ErrorCode    = 7,
            ErrorMessage = "ERR_VERSION_CONFLICT",
            NewVersion   = 0
        });
        f.SvcB.Poll();

        var resultB = await commitB;
        Assert.False(resultB.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", resultB.ErrorMessage);
    }

    [Fact]
    public void ConflictDetection_BothOperatorsReadSnapshot_ReturnsCurrentVersion()
    {
        using var f = new TwoOperatorFixture();
        f.CreateEntityWithVersion(entityId: 10, version: 3);
        f.Repo.GetEntity(10)!.SetDescriptor(new EntityInfo
        {
            EntityId = 10, Name = "Alpha", CommanderId = 0,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY
        });

        var (_, vA) = f.SvcA.GetMissionSnapshot(10);
        var (_, vB) = f.SvcB.GetMissionSnapshot(10);

        // Both see the same version before any commit.
        Assert.Equal(3, vA);
        Assert.Equal(3, vB);
    }

    [Fact]
    public async Task ConflictDetection_SequentialCommits_BothSucceedWithIncreasingVersions()
    {
        using var f = new TwoOperatorFixture();
        f.CreateEntityWithVersion(entityId: 20, version: 1);

        // ── First commit at version 1 ─────────────────────────────────────────
        var plan1   = new MissionPlan { Tasks = new List<MissionTask>() };
        var commit1 = f.SvcA.CommitMissionAsync(entityId: 20, newPlan: plan1, baseVersion: 1);

        f.AckQueueA.Enqueue(new MissionControlAck
        {
            RequestId  = f.WriterA.Written.Last().RequestId,
            ErrorCode  = 0,
            NewVersion = 2
        });
        f.SvcA.Poll();

        var result1 = await commit1;
        Assert.True(result1.Success);
        Assert.Equal(2, result1.NewVersion);

        // ── Second commit at version 2 (operator B read the updated snapshot) ─
        var plan2   = new MissionPlan { Tasks = new List<MissionTask>() };
        var commit2 = f.SvcB.CommitMissionAsync(entityId: 20, newPlan: plan2, baseVersion: 2);

        f.AckQueueB.Enqueue(new MissionControlAck
        {
            RequestId  = f.WriterB.Written.Last().RequestId,
            ErrorCode  = 0,
            NewVersion = 3
        });
        f.SvcB.Poll();

        var result2 = await commit2;
        Assert.True(result2.Success);
        Assert.Equal(3, result2.NewVersion);
    }

    // ── Dispose teardown: orphaned TCS resolved gracefully ───────────────────

    [Fact]
    public async Task Dispose_WithPendingCommit_ResolvesWithFailureNotException()
    {
        var repo   = new DerRepo();
        var writer = new CapturingWriter<MissionControlRequest>();
        var svc    = new MissionEditorService(repo, writer, commitTimeoutMs: 5000);

        var plan       = new MissionPlan { Tasks = new List<MissionTask>() };
        var commitTask = svc.CommitMissionAsync(entityId: 99, newPlan: plan, baseVersion: 0);

        Assert.False(commitTask.IsCompleted, "Must be pending before dispose.");

        // Disposing while a commit is in flight must resolve it with failure,
        // not cancel it (no OperationCanceledException propagated to callers).
        svc.Dispose();

        var result = await commitTask;

        Assert.False(result.Success);
        Assert.Equal("Service disposed", result.ErrorMessage);
    }

    [Fact]
    public async Task Dispose_MultiplePendingCommits_AllResolvedWithFailure()
    {
        var repo   = new DerRepo();
        var writer = new CapturingWriter<MissionControlRequest>();
        var svc    = new MissionEditorService(repo, writer, commitTimeoutMs: 5000);

        var plan  = new MissionPlan { Tasks = new List<MissionTask>() };
        var taskA = svc.CommitMissionAsync(entityId: 1, newPlan: plan, baseVersion: 0);
        var taskB = svc.CommitMissionAsync(entityId: 2, newPlan: plan, baseVersion: 0);
        var taskC = svc.CommitMissionAsync(entityId: 3, newPlan: plan, baseVersion: 0);

        svc.Dispose();

        var results = await Task.WhenAll(taskA, taskB, taskC);

        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Equal("Service disposed", r.ErrorMessage);
        });
    }

    [Fact]
    public void Dispose_IsIdempotent_DoesNotThrow()
    {
        var repo   = new DerRepo();
        var writer = new CapturingWriter<MissionControlRequest>();
        var svc    = new MissionEditorService(repo, writer);

        var ex = Record.Exception(() =>
        {
            svc.Dispose();
            svc.Dispose(); // Second call must be a no-op.
        });

        Assert.Null(ex);
    }
}
