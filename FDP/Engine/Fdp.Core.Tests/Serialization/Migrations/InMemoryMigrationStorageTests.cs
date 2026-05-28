using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="InMemoryMigrationStorage"/> (T1-310..T1-335).
/// </summary>
public sealed class InMemoryMigrationStorageTests
{
    // Canonical test path and derived base name.
    private const string TestPath = @"C:\data\test.json";

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static (JsonObject pre, JsonObject post) MakeLossyPair()
    {
        var pre = JsonNode.Parse("{\"a\":1,\"b\":\"hello\",\"c\":99}")!.AsObject();
        var post = JsonNode.Parse("{\"a\":1,\"b\":\"hello\"}")!.AsObject();
        return (pre, post);
    }

    private static UnknownsJournal MakeLossyJournal(string hash = "testhash00000000")
    {
        var (pre, post) = MakeLossyPair();
        return UnknownsJournal.Compute(pre, post, "Test.Doc", 2, 1, hash, "1.0", "Test");
    }

    // Produces a raw journal JSON string with a custom sourceContentHash in the body.
    private static string BuildRawJournalJson(string bodyHash)
        => "{" +
           "\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1," +
           "\"engineVersion\":\"1.0\",\"createdBy\":\"Test\"}," +
           "\"sourceDocType\":\"Test.Doc\"," +
           "\"sourceFileVersion\":2," +
           "\"downMigratedToVersion\":1," +
           $"\"sourceContentHash\":\"{bodyHash}\"," +
           "\"operations\":[]" +
           "}";

    // Produces a raw journal JSON string with the wrong docType (to simulate corruption).
    private static string BuildCorruptEnvelopeJson(string bodyHash)
        => "{" +
           "\"$meta\":{\"docType\":\"Wrong.DocType\",\"schemaVersion\":1}," +
           "\"sourceDocType\":\"Test.Doc\"," +
           "\"sourceFileVersion\":2," +
           "\"downMigratedToVersion\":1," +
           $"\"sourceContentHash\":\"{bodyHash}\"," +
           "\"operations\":[]" +
           "}";

    // ---------------------------------------------------------------
    // T1-310: ReadOriginalAsync - existing file returns content
    // ---------------------------------------------------------------
    [Fact]
    public async Task ReadOriginalAsync_ExistingFile_ReturnsContent()
    {
        var storage = new InMemoryMigrationStorage();
        storage.Seed(TestPath, "hello world");

        var result = await storage.ReadOriginalAsync(TestPath);

        Assert.Equal("hello world", result);
    }

