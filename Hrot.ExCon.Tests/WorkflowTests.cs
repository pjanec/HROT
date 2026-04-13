using Hrot.ExCon.Logic;
using Hrot.Core.Network;
using Hrot.Core.Mission;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using Hrot.ExCon.Services;
using FDP.Toolkit.DER;
using Moq;

namespace Hrot.ExCon.Tests;

// ──────────────────────────────────────────────────────────────────────────────
// ExCon.9.4 -- Workflow sanity checks
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 4: Full-stack placement + mission modification trace.
/// </summary>
[Collection("Integration")]
public class FullStackWorkflowTests
{
    // ── Test fixture ──────────────────────────────────────────────────────────

    private sealed class WorkflowFixture : IDisposable
    {
        public DerRepo             Repo          { get; } = new DerRepo();
        public CapturingEgressWriters EgressCapture { get; } = new();
        public Mock<ICommandGateway> GatewayMock  { get; } = new();
        public ConcurrentEventQueue<MapClickEventDto>         ClickQueue    { get; } = new();
        public ConcurrentEventQueue<SelectionChangedEventDto> SelectionQueue{ get; } = new();
        public InteractionPanel Log { get; } = new();

        public MissionEditorService MissionSvc { get; }
        public ExConLogic             Logic      { get; }
        public ExConMock              Mock       { get; }

        public WorkflowFixture()
        {
            MissionSvc = new MissionEditorService(Repo, GatewayMock.Object, commitTimeoutMs: 200);

            var contextMenuLogic = new ContextMenuLogic(Repo, EgressCapture);

            Logic = new ExConLogic(
                repo:                Repo,
                missionEditorService: MissionSvc,
                contextMenuLogic:    contextMenuLogic,
                transactionManager:  new RequestTransactionManager(),
                egressWriters:       EgressCapture,
                clickQueue:          ClickQueue,
                selectionQueue:      SelectionQueue,
                interactionPanel:    Log,
                createEntityAckQueue: new ConcurrentEventQueue<EntityLifecycleAckDto>());

            Mock = new ExConMock(
                logic:            Logic,
                configPanel:      new ConfigPanel(),
                orbatPanel:       new OrbatPanel(),
                missionPanel:     new MissionPanel(),
                interactionPanel: Log,
                spawnerPanel:     new SpawnerPanel());
        }

        /// <summary>
        /// Simulates SimHost spawning an entity into the shared DER repo.
        /// Returns the new entity.
        /// </summary>
        public IDerEntity SimHostSpawnEntity(int entityId, string name, long tkbType = 100)
        {
            var entity = Repo.CreateEntity(entityId, tkbType);
            entity.SetDescriptor(new EntityInfoDescriptor
            {
                EntityId    = entityId,
                Name        = name,
                CommanderId = 0,
                Affiliation = eForceIdentifier.FORCE_FRIENDLY.ToString()
            });
            entity.SetDescriptor(new EntityMissionDescriptor
            {
                EntityId = entityId,
                Plan     = new MissionPlan { Tasks = new List<MissionTask>() },
                Version  = 1
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
        Assert.Single(f.EgressCapture.WrittenMapCommands);

        // ── Step 2: IG delivers a placement click ─────────────────────────────
        f.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = f.Logic.ActiveContextId,
            Latitude             = 50.0,
            Longitude            = 14.4
        });
        f.Mock.Update(0f); // Processes the click -> writes CreateEntityCommand.

        Assert.Single(f.EgressCapture.WrittenCreateCommands);
        Assert.Equal(100L, f.EgressCapture.WrittenCreateCommands[0].TkbType);

        // ── Step 3: SimHost spawns entity; ExCon repo is updated ──────────────
        f.SimHostSpawnEntity(entityId: 1001, name: "Alpha-1");

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

        // Set up gateway to return success after a short delay (simulates async ACK).
        var tcs = new TaskCompletionSource<MissionCommitResult>();
        f.GatewayMock.Setup(g => g.SendMissionControlRequestAsync(
                It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var commitTask = f.MissionSvc.CommitMissionAsync(
            entityId: 1001, newPlan: newPlan, baseVersion: 1);

        Assert.False(commitTask.IsCompleted, "Commit must be pending until ACK is delivered.");

        // ── Step 6: Gateway delivers ACK; commit resolves ─────────────────────
        tcs.SetResult(new MissionCommitResult { Success = true, NewVersion = 2 });

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
            f.ClickQueue.Enqueue(new MapClickEventDto
            {
                InteractionContextId = ctx,
                Latitude             = i,
                Longitude            = i
            });
        }
        f.Mock.Update(0f);

        Assert.Equal(5, f.EgressCapture.WrittenCreateCommands.Count);
    }

    [Fact]
    public void FullWorkflow_LogContainsExpectedEntries_AfterWorkflow()
    {
        using var f = new WorkflowFixture();

        // Placement mode -> TX command entry.
        f.Logic.StartPlacementMode(100L);

        // Click -> TX create entry.
        f.ClickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = f.Logic.ActiveContextId,
            Latitude             = 1.0,
            Longitude            = 1.0
        });
        f.Mock.Update(0f);
        // Log entries queued during ProcessClickEvents (frame N) are drained
        // by DrainPendingLogs in the next Update call (frame N+1).
        f.Mock.Update(0f);

