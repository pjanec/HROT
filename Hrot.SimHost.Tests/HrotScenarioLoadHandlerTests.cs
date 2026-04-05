using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
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

    // ── Shared test fixtures ──────────────────────────────────────────────────

    private readonly EntityRepository _repo;
    private readonly ScenarioSerializer _serializer;

    public HrotScenarioLoadHandlerTests()
    {
        _repo       = new EntityRepository();
        _serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
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
        var handler = new HrotScenarioLoadHandler(_serializer, loader, spy, _repo);

        var txId   = Guid.NewGuid();
        var intent = MakeIntent("scenario1", txId);

        await handler.PrepareAsync(intent, default);
        handler.Commit(intent, _repo);

        Assert.Equal(0, spy.LoadZonesCallCount);
    }

    // ── Test 2: JSON with Zones — LoadZones called exactly once ───────────────

    [Fact]
    public async Task Commit_JsonWithZones_CallsLoadZonesOnceBeforeDeserialize()
    {
        // Build a scenario JSON with a valid "zones" section.
        var json = """
            {
              "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" },
              "zones": {
                "zone1": {
                  "obstacles": [
                    { "x": 10.0, "y": 20.0, "radius": 5.0 }
                  ]
                }
              },
              "entities": {}
            }
            """;

        var spy     = new SpyZoneManagerService();
        var loader  = new StubScenarioLoader(json);
        var handler = new HrotScenarioLoadHandler(_serializer, loader, spy, _repo);

        var txId   = Guid.NewGuid();
        var intent = MakeIntent("scenario1", txId);

        await handler.PrepareAsync(intent, default);
        handler.Commit(intent, _repo);

        Assert.Equal(1, spy.LoadZonesCallCount);
        Assert.NotNull(spy.LastZones);
        Assert.True(spy.LastZones!.ContainsKey("zone1"));
    }
}
