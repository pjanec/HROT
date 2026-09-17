using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Serializers;
using Hrot.IG.Components;
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
        _repo.RegisterManagedComponent<InitialUnitSubordinateIntent>();
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

    // ── CS026-T01: DrainDeferredAcks blocks while InitialUnitSubordinateIntent present ──

    [Fact]
    public async Task DrainDeferredAcks_WithPendingSubordinateIntent_DoesNotComplete()
    {
        var spy     = new SpyZoneManagerService();
        var handler = new HrotScenarioLoadHandler(_serializer, new StubScenarioLoader(null), spy, _extractor, _source, _idAllocator, world: _repo);

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

        // Plant a transient intent to block drain.
        var entity = _repo.CreateEntity();
        _repo.SetManagedComponent(entity, new InitialUnitSubordinateIntent { CommanderNetworkId = 42 });

        handler.DrainDeferredAcks();

        // Should still be blocked — intent is present.
        Assert.False(prepareTask.IsCompleted);
    }

    // ── CS026-T02: DrainDeferredAcks completes once InitialUnitSubordinateIntent removed ──

    [Fact]
    public async Task DrainDeferredAcks_AfterRemovingSubordinateIntent_Completes()
    {
        var spy     = new SpyZoneManagerService();
        var handler = new HrotScenarioLoadHandler(_serializer, new StubScenarioLoader(null), spy, _extractor, _source, _idAllocator, world: _repo);

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
        var entity = _repo.CreateEntity();
        _repo.SetManagedComponent(entity, new InitialUnitSubordinateIntent { CommanderNetworkId = 42 });

        handler.DrainDeferredAcks();
        Assert.False(prepareTask.IsCompleted);

        // Remove the intent — drain should now complete.
        _repo.DestroyEntity(entity);
        handler.DrainDeferredAcks();

        await prepareTask;
        Assert.True(prepareTask.IsCompleted);
    }

    // ── C3: the LOCAL terrain invocation ──────────────────────────────────────

    /// <summary>
    /// Records every build so a rail can assert the terrain path ran, and how often.
    /// </summary>
    private sealed class RecordingZoneTileLoader : IZoneTileLoader
    {
        public List<ulong> Built { get; } = new();

        public bool Build(Vector2 boundsMin, Vector2 boundsMax, ulong footprintHash)
        {
            Built.Add(footprintHash);
            return true;
        }
    }

    private static readonly Vector3 ZoneAOrigin = new(670f, 473.5f, 0f);
    private static readonly Vector3 ZoneBOrigin = new(-120f, 40f, 0f);

    private static readonly List<Vector2> ZonePoints = new()
    {
        new(-53f, -88.5f),
        new(47f, -88.5f),
        new(47f, 11.5f),
    };

    private Entity AddZone(Vector3 origin)
    {
        var zone = _repo.CreateEntity();
        _repo.AddComponent(zone, new TkbIdentity { TkbType = TkbEntityTypes.TerrainZone });
        _repo.AddComponent(zone, new SimTransform { Position = origin });
        _repo.SetManagedComponent(zone, new EditablePolyline { Points = new List<Vector2>(ZonePoints) });
        return zone;
    }

    private void RegisterZoneComponents()
    {
        _repo.RegisterComponent<TkbIdentity>();
        _repo.RegisterComponent<SimTransform>();
        _repo.RegisterComponent<TerrainAssetLoadState>();
        _repo.RegisterManagedComponent<EditablePolyline>();
    }

    private HrotScenarioLoadHandler MakeHandlerWithTerrain(IZoneTileLoader tileLoader)
        => new HrotScenarioLoadHandler(
            _serializer, new StubScenarioLoader(null), new SpyZoneManagerService(),
            _extractor, _source, _idAllocator, world: _repo,
            terrainLoadService: new TerrainLoadService(tileLoader));

    private static ExecuteNodeOpIntent MakeOperatingLiveIntent()
        => new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareState,
            DomainPayload = new EditLoadHandlerPayload(
                ScenarioId:  null,
                TargetState: ClusterState.OperatingLive),
        };

    /// <summary>
    /// C3 — the dispatch's success condition, verbatim: "loading a scenario with N zones leaves N
    /// markers Loaded on every participating node, with NO nested 2PC".
    ///
    /// <para>⭐ The "no nested 2PC" half is asserted STRUCTURALLY and it is the load-bearing half: the
    /// prepare task completes inside the very same <c>DrainDeferredAcks</c> call that made the terrain
    /// resident. A nested round would have had to park on a second <c>TaskCompletionSource</c> and wait
    /// for an ack that can only arrive on a LATER tick — so a synchronously-completed task is proof no
    /// round was started. ⚠ This handler is also constructed with no orchestration publisher at all,
    /// which is why there is no publisher spy to interrogate.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.2.
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_WithZoneEntities_MarksEveryZoneLoaded_WithNoNestedRound()
    {
        RegisterZoneComponents();
        var tileLoader = new RecordingZoneTileLoader();
        var handler    = MakeHandlerWithTerrain(tileLoader);

        var prepareTask = handler.PrepareAsync(MakeOperatingLiveIntent(), default);
        Assert.False(prepareTask.IsCompleted);

        // Genesis has finished: the zone ENTITIES now exist. (Before this point the world is empty —
        // that measured fact is why the call sits in the drain and not in Commit.)
        var zoneA = AddZone(ZoneAOrigin);
        var zoneB = AddZone(ZoneBOrigin);

        handler.DrainDeferredAcks();

        // ⭐ N zones ⇒ N markers Loaded, each stamped with the zone's CURRENT footprint.
        foreach (var (zone, origin) in new[] { (zoneA, ZoneAOrigin), (zoneB, ZoneBOrigin) })
        {
            Assert.True(_repo.HasComponent<TerrainAssetLoadState>(zone));
            var marker = _repo.GetComponent<TerrainAssetLoadState>(zone);
            Assert.Equal(LoadPhase.Loaded, marker.Phase);
            Assert.Equal(ZoneFootprint.Compute(origin, ZonePoints), marker.SourceHash);
        }

        Assert.Equal(2, tileLoader.Built.Count);

        // ⛔ No nested 2PC: the round the drain is finishing completed in THIS call.
        await prepareTask;
    }

    /// <summary>
    /// C3 — the same drain is also where a REPEAT load must cost nothing. The idempotency branch lives
    /// in <see cref="TerrainLoadService"/>; this rail pins that the local invocation path actually goes
    /// through it rather than rebuilding on every scenario load.
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_SecondScenarioLoad_RebuildsNothingForUnchangedZones()
    {
        RegisterZoneComponents();
        var tileLoader = new RecordingZoneTileLoader();
        var handler    = MakeHandlerWithTerrain(tileLoader);

        var firstPrepare = handler.PrepareAsync(MakeOperatingLiveIntent(), default);
        var zone = AddZone(ZoneAOrigin);
        handler.DrainDeferredAcks();
        await firstPrepare;
        Assert.Single(tileLoader.Built);

        // A second load round over the same, unedited zone.
        var secondPrepare = handler.PrepareAsync(MakeOperatingLiveIntent(), default);
        handler.DrainDeferredAcks();
        await secondPrepare;

        Assert.Single(tileLoader.Built);   // ⭐ no rebuild
        Assert.Equal(LoadPhase.Loaded, _repo.GetComponent<TerrainAssetLoadState>(zone).Phase);

        // …but an EDITED zone is stale and does rebuild, which is what makes the skip above safe.
        _repo.SetManagedComponent(zone, new EditablePolyline
        {
            Points = new List<Vector2> { new(0f, 0f), new(10f, 0f), new(10f, 10f) },
        });

        var thirdPrepare = handler.PrepareAsync(MakeOperatingLiveIntent(), default);
        handler.DrainDeferredAcks();
        await thirdPrepare;

        Assert.Equal(2, tileLoader.Built.Count);
    }

    /// <summary>
    /// C3 — R-133: a handler built WITHOUT the terrain service must not pretend. It still drains (the
    /// service is optional so lightweight hosts and the pre-existing rails above keep working), and it
    /// leaves NO marker — an absent capability, visibly absent, rather than a silent no-op that reads
    /// as "loaded".
    /// </summary>
    [Fact]
    public async Task DrainDeferredAcks_WithoutTerrainService_LeavesNoMarker()
    {
        RegisterZoneComponents();
        var handler = new HrotScenarioLoadHandler(
            _serializer, new StubScenarioLoader(null), new SpyZoneManagerService(),
            _extractor, _source, _idAllocator, world: _repo);

        var prepareTask = handler.PrepareAsync(MakeOperatingLiveIntent(), default);
        var zone = AddZone(ZoneAOrigin);

        handler.DrainDeferredAcks();
        await prepareTask;

        Assert.False(_repo.HasComponent<TerrainAssetLoadState>(zone));
    }
}