        var entries = f.Log.Entries.ToList();

        Assert.Contains(entries, e => e.Direction == "TX" && e.Topic.Contains("Command"));
        Assert.Contains(entries, e => e.Direction == "TX" && e.Topic.Contains("Create"));
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// ExCon.9.5 -- Conflict detection workflow tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario 5: Optimistic-lock conflict detection via independent gateways.
/// </summary>
[Collection("Integration")]
public class ConflictDetectionWorkflowTests
{
    // ── Two-operator fixture ──────────────────────────────────────────────────

    private sealed class TwoOperatorFixture : IDisposable
    {
        public DerRepo Repo { get; } = new DerRepo();

        public Mock<ICommandGateway> GatewayA { get; } = new();
        public MissionEditorService SvcA { get; }

        public Mock<ICommandGateway> GatewayB { get; } = new();
        public MissionEditorService SvcB { get; }

        public TwoOperatorFixture()
        {
            SvcA = new MissionEditorService(Repo, GatewayA.Object, commitTimeoutMs: 200);
            SvcB = new MissionEditorService(Repo, GatewayB.Object, commitTimeoutMs: 200);
        }

        public IDerEntity CreateEntityWithVersion(int entityId, int version)
        {
            var entity = Repo.CreateEntity(entityId, 100);
            entity.SetDescriptor(new EntityMissionDescriptor
            {
                EntityId = entityId,
                Plan     = new MissionPlan { Tasks = new List<MissionTask>() },
                Version  = version
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

        // Operator A wins.
        f.GatewayA.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = true, NewVersion = 6 });

        // Operator B gets version conflict.
        f.GatewayB.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = false, ErrorMessage = "ERR_VERSION_CONFLICT" });

        var resultA = await f.SvcA.CommitMissionAsync(entityId: 42, newPlan: planA, baseVersion: 5);
        var resultB = await f.SvcB.CommitMissionAsync(entityId: 42, newPlan: planB, baseVersion: 5);

        Assert.True(resultA.Success);
        Assert.Equal(6, resultA.NewVersion);
        Assert.False(resultB.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", resultB.ErrorMessage);
    }

    [Fact]
    public void ConflictDetection_BothOperatorsReadSnapshot_ReturnsCurrentVersion()
    {
        using var f = new TwoOperatorFixture();
        f.CreateEntityWithVersion(entityId: 10, version: 3);

        var (_, vA) = f.SvcA.GetMissionSnapshot(10);
        var (_, vB) = f.SvcB.GetMissionSnapshot(10);

        Assert.Equal(3, vA);
        Assert.Equal(3, vB);
    }

    [Fact]
    public async Task ConflictDetection_SequentialCommits_BothSucceedWithIncreasingVersions()
    {
        using var f = new TwoOperatorFixture();
        f.CreateEntityWithVersion(entityId: 20, version: 1);

        // First commit at version 1.
        f.GatewayA.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = true, NewVersion = 2 });

        var result1 = await f.SvcA.CommitMissionAsync(entityId: 20,
            newPlan: new MissionPlan { Tasks = new List<MissionTask>() }, baseVersion: 1);
        Assert.True(result1.Success);
        Assert.Equal(2, result1.NewVersion);

        // Second commit at version 2.
        f.GatewayB.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = true, NewVersion = 3 });

        var result2 = await f.SvcB.CommitMissionAsync(entityId: 20,
            newPlan: new MissionPlan { Tasks = new List<MissionTask>() }, baseVersion: 2);
        Assert.True(result2.Success);
        Assert.Equal(3, result2.NewVersion);
    }

    // ── Dispose teardown ──────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent_DoesNotThrow()
    {
        var repo    = new DerRepo();
        var gateway = new Mock<ICommandGateway>();
        var svc     = new MissionEditorService(repo, gateway.Object);

        var ex = Record.Exception(() =>
        {
            svc.Dispose();
            svc.Dispose(); // Second call must be a no-op.
        });

        Assert.Null(ex);
    }
}