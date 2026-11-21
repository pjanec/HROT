using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// TKB-021 -- what <c>ScenarioFileService.SaveScenario</c> stamps into the header: the terrain, never a TKB (<c>R-4018</c>).
/// </summary>
// ⭐⭐⭐ QA-008 (second assembly, 2026-09-18) — this class calls ComponentTypeRegistry.Clear(),
//    a PROCESS-GLOBAL mutation. Without this attribute it runs in parallel with every other
//    collection and deletes their component registrations mid-test — the DEBT-AIB-030 flake,
//    measured here as SIX distinct victims across four runs, all green under --filter.
//    📄 ComponentTypeRegistryMutatorCollection.cs carries the measurement.
[Collection(Hrot.Presentation.Tests.ComponentTypeRegistryMutatorCollection.Name)]
public sealed class ScenarioFileServiceTkbTests : IDisposable
{
    private readonly string _tempFile;

    public ScenarioFileServiceTkbTests()
    {
        ComponentTypeRegistry.Clear();
        _tempFile = Path.GetTempFileName() + ".json";
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        ComponentTypeRegistry.Clear();
    }

    private static ScenarioSerializer BuildSerializer() =>
        new ScenarioSerializerBuilder("Hrot.Scenario").Build();

    // ── R-4018 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <c>R-4018</c> — a save never writes a TKB name: a scenario does not name a TKB. ⚠ Asserted on the RAW file,
    /// so neither casing can slip through. (Was: three rails that the active TKB name was stamped into the header.)
    /// </summary>
    [Fact]
    public void SaveScenario_NeverWritesATkbName()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainDefinition>();
        repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainDefinition { Name = "test-town", ResolvedName = "test-town" });

        new ScenarioFileService(BuildSerializer()).SaveScenario(repo, _tempFile);

        Assert.DoesNotContain("tkbname", File.ReadAllText(_tempFile), StringComparison.OrdinalIgnoreCase);
    }

    // ── CE-3015 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// CE-3015: a save stamps the RESIDENT terrain, so a load→save round-trip keeps the scenario's terrain.
    /// </summary>
    [Fact]
    public void SaveScenario_WithResidentTerrain_StampsTerrainNameInHeader_CE3015()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainDefinition>();
        repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainDefinition
        {
            Name = "self-declared", ResolvedName = "test-town",
        });

        var svc = new ScenarioFileService(BuildSerializer());
        svc.SaveScenario(repo, _tempFile);

        var json     = File.ReadAllText(_tempFile);
        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope?.Header);
        Assert.Equal("test-town", envelope!.Header!.TerrainName);
    }
}
