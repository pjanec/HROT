using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CarKinem.Road;
using Fdp.Kernel;
using Fdp.Toolkit.Physics.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-Z006 — Integration test proving the full zone-load pipeline works
/// end-to-end via <see cref="EditorHarness"/>.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class ZoneScenarioLoadIntegrationTests : IDisposable
{
    private readonly string _tempFile;

    public ZoneScenarioLoadIntegrationTests()
    {
        _tempFile = Path.GetTempFileName() + ".json";
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    // ── Single integration test with 8 assertions ─────────────────────────────

    [Fact]
    public void LoadScenario_WithZoneDefinition_PopulatesRoadNetworkAndObstacles()
    {
        // ── Step 1: Build HrotScenarioEnvelopeDto in code ────────────────────
        var envelope = new HrotScenarioEnvelopeDto
        {
            Header = new ScenarioHeaderDto
            {
                SubsystemType = "Hrot.Scenario",
                SchemaVersion = "1.0",
            },
            Zones = new Dictionary<string, ZoneDefinitionDto>
            {
                ["urban_combat_zone"] = new ZoneDefinitionDto
                {
                    RoadNetworkPath = "Assets/sample_road.json",
                    Obstacles = new List<ZoneObstacleDto>
                    {
                        new ZoneObstacleDto { X =  50f, Y =  25f, Radius = 10f },
                        new ZoneObstacleDto { X = -10f, Y = -10f, Radius =  5f },
                    },
                },
            },
            // Entities is null — this is a zone-only scenario with no FDP entities.
        };

        // ── Step 2: Serialise to temp file ────────────────────────────────────
        var json = JsonSerializer.Serialize(envelope, HrotSerializerOptions.HrotJsonOptions);
        File.WriteAllText(_tempFile, json);

        // ── Step 3: Load via EditorHarness ────────────────────────────────────
        using var harness = new EditorHarness();
        harness.Editor.LoadScenario(_tempFile);
        harness.PumpFrames(5);

        var repo = harness.Repo;

        // ── Assertion 4: ZoneEnvironmentData singleton must be set ────────────
        Assert.True(repo.HasSingleton<ZoneEnvironmentData>(),
            "ZoneEnvironmentData singleton must be present after loading a scenario with zones");

        // ── Assertion 5: Road network blobs must be created ───────────────────
        ref var envData = ref repo.GetSingleton<ZoneEnvironmentData>();
        Assert.True(envData.RoadNetwork.Nodes.IsCreated,
            "RoadNetwork.Nodes must be created");
        Assert.True(envData.RoadNetwork.Segments.IsCreated,
            "RoadNetwork.Segments must be created");

        // ── Assertion 6: Exactly 2 obstacle entities ──────────────────────────
        var obstacleQuery = repo.Query()
            .With<PhysicsCollider>()
            .With<SimTransform>()
            .Build();

        int obstacleCount = 0;
        SimTransform obs1 = default;
        SimTransform obs2 = default;
        PhysicsCollider coll1 = default;
        PhysicsCollider coll2 = default;
        int idx = 0;

        foreach (var entity in obstacleQuery)
        {
            if (idx == 0)
            {
                obs1   = repo.GetComponent<SimTransform>(entity);
                coll1  = repo.GetComponent<PhysicsCollider>(entity);
            }
            else if (idx == 1)
            {
                obs2   = repo.GetComponent<SimTransform>(entity);
                coll2  = repo.GetComponent<PhysicsCollider>(entity);
            }
            idx++;
            obstacleCount++;
        }

        Assert.Equal(2, obstacleCount);

        // ── Assertions 7 & 8: Verify obstacle positions and radii ─────────────
        // Entities are created in order, so obstacle 0 = (50, 25, r=10), obstacle 1 = (-10, -10, r=5)
        Assert.Equal(50f,  obs1.Position.X, precision: 4);
        Assert.Equal(25f,  obs1.Position.Y, precision: 4);
        Assert.Equal(10f,  coll1.Radius,    precision: 4);

        Assert.Equal(-10f, obs2.Position.X, precision: 4);
        Assert.Equal(-10f, obs2.Position.Y, precision: 4);
        Assert.Equal(5f,   coll2.Radius,    precision: 4);
    }
}
