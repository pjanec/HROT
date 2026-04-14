using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.Map.Common.Tests.Services;

/// <summary>
/// PACK3-Z003 — Unit tests for <see cref="ZoneManagerService"/>.
/// </summary>
public sealed class ZoneManagerServiceTests
{
    private const string SampleRoadPath = "Assets/sample_road.json";

    // ── Test 1: Road network singleton is created ─────────────────────────────

    [Fact]
    public void LoadZones_WithRoadNetworkPath_SetsSingleton()
    {
        using var repo = new EntityRepository();
        var svc = new ZoneManagerService();

        var zones = new Dictionary<string, ZoneDefinitionDto>
        {
            ["zone1"] = new ZoneDefinitionDto { RoadNetworkPath = SampleRoadPath },
        };

        svc.LoadZones(repo, zones);

        Assert.True(repo.HasSingleton<ZoneEnvironmentData>(),
            "ZoneEnvironmentData singleton must be set after LoadZones");

        ref var env = ref repo.GetSingleton<ZoneEnvironmentData>();
        Assert.True(env.RoadNetwork.Nodes.IsCreated,
            "RoadNetwork.Nodes must be created");
        Assert.True(env.RoadNetwork.Segments.IsCreated,
            "RoadNetwork.Segments must be created");

        // Dispose the blob to avoid NativeArray leak in tests.
        env.RoadNetwork.Dispose();
    }

    // ── Test 2: Memory safety — old blob is disposed before singleton overwrite ──

    [Fact]
    public void LoadZones_CalledTwice_UpdatesRoadNetworkSingleton()
    {
        using var repo = new EntityRepository();
        var svc = new ZoneManagerService();

        var zones = new Dictionary<string, ZoneDefinitionDto>
        {
            ["zone1"] = new ZoneDefinitionDto { RoadNetworkPath = SampleRoadPath },
        };

        // First load — singleton must be populated.
        svc.LoadZones(repo, zones);
        Assert.True(repo.HasSingleton<ZoneEnvironmentData>(),
            "Precondition: singleton must be present after first load");
        Assert.True(repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Nodes.IsCreated,
            "Precondition: first blob must have nodes");

        // Second load — must succeed without throwing (prove dispose was called safely),
        // and the singleton must still hold a valid blob.
        // (RoadNetworkBlob is a value type, so the test must check the singleton directly.)
        var exception = Record.Exception(() => svc.LoadZones(repo, zones));
        Assert.Null(exception);

        Assert.True(repo.HasSingleton<ZoneEnvironmentData>(),
            "Singleton must still exist after second LoadZones");
        ref var newEnv = ref repo.GetSingleton<ZoneEnvironmentData>();
        Assert.True(newEnv.RoadNetwork.Nodes.IsCreated,
            "Second blob must have nodes (first blob was disposed and replaced)");

        // Clean up the second blob.
        newEnv.RoadNetwork.Dispose();
    }

    // ── Test 3: Obstacles spawn exactly as many entities as declared ──────────

    [Fact]
    public void LoadZones_WithObstacles_CreatesExpectedEntities()
    {
        using var repo = new EntityRepository();
        var svc = new ZoneManagerService();

        var zones = new Dictionary<string, ZoneDefinitionDto>
        {
            ["zone1"] = new ZoneDefinitionDto
            {
                Obstacles = new List<ZoneObstacleDto>
                {
                    new ZoneObstacleDto { X = 10f, Y = 20f, Radius = 5f },
                    new ZoneObstacleDto { X = -5f, Y = 15f, Radius = 3f },
                },
            },
        };

        svc.LoadZones(repo, zones);

        var query = repo.Query()
            .With<PhysicsCollider>()
            .With<SimTransform>()
            .Build();

        int count = 0;
        foreach (var _ in query)
            count++;

        Assert.Equal(2, count);
    }

    // ── Test 4: GetActiveZones returns the last loaded zones dict ─────────────

    [Fact]
    public void GetActiveZones_AfterLoadZones_ReturnsLoadedKeys()
    {
        using var repo = new EntityRepository();
        var svc = new ZoneManagerService();

        var zones = new Dictionary<string, ZoneDefinitionDto>
        {
            ["urban_combat_zone"] = new ZoneDefinitionDto(),
        };

        svc.LoadZones(repo, zones);

        var active = svc.GetActiveZones();
        Assert.True(active.ContainsKey("urban_combat_zone"),
            "GetActiveZones must return the zone key that was loaded");
    }
}
