using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
using Hrot.SimHost.Orchestration.Handlers;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// PACK3-Z004 — Unit tests for <see cref="HrotScenarioLoadHandler"/>.
/// </summary>
public sealed class HrotScenarioLoadHandlerTests : IDisposable
{
    // ── Stub implementations ──────────────────────────────────────────────────

    private sealed class SpyZoneManagerService : IZoneManagerService
    {
        public int LoadZonesCallCount { get; private set; }
        public Dictionary<string, ZoneDefinitionDto>? LastZones { get; private set; }

        public void LoadZones(EntityRepository repo, Dictionary<string, ZoneDefinitionDto> zones)
        {
            LoadZonesCallCount++;
            LastZones = zones;
        }

        public Dictionary<string, ZoneDefinitionDto> GetActiveZones()
            => LastZones ?? new Dictionary<string, ZoneDefinitionDto>();
    }

    private sealed class StubScenarioLoader : IScenarioLoader
    {
        private readonly string? _json;

        public StubScenarioLoader(string? json) => _json = json;

        public string? TryLoadScenarioJson(string scenarioId) => _json;
    }

    private sealed class StubScenarioEntityExtractor : IScenarioEntityExtractor
    {
        public IReadOnlyList<EntityCreationRequest> Extract(
            ScenarioSerializer serializer, string json, INetworkIdAllocator idAllocator)
            => Array.Empty<EntityCreationRequest>();
    }

    // ── Shared test fixtures ──────────────────────────────────────────────────

    private readonly EntityRepository _repo;
    private readonly ScenarioSerializer _serializer;
    private readonly StubScenarioEntityExtractor _extractor;
    private readonly ScenarioEntityCreationRequestSource _source;
    private readonly SequentialIdAllocator _idAllocator;

    public HrotScenarioLoadHandlerTests()
    {
        _repo        = new EntityRepository();
        _serializer  = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        _extractor   = new StubScenarioEntityExtractor();
        _source      = new ScenarioEntityCreationRequestSource();
        _idAllocator = new SequentialIdAllocator();
    }

    public void Dispose() => _repo.Dispose();

    private static ExecuteNodeOpIntent MakeIntent(string scenarioId, Guid txId)
        => new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareLive,
            DomainPayload = scenarioId,
        };

    // ── Test 1: JSON without Zones — LoadZones NOT called ─────────────────────

    [Fact]
    public async Task Commit_JsonWithoutZones_DoesNotCallLoadZones()
    {
        // Build a scenario JSON that has no "zones" key.
        var json = """
            {
              "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" },
              "entities": {}
            }
            """;

        var spy     = new SpyZoneManagerService();
        var loader  = new StubScenarioLoader(json);
        var handler = new HrotScenarioLoadHandler(_serializer, loader, spy, _extractor, _source, _idAllocator, world: _repo);

        var txId   = Guid.NewGuid();
        var intent = MakeIntent("scenario1", txId);

        await handler.PrepareAsync(intent, default);
        handler.Commit(intent, _repo);

        Assert.Equal(0, spy.LoadZonesCallCount);
    }

    // ── Test 3: PrepareState(OperatingLive) defers completion ────────────────

    [Fact]
    public async Task PrepareState_OperatingLive_ReturnsIncompleteTask_CompletesAfterDrain()
    {
        var spy     = new SpyZoneManagerService();
        var loader  = new StubScenarioLoader(null);
        var handler = new HrotScenarioLoadHandler(_serializer, loader, spy, _extractor, _source, _idAllocator, world: _repo);

        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareState,
            DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload(
                ScenarioId:  null,
                TargetState: ClusterState.OperatingLive), // OperatingLive
        };

        var prepareTask = handler.PrepareAsync(intent, default);

        // Must not complete immediately.
        Assert.False(prepareTask.IsCompleted);

        // No Constructing entities, no intent DTOs in _repo -> drain should complete task.
        handler.DrainDeferredAcks();

        await prepareTask;
        Assert.True(prepareTask.IsCompleted);
    }

    // ── Test 4: DrainDeferredAcks without world completes immediately ─────────

    [Fact]
    public async Task DrainDeferredAcks_NoWorld_CompletesImmediately()
    {
        var spy     = new SpyZoneManagerService();
        var handler = new HrotScenarioLoadHandler(_serializer, new StubScenarioLoader(null), spy, _extractor, _source, _idAllocator);

        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareState,
            DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload(
                ScenarioId:  null,
                TargetState: ClusterState.OperatingLive),
        };

        var prepareTask = handler.PrepareAsync(intent, default);
        Assert.False(prepareTask.IsCompleted);

        handler.DrainDeferredAcks();

        await prepareTask;
        Assert.True(prepareTask.IsCompleted);
    }

    // ── Test 5: CanHandle returns true for PrepareState ───────────────────────

    [Fact]
    public void CanHandle_ReturnsTrue_ForPrepareLiveAndPrepareState()
    {
        var handler = new HrotScenarioLoadHandler(
            _serializer, new StubScenarioLoader(null), new SpyZoneManagerService(), _extractor, _source, _idAllocator);

        Assert.True(handler.CanHandle(NodeOpType.PrepareLive));
        Assert.True(handler.CanHandle(NodeOpType.PrepareState));
        Assert.False(handler.CanHandle(NodeOpType.FinalizeLive));
    }
}
