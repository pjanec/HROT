using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="ReadOnlyMigrationAdapter"/> and
/// <see cref="ReadOnlyLoadOutcome"/> (T2-001..T2-010).
/// </summary>
public sealed class ReadOnlyMigrationAdapterTests : IDisposable
{
    private readonly string _tempDir;

    public ReadOnlyMigrationAdapterTests()
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
    // Helpers
    // ---------------------------------------------------------------

    // Builds a pipeline with "Test.Doc" registered at the given currentVersion.
    // Migrators use StubMigrator (no-op except for schemaVersion advancement).
    private static MigrationPipeline BuildPipeline(int currentVersion = 2)
    {
        var registry = new MigrationRegistry();
        registry.RegisterDocType(
            "Test.Doc",
            currentVersion,
            MigratorFactory.MakeAllPairs("Test.Doc", currentVersion));
        return new MigrationPipeline(registry);
    }

    // Writes a test JSON document at the given schemaVersion to a temp file.
    private string WriteTestFile(int schemaVersion, string fieldValue = "hello")
    {
        var json = BuildTestJson(schemaVersion, fieldValue);
        var path = Path.Combine(_tempDir, $"doc_v{schemaVersion}_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    // Builds the raw JSON string for a test document.
    private static string BuildTestJson(int schemaVersion, string fieldValue = "hello")
        => "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":" + schemaVersion +
           "},\"name\":\"" + fieldValue + "\"}";

    // Non-seekable stream wrapper for T2-007.
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value)
            => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------
    // T2-001: Fast path — document already at current version
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_AtCurrentVersion_FastPath_NoMigration()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var path = WriteTestFile(schemaVersion: 2);

        var outcome = await adapter.LoadAndMigrateAsync(path);

        Assert.False(outcome.WasMigrated);
        Assert.NotNull(outcome.RawContent);
        Assert.Null(outcome.MigratedDom);
        Assert.Null(outcome.Report);

        // Content should be identical to what was written.
        var expected = BuildTestJson(schemaVersion: 2);
        Assert.Equal(expected, outcome.RawContent);
    }

    // ---------------------------------------------------------------
    // T2-002: Slow path — older version triggers migration
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_OlderVersion_SlowPath_Migrates()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var path = WriteTestFile(schemaVersion: 1);

        var outcome = await adapter.LoadAndMigrateAsync(path);

        Assert.True(outcome.WasMigrated);
        Assert.NotNull(outcome.MigratedDom);
        Assert.Null(outcome.RawContent);

