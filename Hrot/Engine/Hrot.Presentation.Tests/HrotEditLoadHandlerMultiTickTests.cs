using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Handlers;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for multi-tick / ITickableClusterStateHandler behaviour of
/// <see cref="HrotEditLoadHandler"/>.
/// </summary>
public sealed class HrotEditLoadHandlerMultiTickTests : IDisposable
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

    // ── Shared fixtures ───────────────────────────────────────────────────────

    private readonly EntityRepository _repo;
    private readonly ScenarioSerializer _serializer;

    public HrotEditLoadHandlerMultiTickTests()
    {
        _repo       = new EntityRepository();
        _serializer = new ScenarioSerializerBuilder("test").Build();
    }

    public void Dispose() => _repo.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ExecuteNodeOpIntent MakePrepareStateIntent(int targetState)
        => new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareState,
            DomainPayload = new EditLoadHandlerPayload(
                ScenarioId:  null,
                TargetState: targetState),
        };

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// PrepareAsync for PrepareState targeting OperatingEdit (11) must return an
    /// incomplete Task; it must complete only after DrainDeferredAcks is called
    /// and the world has no Constructing entities.
    /// </summary>
    [Fact]
    public async Task PrepareState_OperatingEdit_ReturnsIncompleteTask_CompletesAfterDrain()
    {
        var handler = new HrotEditLoadHandler(_serializer, new NullScenarioLoader(), new NullZoneService(), _repo);
        var intent  = MakePrepareStateIntent(targetState: 11); // OperatingEdit

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);

        // Must not be complete immediately.
        Assert.False(prepareTask.IsCompleted);

        // No Constructing entities -> drain should signal completion.
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
        var handler = new HrotEditLoadHandler(_serializer, new NullScenarioLoader(), new NullZoneService(), _repo);
        var intent  = MakePrepareStateIntent(targetState: 11);

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
        var handler = new HrotEditLoadHandler(_serializer, new NullScenarioLoader(), new NullZoneService());
        var intent  = MakePrepareStateIntent(targetState: 11);

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
        var handler = new HrotEditLoadHandler(_serializer, new NullScenarioLoader(), new NullZoneService(), _repo);
        var intent  = MakePrepareStateIntent(targetState: 11);

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
        var handler = new HrotEditLoadHandler(_serializer, new NullScenarioLoader(), new NullZoneService(), _repo);
        var intent  = MakePrepareStateIntent(targetState: 10); // LoadingEdit

        var prepareTask = handler.PrepareAsync(intent, CancellationToken.None);

        Assert.True(prepareTask.IsCompleted);
        Assert.False(prepareTask.IsFaulted);
    }
}
