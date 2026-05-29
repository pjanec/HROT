using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;

namespace Hrot.Common.Tests.Migrations;

/// <summary>
/// Tests for the HROT migration module registrations (JM-P2-002).
/// </summary>
public sealed class ModuleRegistrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MigrationServices BuildServices(Action<MigrationRegistry> registerFormats)
    {
        return MigrationBootstrap.Build(
            registerFormats,
            new InMemoryMigrationStorage(),
            () => "test-1.0",
            "Hrot.Common.Tests");
    }

    // ── JM-P2-002-T01 ────────────────────────────────────────────────────────

    /// <summary>
    /// PassthroughFormatsModule.RegisterAll registers exactly 5 document types
    /// (StructEdit, MapInteractionConfig, OrchestratorContext, TestScript,
    /// NodeConfiguration) and the resulting registry seals cleanly.
    /// </summary>
    [Fact]
    public void PassthroughFormatsModule_RegisterAll_RegistersFiveDocTypes()
    {
        MigrationServices services = BuildServices(reg => PassthroughFormatsModule.RegisterAll(reg));

        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.StructEdit));
        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.MapInteractionConfig));
        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.OrchestratorContext));
        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.TestScript));
        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.NodeConfiguration));
    }

    // ── JM-P2-002-T02 ────────────────────────────────────────────────────────

    /// <summary>
    /// ScenarioMigrationModule.RegisterAll registers HrotDocumentTypes.Scenario at
    /// CurrentVersion = 1 without throwing.
    /// </summary>
    [Fact]
    public void ScenarioMigrationModule_RegisterAll_RegistersScenarioDocType()
    {
        MigrationServices services = BuildServices(reg => ScenarioMigrationModule.RegisterAll(reg));

        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.Scenario));
        Assert.Equal(ScenarioMigrationModule.CurrentVersion, services.Registry.GetCurrentVersion(HrotDocumentTypes.Scenario));
    }

    // ── JM-P2-002-T03 ────────────────────────────────────────────────────────

    /// <summary>
    /// BlueprintMigrationModule.RegisterAll registers HrotDocumentTypes.Blueprint at
    /// CurrentVersion = 1 without throwing.
    /// </summary>
    [Fact]
    public void BlueprintMigrationModule_RegisterAll_RegistersBlueprintDocType()
    {
        MigrationServices services = BuildServices(reg => BlueprintMigrationModule.RegisterAll(reg));

        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.Blueprint));
        Assert.Equal(BlueprintMigrationModule.CurrentVersion, services.Registry.GetCurrentVersion(HrotDocumentTypes.Blueprint));
    }

    // ── JM-P2-002-T04 ────────────────────────────────────────────────────────

    /// <summary>
    /// TkbMigrationModule.RegisterAll registers HrotDocumentTypes.TkbDefinition at
    /// CurrentVersion = 1 without throwing.
    /// </summary>
    [Fact]
    public void TkbMigrationModule_RegisterAll_RegistersTkbDocType()
    {
        MigrationServices services = BuildServices(reg => TkbMigrationModule.RegisterAll(reg));

        Assert.True(services.Registry.IsRegistered(HrotDocumentTypes.TkbDefinition));
        Assert.Equal(TkbMigrationModule.CurrentVersion, services.Registry.GetCurrentVersion(HrotDocumentTypes.TkbDefinition));
    }

    // ── JM-P2-002-T05 ────────────────────────────────────────────────────────

    /// <summary>
    /// RoadNetworkMigrationModule.RegisterAll registers FdpDocumentTypes.RoadNetwork at
    /// CurrentVersion = 1 without throwing.
    /// </summary>
    [Fact]
    public void RoadNetworkMigrationModule_RegisterAll_RegistersRoadNetworkDocType()
    {
        MigrationServices services = BuildServices(reg => RoadNetworkMigrationModule.RegisterAll(reg));

        Assert.True(services.Registry.IsRegistered(FdpDocumentTypes.RoadNetwork));
        Assert.Equal(RoadNetworkMigrationModule.CurrentVersion, services.Registry.GetCurrentVersion(FdpDocumentTypes.RoadNetwork));
    }

    // ── JM-P2-002-T06 ────────────────────────────────────────────────────────

    /// <summary>
    /// All public const string fields on HrotDocumentTypes are non-null and non-empty.
    /// </summary>
    [Fact]
    public void HrotDocumentTypes_AllConstantsAreNonEmpty()
    {
        var fields = typeof(HrotDocumentTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        // Verify that at least some constants were found (guards against future reflection changes).
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.False(
                string.IsNullOrEmpty(value),
                $"HrotDocumentTypes.{field.Name} must not be null or empty.");
        }
    }

    // ── JM-P2-002-T07 ────────────────────────────────────────────────────────

    /// <summary>
    /// OrchestratorContext is registered at version 2 (C-4). Loading a JSON document
    /// with <c>$meta.schemaVersion: 2</c> succeeds without triggering any migration
    /// (current == file version).
    /// </summary>
    [Fact]
    public async Task OrchestratorContext_RegistersAtVersionTwo()
    {
        MigrationServices services = BuildServices(reg => PassthroughFormatsModule.RegisterAll(reg));

        // Verify the registration is at version 2.
        Assert.Equal(2, services.Registry.GetCurrentVersion(HrotDocumentTypes.OrchestratorContext));

        // Build a minimal valid OrchestratorContext JSON with $meta.schemaVersion = 2.
        const string json = """
            {
              "$meta": {
                "docType": "Hrot.OrchestratorContext",
                "schemaVersion": 2
              },
              "startWallTicks": 0,
              "sceneId": "test-scene",
              "scenarioId": "test-scenario",
              "scenarioTimeSeconds": 0.0
            }
            """;

        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(utf8);

        // LoadAndMigrateAsync on a passthrough type at current version must succeed without error.
        var outcome = await services.ReadOnly.LoadAndMigrateAsync(stream, "test-orchestrator.json");

        Assert.NotNull(outcome);
        // Fast path: version matches, so no migration was applied and Report is null.
        Assert.False(outcome.WasMigrated);
        // When not migrated, Report is null (no migration report produced on fast path).
        Assert.Null(outcome.Report);
    }
}
