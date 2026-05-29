using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Tools.EnvelopeStamper;
using Xunit;

namespace Fdp.Tools.EnvelopeStamper.Tests;

public sealed class FixtureStamperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public FixtureStamperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        _stdout.Dispose();
        _stderr.Dispose();
    }

    // Helper: write a JSON file into the temp directory.
    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // T01 — Scenario file gets stamped with Hrot.Scenario v=1
    [Fact]
    public void T01_ScenarioFile_GetsStamped()
    {
        var path = WriteFile("scenario.json",
            """{ "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }, "entities": {} }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(1, result.Stamped);
        Assert.Equal(0, result.Errors);

        var dom = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Assert.NotNull(dom);

        var meta = dom!["$meta"] as JsonObject;
        Assert.NotNull(meta);
        Assert.Equal("Hrot.Scenario", meta!["docType"]!.GetValue<string>());
        Assert.Equal(1, meta["schemaVersion"]!.GetValue<int>());

        // $meta must be the first property.
        Assert.Equal("$meta", dom.First().Key);
    }

    // T02 — Blueprint file gets stamped with Hrot.Blueprints v=1
    [Fact]
    public void T02_BlueprintFile_GetsStamped()
    {
        var path = WriteFile("foo.bp.json",
            """{ "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" }, "AssetId": "..." }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(1, result.Stamped);

        var dom = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Assert.NotNull(dom);

        var meta = dom!["$meta"] as JsonObject;
        Assert.NotNull(meta);
        Assert.Equal("Hrot.Blueprints", meta!["docType"]!.GetValue<string>());
        Assert.Equal(1, meta["schemaVersion"]!.GetValue<int>());
    }

    // T03 — Road network file gets stamped with Fdp.RoadNetwork v=1
    [Fact]
    public void T03_RoadNetworkFile_GetsStamped()
    {
        var path = WriteFile("sample_road.json",
            """{ "nodes": [], "segments": [] }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(1, result.Stamped);

        var dom = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Assert.NotNull(dom);

        var meta = dom!["$meta"] as JsonObject;
        Assert.NotNull(meta);
        Assert.Equal("Fdp.RoadNetwork", meta!["docType"]!.GetValue<string>());
        Assert.Equal(1, meta["schemaVersion"]!.GetValue<int>());
    }

    // T04 — Already-stamped file is skipped (idempotency)
    [Fact]
    public void T04_AlreadyStampedFile_IsSkipped()
    {
        WriteFile("scenario.json",
            """{ "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1 }, "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }, "entities": {} }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(0, result.Stamped);
        Assert.Equal(1, result.AlreadyStamped);
    }

    // T05 — xunit.runner.json is excluded
    [Fact]
    public void T05_XunitRunnerJson_IsExcluded()
    {
        var path = WriteFile("xunit.runner.json",
            """{ "methodDisplay": "method" }""");
        var originalContent = File.ReadAllText(path);

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(0, result.Stamped);
        Assert.Equal(originalContent, File.ReadAllText(path));
    }

    // T06 — Files in ExtDeps subdirectory are excluded
    [Fact]
    public void T06_ExtDepsFiles_AreExcluded()
    {
        WriteFile(Path.Combine("ExtDeps", "some_lib", "data.json"),
            """{ "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }, "entities": {} }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(0, result.Stamped);
    }

    // T07 — dry-run does not modify files
    [Fact]
    public void T07_DryRun_DoesNotModifyFiles()
    {
        var path = WriteFile("scenario.json",
            """{ "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }, "entities": {} }""");
        var originalContent = File.ReadAllText(path);

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: true, _stdout, _stderr);

        // Counted as would-stamp.
        Assert.Equal(1, result.Stamped);
        // File content must be unchanged.
        Assert.Equal(originalContent, File.ReadAllText(path));
    }

    // T08 — OrchestratorContext fixture gets stamped with schemaVersion=2 (C-4)
    [Fact]
    public void T08_OrchestratorContext_GetsSchemaVersion2()
    {
        var path = WriteFile("context.json",
            """{ "header": { "subsystemType": "Hrot.OrchestratorContext", "schemaVersion": "2.0" }, "data": {} }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(1, result.Stamped);
        Assert.Equal(0, result.Errors);

        var dom = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Assert.NotNull(dom);

        var meta = dom!["$meta"] as JsonObject;
        Assert.NotNull(meta);
        Assert.Equal("Hrot.OrchestratorContext", meta!["docType"]!.GetValue<string>());
        Assert.Equal(2, meta["schemaVersion"]!.GetValue<int>());
    }

    // T09 — Unknown format file is skipped
    [Fact]
    public void T09_UnknownFormatFile_IsSkipped()
    {
        WriteFile("random.json",
            """{ "foo": "bar", "baz": 42 }""");

        var result = FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        Assert.Equal(0, result.Stamped);
        Assert.True(result.Skipped >= 1);
    }

    // T10 — $meta is the first property after stamping; old "header" is preserved
    [Fact]
    public void T10_MetaIsFirstProperty_AndOldHeaderPreserved()
    {
        var path = WriteFile("scenario.json",
            """{ "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }, "entities": {} }""");

        FixtureStamper.StampDirectory(_tempDir, dryRun: false, _stdout, _stderr);

        var dom = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        Assert.NotNull(dom);

        // First key must be "$meta".
        Assert.Equal("$meta", dom!.First().Key);

        // Old "header" field must still be present.
        Assert.True(dom.ContainsKey("header"), "Original 'header' field must be preserved.");
    }
}
