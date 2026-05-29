using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Hrot.ClusterRunner.Migration;
using Hrot.Common.Scenario.Migrations;

namespace Hrot.ClusterRunner.Tests.Migration;

/// <summary>
/// Integration tests for <see cref="MigrateMode"/> (JM-P4-004, JM-P4-005).
/// Each test runs against a fresh temp directory that is deleted in a finally block.
/// </summary>
public sealed class MigrateModeTests
{
    // ── Inline test fixtures ──────────────────────────────────────────────

    private const string V1ScenarioJson = @"{
  ""$meta"": { ""docType"": ""Hrot.Scenario"", ""schemaVersion"": 1 },
  ""entities"": {
    ""test-entity-001"": {
      ""EntityInfo"": { ""Name"": ""Alpha"", ""ForceId"": ""Friend"" }
    }
  }
}";

    private const string V2ScenarioJson = @"{
  ""$meta"": { ""docType"": ""Hrot.Scenario"", ""schemaVersion"": 2 },
  ""entities"": {
    ""test-entity-001"": {
      ""EntityInfo"": { ""Name"": ""Alpha"", ""ForceId"": ""Friend"", ""Tags"": [] }
    }
  }
}";

    private const string NoMetaJson = @"{ ""data"": { ""value"": 42 } }";

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MigrationServices BuildServices() =>
        HrotMigrationBootstrap.BuildClusterRunnerMigrate();

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MigrateModeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static MigrateMode CreateMode(
        MigrationServices services,
        string dir,
        StringWriter output,
        int targetVersion = -1,
        bool dryRun = false) =>
        new MigrateMode(services, dir, targetVersion, dryRun, output);

    // ── T_CLI_01 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoJsonFiles_ReturnsZero()
    {
        string dir = CreateTempDir();
        try
        {
            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output);

            int exitCode = await mode.RunAsync();

            Assert.Equal(0, exitCode);
            Assert.Contains("0 migrated, 0 skipped, 0 failed", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_02 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_FileWithNoMeta_SkipsFile()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "no-meta.json"), NoMetaJson, Encoding.UTF8);

            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("SKIPPED", log);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_03 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_V1FileAlreadyAtCurrent_SkipsFile()
    {
        string dir = CreateTempDir();
        try
        {
            // Current registered version for Hrot.Scenario is 2.
            string filePath = Path.Combine(dir, "scenario.json");
            File.WriteAllText(filePath, V2ScenarioJson, Encoding.UTF8);
            string originalContent = File.ReadAllText(filePath);

            var services = BuildServices();
            var output = new StringWriter();
            // target -1 resolves to current version = 2; file is already v2
            var mode = CreateMode(services, dir, output, targetVersion: -1);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("SKIPPED (already at target)", log);
            // File must be unchanged.
            Assert.Equal(originalContent, File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_04 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_V1File_MigratesToV2_WritesFile()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "scenario.json");
            File.WriteAllText(filePath, V1ScenarioJson, Encoding.UTF8);

            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output, targetVersion: -1, dryRun: false);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("OK (v1 -> v2)", log);

            // Verify the written file has schemaVersion: 2.
            var dom = JsonNode.Parse(File.ReadAllText(filePath))!.AsObject();
            Assert.Equal(2, dom["$meta"]!["schemaVersion"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_05 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DryRun_DoesNotWriteFile()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "scenario.json");
            File.WriteAllText(filePath, V1ScenarioJson, Encoding.UTF8);

            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output, targetVersion: -1, dryRun: true);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("OK (v1 -> v2) [dry-run]", log);

            // File on disk must still be v1.
            var dom = JsonNode.Parse(File.ReadAllText(filePath))!.AsObject();
            Assert.Equal(1, dom["$meta"]!["schemaVersion"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_06 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ExplicitTargetVersion1_OnV2File_MigratesToV1()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "scenario.json");
            File.WriteAllText(filePath, V2ScenarioJson, Encoding.UTF8);

            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output, targetVersion: 1, dryRun: false);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("OK (v2 -> v1)", log);

            // Written file must be v1 and have no Tags.
            string writtenJson = File.ReadAllText(filePath);
            byte[] writtenBytes = Encoding.UTF8.GetBytes(writtenJson);
            var meta = JsonEnvelope.Peek(writtenBytes.AsSpan());
            Assert.Equal(1, meta.SchemaVersion);

            var dom = JsonNode.Parse(writtenJson)!.AsObject();
            var entityInfo = dom["entities"]!["test-entity-001"]!["EntityInfo"]!.AsObject();
            Assert.False(entityInfo.ContainsKey("Tags"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_07 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_FailedFileMigration_ReturnsNonZero()
    {
        string dir = CreateTempDir();
        try
        {
            // A v1 file with targetVersion=99 has no migration path -> Pipeline throws -> FAILED.
            string filePath = Path.Combine(dir, "scenario.json");
            File.WriteAllText(filePath, V1ScenarioJson, Encoding.UTF8);

            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output, targetVersion: 99, dryRun: false);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("FAILED", log);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── T_CLI_08 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MultipleFiles_ReportsAllResults()
    {
        string dir = CreateTempDir();
        try
        {
            // 1 v1 file (will migrate), 1 v2 file (skipped), 1 no-meta (skipped).
            File.WriteAllText(Path.Combine(dir, "v1.json"), V1ScenarioJson, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "v2.json"), V2ScenarioJson, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "no-meta.json"), NoMetaJson, Encoding.UTF8);

            var services = BuildServices();
            var output = new StringWriter();
            var mode = CreateMode(services, dir, output, targetVersion: -1, dryRun: false);

            int exitCode = await mode.RunAsync();

            string log = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 migrated, 2 skipped, 0 failed", log);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
