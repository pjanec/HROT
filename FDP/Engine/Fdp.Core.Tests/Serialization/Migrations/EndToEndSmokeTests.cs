using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// End-to-end smoke tests exercising the full stack from
/// <see cref="MigrationBootstrap.Build"/> through
/// <see cref="FileSystemMigrationStorage"/> (T4-001, T4-002).
/// </summary>
public sealed class EndToEndSmokeTests : IDisposable
{
    private readonly string _tempDir;

    public EndToEndSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ---------------------------------------------------------------
    // T4-001: Full stack bootstrap with real filesystem round-trips losslessly
    // ---------------------------------------------------------------
    [Fact]
    public async Task FullStack_Bootstrap_RealFilesystem_RoundTripsLosslessly()
    {
        // ARRANGE
        var services = MigrationBootstrap.Build(
            reg => reg.RegisterDocType(
                "Test.Doc", 2,
                new IJsonDocumentMigrator[] { new TestDocV1ToV2(), new TestDocV2ToV1() }),
            new FileSystemMigrationStorage(),
            () => "smoke-test-1.0",
            "SmokeTestTool");

        // Write a v1 fixture to disk.
        var docPath = Path.Combine(_tempDir, "smoke-doc.json");
        var v1Json =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1}," +
            "\"items\":[{\"name\":\"alpha\"},{\"name\":\"beta\"}]}";
        File.WriteAllText(docPath, v1Json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // ACT 1: Load v1 — triggers migration to v2 (TestDocV1ToV2 adds "kind":"default").
        var loadResult = await services.Persistent.LoadAndMigrateAsync(docPath);

        // ASSERT 1
        Assert.True(loadResult.WasMigrated);
        Assert.Equal(2, loadResult.CurrentMeta.SchemaVersion);
        Assert.Equal("default", loadResult.Dom["items"]![0]!["kind"]!.GetValue<string>());

        // ACT 2: Edit — rename "alpha" to "alpha-edited".
        loadResult.Dom["items"]![0]!.AsObject()["name"] = "alpha-edited";

        // Save back.
        await services.Persistent.SaveAsync(docPath, loadResult.Dom, loadResult);

        // ACT 3: Reload — should be fast path (already v2).
        var reloadResult = await services.Persistent.LoadAndMigrateAsync(docPath);

        // ASSERT 3
        Assert.False(reloadResult.WasMigrated);
        Assert.Equal(2, reloadResult.CurrentMeta.SchemaVersion);
        // Edit preserved.
        Assert.Equal("alpha-edited", reloadResult.Dom["items"]![0]!["name"]!.GetValue<string>());
        // Original "beta" unchanged.
        Assert.Equal("beta", reloadResult.Dom["items"]![1]!["name"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T4-002: Duplicate journal doc type registration throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void FullStack_Bootstrap_DuplicateJournalRegistration_Throws()
    {
        // MigrationBootstrap.Build auto-registers FdpDocumentTypes.MigrationJournal.
        // Attempting to register it again should throw MigrationException.
        Assert.Throws<MigrationException>(() =>
            MigrationBootstrap.Build(
                reg => reg.RegisterPassthroughDocType(FdpDocumentTypes.MigrationJournal, 1),
                new InMemoryMigrationStorage(),
                () => "1.0",
                "Test"));
    }
}