    // ---------------------------------------------------------------
    // T1-311: ReadOriginalAsync - nonexistent file returns null
    // ---------------------------------------------------------------
    [Fact]
    public async Task ReadOriginalAsync_NonexistentFile_ReturnsNull()
    {
        var storage = new InMemoryMigrationStorage();

        var result = await storage.ReadOriginalAsync(TestPath);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T1-312: WriteOriginalAsync - new file creates entry
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteOriginalAsync_NewFile_Creates()
    {
        var storage = new InMemoryMigrationStorage();

        await storage.WriteOriginalAsync(TestPath, "new content");

        Assert.Equal("new content", storage.ReadCurrent(TestPath));
    }

    // ---------------------------------------------------------------
    // T1-313: WriteOriginalAsync - existing file is overwritten
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteOriginalAsync_ExistingFile_Overwrites()
    {
        var storage = new InMemoryMigrationStorage();
        storage.Seed(TestPath, "old");

        await storage.WriteOriginalAsync(TestPath, "new");

        Assert.Equal("new", storage.ReadCurrent(TestPath));
    }

    // ---------------------------------------------------------------
    // T1-314: WriteSnapshotAsync creates a sidecar entry
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteSnapshotAsync_CreatesSidecarEntry()
    {
        var storage = new InMemoryMigrationStorage();
        const string content = "{\"a\":1}";
        var hash = HashUtilities.ComputeContentHash(content);

        await storage.WriteSnapshotAsync(TestPath, 2, hash, content);

        Assert.True(storage.HasSnapshot(TestPath, 2));
    }

    // ---------------------------------------------------------------
    // T1-315: WriteSnapshotAsync - filename follows naming convention
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteSnapshotAsync_FilenameFollowsConvention()
    {
        var storage = new InMemoryMigrationStorage();
        const string content = "{\"a\":1}";
        var hash = HashUtilities.ComputeContentHash(content);

        await storage.WriteSnapshotAsync(TestPath, 3, hash, content);

        var sidecars = await storage.ListSidecarsAsync(TestPath);
        Assert.Single(sidecars);
        Assert.Equal(SidecarKind.Snapshot, sidecars[0].Kind);
        Assert.Equal(3, sidecars[0].Version);
        Assert.Equal(hash, sidecars[0].ContentHash);
        Assert.EndsWith(".snapshot.json", sidecars[0].FileName);
    }

    // ---------------------------------------------------------------
    // T1-316: FindBestSnapshotAsync - no sidecars returns null
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_NoSidecars_ReturnsNull()
    {
        var storage = new InMemoryMigrationStorage();

        var result = await storage.FindBestSnapshotAsync(TestPath, 10);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T1-317: FindBestSnapshotAsync - exact version match returns entry
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_ExactMatch_ReturnsEntry()
    {
        var storage = new InMemoryMigrationStorage();
        const string content = "v2 snapshot";
        storage.SeedSnapshot(TestPath, 2, content);

        var result = await storage.FindBestSnapshotAsync(TestPath, 2);

        Assert.NotNull(result);
        Assert.Equal(2, result.Version);
        Assert.Equal(content, result.Content);
    }

    // ---------------------------------------------------------------
    // T1-318: FindBestSnapshotAsync - lower snapshot returned when exact missing
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_LowerSnapshot_Returned()
    {
        var storage = new InMemoryMigrationStorage();
        storage.SeedSnapshot(TestPath, 1, "v1 snapshot");

        var result = await storage.FindBestSnapshotAsync(TestPath, 2);

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
    }

    // ---------------------------------------------------------------
    // T1-319: FindBestSnapshotAsync - snapshot above maxVersion not returned
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_HigherSnapshotExists_NotReturned()
    {
        var storage = new InMemoryMigrationStorage();
        storage.SeedSnapshot(TestPath, 5, "v5 snapshot");

        var result = await storage.FindBestSnapshotAsync(TestPath, 3);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T1-320: FindBestSnapshotAsync - multiple snapshots returns highest <= maxVersion
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_MultipleSnapshots_ReturnsHighestAllowed()
    {
        var storage = new InMemoryMigrationStorage();
        storage.SeedSnapshot(TestPath, 1, "v1");
        storage.SeedSnapshot(TestPath, 2, "v2");
        storage.SeedSnapshot(TestPath, 3, "v3");
        storage.SeedSnapshot(TestPath, 5, "v5");

        var result = await storage.FindBestSnapshotAsync(TestPath, 3);

        Assert.NotNull(result);
        Assert.Equal(3, result.Version);
    }

    // ---------------------------------------------------------------
    // T1-321: FindBestSnapshotAsync - hash mismatch throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_HashMismatch_Throws()
    {
        var storage = new InMemoryMigrationStorage();
        const string realContent = "snapshot content";
        // "badhash" is not the actual hash of realContent, so the check will fail.
        storage.SeedRawSidecar(TestPath, "test.v1.badhash.snapshot.json", realContent);

        await Assert.ThrowsAsync<MigrationException>(() =>
            storage.FindBestSnapshotAsync(TestPath, 1));
    }

    // ---------------------------------------------------------------
    // T1-322: WriteJournalAsync - empty operations throws ArgumentException
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteJournalAsync_EmptyOperations_ThrowsArgumentException()
    {
        var storage = new InMemoryMigrationStorage();
        // Lossless diff produces an empty operations list.
        var same = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var same2 = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var emptyJournal = UnknownsJournal.Compute(same, same2, "Test.Doc", 2, 1, "h", "1.0", "T");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.WriteJournalAsync(TestPath, emptyJournal));
    }

    // ---------------------------------------------------------------
    // T1-323: WriteJournalAsync - filename follows naming convention
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteJournalAsync_FilenameFollowsConvention()
    {
        var storage = new InMemoryMigrationStorage();
        var journal = MakeLossyJournal();

        await storage.WriteJournalAsync(TestPath, journal);

        var sidecars = await storage.ListSidecarsAsync(TestPath);
        Assert.Single(sidecars);
        Assert.Equal(SidecarKind.Journal, sidecars[0].Kind);
        Assert.EndsWith(".unknowns.json", sidecars[0].FileName);
    }

    // ---------------------------------------------------------------
    // T1-324: FindJournalAsync - matching hash returns journal
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_MatchingHash_ReturnsJournal()
    {
        var storage = new InMemoryMigrationStorage();
        const string hash = "testhash00000000";
        var journal = MakeLossyJournal(hash);
        await storage.WriteJournalAsync(TestPath, journal);

        var found = await storage.FindJournalAsync(TestPath, hash);

        Assert.NotNull(found);
        Assert.Equal("Test.Doc", found.SourceDocType);
        Assert.Equal(1, found.Operations.Count);
    }

    // ---------------------------------------------------------------
    // T1-325: FindJournalAsync - non-matching hash returns null
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_NonMatchingHash_ReturnsNull()
    {
        var storage = new InMemoryMigrationStorage();
        var journal = MakeLossyJournal("testhash00000000");
        await storage.WriteJournalAsync(TestPath, journal);

        var result = await storage.FindJournalAsync(TestPath, "differenthash___");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T1-326: FindJournalAsync - corrupt journal envelope throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_CorruptJournalEnvelope_Throws()
    {
        var storage = new InMemoryMigrationStorage();
        const string filenameHash = "aabbccddeeff0011";
        var corruptJson = BuildCorruptEnvelopeJson(filenameHash);
        storage.SeedRawSidecar(TestPath, $"test.v2.{filenameHash}.unknowns.json", corruptJson);

        await Assert.ThrowsAsync<MigrationException>(() =>
            storage.FindJournalAsync(TestPath, filenameHash));
    }

    // ---------------------------------------------------------------
    // T1-327: FindJournalAsync - body hash inconsistent with filename hash throws
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_InconsistentHashInsideJournal_Throws()
    {
        var storage = new InMemoryMigrationStorage();
        const string filenameHash = "aabbccddeeff0022";
        const string bodyHash = "zzzz3333yyyy4444"; // differs from filenameHash
        var rawJson = BuildRawJournalJson(bodyHash);
        storage.SeedRawSidecar(TestPath, $"test.v2.{filenameHash}.unknowns.json", rawJson);

        await Assert.ThrowsAsync<MigrationException>(() =>
            storage.FindJournalAsync(TestPath, filenameHash));
    }

    // ---------------------------------------------------------------
    // T1-328: DeleteJournalAsync - existing journal deleted
    // ---------------------------------------------------------------
    [Fact]
    public async Task DeleteJournalAsync_ExistingJournal_Deletes()
    {
        var storage = new InMemoryMigrationStorage();
        const string hash = "testhash00000000";
        var journal = MakeLossyJournal(hash);
        await storage.WriteJournalAsync(TestPath, journal);
        Assert.True(storage.HasJournal(TestPath, hash));

        await storage.DeleteJournalAsync(TestPath, journal);

        Assert.False(storage.HasJournal(TestPath, hash));
    }

    // ---------------------------------------------------------------
    // T1-329: DeleteJournalAsync - nonexistent journal is no-op
    // ---------------------------------------------------------------
    [Fact]
    public async Task DeleteJournalAsync_NonexistentJournal_NoOp()
    {
        var storage = new InMemoryMigrationStorage();
        var journal = MakeLossyJournal();

        // Should not throw.
        await storage.DeleteJournalAsync(TestPath, journal);
    }

    // ---------------------------------------------------------------
    // T1-330: ListSidecarsAsync - no sidecars returns empty list
    // ---------------------------------------------------------------
    [Fact]
    public async Task ListSidecarsAsync_EmptyDirectory_ReturnsEmpty()
    {
        var storage = new InMemoryMigrationStorage();

        var sidecars = await storage.ListSidecarsAsync(TestPath);

        Assert.Empty(sidecars);
    }

    // ---------------------------------------------------------------
    // T1-331: ListSidecarsAsync - multiple sidecars returned
    // ---------------------------------------------------------------
    [Fact]
    public async Task ListSidecarsAsync_MultipleSidecars_ReturnsAll()
    {
        var storage = new InMemoryMigrationStorage();
        storage.SeedSnapshot(TestPath, 2, "v2 snapshot");
        var journal = MakeLossyJournal();
        await storage.WriteJournalAsync(TestPath, journal);

        var sidecars = await storage.ListSidecarsAsync(TestPath);

        Assert.Equal(2, sidecars.Count);
    }

    // ---------------------------------------------------------------
    // T1-332: ListSidecarsAsync - filename parsed into correct SidecarFileInfo fields
    // ---------------------------------------------------------------
    [Fact]
    public async Task ListSidecarsAsync_ParsesFilenameCorrectly()
    {
        var storage = new InMemoryMigrationStorage();
        const string content = "snapshot v2 content";
        var expectedHash = HashUtilities.ComputeContentHash(content);
        storage.SeedSnapshot(TestPath, 2, content);

        var sidecars = await storage.ListSidecarsAsync(TestPath);

        Assert.Single(sidecars);
        Assert.Equal(SidecarKind.Snapshot, sidecars[0].Kind);
        Assert.Equal(2, sidecars[0].Version);
        Assert.Equal(expectedHash, sidecars[0].ContentHash);
    }

    // ---------------------------------------------------------------
    // T1-333: ListSidecarsAsync - sidecars for other base names excluded
    // ---------------------------------------------------------------
    [Fact]
    public async Task ListSidecarsAsync_OtherBaseNames_ExcludedFromResult()
    {
        var storage = new InMemoryMigrationStorage();
        // Sidecar belongs to "other.json" (different base name).
        storage.SeedRawSidecar(TestPath, "other.v1.hash1234abcd5678.snapshot.json", "content");

        var sidecars = await storage.ListSidecarsAsync(TestPath);

        Assert.Empty(sidecars);
    }

    // ---------------------------------------------------------------
    // T1-334: DeleteSidecarAsync - existing file deleted
    // ---------------------------------------------------------------
    [Fact]
    public async Task DeleteSidecarAsync_ExistingFile_Deletes()
    {
        var storage = new InMemoryMigrationStorage();
        storage.SeedSnapshot(TestPath, 2, "v2 content");
        Assert.True(storage.HasSnapshot(TestPath, 2));
        var sidecars = await storage.ListSidecarsAsync(TestPath);
        var fileName = sidecars[0].FileName;

        await storage.DeleteSidecarAsync(TestPath, fileName);

        Assert.False(storage.HasSnapshot(TestPath, 2));
    }

    // ---------------------------------------------------------------
    // T1-335: DeleteSidecarAsync - nonexistent file is no-op
    // ---------------------------------------------------------------
    [Fact]
    public async Task DeleteSidecarAsync_Nonexistent_NoOp()
    {
        var storage = new InMemoryMigrationStorage();

        // Should not throw.
        await storage.DeleteSidecarAsync(TestPath, "nonexistent.snapshot.json");
    }
}
