using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Handlers;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Unit tests for <see cref="HrotEditLoadHandler"/>: multi-tick orchestration behaviour
/// (ITickableClusterStateHandler) and the unified genesis-pipeline loading path.
/// </summary>
public sealed class HrotEditLoadHandlerTests : IDisposable
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class NullScenarioLoader : IScenarioLoader
    {
        public string? TryLoadScenarioJson(string scenarioId) => null;
    }

    private sealed class NullZoneService : IZoneManagerService
    {
        public void LoadZones(EntityRepository repo, System.Collections.Generic.Dictionary<string, ZoneDefinitionDto> zones) { }
        public System.Collections.Generic.Dictionary<string, ZoneDefinitionDto> GetActiveZones()
            => new System.Collections.Generic.Dictionary<string, ZoneDefinitionDto>();
    }

    private sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next = 1000;
        public long AllocateId()            => _next++;
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    // ── Shared fixtures ───────────────────────────────────────────────────────

    private readonly EntityRepository _repo;
    private readonly ScenarioSerializer _serializer;
    private readonly StagingEntityExtractor _extractor;
    private readonly StubIdAllocator _idAllocator;

    public HrotEditLoadHandlerTests()
    {
        _repo        = new EntityRepository();
        _serializer  = new ScenarioSerializerBuilder("test").Build();
        _extractor   = new StagingEntityExtractor();
        _idAllocator = new StubIdAllocator();
    }

    public void Dispose() => _repo.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ScenarioEntityCreationRequestSource MakeSource() => new();

    private HrotEditLoadHandler MakeHandler(
        ScenarioEntityCreationRequestSource? source = null,
        EntityRepository? world = null)
        => new HrotEditLoadHandler(
            _serializer,
            new NullScenarioLoader(),
            new NullZoneService(),
            _extractor,
            source ?? MakeSource(),
            _idAllocator,
            world);

    private static ExecuteNodeOpIntent MakePrepareStateIntent(ClusterState targetState)
        => new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareState,
            DomainPayload = new EditLoadHandlerPayload(
                ScenarioId:  null,
                TargetState: targetState),
        };

    // ── PrepareState orchestration tests (same semantics as before) ───────────

    /// <summary>
    /// PrepareAsync for PrepareState targeting OperatingEdit (11) must return an
    /// incomplete Task; it must complete only after DrainDeferredAcks is called
    /// and the world has no Constructing entities and the source is empty.
    /// </summary>
    [Fact]
    public async Task PrepareState_OperatingEdit_ReturnsIncompleteTask_CompletesAfterDrain()
    {
        var source  = MakeSource();
        var handler = MakeHandler(source, _repo);
        var intent  = MakePrepareStateIntent(targetState: ClusterState.OperatingEdit); // OperatingEdit

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);

        // Must not be complete immediately.
        Assert.False(prepareTask.IsCompleted);

        // Source is empty, no Constructing entities -> drain should signal completion.
        handler.DrainDeferredAcks();

        await prepareTask;
        Assert.True(prepareTask.IsCompleted);
    }

    /// <summary>
    /// If a Constructing entity exists in the world, DrainDeferredAcks must NOT
    /// complete the task.
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_ConstructingEntity_DoesNotComplete()
    {
        var source  = MakeSource();
        var handler = MakeHandler(source, _repo);
        var intent  = MakePrepareStateIntent(targetState: ClusterState.OperatingEdit);

        // Create an entity in Constructing lifecycle state.
        var constructingEntity = _repo.CreateEntity();
        _repo.SetLifecycleState(constructingEntity, EntityLifecycle.Constructing);

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);
        Assert.False(prepareTask.IsCompleted);

        handler.DrainDeferredAcks(); // entity still Constructing

        await Task.Delay(10); // give any completion a chance to propagate
        Assert.False(prepareTask.IsCompleted);
    }

    /// <summary>
    /// DrainDeferredAcks without a world reference completes immediately (no ECS to poll).
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_NoWorld_CompletesImmediately()
    {
        // Pass world: null
        var source  = MakeSource();
        var handler = MakeHandler(source, world: null);
        var intent  = MakePrepareStateIntent(targetState: ClusterState.OperatingEdit);

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);
        Assert.False(prepareTask.IsCompleted);

        handler.DrainDeferredAcks();

        await prepareTask;
        Assert.True(prepareTask.IsCompleted);
    }

    /// <summary>
    /// Abort cancels the deferred task so callers waiting on it are unblocked.
    /// </summary>
    [Fact]
    public async Task Abort_CancelsDeferred_OperatingEditTask()
    {
        var handler = MakeHandler(world: _repo);
        var intent  = MakePrepareStateIntent(targetState: ClusterState.OperatingEdit);

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);
        Assert.False(prepareTask.IsCompleted);

        handler.Abort(intent, null);

        // Task should become cancelled/faulted.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepareTask);
    }

    /// <summary>
    /// PrepareState targeting LoadingEdit (10) must NOT produce a deferred task
    /// (it must return synchronously completed).
    /// </summary>
    [Fact]
    public void PrepareState_LoadingEdit_ReturnsCompletedTask()
    {
        var handler     = MakeHandler(world: _repo);
        var intent      = MakePrepareStateIntent(targetState: ClusterState.LoadingEdit); // LoadingEdit

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);

        Assert.True(prepareTask.IsCompleted);
        Assert.False(prepareTask.IsFaulted);
    }

    // ── New: source-empty check in DrainDeferredAcks ──────────────────────────

    /// <summary>
    /// DrainDeferredAcks must NOT complete while the request source is non-empty,
    /// even if there are no Constructing entities.
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_NonEmptySource_DoesNotComplete()
    {
        var source  = MakeSource();
        var handler = MakeHandler(source, _repo);
        var intent  = MakePrepareStateIntent(targetState: ClusterState.OperatingEdit);

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);
        Assert.False(prepareTask.IsCompleted);

        // Enqueue a dummy request so the source is non-empty.
        source.Enqueue(new EntityCreationRequest { RequestId = Guid.NewGuid(), TkbType = 1 });

        handler.DrainDeferredAcks(); // source not empty -> must not complete

        await Task.Delay(10);
        Assert.False(prepareTask.IsCompleted);
    }

    /// <summary>
    /// DrainDeferredAcks completes after the source is drained (no Constructing entities).
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_AfterSourceDrained_Completes()
    {
        var source  = MakeSource();
        var handler = MakeHandler(source, _repo);
        var intent  = MakePrepareStateIntent(targetState: ClusterState.OperatingEdit);

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);
        Assert.False(prepareTask.IsCompleted);

        // Enqueue then drain the source to simulate CreateEntityRequestSystem consuming it.
        source.Enqueue(new EntityCreationRequest { RequestId = Guid.NewGuid(), TkbType = 1 });
        source.ProcessRequests(_ => { }); // drain all items

        handler.DrainDeferredAcks(); // source empty, no Constructing -> completes

        await prepareTask;
        Assert.True(prepareTask.IsCompleted);
    }

    // ── Commit: zone-only loading for new scenarios ───────────────────────────

    /// <summary>
    /// Commit for a new-scenario intent (IsNewScenario=true) performs no loading
    /// and leaves the source empty.
    /// </summary>
    [Fact]
    public void Commit_NewScenario_DoesNothing()
    {
        var source  = MakeSource();
        var handler = MakeHandler(source, _repo);

        var txId   = Guid.NewGuid();
        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            Operation     = NodeOpType.PrepareEdit,
            DomainPayload = new EditLoadHandlerPayload(
                ScenarioId:    null,
                TargetState:   LoadingEditState,
                IsNewScenario: true),
        };

        handler.PrepareAsync(intent, CancellationToken.None);
        handler.Commit(intent, _repo);

        Assert.True(source.IsEmpty);
        Assert.Equal(0, _repo.EntityCount);
    }

    /// <summary>
    /// CanHandle returns true for PrepareState, PrepareEdit, and FinalizeEdit.
    /// </summary>
    [Fact]
    public void CanHandle_ReturnsTrueForExpectedOperations()
    {
        var handler = MakeHandler();

        Assert.True(handler.CanHandle(NodeOpType.PrepareState));
        Assert.True(handler.CanHandle(NodeOpType.PrepareEdit));
        Assert.True(handler.CanHandle(NodeOpType.FinalizeEdit));
        Assert.False(handler.CanHandle(NodeOpType.PrepareLive));
        Assert.False(handler.CanHandle(NodeOpType.StartEpisode));
    }

    // ── Private constant to avoid magic number ────────────────────────────────
    private const ClusterState LoadingEditState = ClusterState.LoadingEdit;
}
