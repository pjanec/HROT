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
/// TKB-021 -- Unit tests for TkbName stamping in ScenarioFileService.SaveScenario.
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

    // ── Test 1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveScenario_WithActiveTkbName_StampsTkbNameInHeader()
    {
        var db = new TkbDatabase();
        db.ActiveTkbName = "Sample_v1";

        using var repo = new EntityRepository();
        var svc = new ScenarioFileService(BuildSerializer(), tkbDb: db);
        svc.SaveScenario(repo, _tempFile);

        var json     = File.ReadAllText(_tempFile);
        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal("Sample_v1", envelope!.Header!.TkbName);
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveScenario_WithNullActiveTkbName_OmitsOrNullsTkbName()
    {
        var db = new TkbDatabase();
        // ActiveTkbName is null by default

        using var repo = new EntityRepository();
        var svc = new ScenarioFileService(BuildSerializer(), tkbDb: db);
        svc.SaveScenario(repo, _tempFile);

        var json     = File.ReadAllText(_tempFile);
        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        // Phase 2: Header node is omitted entirely when TkbName is null.
        Assert.True(envelope!.Header == null || envelope.Header.TkbName == null);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveScenario_WithoutTkbDatabase_OmitsOrNullsTkbName()
    {
        using var repo = new EntityRepository();
        var svc = new ScenarioFileService(BuildSerializer());
        svc.SaveScenario(repo, _tempFile);

        var json     = File.ReadAllText(_tempFile);
        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        // Phase 2: Header node is omitted entirely when TkbName is null.
        Assert.True(envelope!.Header == null || envelope.Header.TkbName == null);
    }
}