        // Meta should reflect the post-migration (current) version.
        Assert.Equal(2, outcome.Meta.SchemaVersion);
    }

    // ---------------------------------------------------------------
    // T2-003: No sidecar written — structural guarantee, verified by
    //         smoke test: calling twice returns consistent results.
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_NoSidecarWritten()
    {
        // ReadOnlyMigrationAdapter has no storage dependency, so no sidecar
        // files are ever created. Verified structurally by design.
        // Smoke test: two calls on the same file return WasMigrated=false
        // with identical RawContent.
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var path = WriteTestFile(schemaVersion: 2);

        var first = await adapter.LoadAndMigrateAsync(path);
        var second = await adapter.LoadAndMigrateAsync(path);

        Assert.False(first.WasMigrated);
        Assert.False(second.WasMigrated);
        Assert.Equal(first.RawContent, second.RawContent);
    }

    // ---------------------------------------------------------------
    // T2-004: AsJsonObject on fast path allocates DOM on demand
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_AsJsonObject_FastPath_AllocatesOnDemand()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var path = WriteTestFile(schemaVersion: 2, fieldValue: "world");

        var outcome = await adapter.LoadAndMigrateAsync(path);

        Assert.False(outcome.WasMigrated);
        Assert.Null(outcome.MigratedDom);

        var dom = outcome.AsJsonObject();
        Assert.NotNull(dom);

        // Verify the parsed DOM contains expected field data.
        Assert.Equal("world", dom["name"]?.GetValue<string>());
        Assert.Equal(2, dom["$meta"]!["schemaVersion"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T2-005: AsJsonString on slow path serializes the migrated DOM
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_AsJsonString_SlowPath_SerializesDom()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var path = WriteTestFile(schemaVersion: 1);

        var outcome = await adapter.LoadAndMigrateAsync(path);

        Assert.True(outcome.WasMigrated);

        var json = outcome.AsJsonString();
        Assert.NotNull(json);

        // Parse the serialized string and verify it is at the current schema version.
        var reparsed = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(2, reparsed["$meta"]!["schemaVersion"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T2-006: Stream overload works identically to file overload
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_StreamInput_WorksIdentically()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var path = WriteTestFile(schemaVersion: 2, fieldValue: "stream-test");

        var fileOutcome = await adapter.LoadAndMigrateAsync(path);

        var bytes = Encoding.UTF8.GetBytes(BuildTestJson(schemaVersion: 2, fieldValue: "stream-test"));
        using var ms = new MemoryStream(bytes);
        var streamOutcome = await adapter.LoadAndMigrateAsync(ms, "stream-source");

        Assert.Equal(fileOutcome.WasMigrated, streamOutcome.WasMigrated);
        Assert.Equal(fileOutcome.RawContent, streamOutcome.RawContent);
    }

    // ---------------------------------------------------------------
    // T2-007: Non-seekable stream is buffered and processed correctly
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_NonSeekableStream_BuffersAndProcesses()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var bytes = Encoding.UTF8.GetBytes(BuildTestJson(schemaVersion: 2, fieldValue: "non-seek"));
        using var ms = new MemoryStream(bytes);
        using var nonSeekable = new NonSeekableStream(ms);

        var outcome = await adapter.LoadAndMigrateAsync(nonSeekable, "non-seekable-source");

        Assert.False(outcome.WasMigrated);
        Assert.NotNull(outcome.RawContent);
        Assert.Contains("non-seek", outcome.RawContent);
    }

    // ---------------------------------------------------------------
    // T2-008: File not found throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_FileNotFound_Throws()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));
        var missingPath = Path.Combine(_tempDir, "does-not-exist.json");

        await Assert.ThrowsAsync<MigrationException>(() =>
            adapter.LoadAndMigrateAsync(missingPath));
    }

    // ---------------------------------------------------------------
    // T2-009: Unknown doc type throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_UnknownDocType_Throws()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));

        // Build a document with a valid $meta but an unregistered docType.
        var unknownJson = "{\"$meta\":{\"docType\":\"Unknown.Doc\",\"schemaVersion\":1}," +
                          "\"data\":\"value\"}";
        var path = Path.Combine(_tempDir, "unknown.json");
        File.WriteAllText(path, unknownJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await Assert.ThrowsAsync<MigrationException>(() =>
            adapter.LoadAndMigrateAsync(path));
    }

    // ---------------------------------------------------------------
    // T2-010: Malformed envelope (missing $meta) throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_MalformedEnvelope_Throws()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));

        // Build a document with no $meta.
        var noMetaJson = "{\"name\":\"no-meta\",\"value\":42}";
        var path = Path.Combine(_tempDir, "no-meta.json");
        File.WriteAllText(path, noMetaJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await Assert.ThrowsAsync<MigrationException>(() =>
            adapter.LoadAndMigrateAsync(path));
    }

    // ---------------------------------------------------------------
    // T2-011: Null stream throws ArgumentNullException
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrate_NullStream_ThrowsArgumentNullException()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            adapter.LoadAndMigrateAsync(null!, "src"));
    }

    // ---------------------------------------------------------------
    // T2-013: ReadOnlyLoadOutcome.AsJsonObject with invalid state throws
    // ---------------------------------------------------------------
    [Fact]
    public void ReadOnlyLoadOutcome_AsJsonObject_InvalidState_Throws()
    {
        var outcome = new ReadOnlyLoadOutcome
        {
            Meta = new DocumentMeta("Test.Doc", 1),
            WasMigrated = false,
            RawContent = null,
            MigratedDom = null,
        };

        Assert.Throws<InvalidOperationException>(() => outcome.AsJsonObject());
    }

    // ---------------------------------------------------------------
    // T2-014: ReadOnlyLoadOutcome.AsJsonString with invalid state throws
    // ---------------------------------------------------------------
    [Fact]
    public void ReadOnlyLoadOutcome_AsJsonString_InvalidState_Throws()
    {
        var outcome = new ReadOnlyLoadOutcome
        {
            Meta = new DocumentMeta("Test.Doc", 1),
            WasMigrated = false,
            RawContent = null,
            MigratedDom = null,
        };

        Assert.Throws<InvalidOperationException>(() => outcome.AsJsonString());
    }

    // T2-015: ReadOnlyLoadOutcome.AsJsonObject fast path (RawContent set, MigratedDom null)
    // ---------------------------------------------------------------
    [Fact]
    public void ReadOnlyLoadOutcome_AsJsonObject_FastPath_ParsesRawContent()
    {
        const string rawJson = "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1},\"x\":99}";
        var outcome = new ReadOnlyLoadOutcome
        {
            Meta = new DocumentMeta("Test.Doc", 1),
            WasMigrated = false,
            RawContent = rawJson,
            MigratedDom = null,
        };

        var obj = outcome.AsJsonObject();
        Assert.Equal(99, obj["x"]!.GetValue<int>());
    }

    // T2-016: ReadOnlyLoadOutcome.AsJsonString slow path (MigratedDom set, RawContent null)
    // ---------------------------------------------------------------
    [Fact]
    public void ReadOnlyLoadOutcome_AsJsonString_SlowPath_SerializesMigratedDom()
    {
        var dom = System.Text.Json.Nodes.JsonNode.Parse("{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1},\"y\":7}")!.AsObject();
        var outcome = new ReadOnlyLoadOutcome
        {
            Meta = new DocumentMeta("Test.Doc", 1),
            WasMigrated = true,
            RawContent = null,
            MigratedDom = dom,
        };

        var json = outcome.AsJsonString();
        Assert.Contains("\"y\":7", json);
    }

    // T2-017: LoadAndMigrateAsync(path) throws MigrationException on IO error (Windows only).
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task LoadAndMigrateAsync_Path_LockedFile_ThrowsMigrationException()
    {
        Skip.IfNot(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows));

        var filePath = Path.Combine(_tempDir, "locked.json");
        await File.WriteAllTextAsync(filePath,
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1},\"v\":1}");

        var pipeline = BuildPipeline(2);
        var adapter = new ReadOnlyMigrationAdapter(pipeline);

        // Hold an exclusive lock so the adapter cannot read the file.
        using var lockStream = new FileStream(filePath,
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            adapter.LoadAndMigrateAsync(filePath));
    }

    // T2-018: AsJsonObject returns MigratedDom directly when it is not null.
    // ---------------------------------------------------------------
    [Fact]
    public void ReadOnlyLoadOutcome_AsJsonObject_SlowPath_ReturnsMigratedDom()
    {
        var dom = System.Text.Json.Nodes.JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":2},\"z\":42}")!.AsObject();
        var outcome = new ReadOnlyLoadOutcome
        {
            Meta = new DocumentMeta("Test.Doc", 2),
            WasMigrated = true,
            RawContent = null,
            MigratedDom = dom,
        };

        var result = outcome.AsJsonObject();
        Assert.Same(dom, result);
    }

    // T2-019: AsJsonString returns RawContent directly when it is not null.
    // ---------------------------------------------------------------
    [Fact]
    public void ReadOnlyLoadOutcome_AsJsonString_FastPath_ReturnsRawContent()
    {
        const string raw =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":2},\"k\":\"v\"}";
        var outcome = new ReadOnlyLoadOutcome
        {
            Meta = new DocumentMeta("Test.Doc", 2),
            WasMigrated = false,
            RawContent = raw,
            MigratedDom = null,
        };

        Assert.Equal(raw, outcome.AsJsonString());
    }

    // T2-020: ReadOnlyMigrationAdapter constructor rejects null pipeline.
    // ---------------------------------------------------------------
    [Fact]
    public void Constructor_NullPipeline_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ReadOnlyMigrationAdapter(null!));
    }

    // T2-021: LoadAndMigrateAsync (stream) with valid $meta but corrupt body
    //         triggers the JsonException catch inside ProcessBytes.
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadAndMigrateAsync_CorruptJsonBody_ThrowsMigrationException()
    {
        var adapter = new ReadOnlyMigrationAdapter(BuildPipeline(currentVersion: 2));

        // v1 != v2 (currentVersion), so the slow-path DOM parse runs.
        // The garbage after the comma makes JsonNode.Parse throw JsonException.
        var corruptJson =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1},GARBAGE";
        var bytes = Encoding.UTF8.GetBytes(corruptJson);
        using var ms = new MemoryStream(bytes);

        await Assert.ThrowsAsync<MigrationException>(() =>
            adapter.LoadAndMigrateAsync(ms, "corrupt-source"));
    }
}
