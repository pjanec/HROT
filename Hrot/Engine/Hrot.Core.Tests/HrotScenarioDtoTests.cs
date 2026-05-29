using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Xunit;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// PACK3-Z002 — Unit tests for the HROT scenario DTO round-trip and
/// case-insensitive deserialisation.
/// </summary>
public sealed class HrotScenarioDtoTests
{
    // ── Test 1: Round-trip ────────────────────────────────────────────────────

    [Fact]
    public void HrotScenarioEnvelopeDto_RoundTrip_PreservesObstacleValues()
    {
        var dto = new HrotScenarioEnvelopeDto
        {
            Header = new ScenarioHeaderDto
            {
                SubsystemType = "Hrot.Scenario",
            },
            Zones = new Dictionary<string, ZoneDefinitionDto>
            {
                ["urban_zone"] = new ZoneDefinitionDto
                {
                    RoadNetworkPath = "maps/urban/road.bin",
                    Obstacles = new List<ZoneObstacleDto>
                    {
                        new ZoneObstacleDto { X = 10f, Y = 20f, Radius = 5f },
                        new ZoneObstacleDto { X = -15f, Y = 30f, Radius = 3.5f },
                    },
                },
            },
        };

        var json   = JsonSerializer.Serialize(dto, HrotSerializerOptions.HrotJsonOptions);
        var result = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(result);
        Assert.NotNull(result.Zones);
        Assert.True(result.Zones.ContainsKey("urban_zone"), "Zone key must survive round-trip");

        var zone = result.Zones["urban_zone"];
        Assert.NotNull(zone.Obstacles);
        Assert.Equal(2, zone.Obstacles.Count);

        Assert.Equal(10f,  zone.Obstacles[0].X,      3);
        Assert.Equal(20f,  zone.Obstacles[0].Y,      3);
        Assert.Equal(5f,   zone.Obstacles[0].Radius, 3);

        Assert.Equal(-15f, zone.Obstacles[1].X,      3);
        Assert.Equal(30f,  zone.Obstacles[1].Y,      3);
        Assert.Equal(3.5f, zone.Obstacles[1].Radius, 3);
    }

    // ── Test 2: Case-insensitive deserialisation ──────────────────────────────

    [Fact]
    public void ZoneDefinitionDto_DeserializesFromPascalCaseKeys()
    {
        const string pascalCaseJson = """
            {
              "RoadNetworkPath": "maps/urban/road.bin",
              "TerrainDatabaseId": "urban_terrain",
              "Obstacles": [
                { "X": 5.0, "Y": 10.0, "Radius": 2.0 }
              ]
            }
            """;

        var result = JsonSerializer.Deserialize<ZoneDefinitionDto>(
            pascalCaseJson, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(result);
        Assert.NotNull(result.RoadNetworkPath);
        Assert.Equal("maps/urban/road.bin", result.RoadNetworkPath);
        Assert.Equal("urban_terrain", result.TerrainDatabaseId);
        Assert.NotNull(result.Obstacles);
        Assert.Single(result.Obstacles);
        Assert.Equal(5f, result.Obstacles[0].X, 3);
    }
}
