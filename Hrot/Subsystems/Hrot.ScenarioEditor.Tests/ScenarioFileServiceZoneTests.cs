using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// PACK3-Z005 — Unit tests for <see cref="ScenarioFileService.SaveScenario"/> zone support.
/// </summary>
public sealed class ScenarioFileServiceZoneTests : IDisposable
{
    private readonly string _tempFile;

    public ScenarioFileServiceZoneTests()
    {
        _tempFile = Path.GetTempFileName() + ".json";
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScenarioSerializer BuildSerializer() =>
        new ScenarioSerializerBuilder("Hrot.Scenario").Build();

    // ── Test 1: Save with active zone → Zones present in written file ─────────

    [Fact]
    public void SaveScenario_WithActiveZone_WritesZoneSection()
    {
        using var repo = new EntityRepository();

        var zoneSvc = new ZoneManagerService();
        // Populate active zones via LoadZones (obstacles only — no road network file needed).
        zoneSvc.LoadZones(repo, new Dictionary<string, ZoneDefinitionDto>
        {
            ["test_zone"] = new ZoneDefinitionDto
            {
                Obstacles = new List<ZoneObstacleDto>
                {
                    new ZoneObstacleDto { X = 10f, Y = 20f, Radius = 5f },
                },
            },
        });

        var fileService = new ScenarioFileService(BuildSerializer(), zoneService: zoneSvc);
        fileService.SaveScenario(repo, _tempFile);

        var json      = File.ReadAllText(_tempFile);
        var result    = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(result);
        Assert.NotNull(result.Zones);
        Assert.True(result.Zones!.ContainsKey("test_zone"),
            "Saved file must contain the active zone");

        var zone = result.Zones["test_zone"];
        Assert.NotNull(zone.Obstacles);
        Assert.Single(zone.Obstacles!);
        Assert.Equal(10f, zone.Obstacles[0].X, precision: 4);
        Assert.Equal(20f, zone.Obstacles[0].Y, precision: 4);
        Assert.Equal(5f,  zone.Obstacles[0].Radius, precision: 4);
    }

    // ── Test 2: Save without zones → no "zones" key in written file ──────────

    [Fact]
    public void SaveScenario_WithoutActiveZones_OmitsZoneSection()
    {
        using var repo = new EntityRepository();

        // No zones loaded — ZoneManagerService.GetActiveZones returns empty dict.
        var zoneSvc     = new ZoneManagerService();
        var fileService = new ScenarioFileService(BuildSerializer(), zoneService: zoneSvc);
        fileService.SaveScenario(repo, _tempFile);

        var json = File.ReadAllText(_tempFile);
        using var doc  = JsonDocument.Parse(json);

        // "zones" key must be absent when WhenWritingNull is active and no zones are loaded.
        bool hasZones  = doc.RootElement.TryGetProperty("zones", out _) ||
                         doc.RootElement.TryGetProperty("Zones", out _);
        Assert.False(hasZones,
            "Saved file must not contain a 'zones' key when no zones are active");
    }
}
