using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="FileSystemMigrationStorage"/> (T3-001..T3-008).
/// Each test uses an isolated temporary directory that is deleted on disposal.
/// </summary>
public sealed class FileSystemMigrationStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testFile;
    private readonly FileSystemMigrationStorage _storage;

    public FileSystemMigrationStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _testFile = Path.Combine(_tempDir, "test.json");
        _storage = new FileSystemMigrationStorage();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static (JsonObject pre, JsonObject post) MakeLossyPair()
    {
        var pre = JsonNode.Parse("{\"a\":1,\"b\":\"hello\",\"c\":99}")!.AsObject();
        var post = JsonNode.Parse("{\"a\":1,\"b\":\"hello\"}")!.AsObject();
        return (pre, post);
    }

    // ---------------------------------------------------------------
    // T3-001: Full cycle round-trips content losslessly on disk
    // ---------------------------------------------------------------
    [Fact]
    public async Task FullCycle_RealFiles_RoundTripsLosslessly()
    {
        const string original = "{\"version\":2,\"data\":\"hello\"}";
        await _storage.WriteOriginalAsync(_testFile, original);

        var read = await _storage.ReadOriginalAsync(_testFile);
        Assert.Equal(original, read);

        var hash = HashUtilities.ComputeContentHash(original);
        await _storage.WriteSnapshotAsync(_testFile, 2, hash, original);

        var snapshot = await _storage.FindBestSnapshotAsync(_testFile, 2);
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(original, snapshot.Content);
    }

    // ---------------------------------------------------------------
    // T3-002: Atomic write - temp file cleaned up on failure
    // ---------------------------------------------------------------
    [Fact]
    public async Task AtomicWrite_TempFileCleanedUp_OnException()
    {
        // Make the target directory read-only to cause WriteOriginalAsync to fail.
        // We observe that no .tmp.* file is left behind.
        var roDir = Path.Combine(_tempDir, "readonly");
        Directory.CreateDirectory(roDir);
        var roFile = Path.Combine(roDir, "ro.json");

        // Write once (succeeds).
        await _storage.WriteOriginalAsync(roFile, "initial");

        // Lock directory on Windows by removing write permission.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var di = new DirectoryInfo(roDir);
            var acl = di.GetAccessControl();
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().Name,
                System.Security.AccessControl.FileSystemRights.Write,
                System.Security.AccessControl.AccessControlType.Deny));
            di.SetAccessControl(acl);

            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    _storage.WriteOriginalAsync(roFile, "new value"));

                // No .tmp.* file should remain.
                var leftover = Directory.GetFiles(roDir, "*.tmp.*");
                Assert.Empty(leftover);
            }
            finally
            {
                // Restore permissions for cleanup.
                acl.RemoveAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    System.Security.Principal.WindowsIdentity.GetCurrent().Name,
                    System.Security.AccessControl.FileSystemRights.Write,
                    System.Security.AccessControl.AccessControlType.Deny));
                di.SetAccessControl(acl);
            }
        }
        else
        {
            // On non-Windows we simply verify the happy path completes.
            await _storage.WriteOriginalAsync(roFile, "updated");
            Assert.Equal("updated", await _storage.ReadOriginalAsync(roFile));
        }
    }

    // ---------------------------------------------------------------
    // T3-003: Concurrent reads on the same file do not interfere
    // ---------------------------------------------------------------
    [Fact]
    public async Task ConcurrentReads_SameFile_DoNotInterfere()
    {
        const string content = "shared content";
        await _storage.WriteOriginalAsync(_testFile, content);

        // Fire two reads in parallel.
        var t1 = _storage.ReadOriginalAsync(_testFile);
        var t2 = _storage.ReadOriginalAsync(_testFile);
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(content, results[0]);
        Assert.Equal(content, results[1]);
    }

    // ---------------------------------------------------------------
    // T3-004: WriteSnapshotAsync creates .migration-snapshots/ subdirectory
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteSnapshot_CreatesSidecarDirectory_WithCorrectLayout()
    {
        const string content = "{\"a\":1}";
        var hash = HashUtilities.ComputeContentHash(content);

        await _storage.WriteSnapshotAsync(_testFile, 2, hash, content);

        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        Assert.True(Directory.Exists(sidecarDir));
        var files = Directory.GetFiles(sidecarDir, "*.snapshot.json");
        Assert.Single(files);
    }

    // ---------------------------------------------------------------
    // T3-005: Sidecar filename is parseable by ListSidecarsAsync
    // ---------------------------------------------------------------
    [Fact]
    public async Task Sidecar_FilenameParseable_ByListSidecars()
    {
        const string content = "{\"x\":42}";
        var hash = HashUtilities.ComputeContentHash(content);
        await _storage.WriteSnapshotAsync(_testFile, 3, hash, content);

        var sidecars = await _storage.ListSidecarsAsync(_testFile);

        Assert.Single(sidecars);
        Assert.Equal(SidecarKind.Snapshot, sidecars[0].Kind);
        Assert.Equal(3, sidecars[0].Version);
        Assert.Equal(hash, sidecars[0].ContentHash);
    }

    // ---------------------------------------------------------------
    // T3-006: Missing sidecar directory returns empty list (no throw)
    // ---------------------------------------------------------------
    [Fact]
    public async Task MissingSidecarDirectory_ListSidecars_ReturnsEmpty()
    {
        // _testFile exists but the .migration-snapshots/ dir was never created.
        var sidecars = await _storage.ListSidecarsAsync(_testFile);

        Assert.Empty(sidecars);
    }

    // ---------------------------------------------------------------
    // T3-007: Reading a file locked by another handle throws MigrationException
    //         (Windows only - other platforms skip)
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task ReadLockedFile_FailsGracefully()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "File locking test is Windows-only.");

        const string content = "locked content";
        await File.WriteAllTextAsync(_testFile, content);

        // Hold an exclusive lock.
        using var fs = new FileStream(_testFile, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.ReadOriginalAsync(_testFile));
    }

    // ---------------------------------------------------------------
    // T3-008: FileSystemStorage behavior matches InMemoryStorage
    // ---------------------------------------------------------------
    [Fact]
    public async Task FileSystemStorage_BehaviorMatchesInMemoryStorage()
    {
        var memStorage = new InMemoryMigrationStorage();
        const string memPath = @"C:\data\parity.json";

        const string content = "{\"ver\":5}";
        var hash = HashUtilities.ComputeContentHash(content);

        // WriteOriginalAsync
        await _storage.WriteOriginalAsync(_testFile, content);
        await memStorage.WriteOriginalAsync(memPath, content);

        Assert.Equal(content, await _storage.ReadOriginalAsync(_testFile));
        Assert.Equal(content, await memStorage.ReadOriginalAsync(memPath));

        // WriteSnapshotAsync + FindBestSnapshotAsync
        await _storage.WriteSnapshotAsync(_testFile, 5, hash, content);
        await memStorage.WriteSnapshotAsync(memPath, 5, hash, content);

        var fsSnap = await _storage.FindBestSnapshotAsync(_testFile, 5);
        var memSnap = await memStorage.FindBestSnapshotAsync(memPath, 5);

        Assert.NotNull(fsSnap);
        Assert.NotNull(memSnap);
        Assert.Equal(memSnap.Version, fsSnap.Version);
        Assert.Equal(memSnap.Content, fsSnap.Content);
        Assert.Equal(memSnap.ContentHash, fsSnap.ContentHash);

        // ListSidecarsAsync
        var fsSidecars = await _storage.ListSidecarsAsync(_testFile);
        var memSidecars = await memStorage.ListSidecarsAsync(memPath);

        Assert.Equal(memSidecars.Count, fsSidecars.Count);
        Assert.Equal(memSidecars[0].Kind, fsSidecars[0].Kind);
        Assert.Equal(memSidecars[0].Version, fsSidecars[0].Version);
        Assert.Equal(memSidecars[0].ContentHash, fsSidecars[0].ContentHash);

        // WriteJournalAsync + FindJournalAsync
        var (pre, post) = MakeLossyPair();
        var journal = UnknownsJournal.Compute(pre, post, "Test.Doc", 2, 1, hash, "1.0", "Test");
        await _storage.WriteJournalAsync(_testFile, journal);
        await memStorage.WriteJournalAsync(memPath, journal);

        var fsJournal = await _storage.FindJournalAsync(_testFile, hash);
        var memJournal = await memStorage.FindJournalAsync(memPath, hash);

        Assert.NotNull(fsJournal);
        Assert.NotNull(memJournal);
        Assert.Equal(memJournal.Operations.Count, fsJournal.Operations.Count);

        // DeleteJournalAsync
        await _storage.DeleteJournalAsync(_testFile, journal);
        await memStorage.DeleteJournalAsync(memPath, journal);

        Assert.Null(await _storage.FindJournalAsync(_testFile, hash));
        Assert.Null(await memStorage.FindJournalAsync(memPath, hash));

        // DeleteSidecarAsync by filename
        var fsSidecarsBeforeDelete = await _storage.ListSidecarsAsync(_testFile);
        var memSidecarsBeforeDelete = await memStorage.ListSidecarsAsync(memPath);
        Assert.Equal(memSidecarsBeforeDelete.Count, fsSidecarsBeforeDelete.Count);
        Assert.NotEmpty(fsSidecarsBeforeDelete);

        await _storage.DeleteSidecarAsync(_testFile, fsSidecarsBeforeDelete[0].FileName);
        await memStorage.DeleteSidecarAsync(memPath, memSidecarsBeforeDelete[0].FileName);

        var fsSidecarsAfterDelete = await _storage.ListSidecarsAsync(_testFile);
        var memSidecarsAfterDelete = await memStorage.ListSidecarsAsync(memPath);
        Assert.Equal(memSidecarsAfterDelete.Count, fsSidecarsAfterDelete.Count);
    }

    // ---------------------------------------------------------------
    // T3-009: ReadOriginalAsync with non-existent path returns null
    // ---------------------------------------------------------------
    [Fact]
    public async Task ReadOriginalAsync_NonExistentPath_ReturnsNull()
    {
        var nonExistentPath = Path.Combine(_tempDir, "ghost.json");

        var result = await _storage.ReadOriginalAsync(nonExistentPath);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-010: FindBestSnapshotAsync skips file with unparseable snapshot name
    //         (covers SidecarFileHelper.TryParseStem line 64: lastDot < 0)
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_UnparseableSnapshotFileName_IsSkipped()
    {
        // Create the sidecar directory.
        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        Directory.CreateDirectory(sidecarDir);

        // "test.noDot.snapshot.json": stem = "test.noDot", prefix = "test.",
        // rest = "noDot" (no dot) -> TryParseStem returns false at lastDot < 0 check.
        var badFile = Path.Combine(sidecarDir, "test.noDot.snapshot.json");
        await File.WriteAllTextAsync(badFile, "{\"dummy\":1}");

        // Also covers versionPart[0] != 'v': stem = "test.1.abcdef" -> rest = "1.abcdef".
        var badFile2 = Path.Combine(sidecarDir, "test.1.abcdef0123456.snapshot.json");
        await File.WriteAllTextAsync(badFile2, "{\"dummy\":2}");

        var result = await _storage.FindBestSnapshotAsync(_testFile, maxVersion: 99);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-011: FindBestSnapshotAsync skips snapshot with version above maxVersion
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_VersionAboveMaxVersion_IsSkipped()
    {
        const string content = "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":5}}";
        var hash = HashUtilities.ComputeContentHash(content);

        await _storage.WriteSnapshotAsync(_testFile, 5, hash, content);

        // maxVersion = 3: version 5 > 3 should be skipped.
        var result = await _storage.FindBestSnapshotAsync(_testFile, maxVersion: 3);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-012: WriteJournalAsync with zero-ops journal throws ArgumentException
    // ---------------------------------------------------------------
    [Fact]
    public async Task WriteJournalAsync_ZeroOperationsJournal_ThrowsArgumentException()
    {
        var dom = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var identical = dom.DeepClone().AsObject();
        var zeroOpsJournal = UnknownsJournal.Compute(
            dom, identical, "Test.Doc", 2, 1, "hash123abc", "1.0", "Test");

        Assert.Empty(zeroOpsJournal.Operations);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.WriteJournalAsync(_testFile, zeroOpsJournal));
    }

    // ---------------------------------------------------------------
    // T3-013: FindJournalAsync skips file with unparseable journal name
    //         (covers FileSystemMigrationStorage.FindJournalAsync continue at line 134)
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_UnparseableJournalFileName_IsSkipped()
    {
        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        Directory.CreateDirectory(sidecarDir);

        // "test.noDot.unknowns.json": stem = "test.noDot", rest = "noDot" (no dot)
        // -> TryParseStem returns false -> continue.
        var badJournalFile = Path.Combine(sidecarDir, "test.noDot.unknowns.json");
        await File.WriteAllTextAsync(badJournalFile, "{\"dummy\":1}");

        var result = await _storage.FindJournalAsync(_testFile, "someContentHash");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-014: FindJournalAsync with non-matching hash returns null (hash skipped)
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_NonMatchingHash_ReturnsNull()
    {
        // Write a real journal for hash "aaaa1111bbbb2222".
        var (pre, post) = MakeLossyPair();
        const string knownHash = "aaaa1111bbbb2222";
        var journal = UnknownsJournal.Compute(
            pre, post, "Test.Doc", 2, 1, knownHash, "1.0", "Test");
        await _storage.WriteJournalAsync(_testFile, journal);

        // Search with a different hash -> the file's filename hash doesn't match -> returns null.
        var result = await _storage.FindJournalAsync(_testFile, "zzzz9999zzzz9999");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-016: FindBestSnapshotAsync picks the highest version among multiple
    //         valid snapshots (v9 and v10); exercises the "version <= bestVersion"
    //         skip branch when v9 is enumerated after v10 (alphabetical: v10 before v9).
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_MultipleSnapshots_ReturnsBestVersion()
    {
        const string content10 =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":10},\"x\":1}";
        const string content9 =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":9},\"x\":1}";

        var hash10 = HashUtilities.ComputeContentHash(content10);
        var hash9 = HashUtilities.ComputeContentHash(content9);

        // Write v10 first; its filename ("test.v10....")  sorts alphabetically
        // BEFORE "test.v9...." because '1' < '9', so v10 is enumerated first.
        await _storage.WriteSnapshotAsync(_testFile, 10, hash10, content10);
        await _storage.WriteSnapshotAsync(_testFile, 9, hash9, content9);

        var result = await _storage.FindBestSnapshotAsync(_testFile, maxVersion: 15);

        Assert.NotNull(result);
        Assert.Equal(10, result.Version);
    }

    // ---------------------------------------------------------------
    // T3-017: ReadOriginalAsync throws MigrationException when file is locked (Windows only).
    //         Covers the IOException catch block in ReadOriginalAsync.
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task ReadOriginalAsync_LockedFile_ThrowsMigrationException()
    {
        Skip.IfNot(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows));

        await File.WriteAllTextAsync(_testFile, "{\"a\":1}");

        using var lockStream = new FileStream(
            _testFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.ReadOriginalAsync(_testFile));
    }

    // ---------------------------------------------------------------
    // T3-018: FindBestSnapshotAsync returns null when the sidecar directory
    //         does not exist (covers the early-exit "return null" branch).
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_NoSidecarDirectory_ReturnsNull()
    {
        // No snapshots written, so no ".migration-snapshots" directory exists.
        var result = await _storage.FindBestSnapshotAsync(_testFile, maxVersion: 10);
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-019: FindJournalAsync returns null when the sidecar directory
    //         does not exist (covers the early-exit "return null" branch).
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_NoSidecarDirectory_ReturnsNull()
    {
        var result = await _storage.FindJournalAsync(_testFile, "aabbccdd11223344");
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T3-020: FindBestSnapshotAsync throws MigrationException when the hash
    //         in the snapshot filename does not match the computed content hash.
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindBestSnapshotAsync_HashMismatch_ThrowsMigrationException()
    {
        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        Directory.CreateDirectory(sidecarDir);

        // "0000000000000000" will not match the real content hash of this JSON.
        const string content = "{\"x\":1}";
        var fakeFile = Path.Combine(sidecarDir, "test.v1.0000000000000000.snapshot.json");
        await File.WriteAllTextAsync(fakeFile, content);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.FindBestSnapshotAsync(_testFile, maxVersion: 5));
    }

    // ---------------------------------------------------------------
    // T3-021: FindJournalAsync throws MigrationException when the hash in the
    //         journal filename does not match the SourceContentHash in the body.
    // ---------------------------------------------------------------
    [Fact]
    public async Task FindJournalAsync_HashMismatch_ThrowsMigrationException()
    {
        // Write a real journal (body will contain SourceContentHash = "aabbccdd11223344").
        var (pre, post) = MakeLossyPair();
        const string bodyHash = "aabbccdd11223344";
        var journal = UnknownsJournal.Compute(pre, post, "Test.Doc", 2, 1, bodyHash, "1.0", "test");
        await _storage.WriteJournalAsync(_testFile, journal);

        // Copy the journal content to a file whose filename has a different hash.
        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        var realFile = Directory.GetFiles(sidecarDir, "*.unknowns.json").First();
        var content = await File.ReadAllTextAsync(realFile);

        const string fakeHash = "0000000000000000";
        var fakeFile = Path.Combine(sidecarDir, $"test.v1.{fakeHash}.unknowns.json");
        await File.WriteAllTextAsync(fakeFile, content);

        // Search with the fake hash: file is found (filename hash matches),
        // but body SourceContentHash != fakeHash -> MigrationException.
        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.FindJournalAsync(_testFile, fakeHash));
    }

    // ---------------------------------------------------------------
    // T3-022: ListSidecarsAsync returns both snapshots and journals when
    //         the sidecar directory contains one of each.
    //         Covers the "else if (TryParseJournalFileName)" branch.
    // ---------------------------------------------------------------
    [Fact]
    public async Task ListSidecarsAsync_WithSnapshotAndJournal_ReturnsBoth()
    {
        const string content = "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1}}";
        var hash = HashUtilities.ComputeContentHash(content);
        await _storage.WriteSnapshotAsync(_testFile, 1, hash, content);

        var (pre, post) = MakeLossyPair();
        var journal = UnknownsJournal.Compute(pre, post, "Test.Doc", 2, 1, hash, "1.0", "test");
        await _storage.WriteJournalAsync(_testFile, journal);

        var result = await _storage.ListSidecarsAsync(_testFile);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Kind == SidecarKind.Snapshot);
        Assert.Contains(result, s => s.Kind == SidecarKind.Journal);
    }

    // ---------------------------------------------------------------
    // T3-023: FindBestSnapshotAsync throws MigrationException when the
    //         snapshot file cannot be read due to an exclusive lock (Windows only).
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task FindBestSnapshotAsync_LockedSnapshotFile_ThrowsMigrationException()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        const string content = "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1}}";
        var hash = HashUtilities.ComputeContentHash(content);
        await _storage.WriteSnapshotAsync(_testFile, 1, hash, content);

        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        var snapshotFile = Directory.GetFiles(sidecarDir, "*.snapshot.json").First();
        using var lockStream = new FileStream(
            snapshotFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.FindBestSnapshotAsync(_testFile, maxVersion: 10));
    }

    // ---------------------------------------------------------------
    // T3-024: FindJournalAsync throws MigrationException when the journal
    //         file cannot be read due to an exclusive lock (Windows only).
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task FindJournalAsync_LockedJournalFile_ThrowsMigrationException()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        var (pre, post) = MakeLossyPair();
        const string hash = "1122334455667788";
        var journal = UnknownsJournal.Compute(pre, post, "Test.Doc", 2, 1, hash, "1.0", "test");
        await _storage.WriteJournalAsync(_testFile, journal);

        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        var journalFile = Directory.GetFiles(sidecarDir, "*.unknowns.json").First();
        using var lockStream = new FileStream(
            journalFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.FindJournalAsync(_testFile, hash));
    }

    // ---------------------------------------------------------------
    // T3-025: DeleteJournalAsync throws MigrationException when the journal
    //         file is locked and File.Delete fails (Windows only).
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task DeleteJournalAsync_LockedFile_ThrowsMigrationException()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        var (pre, post) = MakeLossyPair();
        const string hash = "aabbccdd11223344";
        var journal = UnknownsJournal.Compute(pre, post, "Test.Doc", 2, 1, hash, "1.0", "test");
        await _storage.WriteJournalAsync(_testFile, journal);

        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        var journalFile = Directory.GetFiles(sidecarDir, "*.unknowns.json").First();
        using var lockStream = new FileStream(
            journalFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.DeleteJournalAsync(_testFile, journal));
    }

    // ---------------------------------------------------------------
    // T3-026: DeleteSidecarAsync throws MigrationException when the sidecar
    //         file is locked and File.Delete fails (Windows only).
    // ---------------------------------------------------------------
    [SkippableFact]
    public async Task DeleteSidecarAsync_LockedFile_ThrowsMigrationException()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        const string content = "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1}}";
        var hash = HashUtilities.ComputeContentHash(content);
        await _storage.WriteSnapshotAsync(_testFile, 1, hash, content);

        var sidecarDir = Path.Combine(_tempDir, ".migration-snapshots");
        var snapshotFile = Directory.GetFiles(sidecarDir, "*.snapshot.json").First();
        using var lockStream = new FileStream(
            snapshotFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<MigrationException>(() =>
            _storage.DeleteSidecarAsync(_testFile, Path.GetFileName(snapshotFile)));
    }
}

